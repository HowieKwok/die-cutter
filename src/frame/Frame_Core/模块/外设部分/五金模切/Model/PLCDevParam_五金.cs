using System;

namespace Frame_Core
{
    /// <summary>
    /// 五金的 PLC 参数：结构与激光共用，默认值不同。
    ///
    /// 五金走 CIP 符号寻址（CCD_DATA_RD.*），激光走 Fins 的 D 寄存器，
    /// 所以首启动、参数文件还不存在时，这里必须给出 CIP，否则会按 0（Fins）去连，
    /// 现场表现为连不上而日志里看不出"协议选错了"。
    ///
    /// 只改默认值：已存在的参数文件仍然以文件里的协议为准，
    /// 调试时想临时切 Fins 不用改代码。
    /// </summary>
    public class PLCDevParam_五金 : PLCDevParam
    {
        public PLCDevParam_五金()
        {
            ComType = (int)CommunicationProtocolEnum.CIP;
        }
    }
}