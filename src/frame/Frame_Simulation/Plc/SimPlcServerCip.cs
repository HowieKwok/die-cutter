using System;
using System.Collections.Generic;
using HslCommunication.Core.Device;
using HslCommunication.Profinet.Omron;
using Frame_Simulation.Config;
using Frame_Simulation.Log;

namespace Frame_Simulation.Plc
{
    /// <summary>
    /// 符号寻址（CIP）的仿真服务端。
    ///
    /// 与 FINS 的关键差别：CIP 必须先登记符号名并指定值类型，客户端写未登记的符号会失败，
    /// 累计 3 次即触发"主设备连接失败"断链。符号表由宿主注入（来自当前机型的点位表），
    /// 本类不认识任何机型，也不保存任何业务点位。
    /// </summary>
    public class SimPlcServerCip : SimPlcServerBase
    {
        private readonly List<string> _symbols;

        public SimPlcServerCip(SimulationConfig config, SimRecorder recorder, IEnumerable<string> symbols)
            : base(config, recorder)
        {
            _symbols = symbols == null ? new List<string>() : new List<string>(symbols);
        }

        protected override DeviceServer CreateServer()
        {
            return new OmronCipServer();
        }

        protected override void OnBeforeStart()
        {
            OmronCipServer cip = Server as OmronCipServer;
            if (cip == null) return;

            foreach (string tag in _symbols)
            {
                // 必须用带值类型的重载 AddTagValue(name, (short)0)。
                // OmronCipServer 的 AddTagValue(name, string, int) 第二参是"字符串初值"，
                // 传 "INT" 会注册成一个内容为 "INT" 的 STRING 标签，客户端写入永远报"标记不存在"。
                try { cip.AddTagValue(tag, (short)0); }
                catch (Exception ex)
                {
                    Record(SimRecordKind.Warning, "注册符号失败 " + tag + "：" + ex.Message);
                }
            }

            Watch(_symbols.ToArray());
        }

        /// <summary>本次实际注册的符号表，供启动体检与仿真界面展示。</summary>
        public IReadOnlyList<string> RegisteredTags
        {
            get { return _symbols; }
        }
    }
}