using FrameCommunity;
using FrameCommunity.Data;
using HalconDotNet;
using LogTool;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Frame_Core
{
    public class BaseProParam : IDisposable
    {

        private static readonly ConcurrentDictionary<int, SemaphoreSlim> camSemaphoreMap = new ConcurrentDictionary<int, SemaphoreSlim>();
        /// <summary>
        /// 原始图像信号
        /// </summary>
        public SemaphoreSlim semaphoreSlimCam => camSemaphoreMap.GetOrAdd(_camID, _ => new SemaphoreSlim(0, 100));

        public readonly int _camID;


        /// <summary>
        /// 相机的图像ID，默认第一片为1，预处理使用
        /// </summary>
        public int nCurProImgId = 1;
        /// <summary>
        /// EA计数索引
        /// </summary>
        public int nEAId = 1;

        /// <summary>
        /// 相机名字
        /// </summary>
        public string strCamreaName { get; }

        private static readonly ConcurrentDictionary<int, SemaphoreSlim> camSemaphoreMapSlim = new ConcurrentDictionary<int, SemaphoreSlim>();
        

        /// <summary>
        /// 裁图EA信号
        /// </summary>
        public SemaphoreSlim semaphoreSlim => camSemaphoreMapSlim.GetOrAdd(_camID, _ => new SemaphoreSlim(0, 100));




        /// <summary>
        /// 相机枚举
        /// </summary>
        public CameraEnum cameraEnum = CameraEnum.Cam1;
        /// <summary>
        /// 相机图像队列
        /// </summary>
        public List<ImageInfo> m_listImageInfo_Cam = new List<ImageInfo>();
        public QueuePackage<ImageInfo> QueueGrabImage = new QueuePackage<ImageInfo>();
        public bool bIs3D = false;
        /// <summary>
        /// 用于3D图像队列
        /// </summary>
        public QueuePackage<Image3DClass> Queue3DImage = new QueuePackage<Image3DClass>();
        /// <summary>
        /// 已经处理的图像队列
        /// </summary>
        public QueuePackage<ImageInfo> alreadyDealGrabImage = new QueuePackage<ImageInfo>();
        public QueuePackage<Image3DClass> alreadyDealGrabImage3D = new QueuePackage<Image3DClass>();


        public Action<ProductBase> ProductCallBack; //原相机图像单帧回调 -预处理
        public Action<ProductBase> ProductCallBackCraft; //EA图像单帧回调 - 工艺流程


        public delegate void AutoCalibration(CameraEnum camName, HObject ho_image); //自动标定委托
        public AutoCalibration AutoCalibrationDelegate;//自动标定委托
        public object m_lImgInfolock = new object();

        /// <summary>
        /// 销毁的图像数
        /// </summary>
        public int disposeImageCount = 0;

        public BaseProParam(string cameraName)
        {
            strCamreaName = cameraName;
            Enum.TryParse(cameraName, out CameraEnum camEnum);
            _camID = (int)camEnum;
        }


        public virtual void Dispose()
        {

            while (QueueGrabImage.Count > 0)
            {
                QueueGrabImage.Dequeue()?.Dispose();
            }

            while (alreadyDealGrabImage.Count > 0)
            {
                alreadyDealGrabImage.Dequeue()?.Dispose();
            }
            if (m_listImageInfo_Cam.Count() > 0)
            {
                foreach (var item in m_listImageInfo_Cam)
                {
                    item?.Dispose();
                }
            }
            m_listImageInfo_Cam.Clear();

            while (Queue3DImage.Count > 0)
            {
                Queue3DImage.Dequeue().Dispose();
            }

            nCurProImgId = 1;
            nEAId = 1;
            // throw new NotImplementedException();
        }

        /// <summary>
        /// 为新到的图分配业务帧号。返回 false 表示这一帧不要（无效图 / 重复触发 /
        /// 帧号校验失败），调用方不入队；被拒绝的图由拒绝方负责释放。
        ///
        /// 默认实现保持各机型原有语义：每相机独立自增，激光走的就是这段。
        /// 五金由专属子类覆写为工位级序号，基类行为不变。
        /// </summary>
        protected virtual bool TryAssignFrameId(ImageInfo image)
        {
            if (image.image_ID == 0) return false;
            image.image_ID = nCurProImgId;
            nCurProImgId++;
            return true;
        }

        /// <summary>
        /// 当前帧是否允许进入帧队列。检查点在 TryAssignFrameId 之前：
        /// 拒绝的帧不占帧号、不产生待检，避免 PLC 永远等不到结论。
        /// 默认永远允许，激光/其它机型行为不变；五金在暂停积压超限时覆写。
        /// </summary>
        protected virtual bool 允许接收新帧()
        {
            return true;
        }

        /// <summary>
        /// 信号量已到上限时静默忽略：消费线程本来就会每 50ms 检查队列，
        /// 不依赖 Release 才能取到帧；抛 SemaphoreFullException 反而会把回调线程打断。
        /// </summary>
        private void ReleaseCamSemaphore()
        {
            try
            {
                semaphoreSlimCam.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }

        /// <summary>
        /// 回调数据处理
        /// </summary>
        public virtual void CallBackImgDataDeal()
        {


            if (QueueGrabImage.Count > 0)
            {
                if (!Project.Instance.GetStates())
                {
                    ImageInfo imageInfo = QueueGrabImage.Dequeue();
                    disposeImageCount++;
                    Log4netRecord.RunLog.Debug(strCamreaName + "销毁回调的图像，图像帧号为:" + imageInfo.image_ID + "\n" + "销毁的图像数：" + disposeImageCount);
                    imageInfo.Dispose();
                }
                else
                {
                    disposeImageCount = 0;
                    ImageInfo InfoImgInfo = QueueGrabImage.Dequeue();
                    try
                    {
                        if (InfoImgInfo != null)
                        {
                            // 暂停积压超限/正在清场：在取号前丢弃，避免产生无结论的 Pending
                            if (!允许接收新帧())
                            {
                                InfoImgInfo.Dispose();
                                return;
                            }

                            if (TryAssignFrameId(InfoImgInfo))
                            {
                                alreadyDealGrabImage.Enqueue(InfoImgInfo);
                                ReleaseCamSemaphore();
                            }
                        }
                        else
                        {
                            MessageBox.Show(strCamreaName + "图像来源为空");
                        }

                    }
                    catch (Exception ee)
                    {

                        MessageBox.Show(strCamreaName + "图像来源为空:" + ee.ToString());
                    }



                }

            }




        }

    }
}
