using System;
using System.Collections.Generic;

namespace Frame_MetalRun
{
    /// <summary>一条待发布的结论。Attempts 由发布器累加，队列本身不改它。</summary>
    public sealed class PlcResultItem
    {
        public int FrameId;
        public PlcResult Value;
        public int Attempts;
        public DateTime CreatedAt;
    }

    /// <summary>
    /// 单通道结果队列：视觉流程入队，发布器取队首。
    /// 只取队首、成功才移除；满了拒绝新入队，不丢最老的一条。
    /// </summary>
    public sealed class PlcResultChannel
    {
        private readonly object _sync = new object();
        private readonly LinkedList<PlcResultItem> _items = new LinkedList<PlcResultItem>();

        public PlcResultChannel(string name, int capacity = 64)
        {
            Name = name;
            Capacity = capacity <= 0 ? 64 : capacity;
        }

        public string Name { get; private set; }
        public int Capacity { get; private set; }

        /// <summary>PLC 发布通道故障暂停；与运行暂停（MetalRunState.Paused）无关。</summary>
        public bool IsPaused { get; private set; }

        public int RejectedCount { get; private set; }

        public int Count
        {
            get { lock (_sync) { return _items.Count; } }
        }

        public int LastCompletedFrameId { get; private set; }

        public bool Enqueue(int frameId, PlcResult value)
        {
            lock (_sync)
            {
                if (_items.Count >= Capacity)
                {
                    RejectedCount++;
                    return false;
                }

                _items.AddLast(new PlcResultItem
                {
                    FrameId = frameId,
                    Value = value,
                    CreatedAt = DateTime.Now,
                });
                return true;
            }
        }

        public bool TryPeek(out PlcResultItem item)
        {
            lock (_sync)
            {
                item = _items.Count > 0 ? _items.First.Value : null;
                return item != null;
            }
        }

        public void Complete()
        {
            lock (_sync)
            {
                if (_items.Count == 0) return;
                LastCompletedFrameId = _items.First.Value.FrameId;
                _items.RemoveFirst();
            }
        }

        public int RecordFailure()
        {
            lock (_sync)
            {
                if (_items.Count == 0) return 0;
                _items.First.Value.Attempts++;
                return _items.First.Value.Attempts;
            }
        }

        public void Pause()
        {
            lock (_sync) { IsPaused = true; }
        }

        public void Resume()
        {
            lock (_sync)
            {
                IsPaused = false;
                if (_items.Count > 0) _items.First.Value.Attempts = 0;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _items.Clear();
                IsPaused = false;
                RejectedCount = 0;
            }
        }

        public PlcResultItem[] Snapshot()
        {
            lock (_sync)
            {
                var buffer = new PlcResultItem[_items.Count];
                _items.CopyTo(buffer, 0);
                return buffer;
            }
        }
    }
}
