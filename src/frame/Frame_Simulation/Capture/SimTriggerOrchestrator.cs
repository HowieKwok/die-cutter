using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Frame_Simulation.Capture
{
    /// <summary>
    /// 仿真触发编排：一个产品节拍产生 t0，各工位在 t0 + 偏移 时刻各取一张。
    ///
    /// 为什么不能让每台相机各自按固定间隔取图：那样三台尺寸相机永远同时出图、
    /// 上下线也永远同时出图，"帧号错位""等不齐"这类问题在仿真里根本复现不出来。
    /// 真实产线上物料是顺序经过各工位的，仿真必须保留这个时间差。
    ///
    /// 偏移在每一拍上用独立 Timer 兑现，而不是单线程 sleep 依次发放：
    /// 2500ms 的偏移可能跨过下一个 2000ms 的 t0，串行发放会越拖越晚。
    /// </summary>
    public sealed class SimTriggerOrchestrator : IDisposable
    {
        /// <summary>槽在"产品流"里的角色。</summary>
        public enum SlotRole
        {
            /// <summary>不参与产品流（激光只有一台相机）。</summary>
            None = 0,
            /// <summary>上线扫：每放行一次就发出一个产品令牌。</summary>
            ProduceProduct = 1,
            /// <summary>尺寸工位：每开一次组窗口就领走一个产品令牌。</summary>
            ConsumeProduct = 2
        }

        private sealed class Slot
        {
            public int OffsetMs;
            public SlotRole Role;
            /// <summary>是否已经起跑（已有在途定时）。没起跑的槽由 tick 补排，起跑后由 OnDue 自续。</summary>
            public bool Armed;
            public AutoResetEvent Signal = new AutoResetEvent(false);
            public Timer Due;
        }

        // 产品令牌：上线放行时入队，尺寸开窗时出队。
        //
        // 为什么用队列而不是算"落后几片"：落后量 = 输送延时 / 节拍，是延时的投影。
        // 延时一改、产能一改，片数就该跟着变；写死 2 片只在当前配方下成立。
        // 队列深度由放行节奏自动长成，代码里不出现任何片数常量。
        private readonly Queue<int> _products = new Queue<int>();
        private int _productSeq;

        // 输送中的产品：(帧号, 到达尺寸工位的时刻)。
        //
        // 尺寸工位不能用"固定节拍 + 相位偏移"来模拟：所有工位同节拍运行，
        // 相位差被折叠在一个节拍内，队列深度恒为 0~1，加大延时也不会更落后
        // （实测 5000 与 10000 的 take/enqueue 差值都是 1）。
        // 物理上是"产线输送中同时存在若干片"，所以按产品逐片定时送达。
        private struct Delivery
        {
            public int FrameId;
            public long DueAtMs;
        }

        private readonly Queue<Delivery> _pending = new Queue<Delivery>();
        private Timer _deliveryTimer;
        private int _sizeDelayMs;
        private int _consumerSlots;

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly object _sync = new object();

        private Thread _tickThread;
        private int _intervalMs = 2000;

        // 共同纪元：所有槽的到期时刻都落在同一条网格 epoch + k*interval + offset 上。
        // 没有纪元时，槽的参考点是"TickLoop 发现它的那一刻"，跨 tick 边界注册的两路
        // 会天然差整数拍——偏移配对了，实际时序还是错的，而且只在启动时序变化时复现。
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _epochMs;

        /// <summary>注册一个工位，返回它的下标。offset 是相对 t0 的毫秒偏移。</summary>
        public int Register(int offsetMs, SlotRole role = SlotRole.None)
        {
            lock (_sync)
            {
                Slot slot = new Slot { OffsetMs = Math.Max(0, offsetMs), Role = role };
                slot.Due = new Timer(OnDue, slot, Timeout.Infinite, Timeout.Infinite);
                _slots.Add(slot);

                // 尺寸工位的触发不排周期定时器，改由产品逐片送达驱动；
                // 这里只记下它的延时（= 上线到尺寸的输送时间）与路数。
                if (role == SlotRole.ConsumeProduct)
                {
                    _sizeDelayMs = Math.Max(_sizeDelayMs, slot.OffsetMs);
                    _consumerSlots++;
                }

                return _slots.Count - 1;
            }
        }

        /// <summary>
        /// 尺寸工位取一个产品令牌。返回 0 表示队列空（尺寸比上线先到）。
        /// 只在"开新组窗口"时调用：三路共用同一个号，令牌也只能出队一次。
        /// </summary>
        public int TakeSizeFrameId()
        {
            lock (_sync)
            {
                return _products.Count == 0 ? 0 : _products.Dequeue();
            }
        }

        /// <summary>在途产品数（= 输送延时 / 节拍）。仅用于界面与提示，不参与判定。</summary>
        public int ProductsInFlight
        {
            get { lock (_sync) { return _products.Count; } }
        }

        /// <summary>停机/换卷后清空在途产品，与帧号重置同一时刻。</summary>
        public void ClearProducts()
        {
            lock (_sync)
            {
                _products.Clear();
                _productSeq = 0;
            }
        }

        /// <summary>
        /// 下线接受一帧后调用：登记"这片产品还在输送中"，到期再送到尺寸工位。
        /// 送达时刻 = 现在 + 尺寸延时，所以延时越大，尺寸拿到这一帧就越晚。
        /// </summary>
        public void EnqueueDownProduct(int frameId)
        {
            lock (_sync)
            {
                // 送达时刻从"下线接受"起算，不引用上线触发队列。
                // 曾改成用上线出生时刻配对，但下线会丢帧（片距未满足、超前丢弃），
                // 出生时刻队列只进不出地积压，取到的是很久以前那一片，
                // DueAt 落在过去 → 立即送达 → 尺寸与下线同步变化。
                _pending.Enqueue(new Delivery
                {
                    FrameId = frameId,
                    DueAtMs = _clock.ElapsedMilliseconds + _sizeDelayMs
                });
            }

            ArmDelivery();
        }

        /// <summary>
        /// 把定时器对准"最早那一片的送达时刻"。用一次性 Timer，不用轮询也不 sleep：
        /// 到期即唤醒，精度是毫秒级的系统定时器，不受节拍限制。
        /// </summary>
        private void ArmDelivery()
        {
            lock (_sync)
            {
                if (_pending.Count == 0 || _stop.WaitOne(0)) return;

                long due = _pending.Peek().DueAtMs;
                long wait = due - _clock.ElapsedMilliseconds;
                if (wait < 0) wait = 0;

                if (_deliveryTimer == null)
                    _deliveryTimer = new Timer(OnDelivery, null, wait, Timeout.Infinite);
                else
                    _deliveryTimer.Change(wait, Timeout.Infinite);
            }
        }

        private void OnDelivery(object state)
        {
            lock (_sync)
            {
                if (_pending.Count == 0 || _stop.WaitOne(0)) return;
                _products.Enqueue(_pending.Dequeue().FrameId);
            }

            // 送达即唤醒尺寸三路：AutoResetEvent 一次只放一路，所以要逐个 Set。
            lock (_sync)
            {
                foreach (Slot slot in _slots)
                {
                    if (slot.Role != SlotRole.ConsumeProduct) continue;
                    try { slot.Signal.Set(); } catch (ObjectDisposedException) { }
                }
            }

            ArmDelivery();
        }

        public int SlotCount
        {
            get { lock (_sync) { return _slots.Count; } }
        }

        public void Start(int intervalMs)
        {
            lock (_sync)
            {
                _intervalMs = Math.Max(10, intervalMs);
                if (_tickThread != null && _tickThread.IsAlive) return;
                _stop.Reset();
                _epochMs = _clock.ElapsedMilliseconds;

                // 重启时所有槽重新起跑：上次线程可能停在 OnDue 的提前返回处，Armed 仍为 true，
                // 不复位的话 tick 会一直认为它们"已在途"，重启后不再有任何触发
                foreach (Slot slot in _slots) slot.Armed = false;

                _tickThread = new Thread(TickLoop) { IsBackground = true, Name = "SimTrigger编排" };
                _tickThread.Start();
            }
        }

        /// <summary>
        /// 产能变更后必须同步节拍。不调用的话，界面上的产能改了、编排器还在用旧间隔，
        /// 两处数值会长期不一致，而且只有仿真时序受影响，很难联想到产能设置。
        /// </summary>
        public void SetInterval(int intervalMs)
        {
            lock (_sync)
            {
                int next = Math.Max(10, intervalMs);
                if (next == _intervalMs) return;

                // 节拍一变，旧的到期时刻就落在旧网格上：全部撤回让 TickLoop 按新网格重排。
                // 只改 _intervalMs 不撤回的话，各槽会继续按旧相位自续，偏移看着对、间隔却不是新的。
                _intervalMs = next;
                foreach (Slot slot in _slots)
                {
                    try { slot.Due.Change(Timeout.Infinite, Timeout.Infinite); }
                    catch (ObjectDisposedException) { }
                    slot.Armed = false;
                }
            }
        }

        /// <summary>
        /// 把"该槽本次触发时刻"对齐到共同网格：epoch + k*interval + offset。
        ///
        /// offset 可以大于 interval（尺寸 5000ms / 节拍 2000ms）：稳态相位 = offset % interval，
        /// 看起来"只差 1 秒"，但那正是落后 2.5 片的物理事实，不要试图压成整拍。
        /// </summary>
        private long AlignToGrid(long nowMs, int offsetMs)
        {
            long elapsed = nowMs - _epochMs;
            if (elapsed < 0) elapsed = 0;

            long k = elapsed / _intervalMs;
            long due = _epochMs + k * _intervalMs + offsetMs;
            if (due < nowMs) due += _intervalMs;
            return due;
        }

        public void Stop()
        {
            _stop.Set();

            Thread thread;
            lock (_sync) { thread = _tickThread; _tickThread = null; }

            if (thread != null && thread.IsAlive) thread.Join(2000);
        }

        /// <summary>
        /// 阻塞到本轮该这个工位触发。返回 false 表示收到停止信号，调用方应退出循环。
        /// 下标无效（未编排）时直接放行，让相机退回自己的固定节拍。
        /// </summary>
        public bool WaitTurn(int index, WaitHandle stop)
        {
            Slot slot;
            lock (_sync)
            {
                if (index < 0 || index >= _slots.Count) return true;
                slot = _slots[index];
            }

            int which = WaitHandle.WaitAny(new WaitHandle[] { slot.Signal, stop }, Timeout.Infinite);
            return which == 0;
        }

        private void TickLoop()
        {
            while (!_stop.WaitOne(0))
            {
                lock (_sync)
                {
                    // 只给"尚未起跑"的槽按偏移排一次；已起跑的槽由 OnDue 自续下一拍。
                    // 不能锁死在首拍：相机槽可能在本线程启动之后才注册（实测首拍 slots=0，四路全哑）。
                    // 也不能每拍无条件 Change：那会把大于节拍的偏移（尺寸 2500ms）永远推迟。
                    foreach (Slot slot in _slots)
                    {
                        if (slot.Armed) continue;

                        // 尺寸工位不排周期定时器：它的触发由"产品送达"驱动，
                        // 排了反而会按节拍空转，产生没有产品的空帧。
                        if (slot.Role == SlotRole.ConsumeProduct) continue;
                        try
                        {
                            long now = _clock.ElapsedMilliseconds;
                            long due = AlignToGrid(now, slot.OffsetMs);
                            slot.Due.Change(due - now, Timeout.Infinite);
                            slot.Armed = true;
                        }
                        catch (ObjectDisposedException) { }
                    }
                }

                if (_stop.WaitOne(_intervalMs)) break;
            }
        }

        private void OnDue(object state)
        {
            Slot slot = state as Slot;
            if (slot == null || _stop.WaitOne(0)) return;

            // 令牌改由上线帧号分配处发出（LineScan.NextUp → UpFrameAssigned），
            // 这里只放行图像，不再凭空造号。

            try { slot.Signal.Set(); }
            catch (ObjectDisposedException) { }

            // 自排下一拍：让每个工位拥有独立定时链，
            // 偏移大于节拍时（尺寸 2500ms）才不会被下一拍取消
            int interval;
            lock (_sync) { interval = _intervalMs; }
            try
            {
                slot.Due.Change(interval, Timeout.Infinite);
                slot.Armed = true;
            }
            catch (ObjectDisposedException) { slot.Armed = false; }
        }

        public void Dispose()
        {
            Stop();

            lock (_sync)
            {
                foreach (Slot slot in _slots)
                {
                    try { slot.Due.Dispose(); } catch { }
                    try { slot.Signal.Close(); } catch { }
                }
                _slots.Clear();
            }

            _stop.Close();
        }
    }
}