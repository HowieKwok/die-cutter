using System;
using System.Diagnostics;

namespace Frame_Core.业务参数类
{
    /// <summary>单调毫秒时钟。用 Stopwatch 而不是 DateTime：50ms 阈值下 DateTime 的 15.6ms 取整会把 60ms 判成 46ms。</summary>
    internal static class MonotonicClock
    {
        private static readonly long Frequency = Stopwatch.Frequency;

        public static long NowMs()
        {
            return Stopwatch.GetTimestamp() * 1000L / Frequency;
        }
    }

    /// <summary>
    /// 尺寸工位帧号：按"组窗口"分配，不按相机分配。
    ///
    /// 三路相机各自触发，窗口内到达的复用同一个号；窗口超时或已满三路则开新窗。
    /// 这样"同一组必然同号、不同组必然不同号"，合并槽才能通过比较帧号发现错配。
    ///
    /// 为什么不让第一个完成的路决定帧号：那样三路天然相等，
    /// "帧号不一致"这个故障永远检测不出来。
    /// </summary>
    public sealed class SizeFrameSequencer
    {
        public const int CamerasPerGroup = 3;

        private readonly object _sync = new object();
        private int _seq;
        private int _windowFrameId;
        private int _windowTaken;
        private long _windowOpenedAt;

        /// <summary>仿真单步的一次性帧号。下一次开新窗口时优先使用并清空。</summary>
        private int _manualFrameId;

        /// <summary>窗口宽度。超过则视为下一组，重新取号。</summary>
        public int WindowMilliseconds { get; set; } = 1500;

        /// <summary>上一个窗口分配的帧号（0 表示还没开过窗）。界面展示用。</summary>
        public int CurrentFrameId { get { lock (_sync) { return _seq; } } }

        /// <summary>上一组窗口关闭时缺了几路（0 表示齐）。用于提示缺帧。</summary>
        public int LastWindowMissing { get; private set; }

        /// <summary>
        /// 帧号来源。默认 null = 本序号器自增（真机走这条，行为一字不改）。
        ///
        /// 仿真注入"产品令牌"取号器：号来自上线放行时发出的令牌，
        /// 因此尺寸帧号必然落后于上线帧号；落后几片 = 输送延时 / 节拍，
        /// 由队列深度自然决定，这里不计算也不硬编码。
        /// </summary>
        public Func<int> NextFrameIdProvider { get; set; }

        /// <summary>
        /// 一组三路取号。尺寸工位与线扫相互独立：这里只做自己的计数，
        /// 不看上线帧号，也不计算"落后几片"——落后是触发延时造成的自然结果。
        /// </summary>
        public int Acquire()
        {
            lock (_sync)
            {
                long now = MonotonicClock.NowMs();
                bool windowValid = _windowFrameId != 0
                    && _windowTaken < CamerasPerGroup
                    && now - _windowOpenedAt < WindowMilliseconds;

                if (windowValid)
                {
                    _windowTaken++;
                    return _windowFrameId;
                }

                // 每个新窗口开启时都要刷新上一组的到齐情况。
                // 只在"不满"时写会让缺过一次之后永远显示旧值，齐了也要写 0。
                if (_windowFrameId != 0)
                {
                    LastWindowMissing = Math.Max(0, CamerasPerGroup - _windowTaken);
                }

                int next;
                if (_manualFrameId > 0)
                {
                    // 仿真单步：下线刚接受的帧号，三路必须同号；用完即弃
                    next = _manualFrameId;
                    _manualFrameId = 0;
                }
                else
                {
                    next = NextFrameIdProvider != null ? NextFrameIdProvider() : _seq + 1;
                }

                // 没有可用产品号（队列空）：不开窗，返回 0 让上层丢弃这一帧。
                // 若把 0 写进窗口，三路会分别复用到 0 和不存在的 1，触发"帧号不一致"告警。
                if (next <= 0) return 0;

                _seq = next;
                _windowFrameId = _seq;
                _windowOpenedAt = now;
                _windowTaken = 1;
                return _seq;
            }
        }

        /// <summary>
        /// 仿真单步：强制下一组窗口使用指定帧号，并关闭当前窗口。
        /// 三路必须在新窗口内先后取号，才能同号。
        /// </summary>
        public void PushManualFrameId(int frameId)
        {
            lock (_sync)
            {
                _manualFrameId = frameId;
                _windowFrameId = 0;
                _windowTaken = 0;
                _windowOpenedAt = 0;
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _seq = 0;
                _windowFrameId = 0;
                _windowTaken = 0;
                _windowOpenedAt = 0;
                _manualFrameId = 0;
                LastWindowMissing = 0;
            }
        }
    }

