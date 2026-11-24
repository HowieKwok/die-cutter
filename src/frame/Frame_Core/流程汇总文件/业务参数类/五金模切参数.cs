using Frame_Core.业务类;
using FrameCommunity.Data;
using System;

namespace Frame_Core.业务参数类
{
    /// <summary>线扫相机的安装位置。安装位置决定它在帧号链路里的角色。</summary>
    public enum 线扫安装位
    {
        上线 = 0,
        下线 = 1,
    }

    /// <summary>
    /// 五金线扫相机（模切主 / 从）的业务参数。
    ///
    /// 与基类的唯一差别是帧号来源：上线按下发号，下线与上线对齐。
    /// 入队、销毁、信号量这些既有逻辑仍由基类执行，这里只决定"这一帧要不要、号是多少"。
    /// </summary>
    public sealed class 五金模切LineScanParam : DieCutParam
    {
        private readonly 线扫安装位 _side;
        private readonly FrameTriggerGate _gate = new FrameTriggerGate(50);

        public 五金模切LineScanParam(string cameraName, 线扫安装位 side)
            : base(cameraName)
        {
            _side = side;
        }

        protected override bool TryAssignFrameId(ImageInfo image)
        {
            // 相机没给出有效图（与基类同口径），直接不要
            if (image.image_ID == 0) return false;

            // 50ms 内的重复触发属于正常抖动：不占号、不提示，图必须释放
            if (!_gate.Accept())
            {
                image.Dispose();
                return false;
            }

            int frameId;
            if (_side == 线扫安装位.上线)
            {
                frameId = 五金模切帧序号.LineScan.NextUp();
            }
            else
            {
                int upFrameId = 五金模切帧序号.LineScan.UpFrameId;
                if (!五金模切帧序号.LineScan.TryNextDown(out frameId, out FrameRejectReason reason))
                {
                    // 这一帧没有帧号：后续流程拿不到它，PLC 也不能收它。
                    // 序号已在 TryNextDown 内钳位，下一次触发即应对齐。
                    // 启动预热不提示：片距为 N 时前 N 片下线本来就没有对应产品，
                    // 每次都弹窗会把正常冷启动当成故障。
                    if (reason != FrameRejectReason.启动预热)
                    {
                        五金模切告警.Raise(
                            五金模切告警.线扫帧号错位,
                            "线扫帧号错位",
                            $"下线扫丢帧：{reason}。上线帧号={upFrameId}，" +
                            $"下线已接受={五金模切帧序号.LineScan.DownFrameId}。已钳位，下一帧应对齐。");
                    }
                    image.Dispose();
                    return false;
                }
                else if (reason != FrameRejectReason.无)
                {
                    // 滞后已被接受（跳号对齐，不丢片），只提示一次，不阻断
                    五金模切告警.Raise(
                        五金模切告警.线扫帧号错位,
                        "线扫帧号重对齐",
                        $"下线扫帧号跳号：{reason}。上线帧号={upFrameId}，" +
                        $"本帧已按 {frameId} 接受（未丢片）。");
                }
            }

            image.image_ID = frameId;
            五金模切帧序号.Register(frameId, _side == 线扫安装位.上线
                ? 五金模切帧序号.FrameStation.上线扫
                : 五金模切帧序号.FrameStation.下线扫);

            // 恢复正常后自动撤销提示，避免操作员对着一条已经不复存在的故障排查
            if (_side == 线扫安装位.下线) 五金模切告警.Dismiss(五金模切告警.线扫帧号错位);
            return true;
        }

        public override void Dispose()
        {
            _gate.Reset();
            base.Dispose();
        }
    }

    /// <summary>
    /// 五金尺寸相机（尺寸 1/2/3）的业务参数。
    ///
    /// 帧号由尺寸工位的组窗口统一分配：三路在同一窗口内到达就复用同一个号，
    /// 合并槽才能通过比较帧号发现"三路处理的不是同一片"。
    /// </summary>
    public sealed class 五金模切SizeParam : SizeDetectParam
    {
        private readonly FrameTriggerGate _gate = new FrameTriggerGate(50);

        public 五金模切SizeParam(string cameraName)
            : base(cameraName)
        {
        }

        protected override bool TryAssignFrameId(ImageInfo image)
        {
            if (image.image_ID == 0) return false;

            if (!_gate.Accept())
            {
                image.Dispose();
                return false;
            }

            int frameId = 五金模切帧序号.Size.Acquire();
            image.image_ID = frameId;
            五金模切帧序号.Register(frameId, 五金模切帧序号.FrameStation.尺寸);
            return true;
        }

        public override void Dispose()
        {
            _gate.Reset();
            base.Dispose();
        }
    }
}