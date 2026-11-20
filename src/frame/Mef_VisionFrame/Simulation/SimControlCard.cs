using System;
using ControlBoard.Interface;
using ControlBoard.板卡库;
using Frame_Simulation.Log;

namespace Mef_VisionFrame.Simulation
{
    /// <summary>
    /// 仿真的 0640 控制卡：不碰硬件，初始化恒成功，输出写入只落一条仿真记录。
    ///
    /// 它存在的理由不是"让代码别崩"，而是让"这台机器有一张可用的 IO 卡"成为仿真模式下
    /// 同样成立的事实。Server 构造期间就要配中断、建 IO 脉冲输出、绑心跳，
    /// 缺了这张卡，仿真只能靠"卡对象为空"表达，故障就会跑到远处变成空引用。
    /// </summary>
    internal sealed class SimControlCard : IControlCard
    {
        private const int Ok = 0;
        private const int Connected = 1;

        private readonly SimRecorder _recorder;

        public SimControlCard(SimRecorder recorder)
        {
            _recorder = recorder;
        }

        public int IsConnnected() { return Connected; }

        public int Board_Init() { return Ok; }

        public int Board_Close() { return Ok; }

        public int Write_outbit(ushort cardno, ushort bitno, int on_off)
        {
            Record("输出", cardno, bitno, on_off == 0 ? "0" : "1");
            return Ok;
        }

        /// <summary>输入口在仿真里没有外部信号源，保持低电平。</summary>
        public int Read_Inbit(ushort cardno, ushort bitno) { return 0; }

        public int Read_Outbit(ushort cardno, ushort m_PortNo) { return 0; }

        public int Set_Filter(ushort cardno, double filter) { return Ok; }

        public int Read_Inport(ushort cardno, ushort m_PortNo) { return 0; }

        public int Read_Outport(ushort cardno, ushort m_PortNo) { return 0; }

        public int Write_Outport(ushort cardno, ushort m_PortNo, uint port_value)
        {
            Record("端口输出", m_PortNo, 0, "0x" + port_value.ToString("X8"));
            return Ok;
        }

        /// <summary>
        /// 中断回调不注册：真机靠上升沿触发，仿真里由流程驱动（SimCamera 的 TriggerWhen），
        /// 再挂一层中断回调只会产生用户不存在的"外部触发"。
        /// </summary>
        public int Int_Enable(ushort cardno, IOC0640_OPERATE funcIntHandler, IntPtr operate_data) { return Ok; }

        public int Int_Disable(ushort cardno) { return Ok; }

        public int Config_Intbitmode(ushort cardno, ushort bitno, ushort enable, ushort logic) { return Ok; }

        public int Config_Intbitmode(ushort cardno, ushort bitno, ushort[] enable, ushort[] logic) { return Ok; }

        public int Read_Intbitstatus(ushort cardno, ushort bitno) { return 0; }

        public int Config_Intporten(ushort cardno, ushort m_PortNo, uint port_en) { return Ok; }

        public int Config_Intportlogic(ushort cardno, ushort m_PortNo, uint port_logic) { return Ok; }

        public int Read_Intportmode(ushort cardno, ushort m_PortNo, uint[] enable, uint[] logic) { return 0; }

        public int Read_Intportstatus(ushort cardno, ushort m_PortNo) { return 0; }

        private void Record(string action, ushort cardno, ushort bitno, string value)
        {
            if (_recorder == null) return;
            _recorder.Plc(SimRecordKind.Write, "[控制卡]",
                string.Format("{0} 卡{1} 位{2} = {3}", action, cardno, bitno, value));
        }
    }
}