    /// <summary>
    /// 每相机一个的触发去抖门。
    ///
    /// 必须作用在"图像入队"这一步，不能只作用在触发事件上：
    /// 否则图照样进队列，帧号与实际图数量会再次分叉。
    /// 被丢掉的重复触发不占号、不提示——它是正常抖动，不是故障。
    /// </summary>
    public sealed class FrameTriggerGate
    {
        private readonly object _sync = new object();
        private readonly int _minIntervalMs;
        private long _lastAcceptedAt;

        public FrameTriggerGate(int minIntervalMs = 50)
        {
            _minIntervalMs = minIntervalMs;
        }

        public bool Accept()
        {
            lock (_sync)
            {
                long now = MonotonicClock.NowMs();
                if (_lastAcceptedAt != 0 && now - _lastAcceptedAt < _minIntervalMs) return false;
                _lastAcceptedAt = now;
                return true;
            }
        }

        public void Reset()
        {
            lock (_sync) { _lastAcceptedAt = 0; }
        }
    }

    /// <summary>
    /// 五金模切的帧号入口。同一工位内的所有相机共享一个实例——
    /// 这正是"同工位帧号必须一致"的落点，不能让每台相机各拿一份。
    /// </summary>
    public static class 五金模切帧序号
    {
        public static readonly LineScanFrameSequencer LineScan = new LineScanFrameSequencer();
        public static readonly SizeFrameSequencer Size = new SizeFrameSequencer();

        private static readonly object _genSync = new object();
        private static readonly System.Collections.Generic.HashSet<int> _liveFrames =
            new System.Collections.Generic.HashSet<int>();
        private static int _generation;

        /// <summary>当前代际。停机 / 换卷 / 复位各 +1。</summary>
        public static int Generation { get { lock (_genSync) { return _generation; } } }

        /// <summary>帧属于哪个工位。待检投递需要知道该发给哪条结果队列。</summary>
        public enum FrameStation
        {
            上线扫 = 0,
            下线扫 = 1,
            尺寸 = 2,
        }

        /// <summary>
        /// 帧被接受并拿到帧号。业务层据此投递"待检"，
        /// 保证 Pending 一定早于同一帧的结论入队。
        /// </summary>
        public static event Action<int, FrameStation> FrameAssigned;

        /// <summary>登记一帧属于当前代际。序号器分配帧号后立即调用。</summary>
        public static void Register(int frameId, FrameStation station)
        {
            lock (_genSync) { _liveFrames.Add(frameId); }

            // 结算守卫在同一时刻登记：只有登记过的帧才允许认领终态，
            // 这样才能把「被接受但从未结算」的帧暴露出来，而不是等它永远停在待检
            FrameSettlement.Begin(frameId, station);

            Action<int, FrameStation> handler = FrameAssigned;
            if (handler == null) return;
            try { handler(frameId, station); }
            catch (Exception ex) { LogTool.Log4netRecord.ErrorLog.Error("帧分配通知失败：" + ex); }
        }

        /// <summary>
        /// 该帧是否属于当前代际。旧代际的帧一律作废——
        /// 这是为了防止"停机瞬间已出队、随后才判完"的迟到结果
        /// 把停机时写下的 0 覆盖成 OK/NG。
        /// </summary>
        public static bool IsLive(int frameId)
        {
            lock (_genSync) { return _liveFrames.Contains(frameId); }
        }

        /// <summary>
        /// 一帧被放弃（流程抛异常、无产出）。业务层据此补一条 NG，保证 PLC 每片必有一拍。
        ///
        /// 为什么不在核心层直接结算：核心层不认识结果队列，也拿不到 PLC 点位。
        /// 它只负责说"这一帧没了、原因是这个"，补什么值由业务层决定。
        /// </summary>
        public static event Action<int, FrameStation, string> FrameAbandoned;

        public static void Abandon(int frameId, FrameStation station, string reason)
        {
            if (frameId <= 0) return;

            LogTool.Log4netRecord.RunLog.Error($"[五金帧放弃] 帧{frameId} {station}：{reason}");

            Action<int, FrameStation, string> handler = FrameAbandoned;
            if (handler == null) return;

            try { handler(frameId, station, reason); }
            catch (Exception ex) { LogTool.Log4netRecord.ErrorLog.Error("帧放弃通知失败：" + ex); }
        }

