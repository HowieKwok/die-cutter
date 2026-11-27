using Frame_Core;
using Frame_MetalRun;
using System;

namespace Mef_VisionFrame
{
    // PlcResult 已迁到 Frame_MetalRun（0 停止 / 1 OK / 2 NG / 3 待检）。
    /// <summary>
    /// 五金模切的 PLC 信号层：把"结论"翻译成"值 + 时机"，是唯一知道 1 是 OK 的地方。
    ///
    /// 与激光模切的关系：零。激光走控制卡脉冲 + PLC 请求中断，五金走 CIP 符号地址，
    /// 结果一片一写、不需要请求。两边的点位表、日志、状态机都不共用。
    ///
    /// 三条独立的时间线：
    ///   结果（0/1/2/3）  ：帧开始写 3，判定完写 1 或 2，停机/复位/换卷写 0；
    ///   多次NG（0/1）    ：满阈值写 1，2 秒后写 0，三路各自独立；
    ///   心跳（0/1）      ：通讯连上就每 1 秒翻转，与运行/停机无关。
    /// </summary>
    public partial class Server_五金模切
    {
        private static Server_五金模切PLC Plc
        {
            get { return Project.Instance.server_五金模切PLC; }
        }

        private static PLCAddress_五金模切 Addr
        {
            get
            {
                Server_五金模切PLC plc = Plc;
                return plc == null ? null : plc.Address;
            }
        }

        /// <summary>多次NG 的置位/复位值。与结果值的 1 同名不同义，单独给常量避免误读。</summary>
        private const short PulseHigh = 1;
        private const short PulseLow = 0;

        /// <summary>心跳翻转周期，点位表规定 1 秒。</summary>
        private const int HeartbeatPeriodMs = 1000;

        /// <summary>多次NG 的脉宽，点位表规定 2 秒后置 0。</summary>
        private const int NgPulseMs = 2000;

        /// <summary>
        /// PLC 读写不是线程安全的：上线/下线各在自己的相机线程写，心跳在通讯线程写。
        /// 所有写入口都过这把锁，避免两条线程半包交错把值写坏。
        /// </summary>
        private static readonly object PlcWriteLock = new object();

        /// <summary>
        /// 结果值的写入口。只做枚举到 short 的翻译，实际写入统一走 <see cref="WriteRaw"/>，
        /// 避免两份一模一样的锁与 InitState 判断各自演化。
        /// </summary>
        private static void WriteResult(CommunicationData<short> point, PlcResult value)
        {
            WriteRaw(point, (short)value);
        }

        /// <summary>
        /// 所有点位的唯一写入口：0/1 通道（心跳、多次NG）也用这里，不借结果枚举。
        /// </summary>
        private static void WriteRaw(CommunicationData<short> point, short value)
        {
            if (point == null) return;

            Server_五金模切PLC plc = Plc;
            if (plc == null || !plc.InitState) return;

            lock (PlcWriteLock)
            {
                point.Value = value;
                plc.Write(point, true, plc.CommunicationLog);
            }
        }

        #region 结果：帧开始写待检，判定完写 OK/NG

        #region 结果：入队，不直写

        /// <summary>
        /// 上线/下线本片外观结论入队。发布时机由发布器决定，视觉侧不再等 PLC。
        ///
        /// 两道闸门都必须保留：
        /// 1. 停机后不再入队——停机已把点位写回 0，再发一条会把 0 覆盖成 OK/NG；
        /// 2. 代际校验——停机/换卷瞬间已出队、随后才判完的帧属于旧代际，必须丢弃。
        ///
        /// 待检（Pending）不在这里投递：它在帧号分配时随 FrameAssigned 事件入队，
        /// 这样被去抖或帧号校验丢弃的帧不会留下多余的 Pending。
        /// </summary>
        public void EnqueueAppearanceResult(bool isUp, int frameId, bool isNG)
        {
            if (!Project.Instance.Is_Run) return;
            if (!Frame_Core.业务参数类.五金模切帧序号.IsLive(frameId)) return;

            EnqueueResult(isUp ? UpResultChannel : DownResultChannel,
                frameId,
                isNG ? PlcResult.Ng : PlcResult.Ok);
        }

        /// <summary>三路尺寸合成后的本片结论入队。与外观同理，过同样的两道闸门。</summary>
        public void EnqueueSizeResult(int frameId, bool isNG)
        {
            if (!Project.Instance.Is_Run) return;
            if (!Frame_Core.业务参数类.五金模切帧序号.IsLive(frameId)) return;

            EnqueueResult(SizeResultChannel, frameId, isNG ? PlcResult.Ng : PlcResult.Ok);
        }

        #endregion

        /// <summary>
        /// 停机/复位/换卷：三路结果回 0，多次NG 清回 0。
        /// 心跳不在其中——它只跟 PLC 通讯状态有关，停机也照常翻转，
        /// 否则 PLC 无法区分"机器停了"和"通讯断了"。
        /// </summary>
        public void ResetAllResults()
        {
            WriteResult(Addr?.UpLineScanResult, PlcResult.Stopped);
            WriteResult(Addr?.DownLineScanResult, PlcResult.Stopped);
            WriteResult(Addr?.SizeResult, PlcResult.Stopped);

            _upNgChannel?.Clear();
            _downNgChannel?.Clear();
            _sizeNgChannel?.Clear();
        }

