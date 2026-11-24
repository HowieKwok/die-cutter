using Frame_Core.业务参数类;
using FrameCommunity.Data;

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
        private static void EnqueueEAImage(DieCutParam param, ImageInfo image)
        {
            if (param == null || image == null) return;
            lock (param.m_lImgInfolock)
            {
                param.imageEAInfos.Add(image);
            }
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