        /// <summary>
        /// 已废弃：上下线片差 = 相机间距 / 皮带速度，不是常数，不能当固定偏移用。
        ///
        /// 两处必须都调：启动流程前（StartRun）与仿真建相机前（触发偏移要按它算）。
        /// 只在 StartRun 读的话，仿真首次建相机拿到的还是旧值，首轮时序就不对。
        /// </summary>
        public static int LoadDownOffsetFromCraft()
        {
            // 已废弃：上下线片差 = 相机间距 / 皮带速度，是不定值，不能当固定偏移用。
            // 保留空实现只为兼容既有调用方（配方变量「下线片距」不再生效）。
            return 0;
        }

        /// <summary>
        /// 换卷 / 复位时通知外部清空"在途产品"。仿真用它丢弃输送中的产品：
        /// 否则复位后尺寸侧仍会把换卷前的那几片当成新片依次送进来。
        /// </summary>
        public static Action OnProductsCleared;

        /// <summary>换卷 / 复位：序号归零、下线首帧放行、旧代际的帧全部作废。</summary>
        public static void Reset()
        {
            LineScan.Reset();
            Size.Reset();
            BumpGeneration();

            Action handler = OnProductsCleared;
            if (handler != null) handler();
        }

        /// <summary>
        /// 停机只作废旧帧，不动序号：现场停一次再启动要接着原来的片序跑，
        /// 不能从 1 重新数，否则下线会和上线永久错位。
        /// </summary>
        public static void Stop()
        {
            BumpGeneration();
        }

        private static void BumpGeneration()
        {
            lock (_genSync)
            {
                _generation++;
                _liveFrames.Clear();
            }

            // 结算守卫跟着换代：不复用帧号时旧终态会挡住新帧。
            // 必须在 _genSync 之外调用，避免两把锁嵌套顺序反转。
            FrameSettlement.NewGeneration();
        }
    }

    /// <summary>
    /// 五金帧准入闸门。Accepting 在清场期间关闭，防止已通过 GetStates 检查的回调
    /// 在清场后把旧图塞回队列；Paused 时限制每相机积压，防止自动模式暂停期间内存失控。
    /// 激光不读这个类，行为不变。
    /// </summary>
    public static class 五金模切帧门控
    {
        public static volatile bool Accepting;
        public static volatile bool Paused;
        public static int Limit = 8;

        private static int _dropCount;
        private static int _alarmRaised;

        public static int DropCount { get { return System.Threading.Volatile.Read(ref _dropCount); } }

        /// <summary>
        /// 返回 false 表示这一帧必须丢弃（不占帧号、不产生待检）。
        /// firstDrop 为 true 时调用方发一次“暂停积压已满”告警，避免每帧刷屏。
        /// </summary>
        public static bool Allow(int backlog, out bool firstDrop)
        {
            firstDrop = false;

            // 清场期间拒绝是预期行为：不计数、不告警，
            // 否则回滚等待/确认弹窗期间会污染 DropCount 并误弹“暂停积压已满”。
            if (!Accepting) return false;

            if (!(Paused && backlog >= Limit)) return true;

            System.Threading.Interlocked.Increment(ref _dropCount);
            if (System.Threading.Interlocked.Exchange(ref _alarmRaised, 1) == 0)
            {
                firstDrop = true;
            }
            return false;
        }

        /// <summary>开始/继续/结束时调用：清暂停标志与丢弃计数/告警去重。</summary>
        public static void ResetForRun()
        {
            Paused = false;
            System.Threading.Interlocked.Exchange(ref _dropCount, 0);
            System.Threading.Interlocked.Exchange(ref _alarmRaised, 0);
        }

        /// <summary>暂停时打开上限；继续/开始时清除告警去重，保留本轮丢弃计数供结束快照读取。</summary>
        public static void SetPaused(bool paused)
        {
            Paused = paused;
            if (!paused) System.Threading.Interlocked.Exchange(ref _alarmRaised, 0);
        }

        public static void ResetDropCount()
        {
            System.Threading.Interlocked.Exchange(ref _dropCount, 0);
            System.Threading.Interlocked.Exchange(ref _alarmRaised, 0);
        }
    }
}