        #endregion

        #region 多次NG：置 1，2 秒后置 0

        /// <summary>一个多次NG 信号 = 点位 + 到期时间。三个信号共用同一份逻辑，只是点位不同。</summary>
        private sealed class PulseChannel
        {
            private readonly CommunicationData<short> point;
            private DateTime? expireAt;

            public PulseChannel(CommunicationData<short> point)
            {
                this.point = point;
            }

            public void Trigger()
            {
                if (point == null) return;
                WriteRaw(point, PulseHigh);
                expireAt = DateTime.Now.AddMilliseconds(NgPulseMs);
            }

            public void Tick()
            {
                if (point == null || !expireAt.HasValue) return;
                if (DateTime.Now < expireAt.Value) return;

                WriteRaw(point, PulseLow);
                expireAt = null;
            }

            public void Clear()
            {
                expireAt = null;
                if (point != null) WriteRaw(point, PulseLow);
            }
        }

        private PulseChannel _upNgChannel;
        private PulseChannel _downNgChannel;
        private PulseChannel _sizeNgChannel;

        /// <summary>把三个信号绑到当前机型点位表。PLC 未创建时点位为 null，通道内部全部走空判断。</summary>
        private void InitSignalChannels()
        {
            PLCAddress_五金模切 addr = Addr;

            _upNgChannel = new PulseChannel(addr == null ? null : addr.UpLineScanNgOverTimes);
            _downNgChannel = new PulseChannel(addr == null ? null : addr.DownLineScanNgOverTimes);
            _sizeNgChannel = new PulseChannel(addr == null ? null : addr.SizeNgOverTimes);
        }

        /// <summary>上线扫满阈值：上线自己的那一路，不受下线影响。</summary>
        public void TriggerUpNg()
        {
            _upNgChannel?.Trigger();
        }

        /// <summary>下线扫满阈值。</summary>
        public void TriggerDownNg()
        {
            _downNgChannel?.Trigger();
        }

        /// <summary>尺寸满阈值。</summary>
        public void TriggerSizeNg()
        {
            _sizeNgChannel?.Trigger();
        }

        /// <summary>
        /// 连续NG 归属哪一路：外观分上线/下线两个独立点位（按相机分），
        /// 尺寸三路共用一个点位。
        /// </summary>
        private void TriggerNgSignal(bool isAppearanceStation, int camIndex)
        {
            if (!isAppearanceStation)
            {
                TriggerSizeNg();
                return;
            }

            int upCamIndex;
            bool isUp = Project.Instance.baseProDevice.TryGetCamIndex(CamRole.模切主相机, 0, out upCamIndex)
                          && upCamIndex == camIndex;

            if (isUp) TriggerUpNg();
            else TriggerDownNg();
        }

        #endregion

        #region 心跳

        private DateTime _nextHeartbeat;
        private short _heartbeatValue;

        private void HeartbeatTick()
        {
            // 心跳只表达"PLC 通讯是活的"，与运行/停机无关：连上就一直翻转，断线就停。
            // 翻转放在通讯检查之后，断线期间相位不空转，重连后不会补发一串值。
            Server_五金模切PLC plc = Plc;
            if (plc == null || !plc.InitState) return;

            if (DateTime.Now < _nextHeartbeat) return;

            _heartbeatValue = (short)(_heartbeatValue == 0 ? 1 : 0);
            WriteRaw(Addr?.Heartbeat, _heartbeatValue);
            _nextHeartbeat = DateTime.Now.AddMilliseconds(HeartbeatPeriodMs);
        }

        #endregion

        #region 连接复位

        /// <summary>上一次看到的 PLC 连接状态。null = 还没看过，首次连上也要复位一次。</summary>
        private bool? _lastPlcConnected;

        /// <summary>
        /// PLC 从"未连接"变成"已连接"的瞬间，把三路结果清回停止态。
        ///
        /// 为什么需要这层：停机写 0 依赖 InitState，断线那一刻写不进去；软件重启后
        /// PLC 里还留着上一片的 1/2/3。连上瞬间补一次 0，让"软件起来"与"点位归零"是同一时刻。
        /// 心跳不在这里清——它只要连上就自己开始翻转。
        /// </summary>
        private void ResetOnConnectEdge()
        {
            Server_五金模切PLC plc = Plc;
            bool connected = plc != null && plc.InitState;

            bool wasConnected = _lastPlcConnected ?? false;
            _lastPlcConnected = connected;

            if (connected && !wasConnected) ResetAllResults();
        }

        #endregion

        /// <summary>通讯线程入口：所有定时推进都在这里，5ms 一次。</summary>
        public override void Communication()
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                ResetOnConnectEdge();
                HeartbeatTick();
                _upNgChannel?.Tick();
                _downNgChannel?.Tick();
                _sizeNgChannel?.Tick();
            });
        }

        /// <summary>
        /// 配方切换回调。五金没有脉宽需要下发（多次NG 的 2 秒、心跳的 1 秒都是点位表规定的固定值），
        /// 这里只重建通道，保证换机型/换点位后绑的是新表。
        /// </summary>
        public override void Udpate()
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                InitSignalChannels();
            });
        }
    }
}