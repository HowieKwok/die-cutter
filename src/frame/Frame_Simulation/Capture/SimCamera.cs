using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DevMgmt.Cameras.SDK;
using FrameCommunity.Data;
using HalconDotNet;
using LogTool;

namespace Frame_Simulation.Capture
{
    /// <summary>
    /// 仿真相机。对下游而言它就是一台相机：同样的 QueueGrabImage、同样的
    /// RaiseCamCallbackStart / CamCallbackEndEvent，流程不需要知道图从哪来。
    /// 唯一的差别是"图不是从网口来的，是从磁盘轮读的"，且读取节奏由 FramePacer 控制。
    /// </summary>
    public class SimCamera : CameraObj
    {
        private readonly string _name;
        private readonly FramePacer _pacer;
        private readonly Log.SimRecorder _recorder;

        private readonly object _sync = new object();
        private string[] _files = new string[0];
        private int _cursor;
        private string _imageDir;
        private HObject _image;
        private bool _isExist;
        private long _frameId;

        private Thread _grabThread;
        private ManualResetEvent _stopEvent;

        // 与真机一致：队列由相机自己持有，通过 QueueGrabImage 暴露给业务侧绑定。
        // 基类的 QueueGrabImage 是只读自动属性（返回 null），不覆写就会在入队时空引用。
        private readonly FrameCommunity.QueuePackage<ImageInfo> _queueGrabImage =
            new FrameCommunity.QueuePackage<ImageInfo>();

        public SimCamera(string name, string imageDir, FramePacer pacer, Log.SimRecorder recorder)
        {
            _name = string.IsNullOrEmpty(name) ? "SimCamera" : name;
            _imageDir = imageDir;
            _pacer = pacer ?? new FramePacer(30);
            _recorder = recorder;
            HOperatorSet.GenEmptyObj(out _image);
            _isExist = true;
        }

        public override HObject Image
        {
            get { return _image; }
        }

        public override FrameCommunity.QueuePackage<ImageInfo> QueueGrabImage
        {
            get { return _queueGrabImage; }
        }

        /// <summary>与真机同义：表示"正在采集"，不是"相机存在"。</summary>
        public override bool IsCameraStatue
        {
            get
            {
                lock (_sync) { return _grabThread != null && _grabThread.IsAlive; }
            }
        }

        public override bool IsExist
        {
            get { return _isExist; }
            set { _isExist = value; }
        }

        public string CameraName { get { return _name; } }

        /// <summary>
        /// 何时算"被触发"。真机靠硬件触发，仿真用"流程是否在运行"代替——
        /// 否则程序一启动就持续产图，与真机行为不符，也会让下游收到运行前的帧。
        /// 为 null 时按"始终触发"处理（标定/点检等一次性取图场景）。
        /// </summary>
        public Func<bool> TriggerWhen { get; set; }

        /// <summary>重新扫描图片目录。相机设置里改了仿真路径后调用，无需重启。</summary>
        public void ReloadImageSet(string imageDir = null)
        {
            lock (_sync)
            {
                if (!string.IsNullOrEmpty(imageDir)) _imageDir = imageDir;
                _files = ScanImages(_imageDir);
                _cursor = 0;
            }

            Record(Log.SimRecordKind.Info, "加载图片集：" + _files.Length + " 张 ← " + _imageDir);
        }

        public override bool InitCam()
        {
            ReloadImageSet();
            return _files.Length > 0;
        }

        public override bool OpenCam()
        {
            _isExist = true;
            return _files.Length > 0;
        }

        public override bool StartGrabImage()
        {
            lock (_sync)
            {
                if (_grabThread != null && _grabThread.IsAlive) return true;
                if (_files.Length == 0) ReloadImageSet();
                if (_files.Length == 0)
                {
                    Record(Log.SimRecordKind.Error, "没有可用仿真图片，无法启动采集");
                    return false;
                }

                _stopEvent = new ManualResetEvent(false);
                _grabThread = new Thread(GrabLoop);
                _grabThread.IsBackground = true;
                _grabThread.Name = "SimCamera_" + _name;
                _grabThread.Start();
            }
            return true;
        }

