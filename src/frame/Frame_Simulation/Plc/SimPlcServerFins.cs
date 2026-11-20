using System;
using HslCommunication.Core.Device;
using HslCommunication.Profinet.Omron;
using Frame_Simulation.Config;
using Frame_Simulation.Log;

namespace Frame_Simulation.Plc
{
    /// <summary>
    /// 激光模切用的 FINS 服务端。
    /// 关注点位来自 PLCAddress_DieCuttingCut：D1004 心跳、D2000/D2002 纠偏值、D2004 有无极耳、D2208 寻极耳中。
    /// 服务端不主动产生心跳——心跳由应用程序侧（控制卡 IO 脉冲）写进来，仿真在这里只做"接住并记录"。
    /// </summary>
    public class SimPlcServerFins : SimPlcServerBase
    {
        public SimPlcServerFins(SimulationConfig config, SimRecorder recorder)
            : base(config, recorder)
        {
        }

        protected override DeviceServer CreateServer()
        {
            return new OmronFinsServer();
        }

        protected override void OnBeforeStart()
        {
            Watch("D1004", "D2000", "D2002", "D2004", "D2208", "D1002");
        }

        /// <summary>
        /// 预置一份"空值表"，让客户端启动阶段的读值不会拿到错误。
        /// FINS 的写地址会自己落到数据池，这里只是把常见点位初始化为 0。
        /// </summary>
        public void SeedDefaultValues()
        {
            if (Server == null) return;
            string[] zeroAddr = { "D1004", "D2000", "D2002", "D2004", "D2208", "D1002" };
            foreach (string addr in zeroAddr)
            {
                try { Server.Write(addr, (short)0); } catch (Exception) { }
            }
        }
    }
}