using System;

namespace Frame_Core.业务参数类
{
    /// <summary>
    /// 一帧被拒绝的原因。正常帧不产生此值。
    /// </summary>
    public enum FrameRejectReason
    {
        无 = 0,
        /// <summary>下线比上线领先，或上线还没出过帧。</summary>
        下线超前,
        /// <summary>
        /// 启动 / 换卷初期，上线还没攒够片距，下线这几帧本来就没有对应产品。
        /// 属于正常冷启动相位，不作为故障提示，否则每次启动都会弹窗。
        /// </summary>
        启动预热,
        /// <summary>
        /// 仿真单步后的首次下线钳位：手动步进造成的恢复暂态，不是真实丢帧，不提示。
        /// </summary>
        单步恢复,
        /// <summary>下线落后于上线（漏触发 / 丢图）。</summary>
        下线滞后,
        /// <summary>同一相机 50ms 内的重复触发，属于正常抖动。</summary>
        重复触发,
    }

    /// <summary>
    /// 线扫工位帧号：上线是唯一源头，下线恒为"上线最新帧号 - 1"。
    ///
    /// 下线不是自由计数。每一帧都要和上线对齐：对齐就接受，不对齐就丢弃并钳位，
    /// 这样"下线比上线先触发"或"某路漏了一张图"都不会把错位带进后续所有产品。
    ///
    /// 为什么钳位目标不是"当前对齐值"而是 expected - 1：
    /// 序号保存的是"已接受帧号"，下一帧分配时才 +1。要让下一次触发拿到 expected，
    /// 现在就必须停在 expected - 1。
    /// </summary>
    public sealed class LineScanFrameSequencer
    {
        private readonly object _sync = new object();
        private int _upFrameId;   // 最新已分配的上线帧号
        private int _downFrameId; // 最新已接受的下线帧号
        private int _suppressLeadClamp; // 仿真单步后允许一次“下线超前”静默钳位

        public int UpFrameId { get { lock (_sync) { return _upFrameId; } } }
        public int DownFrameId { get { lock (_sync) { return _downFrameId; } } }

        /// <summary>
        /// 上线分配帧号后的回调。仿真用它发出"产品令牌"，真机不设置（null）。
        ///
        /// 必须挂在 NextUp 而不是触发放行处：上线帧号是在图像处理时才分配的，
        /// 触发到分配之间隔着整个处理耗时（仿真里实测 6 秒级）。
        /// 从触发处发令牌，令牌号与界面上线号就不在同一时间轴，落后量会被抵消。
        /// </summary>
        public static Action<int> UpFrameAssigned;

        /// <summary>
        /// 下线接受一帧后的回调。仿真用它把"已过下线的产品号"交给尺寸工位。
        ///
        /// 必须挂在"下线接受"而不是上线分配处：产品物理上是 上线 → 下线 → 尺寸，
        /// 尺寸的号一旦能越过下线，界面上就会出现"产品刚到上线就直接到了尺寸"。
        /// </summary>
        public static Action<int> DownFrameAssigned;

        /// <summary>上线按下发号，永远成功。序号从 1 开始。</summary>
        public int NextUp()
        {
            int id;
            lock (_sync) { id = ++_upFrameId; }
            UpFrameAssigned?.Invoke(id);
            return id;
        }

        /// <summary>
        /// 下线取号。返回 false 表示这一帧必须丢弃：不入队、不处理、不写 PLC。
        /// 丢帧时已把下线序号钳到上线最新号，上线再发一个号即可恢复。
        /// </summary>
        public bool TryNextDown(out int frameId, out FrameRejectReason reason)
        {
            lock (_sync)
            {
                frameId = 0;
                reason = FrameRejectReason.无;

                // 上线还没出过帧：这几帧下线本来就没有对应产品（启动 / 换卷暂态），静默丢弃。
                if (_upFrameId < 1)
                {
                    reason = FrameRejectReason.启动预热;
                    return false;
                }

                // 唯一约束：下线不能超过上线已经发出的最大帧号。
                // 落后多少片 = 相机间距 / 皮带速度，是不定值，既不判定也不告警。
                //
                // 比较对象是 _upFrameId 这个标量，不是帧队列尾部：帧出队进流程后会从队列移除，
                // 此时队列可能为空，读队列会把"上线在跑但队列空"误判成"上线没有帧"。
                int next = _downFrameId + 1;
                if (next > _upFrameId)
                {
                    // 下线超前：这一帧没有对应的上游产品，丢弃并钳齐上线。
                    // 仿真单步后的首次钳位是手动步进的恢复暂态，静默处理。
                    if (_suppressLeadClamp > 0)
                    {
                        _suppressLeadClamp--;
                        reason = FrameRejectReason.单步恢复;
                    }
                    else
                    {
                        reason = FrameRejectReason.下线超前;
                    }
                    _downFrameId = _upFrameId;
                    return false;
                }

                _downFrameId = next;
                frameId = next;
                NotifyDownAccepted(frameId);
                return true;
            }
        }

        /// <summary>
        /// 下线接受一帧后的回调。仿真用它把"已过下线的产品号"传给尺寸工位，
        /// 保证尺寸帧号不会越过下线帧号（物理顺序 上线 → 下线 → 尺寸）。
        /// </summary>
        private static void NotifyDownAccepted(int frameId)
        {
            Action<int> handler = DownFrameAssigned;
            if (handler != null) handler(frameId);
        }

        /// <summary>
        /// 换卷/复位：序号归零。
        /// 不需要"放行首帧"的特殊分支：上线为 0 时下线走启动预热，上线发出第 1 个号后自然接上。
        /// </summary>
        /// <summary>仿真单步后调用：下一次“下线超前”钳位静默处理，避免恢复运行弹错位告警。</summary>
        public void SuppressLeadClampOnce()
        {
            lock (_sync) { _suppressLeadClamp = 1; }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _upFrameId = 0;
                _downFrameId = 0;
                _suppressLeadClamp = 0;
            }
        }
    }
}