        public override bool StopGrabImage()
        {
            Thread thread;
            ManualResetEvent stop;
            lock (_sync)
            {
                thread = _grabThread;
                stop = _stopEvent;
                _grabThread = null;
                _stopEvent = null;
            }

            if (stop != null) stop.Set();
            if (thread != null && thread.IsAlive) thread.Join(2000);
            if (stop != null) stop.Dispose();
            return true;
        }

        public override void DestroyCam()
        {
            StopGrabImage();
        }

        public override void DestroyObjects2()
        {
            StopGrabImage();
            lock (_sync)
            {
                if (_image != null) { _image.Dispose(); _image = null; }
            }
        }

        public override bool SetTriggerMode(int mode) { return true; }
        public override bool SetGrabState(bool bStart) { return bStart ? StartGrabImage() : StopGrabImage(); }
        public override bool SetGain(float gain) { return true; }

        /// <summary>采集主循环。节拍与退出信号都来自 FramePacer，这里只管"取一张、送上岸"。</summary>
        private void GrabLoop()
        {
            ManualResetEvent stop;
            lock (_sync) { stop = _stopEvent; }
            if (stop == null) return;

            while (!stop.WaitOne(0))
            {
                if (!_pacer.WaitNextFrame(stop)) break;

                // 未运行时不产图：等价于真机"没有触发信号"，自然不会进队列、不会记录。
                Func<bool> trigger = TriggerWhen;
                if (trigger != null && !trigger()) continue;

                try
                {
                    PushOneFrame();
                }
                catch (Exception ex)
                {
                    Record(Log.SimRecordKind.Error, "取图异常：" + ex.Message);
                }
            }
        }

        private void PushOneFrame()
        {
            string path;
            long frame;
            lock (_sync)
            {
                if (_files.Length == 0) return;
                if (_cursor >= _files.Length) _cursor = 0;   // 循环播放，模拟连续料带
                path = _files[_cursor++];
                frame = ++_frameId;
            }

            HObject img;
            try
            {
                HOperatorSet.ReadImage(out img, path);
            }
            catch (HalconException)
            {
                return;
            }
            if (img == null) return;

            lock (_sync)
            {
                if (_image != null) _image.Dispose();
                _image = img;
            }

            // 尺寸必须回填：下游拼图/裁剪（DieCutProDevice、Plugin.CropPart）全部按
            // imageWidth/imageHeight 算画布和窗口，缺省值 16384x10000 会让画面比例失真。
            HOperatorSet.GetImageSize(img, out HTuple imgWidth, out HTuple imgHeight);
            long frameHeight = imgHeight.I;

            ImageInfo info = new ImageInfo
            {
                ho_image = img.Clone(),
                image_ID = frame,
                cameraName = _name,
                ImageDescription = Path.GetFileName(path),
                imageWidth = imgWidth.I,
                imageHeight = imgHeight.I,
                nFirstLineEncoderCount = (frame - 1) * frameHeight,
                nLastLineEncoderCount = frame * frameHeight
            };

            // 严格照真机（CameraHKControl.get_Image）的顺序：先通知"帧开始"，再入队，最后收尾。
            // 顺序反了会让下游在图像还没进队时就来取，表现为偶发空帧。
            RaiseCamCallbackStart(frame);
            _queueGrabImage.Enqueue(info, (uint)Math.Max(1, 3), true);

            delegateCallBack handler = CamCallbackEndEvent;
            if (handler != null) handler();

            Record(Log.SimRecordKind.Info, string.Format("帧 {0} ← {1}", frame, Path.GetFileName(path)));
        }

        private static string[] ScanImages(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return new string[0];
            return Directory.GetFiles(dir)
                .Where(p => p.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void Record(Log.SimRecordKind kind, string message)
        {
            if (_recorder == null) return;
            _recorder.Cam(kind, _name, message);
        }
    }
}