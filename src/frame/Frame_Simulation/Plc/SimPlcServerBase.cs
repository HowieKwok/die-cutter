using System;
using System.Collections.Generic;
using System.Text;
using HslCommunication;
using HslCommunication.Core.Device;
using HslCommunication.Core.Net;
using Frame_Simulation.Config;
using Frame_Simulation.Log;

namespace Frame_Simulation.Plc
{
    /// <summary>
    /// 仿真 PLC 服务端的公共部分：生命周期、写入观察、只看不答。
    ///
    /// 为什么"只看不答"：激光与五金两个机型对 PLC 都是只写不读（激光写纠偏值/心跳 D1004，
    /// 五金写三路结果与心跳）。所以仿真侧不需要构造任何读请求的应答逻辑，
    /// 只要把服务端跑起来、把客户端的写入记录下来即可。
    /// </summary>
    public abstract class SimPlcServerBase : IDisposable
    {
        private readonly object _sync = new object();
        private readonly List<string> _watch = new List<string>();
        private readonly Dictionary<string, int> _lastValue = new Dictionary<string, int>();
        private long _receivedCount;

        /// <summary>
        /// 累计收包次数。仿真界面的心跳点用它驱动闪烁：计数变化即"通讯还活着"。
        /// 在 OnDataReceived 里自增，不加锁——界面只关心"有没有变"，不关心精确值。
        /// </summary>
        public long ReceivedCount
        {
            get { return System.Threading.Interlocked.Read(ref _receivedCount); }
        }

        /// <summary>最近一次收包时刻；从未收到过时为默认值。</summary>
        public DateTime LastReceivedAt { get; private set; }

        protected DeviceServer Server;
        protected readonly SimulationConfig Config;
        protected readonly SimRecorder Recorder;
        protected int _port;

        /// <summary>服务端是否已启动。启动体检与界面状态都读这个。</summary>
        public bool IsStarted
        {
            get { lock (_sync) { return Server != null && Server.IsStarted; } }
        }

        public int Port
        {
            get { lock (_sync) { return _port; } }
        }

        protected SimPlcServerBase(SimulationConfig config, SimRecorder recorder)
        {
            Config = config ?? new SimulationConfig();
            Recorder = recorder;
        }

        /// <summary>子类返回具体协议的服务端实例（Fins / Cip）。</summary>
        protected abstract DeviceServer CreateServer();

        /// <summary>子类在启动前注册符号/点位（CIP 必须，FINS 可选）。</summary>
        protected virtual void OnBeforeStart() { }

        /// <summary>运行期需要跟踪的地址（只用于记录，不影响应答）。</summary>
        protected void Watch(params string[] addresses)
        {
            if (addresses == null) return;
            foreach (string addr in addresses)
            {
                if (!string.IsNullOrWhiteSpace(addr) && !_watch.Contains(addr)) _watch.Add(addr);
            }
        }

        public bool Start(int port)
        {
            lock (_sync)
            {
                if (Server != null && Server.IsStarted) return true;

                try
                {
                    Server = CreateServer();
                    if (Server == null)
                    {
                        Record(SimRecordKind.Error, "创建 PLC 服务端失败");
                        return false;
                    }

                    Server.EnableWrite = true;      // 不打开的话客户端写会被丢弃
                    OnBeforeStart();
                    Server.OnDataReceived += OnDataReceived;
                    // 第二个参数是 modeTcp：传 false 会起 UDP 监听，而 OmronFinsNet / OmronCipNet
                    // 都是 TCP 客户端，现场表现就是"服务端已启动"但客户端连接被拒
                    // （Socket Connect 127.0.0.1:端口 -> 目标计算机积极拒绝）。
                    Server.ServerStart(port, true);

                    _port = port;
                    Record(SimRecordKind.Info, "服务端已启动，监听端口 " + port);
                    return Server.IsStarted;
                }
                catch (Exception ex)
                {
                    Record(SimRecordKind.Error, "服务端启动异常：" + ex.Message);
                    return false;
                }
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (Server == null) return;
                try { Server.ServerClose(); }
                catch (Exception ex) { Record(SimRecordKind.Warning, "关闭服务端异常：" + ex.Message); }

                try { Server.OnDataReceived -= OnDataReceived; } catch { }
                Server = null;
                Record(SimRecordKind.Info, "服务端已停止");
            }
        }

        private void OnDataReceived(object sender, PipeSession session, byte[] data)
        {
            System.Threading.Interlocked.Increment(ref _receivedCount);
            LastReceivedAt = DateTime.Now;

            string hex = data == null ? "" : BitConverter.ToString(data).Replace("-", " ");
            Record(SimRecordKind.Write, string.Format("收包 {0} 字节：{1}", data == null ? 0 : data.Length, hex));
            RecordWatchedValues();
        }

        /// <summary>
        /// 把关注地址的当前值记下来。只在收到写入时读一次，避免后台轮询干扰真实时序。
        /// 读不到（未写过/地址非法）不报错——仿真里"没写过"本身就是合法状态。
        /// </summary>
        private void RecordWatchedValues()
        {
            if (_watch.Count == 0 || Server == null) return;
            foreach (string addr in _watch)
            {
                try
                {
                    OperateResult<short> r = Server.ReadInt16(addr);
                    if (!r.IsSuccess) continue;

                    int old;
                    if (_lastValue.TryGetValue(addr, out old) && old == r.Content) continue;

                    _lastValue[addr] = r.Content;
                    Record(SimRecordKind.Read, string.Format("{0} = {1}", addr, r.Content));
                }
                catch { }
            }
        }

        protected void Record(SimRecordKind kind, string message)
        {
            if (Recorder != null) Recorder.Plc(kind, "[" + _port + "]", message);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}