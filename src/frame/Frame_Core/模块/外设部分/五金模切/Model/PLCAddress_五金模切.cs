using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Frame_Core
{
    /// <summary>
    /// 五金模切的 PLC 点位表（CIP，符号寻址）。
    ///
    /// 与激光模切的 <see cref="PLCAddress_DieCuttingCut"/> 是两张独立的表：
    /// 五金走 Omron CIP 的符号名（CCD_DATA_RD.*），激光走 D 寄存器。
    /// 不要合并成一张表——两种机型的点位数、含义、值语义都不同，
    /// 合并后任何一台上机调整都会牵连另一台。
    ///
    /// 值语义（只对三路结果有效）：
    ///   0 = 停止中   1 = OK   2 = NG   3 = 待检（已触发、结论未出）
    /// 多次NG信号是 0/1，由软件置 1、2 秒后置 0。
    /// 心跳信号是 0/1，由软件每 1 秒翻转一次。
    /// </summary>
    public class PLCAddress_五金模切 : IPLCAddressTable
    {
        /// <summary>上线扫（模切主相机）本片外观结论。</summary>
        public CommunicationData<short> UpLineScanResult = new CommunicationData<short>();

        /// <summary>下线扫（模切从相机）本片外观结论。</summary>
        public CommunicationData<short> DownLineScanResult = new CommunicationData<short>();

        /// <summary>三路尺寸相机合成后的本片尺寸结论。</summary>
        public CommunicationData<short> SizeResult = new CommunicationData<short>();

        /// <summary>心跳，软件每 1 秒翻转一次。</summary>
        public CommunicationData<short> Heartbeat = new CommunicationData<short>();

        /// <summary>上线连续多次 NG，软件置 1，2 秒后置 0。</summary>
        public CommunicationData<short> UpLineScanNgOverTimes = new CommunicationData<short>();

        /// <summary>下线连续多次 NG，与上线各自独立。</summary>
        public CommunicationData<short> DownLineScanNgOverTimes = new CommunicationData<short>();

        /// <summary>尺寸连续多次 NG。</summary>
        public CommunicationData<short> SizeNgOverTimes = new CommunicationData<short>();

        public PLCAddress_五金模切()
        {
            UpLineScanResult.SetAddress("CCD_DATA_RD.LineScan_Up").SetName("CCD上线扫数据");
            DownLineScanResult.SetAddress("CCD_DATA_RD.LineScan_Down").SetName("CCD下线扫数据");
            SizeResult.SetAddress("CCD_DATA_RD.Size").SetName("CCD尺寸数据");
            Heartbeat.SetAddress("CCD_DATA_RD.CcdRunning").SetName("CCD心跳信号");
            UpLineScanNgOverTimes.SetAddress("CCD_DATA_RD.LineScan_Up_NG_OverTimes").SetName("CCD上线扫多次NG");
            DownLineScanNgOverTimes.SetAddress("CCD_DATA_RD.LineScan_Down_NG_OverTimes").SetName("CCD下线扫多次NG");
            SizeNgOverTimes.SetAddress("CCD_DATA_RD.Size_NG_OverTimes").SetName("CCD尺寸多次NG");
        }

        /// <summary>
        /// 五金点位全部是 CIP 符号，服务端必须逐个注册才允许写入。
        /// 显式列出而不是反射：新增/改名点位时这里编译期就能暴露遗漏。
        /// </summary>
        public IEnumerable<string> GetRegisterSymbols()
        {
            yield return UpLineScanResult.Address;
            yield return DownLineScanResult.Address;
            yield return SizeResult.Address;
            yield return Heartbeat.Address;
            yield return UpLineScanNgOverTimes.Address;
            yield return DownLineScanNgOverTimes.Address;
            yield return SizeNgOverTimes.Address;
        }
    }
}