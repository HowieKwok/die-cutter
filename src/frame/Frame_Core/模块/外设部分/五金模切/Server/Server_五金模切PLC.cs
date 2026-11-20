using LogTool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static log4net.Appender.RollingFileAppender;

namespace Frame_Core
{
    /// <summary>
    /// 五金模切的 PLC 通讯服务：CIP + 符号地址，只负责收发与日志。
    ///
    /// 复用 <see cref="PLCCommunicationBase"/> 的连接与读写实现（CIP 由
    /// <see cref="CommunicationProtocolEnum.CIP"/> 分支转成 OmronCipNet），
    /// 但地址表、日志文件都是五金自己的，不与激光的 CutOverlayPlc* 混用。
    ///
    /// 本类不含任何业务语义：1 是 OK 还是 NG 由调用方决定。
    /// </summary>
    public class Server_五金模切PLC : PLCCommunicationBase
    {
        public Server_五金模切PLC(PLCDevParam pLCDevParam)
        {
            m_pLCDevParam = pLCDevParam;
        }

        public void SetLogPath(string logPath)
        {
            HeartbeatLog = LogObject.SetLog(logPath, "WujinCutPlcHeartbeatLog", 5, 100, RollingMode.Composite);
            CommunicationLog = LogObject.SetLog(logPath, "WujinCutPlcCommunicationLog", 5, 100, RollingMode.Composite);
        }

        public log4net.ILog HeartbeatLog = log4net.LogManager.GetLogger("WujinCutPlcHeartbeatLog");

        public log4net.ILog CommunicationLog = log4net.LogManager.GetLogger("WujinCutPlcCommunicationLog");

        #region 参数

        /// <summary>五金自己的点位表，与激光的 PLCAddress_DieCuttingCut 互不相干。</summary>
        public PLCAddress_五金模切 Address = new PLCAddress_五金模切();

        /// <summary>把点位表的符号声明转出去，仿真服务端只认这个能力。</summary>
        public override IEnumerable<string> GetRegisterSymbols()
        {
            return Address.GetRegisterSymbols();
        }

        #endregion
    }
}