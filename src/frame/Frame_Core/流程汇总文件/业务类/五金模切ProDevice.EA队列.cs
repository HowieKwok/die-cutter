using Frame_Core.业务参数类;
using FrameCommunity.Data;
using LogTool;

namespace Frame_Core.业务类
{
    /// <summary>
    /// EA 图像队列的并发访问。
    ///
    /// 裁图线程在 Add，EA 线程在取。基类给的是裸 List，
    /// "先判 Count 再取 [0]" 不是原子的：被裁图线程插队就是越界异常。
    /// 这里把"判空 + 摘除"收进同一个临界区。
    ///
    /// 锁用基类已有的 m_lImgInfolock（原先无人使用），
    /// 所以激光那条路径的行为与加锁前完全一致。
    /// </summary>
    partial class 五金模切ProDevice
    {
        /// <summary>EA 队列容量。EA 线程只要活着就会消费，触发上限说明它被卡住了。</summary>
        private const int EaQueueCapacity = 64;

        /// <summary>入队一张 EA 图。返回 false 表示队列已满，调用方必须结算该帧并释放图。</summary>
        private static bool EnqueueEAImage(DieCutParam param, ImageInfo image)
        {
            if (param == null || image == null) return false;

            lock (param.m_lImgInfolock)
            {
                if (param.imageEAInfos.Count >= EaQueueCapacity) return false;
                param.imageEAInfos.Add(image);
                return true;
            }
        }

        /// <summary>
        /// 入队一张 EA 图；队列满时按"该帧没有可判定产物"补 NG。
        ///
        /// 队列溢出说明 EA 线程被卡住，此时顺序已不可靠，优先保证 PLC 每片有一拍，
        /// 并用告警把故障暴露出来，而不是留下一条永远停在待检的帧。
        /// </summary>
        private static bool EnqueueEaOrAbandon(DieCutParam param, ImageInfo image, int camID)
        {
            if (EnqueueEAImage(param, image))
            {
                param.semaphoreSlim.Release();
                return true;
            }

            int frameId = image == null ? 0 : (int)image.image_ID;
            Log4netRecord.ErrorLog.Error($"EA 队列已满（上限 {EaQueueCapacity}），帧 {frameId} 按 NG 结算");
            MessageNotify.AddErrorLog(
                $"EA 队列已满（上限 {EaQueueCapacity}），帧 {frameId} 无法检测，已按 NG 回传，请检查 EA 流程是否卡死");
            image?.Dispose();

            五金模切帧序号.Abandon(frameId,
                camID == 0 ? 五金模切帧序号.FrameStation.上线扫 : 五金模切帧序号.FrameStation.下线扫,
                "EA 队列溢出");

            param.semaphoreSlim.Release();
            return false;
        }

        /// <summary>
        /// 安全取一帧 EA 图。返回 false 表示队列为空——注意此时可能已经消耗过一次信号量，
        /// 调用方必须直接返回，不能当作"有图但图是 null"继续往下跑。
        /// </summary>
        private static bool TryTakeEAImage(DieCutParam param, out ImageInfo image)
        {
            image = null;
            if (param == null) return false;

            lock (param.m_lImgInfolock)
            {
                if (param.imageEAInfos.Count == 0) return false;
                image = param.imageEAInfos[0];
                param.imageEAInfos.RemoveAt(0);
                return true;
            }
        }
    }
}