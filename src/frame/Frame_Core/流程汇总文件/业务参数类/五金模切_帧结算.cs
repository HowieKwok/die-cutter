using System;
using System.Collections.Generic;

namespace Frame_Core.业务参数类
{
    /// <summary>帧的终态。每帧在每个代际内只能从 Open 迁移一次。</summary>
    public enum FrameSettlementState
    {
        Open = 0,
        Ok = 1,
        Ng = 2,
        /// <summary>人工复检：既不是 OK 也不是 NG，需要人工确认。</summary>
        Review = 3,
    }

    /// <summary>
    /// 三态结论的合成规则。
    /// 优先级 NG &gt; 待检 &gt; OK；枚举数值只用于排序，业务比较一律走 Rank/Combine。
    /// </summary>
    public static class FrameSettlementRules
    {
        public static int Rank(FrameSettlementState state)
        {
            switch (state)
            {
                case FrameSettlementState.Ng: return 3;
                case FrameSettlementState.Review: return 2;
                case FrameSettlementState.Ok: return 1;
                default: return 0;
            }
        }

        public static bool IsTerminal(FrameSettlementState state)
        {
            return state != FrameSettlementState.Open;
        }

        /// <summary>两项结论合成：取优先级更高者；Open 表示未结算，不参与合成。</summary>
        public static FrameSettlementState Combine(FrameSettlementState left, FrameSettlementState right)
        {
            if (!IsTerminal(left)) return right;
            if (!IsTerminal(right)) return left;
            return Rank(left) >= Rank(right) ? left : right;
        }

        /// <summary>按布尔标志生成终态：NG 优先，其次待检，最后 OK。</summary>
        public static FrameSettlementState FromFlags(bool isNg, bool isReview)
        {
            if (isNg) return FrameSettlementState.Ng;
            if (isReview) return FrameSettlementState.Review;
            return FrameSettlementState.Ok;
        }
    }

    /// <summary>谁给出的结论。只用于日志与排查，不改变判定结果。</summary>
    public enum SettlementSource
    {
        /// <summary>尺寸三路合算后的工位结论，唯一允许出 OK 的尺寸来源。</summary>
        Group = 0,
        /// <summary>尺寸单路失败或放弃。</summary>
        Route = 1,
        /// <summary>外观 EA 正常判定。</summary>
        Appearance = 2,
        /// <summary>裁图流程异常。</summary>
        CropFlow = 3,
        /// <summary>EA 流程异常。</summary>
        EaFlow = 4,
        /// <summary>Server 侧判定、落盘或显示异常。</summary>
        ServerJudge = 5,
    }

    /// <summary>
    /// 帧结算守卫：保证一帧恰好一条结论。
    ///
    /// 为什么不能靠结果队列判重：队列是副作用容器，结论一旦被发布器取走，
    /// 状态就没了，迟到的异常会补出第二条，把 PLC 后面的片全部错位。
    /// 这里按 (工位, 帧号) 记录代际与终态，先认领者赢，后到的一律拒绝。
    ///
    /// 锁内只做字典操作：入队、告警、事件、日志都由调用方在锁外完成，
    /// 避免把发布器或界面线程拖进这把锁。
    /// </summary>
    public static class FrameSettlement
    {
        private struct FrameKey : IEquatable<FrameKey>
        {
            public readonly 五金模切帧序号.FrameStation Station;
            public readonly int FrameId;

            public FrameKey(五金模切帧序号.FrameStation station, int frameId)
            {
                Station = station;
                FrameId = frameId;
            }

            public bool Equals(FrameKey other)
            {
                return Station == other.Station && FrameId == other.FrameId;
            }

            public override bool Equals(object obj)
            {
                return obj is FrameKey && Equals((FrameKey)obj);
            }

            public override int GetHashCode()
            {
                return ((int)Station * 397) ^ FrameId;
            }
        }

        private sealed class Entry
        {
            public int Generation;
            public FrameSettlementState State;
            public SettlementSource Source;
            public string Reason;
        }

        private static readonly object _sync = new object();
        private static readonly Dictionary<FrameKey, Entry> _entries = new Dictionary<FrameKey, Entry>();
        private static int _generation;

