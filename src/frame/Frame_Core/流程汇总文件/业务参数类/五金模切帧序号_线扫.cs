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
        private int _upFrameId;   // 最新已接受的上线帧号
        private int _downFrameId; // 最新已接受的下线帧号
        private bool _downFirstAfterReset;

        /// <summary>
        /// 下线相对上线的工位偏移：正常运行时下线帧号 = 上线帧号 - DownOffset。
        /// 语义就是两台线扫相机之间隔了几片。
        ///
        /// 默认 0 而不是 1：0 表示"不隔片"，配置不符时表现为合片配不上、能立刻发现；
        /// 1 表示"隔一片"，一旦与实际不符，下线会持续判超前 → 钳位 → 再错，
        /// 永久丢片且很难定位。默认值应当选择能暴露问题的那个。
        /// </summary>
        public int DownOffset { get; set; } = 0;

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
        /// 返回 false 前已把下线序号钳到正确值，下一次触发即可对齐。
        /// </summary>
        public bool TryNextDown(out int frameId, out FrameRejectReason reason)
        {
            lock (_sync)
            {
                reason = FrameRejectReason.无;

                // 换卷/复位后首帧放行：只有片距为 0 时才需要——此时首帧的 expected 会算成 0，
                // 而 0 是非法帧号。片距 ≥1 时不该放行：放行会凭空给一个号，
                // 下一帧立刻变成"下线超前"并丢片（实测 offset=1 时必然重现）。
                // 片距 ≥1 的冷启动由后面的"启动预热"静默跳过。
                if (_downFirstAfterReset && DownOffset == 0)
                {
                    _downFirstAfterReset = false;
                    if (_upFrameId >= 1)
                    {
                        frameId = ++_downFrameId;
                        NotifyDownAccepted(frameId);
                        return true;
                    }
                }

                int expected = _upFrameId - DownOffset;
                if (expected < 1)
                {
                    // 上线还没攒够片距：这几帧下线本来就没有对应产品（启动/换卷暂态）。
                    // 静默丢弃，不报"下线超前"——那是稳态故障的语义。
                    reason = FrameRejectReason.启动预热;
                    frameId = 0;
                    return false;
                }

                int actual = _downFrameId + 1;
                if (actual == expected)
                {
                    _downFrameId = actual;
                    frameId = actual;
                    NotifyDownAccepted(frameId);
                    return true;
                }

                if (actual > expected)
                {
                    // 下线超前：多出来的这一帧没有对应的上线帧，丢弃并回到上一号
                    reason = FrameRejectReason.下线超前;
                    _downFrameId = expected - 1;
                    frameId = 0;
                    return false;
                }

                // 下线滞后：帧本身是真实的，只是号落后了。原实现每拍只把号补 1，
                // 而上线每拍也推进 1，差值恒定不变，永远追不上（实测 up=n / down=n-1 无限循环）。
                // 直接把这一帧对齐到 expected 并接受，保证每片都有结论。
                reason = FrameRejectReason.下线滞后;
                _downFrameId = expected;
                frameId = expected;
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
        /// 换卷/复位：序号归零，并放行下线首帧。
        /// 不放行的话换卷后第一片下线必被丢弃（expected 会算成 0）。
        /// </summary>
        public void Reset()
        {
            lock (_sync)
            {
                _upFrameId = 0;
                _downFrameId = 0;
                _downFirstAfterReset = true;
            }
        }
    }
}