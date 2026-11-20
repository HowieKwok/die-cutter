using System;
using System.Diagnostics;
using System.Threading;

namespace Frame_Simulation.Capture
{
    /// <summary>
    /// 节拍源：把"产能"翻译成"取下一张图的延时"，并负责等待。
    /// 单独成类是为了让 SimCamera 只关心"取完一张要等多久"，
    /// 而"产能怎么变成延时、运行时能否改"这两件事留在配置侧。
    /// </summary>
    public class FramePacer
    {
        private readonly object _sync = new object();
        private int _intervalMs;

        /// <summary>当前帧间隔（毫秒）。最小值 10ms，防止产能填大后空转。</summary>
        public int IntervalMs
        {
            get { lock (_sync) { return _intervalMs; } }
            set
            {
                if (value < 10) value = 10;
                lock (_sync) { _intervalMs = value; }
            }
        }

        /// <summary>产能（片/分）。读写都走换算，外部只关心产能这一个概念。</summary>
        public int CapacityPerMinute
        {
            get
            {
                int interval = IntervalMs;
                return interval <= 0 ? 0 : 60000 / interval;
            }
            set
            {
                IntervalMs = value <= 0 ? 200 : 60000 / value;
            }
        }

        public FramePacer(int capacityPerMinute)
        {
            CapacityPerMinute = capacityPerMinute;
        }

        /// <summary>
        /// 等待下一拍。返回 false 表示等待期间被要求停止，调用方应结束采集线程。
        /// 用 WaitHandle 而不是 Thread.Sleep：产能调大后能立刻响应，不用等上一拍睡完。
        /// </summary>
        public bool WaitNextFrame(ManualResetEvent stopEvent)
        {
            int timeout;
            lock (_sync) { timeout = _intervalMs; }

            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                int remain = timeout - (int)sw.ElapsedMilliseconds;
                if (remain <= 0) return true;

                if (stopEvent.WaitOne(remain)) return false;

                // 等待期间产能被调整：按新间隔重新算剩余时间，避免手感迟钝。
                int latest;
                lock (_sync) { latest = _intervalMs; }
                if (latest != timeout)
                {
                    timeout = latest;
                    sw.Restart();
                }
            }
        }
    }
}