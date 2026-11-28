using Frame_Core;
using Frame_Core.业务类;
using Frame_MetalRun;
using LogTool;
using System;
using System.Threading;

namespace Mef_VisionFrame
{
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
            if (_resultPublisher != null) return;

            _resultPublisher = new PlcResultPublisher(
                AllResultChannels,
                TryWriteResult,
                (key, title, message) => 五金模切告警.Raise(key, title, message),
                message => AppLog.Error(message));
            _resultPublisher.Start();
        }

        public void StopResultPublisher()
        {
            PlcResultPublisher publisher = _resultPublisher;
            _resultPublisher = null;
            publisher?.Dispose();
        }

        /// <summary>
        /// 唯一结算入口：先向帧结算守卫认领终态，认领成功才允许入队。
        ///
        /// 所有结论（正常 OK/NG、异常补 NG、放弃补 NG）都必须经过这里，
        /// 这样「一帧恰好一条结论」由结构保证，而不是靠每个调用点自觉判重。
        /// 返回 false 表示本帧已结算、已停机或帧号不属于当前代际。
        /// </summary>
        public bool SettleAndEnqueue(int frameId,
            Frame_Core.业务参数类.五金模切帧序号.FrameStation station,
            bool isNG,
            Frame_Core.业务参数类.SettlementSource source,
            string reason)
        {
            return SettleAndEnqueue(frameId, station,
                isNG ? Frame_Core.业务参数类.FrameSettlementState.Ng
                     : Frame_Core.业务参数类.FrameSettlementState.Ok,
                source, reason);
        }

        /// <summary>
        /// 三态结算入口。这是唯一允许把 PlcResult.Review 写入结果队列的地方：
        /// 帧号分配阶段不再写 3，结果点位只在判定完成后写一次。
        /// </summary>
        public bool SettleAndEnqueue(int frameId,
            Frame_Core.业务参数类.五金模切帧序号.FrameStation station,
            Frame_Core.业务参数类.FrameSettlementState verdict,
            Frame_Core.业务参数类.SettlementSource source,
            string reason)
        {
            if (frameId <= 0) return false;

            // 停机后不再入队：停机已把点位写回 0，再发一条会把 0 覆盖成结果
            if (!Project.Instance.Is_Run) return false;
            if (!Frame_Core.业务参数类.五金模切帧序号.IsLive(frameId)) return false;

            if (!Frame_Core.业务参数类.FrameSettlement.TryClaim(frameId, station, verdict, source, reason))
            {
                AppLog.Warn(
                    $"[五金帧结算] 帧 {frameId} {station} 已有结论，忽略本次 {source}：{reason}");
                return false;
            }

            PlcResultChannel channel =
                station == Frame_Core.业务参数类.五金模切帧序号.FrameStation.上线扫 ? UpResultChannel :
                station == Frame_Core.业务参数类.五金模切帧序号.FrameStation.下线扫 ? DownResultChannel :
                SizeResultChannel;

            if (!EnqueueResult(channel, frameId, ToPlcResult(verdict)))
            {
                // 队列满时回滚认领，否则守卫认为已结算、PLC 永远停在等待
                Frame_Core.业务参数类.FrameSettlement.Reopen(frameId, station);
                return false;
            }

            return true;
        }

        /// <summary>结算三态映射到 PLC 结果值。Open 不是结论，不允许发布。</summary>
        private static PlcResult ToPlcResult(Frame_Core.业务参数类.FrameSettlementState verdict)
        {
            switch (verdict)
            {
                case Frame_Core.业务参数类.FrameSettlementState.Ng: return PlcResult.Ng;
                case Frame_Core.业务参数类.FrameSettlementState.Review: return PlcResult.Review;
                case Frame_Core.业务参数类.FrameSettlementState.Ok: return PlcResult.Ok;
                default:
                    AppLog.Error($"[五金帧结算] 非法结论 {verdict}，按 NG 回传，避免 PLC 停在等待。");
                    return PlcResult.Ng;
            }
        }

        /// <summary>结论入队后唤醒发布器。入队失败说明队列满了，必须告警而不是静默丢弃。</summary>
        public bool EnqueueResult(PlcResultChannel channel, int frameId, PlcResult value)
        {
            if (!channel.Enqueue(frameId, value))
            {
                五金模切告警.Raise(
                    "PLC结果队列溢出_" + channel.Name,
                    "结果队列溢出",
                    $"{channel.Name} 帧 {frameId} 未能入队：队列已满（{channel.Count}/{channel.Capacity}）。" +
                    "发布速度跟不上视觉产出，请检查通讯。");
                return false;
            }

            _resultPublisher?.Notify();
            return true;
        }
    }
}