        public static int Generation
        {
            get { lock (_sync) { return _generation; } }
        }

        /// <summary>还没结算的帧数。结束膜卷确认、巡检用。</summary>
        public static int OpenCount
        {
            get
            {
                lock (_sync)
                {
                    int count = 0;
                    foreach (Entry entry in _entries.Values)
                    {
                        if (entry.State == FrameSettlementState.Open) count++;
                    }
                    return count;
                }
            }
        }

        /// <summary>停机、换卷、复位：代际 +1 并清表。旧代际的迟到结算会被拒绝。</summary>
        public static void NewGeneration()
        {
            lock (_sync)
            {
                _generation++;
                _entries.Clear();
            }
        }

        /// <summary>
        /// 帧号分配时登记。尺寸三路共用一个帧号，重复登记是幂等的。
        /// 返回 false 表示帧号非法（0 或负数），该帧不应进入任何结算。
        /// </summary>
        public static bool Begin(int frameId, 五金模切帧序号.FrameStation station)
        {
            if (frameId <= 0) return false;

            lock (_sync)
            {
                FrameKey key = new FrameKey(station, frameId);
                Entry entry;
                if (_entries.TryGetValue(key, out entry))
                {
                    // 同代际重复登记：尺寸三路共用帧号，属正常
                    return entry.Generation == _generation;
                }

                _entries[key] = new Entry
                {
                    Generation = _generation,
                    State = FrameSettlementState.Open,
                };
                return true;
            }
        }

        /// <summary>
        /// 认领本帧的终态。返回 true 表示本次调用是这帧唯一的结算者。
        /// 返回 false 的三种情况：帧号非法、帧未在当前代际登记、已被别人结算。
        /// 调用方应在锁外记日志。
        /// </summary>
        public static bool TryClaim(int frameId, 五金模切帧序号.FrameStation station, bool isNg,
                                    SettlementSource source, string reason)
        {
            return TryClaim(frameId, station,
                isNg ? FrameSettlementState.Ng : FrameSettlementState.Ok,
                source, reason);
        }

        /// <summary>
        /// 三态认领。verdict 必须是 Ok/Ng/Review；Open 表示没有结论，直接拒绝。
        /// 与 bool 重载共用同一把锁与同一套代际规则，一帧仍然只能有一个终态。
        /// </summary>
        public static bool TryClaim(int frameId, 五金模切帧序号.FrameStation station,
                                    FrameSettlementState verdict,
                                    SettlementSource source, string reason)
        {
            if (frameId <= 0) return false;
            if (!FrameSettlementRules.IsTerminal(verdict)) return false;

            lock (_sync)
            {
                Entry entry;
                if (!_entries.TryGetValue(new FrameKey(station, frameId), out entry)) return false;
                if (entry.Generation != _generation) return false;
                if (entry.State != FrameSettlementState.Open) return false;

                entry.State = verdict;
                entry.Source = source;
                entry.Reason = reason;
                return true;
            }
        }

        /// <summary>
        /// 入队失败（队列满）时把认领回滚成 Open，否则 PLC 会永远停在待检。
        /// 只回滚刚认领的那一帧，已被其它路径再次认领时不动。
        /// </summary>
        public static bool Reopen(int frameId, 五金模切帧序号.FrameStation station)
        {
            if (frameId <= 0) return false;

            lock (_sync)
            {
                Entry entry;
                if (!_entries.TryGetValue(new FrameKey(station, frameId), out entry)) return false;
                if (entry.Generation != _generation) return false;
                if (entry.State == FrameSettlementState.Open) return false;

                entry.State = FrameSettlementState.Open;
                entry.Source = default(SettlementSource);
                entry.Reason = null;
                return true;
            }
        }

        /// <summary>查询帧当前状态。未登记返回 Open。</summary>
        public static FrameSettlementState Query(int frameId, 五金模切帧序号.FrameStation station)
        {
            lock (_sync)
            {
                Entry entry;
                if (!_entries.TryGetValue(new FrameKey(station, frameId), out entry)) return FrameSettlementState.Open;
                if (entry.Generation != _generation) return FrameSettlementState.Open;
                return entry.State;
            }
        }
    }
}
