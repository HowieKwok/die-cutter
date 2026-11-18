using Frame_Core;
using System;
using System.Collections.Generic;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 一路相机的显示单元：物理相机下标 + 它在界面上占用的标题位与图像流。
    /// 控件仍由 Designer 固定绘制，本表只声明"这一路用哪几个"。
    /// </summary>
    internal sealed class CamDisplayUnit
    {
        /// <summary>物理相机下标，连续 0..N-1，与 M_CamParam.l_CameraDevs 顺序对齐。</summary>
        public int CamIndex;

        /// <summary>所属 tab 分组（"线扫" / "面阵"）。当前仅声明，B 档动态建格子时才使用。</summary>
        public string TabGroup;

        /// <summary>该路占用的标题位。一路可能占多个（cam0/1 同时有大图与 EA 图）。</summary>
        public List<CamTitleSlot> Titles = new List<CamTitleSlot>();

        /// <summary>原图只存不显（无显示队列）。仅 cam0/1 有。</summary>
        public CamStreamUnit RawSaveOnly;

        /// <summary>主图：cam0/1 为大图，cam2..5 为原图。</summary>
        public CamStreamUnit Main;

        /// <summary>EA 图。仅 cam0/1 有。</summary>
        public CamStreamUnit Ea;

        /// <summary>NG 缺陷图（轮播池）。除 cam2 外都有。</summary>
        public CamStreamUnit Ng;
    }

    internal sealed class CamTitleSlot
    {
        public UC_HalconWindow Control;

        /// <summary>"大图" / "EA图" / null（尺寸相机无后缀）。</summary>
        public string Suffix;

        public static CamTitleSlot Of(UC_HalconWindow control, string suffix = null)
        {
            return new CamTitleSlot { Control = control, Suffix = suffix };
        }
    }

    /// <summary>
    /// 一类图像流（原图 / 大图 / EA 图 / NG 图）的队列组合。
    /// ShowQueue 为 null 表示只保存不显示；FixedWindow 与 Pool 同时为 null 表示不显示。
    /// ScreenshotSaveQueue 与 HighScreenshotQueue 同时为 null 表示该流不产生截图。
    /// </summary>
    internal sealed class CamStreamUnit
    {
        /// <summary>固定显示格子。与 Pool 二选一。</summary>
        public NewDisplayWindowClass FixedWindow;

        /// <summary>轮播池取格子（NG 用）。有状态：每取一次推进一格，只能在真正显示时取一次。</summary>
        public Func<NewDisplayWindowClass> Pool;

        public ImageQueueData ShowQueue;
        public ImageQueueData SaveQueue;
        public ImageQueueData DisposeQueue;
        public ImageQueueData ScreenshotSaveQueue;
        public ImageQueueData HighScreenshotQueue;

        private bool HasScreenshot
        {
            get { return ScreenshotSaveQueue != null && HighScreenshotQueue != null; }
        }

        /// <summary>只保存不显示（cam0/1 的原图流：生产者直接把图塞进 Save 队列）。</summary>
        public static CamStreamUnit SaveOnly(ImageQueueData saveQueue, ImageQueueData disposeQueue)
        {
            return new CamStreamUnit { SaveQueue = saveQueue, DisposeQueue = disposeQueue };
        }

        /// <summary>固定格子显示 + 保存。两个截图队列都为 null 时不产生截图（大图流即如此）。</summary>
        public static CamStreamUnit ShowAt(NewDisplayWindowClass window, ImageQueueData showQueue, ImageQueueData saveQueue, ImageQueueData disposeQueue,
                                           ImageQueueData screenshotSaveQueue = null, ImageQueueData highScreenshotQueue = null)
        {
            return new CamStreamUnit
            {
                FixedWindow = window,
                ShowQueue = showQueue,
                SaveQueue = saveQueue,
                DisposeQueue = disposeQueue,
                ScreenshotSaveQueue = screenshotSaveQueue,
                HighScreenshotQueue = highScreenshotQueue
            };
        }

        /// <summary>轮播池显示 + 保存。池不参与截图，避免每路多取一次格子导致串位。</summary>
        public static CamStreamUnit RotateAt(Func<NewDisplayWindowClass> pool, ImageQueueData showQueue, ImageQueueData saveQueue, ImageQueueData disposeQueue)
        {
            return new CamStreamUnit
            {
                Pool = pool,
                ShowQueue = showQueue,
                SaveQueue = saveQueue,
                DisposeQueue = disposeQueue
            };
        }

        /// <summary>
        /// 按"显示 -> 保存 -> 高清截图 -> 截图保存"的固定顺序处理一路流。
        /// 顺序与改造前逐行一致，不可调整：显示回调会把图像转存到 SaveQueue。
        /// </summary>
        public static void Handle(CamStreamUnit stream)
        {
            if (stream == null) return;

            if (stream.ShowQueue != null)
            {
                // 取格子必须留在 lambda 内部：队列为空时原本不会推进轮播索引
                if (stream.HasScreenshot)
                {
                    Server_ImageProcess.ShowImagesAsync(
                        (showHalconWindow) => Server_ImageProcess.ImageShowCallback(stream.ResolveShowWindow(), showHalconWindow, stream.ScreenshotSaveQueue, stream.HighScreenshotQueue),
                        stream.ShowQueue, stream.SaveQueue);
                }
                else
                {
                    Server_ImageProcess.ShowImagesAsync(
                        (showHalconWindow) => Server_ImageProcess.ImageShowCallback(stream.ResolveShowWindow(), showHalconWindow),
                        stream.ShowQueue, stream.SaveQueue);
                }
            }

            Server_ImageProcess.SaveImagesAsync(Server_ImageProcess.ImageSaveCallback, stream.SaveQueue, stream.DisposeQueue);

            if (!stream.HasScreenshot) return;

            // 截图一律落在固定格子：轮播流不参与截图，避免二次取格子导致串位
            Server_ImageProcess.HighScreenshotAsync(
                (showHalconWindow) => Server_ImageProcess.HighScreenshotCallback(stream.FixedWindow, showHalconWindow, stream.ScreenshotSaveQueue),
                stream.HighScreenshotQueue);

            Server_ImageProcess.SaveScreenshotAsync(Server_ImageProcess.ScreenshotSaveCallback, stream.ScreenshotSaveQueue);
        }

        private NewDisplayWindowClass ResolveShowWindow()
        {
            return FixedWindow ?? (Pool != null ? Pool() : null);
        }
    }
}