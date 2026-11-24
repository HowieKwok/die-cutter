using Frame_Core.业务类;
using LogTool;
using System;
using System.Threading;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 三条结果通道的发布器：单线程串行提交，视觉侧完全不等待它。
    ///
    /// 为什么必须串行：五金只有一个 PLC 实例、一条连接。三条线程并发写同一连接
    /// 会让报文交错；而共用一把长锁又会被一次超时拖住。串行 + 单次写 ≤300ms
    /// 是同时满足"保序"和"不饿死心跳"的唯一形态。
    ///
    /// 失败语义：单通道累计 3 次失败即暂停该通道（队首保留），提示后等操作员确认。
    /// 暂停只影响这一条通道，另外两条继续发——一条线坏了不能把另外两条也拖停。
    /// </summary>
    public sealed class PlcResultPublisher : IDisposable
    {
        public const int MaxAttempts = 3;

        /// <summary>一次失败后到下次重试的间隔。太密等于瞬间用完 3 次，PLC 还没缓过来。</summary>
        private const int RetryIntervalMs = 300;

        private readonly PlcResultChannel[] _channels;
        private readonly Func<PlcResultChannel, PlcResultItem, bool> _write;
        private readonly long[] _retryAfter;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly ManualResetEvent _exit = new ManualResetEvent(false);

        private Thread _thread;
        private int _cursor;

        public PlcResultPublisher(PlcResultChannel[] channels, Func<PlcResultChannel, PlcResultItem, bool> write)
        {
            if (channels == null) throw new ArgumentNullException("channels");
            if (write == null) throw new ArgumentNullException("write");

            _channels = channels;
            _write = write;
            _retryAfter = new long[_channels.Length];
        }

        public void Start()
        {
            if (_thread != null) return;
            _exit.Reset();
            _thread = new Thread(Loop) { IsBackground = true, Name = "五金PLC结果发布" };
            _thread.Start();
        }

        public void Stop()
        {
            _exit.Set();
            _wake.Set();

            // 必须先等线程真正退出再释放句柄：线程此刻正卡在 WaitOne 里，
            // 提前 Close 会抛 ObjectDisposedException（停止时必现，见运行堆栈）。
            Thread thread = _thread;
            _thread = null;
            if (thread != null && thread.IsAlive && !thread.Join(2000))
            {
                // 超时说明卡在某次写里；_exit 已置位，让它走完这一轮自己退出，
                // 句柄不关（留着让它安全返回），进程退出时一并回收
                Log4netRecord.ErrorLog.Warn("[五金PLC发布] 线程 2s 内未退出，跳过句柄释放");
                return;
            }

            _wake.Close();
            _exit.Close();
        }

        /// <summary>视觉侧入队后调用，缩短一次调度延迟。不调用也能跑，只是最坏晚 20ms。</summary>
        public void Notify()
        {
            _wake.Set();
        }

        private void Loop()
        {
            while (!_exit.WaitOne(0))
            {
                try { Pump(); }
                catch (Exception ex) { Log4netRecord.ErrorLog.Error("五金结果发布异常：" + ex); }

                _wake.WaitOne(20);
            }
        }

        private void Pump()
        {
            long now = Environment.TickCount;

            // 轮转起点每轮后移，避免总线的通道一直抢先
            for (int i = 0; i < _channels.Length; i++)
            {
                int index = (_cursor + i) % _channels.Length;
                PlcResultChannel channel = _channels[index];

                if (channel.IsPaused) continue;
                if (now < _retryAfter[index]) continue;

                PlcResultItem item;
                if (!channel.TryPeek(out item)) continue;

                if (_write(channel, item))
                {
                    channel.Complete();
                    _retryAfter[index] = 0;
                    continue;
                }

                int attempts = channel.RecordFailure();
                if (attempts >= MaxAttempts)
                {
                    channel.Pause();
                    _retryAfter[index] = 0;
                    五金模切告警.Raise(
                        "PLC发布暂停_" + channel.Name,
                        "PLC 结果发送失败",
                        $"{channel.Name} 帧 {item.FrameId} 连续 {attempts} 次失败，已暂停本通道。" +
                        $"队列积压 {channel.Count} 条。确认通讯恢复后点【重试】。");
                }
                else
                {
                    _retryAfter[index] = now + RetryIntervalMs;
                }
            }

            _cursor++;
        }

        public void Dispose()
        {
            // 释放全部收敛到 Stop：它已保证线程退出后才关句柄
            Stop();
        }
    }

    /// <summary>
    /// 发布器与 PLC 的接线。放在 partial 里，复用既有的 Plc / PlcWriteLock / Addr，
    /// 不另造一套写通道——否则又会出现第二个能写 PLC 的地方。
    /// </summary>
    public partial class Server_五金模切
    {
        private PlcResultPublisher _resultPublisher;

        private bool TryWriteResult(PlcResultChannel channel, PlcResultItem item)
        {
            var point = ReferenceEquals(channel, UpResultChannel) ? Addr?.UpLineScanResult
                      : ReferenceEquals(channel, DownResultChannel) ? Addr?.DownLineScanResult
                      : Addr?.SizeResult;
            if (point == null) return false;

            // 用局部快照：Plc 可能在判断与使用之间被重连置空
            var plc = Plc;
            if (plc == null || !plc.InitState) return false;

            // 与心跳共用同一把锁：单次写已被限到 300ms，不会跨过 1s 的心跳周期，
            // 因此不需要把心跳也搬进这里
            lock (PlcWriteLock)
            {
                point.Value = (short)item.Value;
                return plc.TryWriteWithoutAlarm(point, plc.CommunicationLog);
            }
        }

        public void StartResultPublisher()
        {
            if (_resultPublisher != null) return;            _resultPublisher = new PlcResultPublisher(AllResultChannels, TryWriteResult);
            _resultPublisher.Start();
        }

        public void StopResultPublisher()
        {
            PlcResultPublisher publisher = _resultPublisher;
            _resultPublisher = null;
            publisher?.Dispose();
        }

        /// <summary>
        /// 自动模式下确保流程在跑。
        ///
        /// 首次进主界面与复位后都调这一个方法，避免写两套开机逻辑。
        /// 仿真返回 false：仿真要人为开始，自动起会让人来不及观察单步。
        /// </summary>
        public bool EnsureAutoRunning()
        {
            if (Frame_Core.Project.Instance.Is_simulation) return false;
            if (Frame_Core.Project.Instance.Is_Run) return true;

            try
            {
                StartRun();
                LogTool.Log4netRecord.RunLog.Info("[五金自动] 已自动启动流程（自动模式无启停按钮）");
                return true;
            }
            catch (Exception ex)
            {
                LogTool.Log4netRecord.ErrorLog.Error("[五金自动] 自动启动失败", ex);
                return false;
            }
        }

        /// <summary>结论入队后唤醒发布器。入队失败说明队列满了，必须告警而不是静默丢弃。</summary>
        public void EnqueueResult(PlcResultChannel channel, int frameId, PlcResult value)
        {
            if (!channel.Enqueue(frameId, value))
            {
                五金模切告警.Raise(
                    "PLC结果队列溢出_" + channel.Name,
                    "结果队列溢出",
                    $"{channel.Name} 帧 {frameId} 未能入队：队列已满（{channel.Count}/{channel.Capacity}）。" +
                    "发布速度跟不上视觉产出，请检查通讯。");
                return;
            }

            _resultPublisher?.Notify();
        }
    }
}