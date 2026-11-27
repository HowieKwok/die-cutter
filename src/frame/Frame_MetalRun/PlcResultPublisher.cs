using System;
using System.Threading;

namespace Frame_MetalRun
{
    /// <summary>
    /// 三条结果通道的发布器：单线程串行提交，视觉侧完全不等待它。
    /// 失败累计到上限只暂停该通道，队首保留；暂停/继续由告警窗触发。
    ///
    /// 该类型放在独立模块里，日志与告警通过委托注入，因此可以脱离主界面测试。
    /// </summary>
    public sealed class PlcResultPublisher : IDisposable
    {
        public const int MaxAttempts = 3;

        private const int RetryIntervalMs = 300;

        private readonly PlcResultChannel[] _channels;
        private readonly Func<PlcResultChannel, PlcResultItem, bool> _write;
        private readonly Action<string, string, string> _raiseAlert;
        private readonly Action<string> _log;
        private readonly long[] _retryAfter;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly ManualResetEvent _exit = new ManualResetEvent(false);

        private Thread _thread;
        private int _cursor;

        public PlcResultPublisher(
            PlcResultChannel[] channels,
            Func<PlcResultChannel, PlcResultItem, bool> write,
            Action<string, string, string> raiseAlert = null,
            Action<string> log = null)
        {
            if (channels == null) throw new ArgumentNullException("channels");
            if (write == null) throw new ArgumentNullException("write");

            _channels = channels;
            _write = write;
            _raiseAlert = raiseAlert;
            _log = log;
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

            Thread thread = _thread;
            _thread = null;
            if (thread != null && thread.IsAlive && !thread.Join(2000))
            {
                Log("[五金PLC发布] 线程 2s 内未退出，跳过句柄释放");
                return;
            }

            try { _wake.Close(); } catch { }
            try { _exit.Close(); } catch { }
        }

        public void Notify()
        {
            try { _wake.Set(); } catch (ObjectDisposedException) { }
        }

        private void Loop()
        {
            while (!_exit.WaitOne(0))
            {
                try { Pump(); }
                catch (Exception ex) { Log("五金结果发布异常：" + ex); }

                _wake.WaitOne(20);
            }
        }

        private void Pump()
        {
            long now = Environment.TickCount;

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
                    RaiseAlert(
                        "PLC发布暂停_" + channel.Name,
                        "PLC 结果发送失败",
                        string.Format("{0} 帧 {1} 连续 {2} 次失败，已暂停本通道。队列积压 {3} 条。确认通讯恢复后点【重试】。",
                            channel.Name, item.FrameId, attempts, channel.Count));
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
            Stop();
        }

        private void RaiseAlert(string key, string title, string message)
        {
            Action<string, string, string> handler = _raiseAlert;
            if (handler == null) return;
            try { handler(key, title, message); } catch { }
        }

        private void Log(string message)
        {
            Action<string> handler = _log;
            if (handler == null) return;
            try { handler(message); } catch { }
        }
    }
}
