using System;
using System.Collections.Generic;

namespace Frame_Simulation.Log
{
    public enum SimRecordChannel
    {
        Camera = 0,
        Plc = 1
    }

    public enum SimRecordKind
    {
        Info = 0,
        Write = 1,
        Read = 2,
        Warning = 3,
        Error = 4
    }

    public class SimRecordItem
    {
        public DateTime Time { get; set; }
        public SimRecordChannel Channel { get; set; }
        public SimRecordKind Kind { get; set; }
        /// <summary>相机名 / PLC 点位名，例如 "模切主相机"、"[D1004]"。</summary>
        public string Source { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            return string.Format("{0:HH:mm:ss.fff} [{1}] {2} {3}", Time,
                Channel == SimRecordChannel.Camera ? "CAM" : "PLC", Source, Message);
        }
    }

    /// <summary>
    /// 仿真记录：相机与 PLC 两个独立通道，内存环形缓冲。
    /// 只做观察，不参与任何控制逻辑——仿真界面只是它的一个消费者。
    /// </summary>
    public class SimRecorder
    {
        private readonly object _sync = new object();
        private readonly LinkedList<SimRecordItem> _items = new LinkedList<SimRecordItem>();
        private int _capacity;

        /// <summary>有新记录时触发（可能在采集线程上触发，界面需自行切回 UI 线程）。</summary>
        public event Action<SimRecordItem> Recorded;

        public SimRecorder(int capacity = 5000)
        {
            _capacity = capacity < 100 ? 100 : capacity;
        }

        public int Capacity
        {
            get { lock (_sync) { return _capacity; } }
            set
            {
                lock (_sync)
                {
                    _capacity = value < 100 ? 100 : value;
                    Trim();
                }
            }
        }

        public void Add(SimRecordChannel channel, SimRecordKind kind, string source, string message)
        {
            SimRecordItem item = new SimRecordItem
            {
                Time = DateTime.Now,
                Channel = channel,
                Kind = kind,
                Source = source,
                Message = message
            };

            lock (_sync)
            {
                _items.AddLast(item);
                Trim();
            }

            Action<SimRecordItem> handler = Recorded;
            if (handler != null)
            {
                try { handler(item); }
                catch { /* 记录是旁路，不能因为界面异常拖垮采集线程 */ }
            }
        }

        public void Cam(SimRecordKind kind, string source, string message)
        {
            Add(SimRecordChannel.Camera, kind, source, message);
        }

        public void Plc(SimRecordKind kind, string source, string message)
        {
            Add(SimRecordChannel.Plc, kind, source, message);
        }

        public List<SimRecordItem> Snapshot(SimRecordChannel? channel = null, int maxCount = 500)
        {
            List<SimRecordItem> list = new List<SimRecordItem>();
            lock (_sync)
            {
                LinkedListNode<SimRecordItem> node = _items.Last;
                while (node != null && list.Count < maxCount)
                {
                    if (!channel.HasValue || node.Value.Channel == channel.Value) list.Add(node.Value);
                    node = node.Previous;
                }
            }
            list.Reverse();   // 时间正序
            return list;
        }

        public void Clear()
        {
            lock (_sync) { _items.Clear(); }
        }

        private void Trim()
        {
            while (_items.Count > _capacity) _items.RemoveFirst();
        }
    }
}