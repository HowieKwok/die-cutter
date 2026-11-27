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
                message => Log4netRecord.ErrorLog.Error(message));
            _resultPublisher.Start();
        }

        public void StopResultPublisher()
        {
            PlcResultPublisher publisher = _resultPublisher;
            _resultPublisher = null;
            publisher?.Dispose();
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