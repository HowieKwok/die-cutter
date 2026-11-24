using System;
using System.Collections.Generic;

namespace Mef_VisionFrame
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
    ///
    /// 只取队首、成功才移除，是保序的唯一实现方式：
    /// 后一帧不能越过前一帧尚未发出的一条，否则 PLC 会把两片的结论错位。
    ///
    /// 满了拒绝新入队，不丢最老的一条——丢最老等于用一个更严重的错误
    /// （结论静默丢失）去修一个小问题（队列变长）。被拒时由调用方告警。
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

        /// <summary>暂停中时发布器不再提交，但入队继续——视觉不因 PLC 卡住而停。</summary>
        public bool IsPaused { get; private set; }

        /// <summary>因超容量被拒绝入队的条数。持续增长说明发布吞吐跟不上视觉产出。</summary>
        public int RejectedCount { get; private set; }

        public int Count
        {
            get { lock (_sync) { return _items.Count; } }
        }

        /// <summary>最近一次成功发布的帧号。0 表示还没有。状态板用。</summary>
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

        /// <summary>看队首，不移除。发布器用它重试，失败时队列内容保持不变。</summary>
        public bool TryPeek(out PlcResultItem item)
        {
            lock (_sync)
            {
                item = _items.Count > 0 ? _items.First.Value : null;
                return item != null;
            }
        }

        /// <summary>发布成功：记录完成帧号后移除队首。</summary>
        public void Complete()
        {
            lock (_sync)
            {
                if (_items.Count == 0) return;
                LastCompletedFrameId = _items.First.Value.FrameId;
                _items.RemoveFirst();
            }
        }

        /// <summary>记一次失败。只改队首的尝试次数，不改顺序。</summary>
        public int RecordFailure()
        {
            lock (_sync)
            {
                if (_items.Count == 0) return 0;
                _items.First.Value.Attempts++;
                return _items.First.Value.Attempts;
            }
        }

        /// <summary>累计失败到上限：停止本通道，队首保留，等操作员确认。</summary>
        public void Pause()
        {
            lock (_sync) { IsPaused = true; }
        }

        /// <summary>操作员确认恢复：尝试次数归零，从队首继续。</summary>
        public void Resume()
        {
            lock (_sync)
            {
                IsPaused = false;
                if (_items.Count > 0) _items.First.Value.Attempts = 0;
            }
        }

        /// <summary>停机/换卷：整条清空。停机后补发旧结论会把 PLC 状态改回 OK/NG。</summary>
        public void Clear()
        {
            lock (_sync)
            {
                _items.Clear();
                IsPaused = false;
                RejectedCount = 0;
            }
        }

        /// <summary>供界面读取的只读快照，避免外部持有内部结点。</summary>
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

    /// <summary>
    /// 五金的三个发布通道。只独立队列与状态机，不各开 socket：
    /// 五金只有一个 Server_五金模切PLC 实例，三条线程并发写同一连接会让报文交错。
    /// 真正的写入串行化由单写调度器负责（见实施第 6 步）。
    /// </summary>
    public partial class Server_五金模切
    {
        public static readonly PlcResultChannel UpResultChannel = new PlcResultChannel("上线扫结果");
        public static readonly PlcResultChannel DownResultChannel = new PlcResultChannel("下线扫结果");
        public static readonly PlcResultChannel SizeResultChannel = new PlcResultChannel("尺寸结果");

        public static readonly PlcResultChannel[] AllResultChannels =
        {
            UpResultChannel,
            DownResultChannel,
            SizeResultChannel,
        };
    }
}