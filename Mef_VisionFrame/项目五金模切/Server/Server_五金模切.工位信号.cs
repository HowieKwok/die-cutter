using ControlBoard.服务类;
using Frame_Core;
using LogTool;
using System;
using System.Collections.Generic;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切的工位 IO 信号：心跳 + 判定结果回传 PLC。
    ///
    /// 名字、位号、脉宽全部来自机型点位表（<see cref="DieCuttingCutIoPoints"/>），
    /// 上机后换成五金自己的点位表即可，本文件不用改。
    ///
    /// 当前只有尺寸工位会真正发结果信号；缺陷工位的心跳先跑起来（PLC 侧要看到心跳），
    /// 缺陷判定接入后再补 MinorNG/SevereNG/OK/连续NG 的触发。
    /// 裁切工位那一路在五金上是冲模的位置，先只发心跳。
    /// </summary>
    public partial class Server_五金模切
    {
        /// <summary>已创建的所有脉冲输出，统一在这里跑状态机（OnTimerTick）。</summary>
        private readonly List<IOPulseOutput> 工位信号表 = new List<IOPulseOutput>();

        public IOPulseOutput CutStation_Heartbeat = null;

        public IOPulseOutput DefectStation_Heartbeat = null;
        public IOPulseOutput DefectStation_MinorNGSignal = null;
        public IOPulseOutput DefectStation_SevereNGSignal = null;
        public IOPulseOutput DefectStation_OKSignal = null;
        public IOPulseOutput DefectStation_ContinuousNGSignal = null;

        public IOPulseOutput SizeStation_Heartbeat = null;
        public IOPulseOutput SizeStation_NGSignal = null;
        public IOPulseOutput SizeStation_OKSignal = null;
        public IOPulseOutput SizeStation_ContinuousNGSignal = null;

        /// <summary>尺寸工位连续 NG 片数。达到配方阈值后发一次连续 NG 信号并清零。</summary>
        private int 尺寸连续NG计数 = 0;

        /// <summary>
        /// 按机型点位表创建 IO 输出。没有控制卡时全部保持 null，后续触发都有 null 判断，
        /// 这样没有板卡的调试环境也能正常启动。
        /// </summary>
        private void InitIoOutputs()
        {
            IoPointTable ioPoints = new IoPointTable(DeviceProfileRegistry.Resolve(Project.Instance.systemInfo.CraftType).IoPoints);

            CutStation_Heartbeat = CreateIoOutput(ioPoints, "CutStation_Heartbeat");

            DefectStation_Heartbeat = CreateIoOutput(ioPoints, "DefectStation_Heartbeat");
            DefectStation_MinorNGSignal = CreateIoOutput(ioPoints, "DefectStation_MinorNGSignal");
            DefectStation_SevereNGSignal = CreateIoOutput(ioPoints, "DefectStation_SevereNGSignal");
            DefectStation_OKSignal = CreateIoOutput(ioPoints, "DefectStation_OKSignal");
            DefectStation_ContinuousNGSignal = CreateIoOutput(ioPoints, "DefectStation_ContinuousNGSignal");

            SizeStation_Heartbeat = CreateIoOutput(ioPoints, "SizeStation_Heartbeat");
            SizeStation_NGSignal = CreateIoOutput(ioPoints, "SizeStation_NGSignal");
            SizeStation_OKSignal = CreateIoOutput(ioPoints, "SizeStation_OKSignal");
            SizeStation_ContinuousNGSignal = CreateIoOutput(ioPoints, "SizeStation_ContinuousNGSignal");
        }

        private IOPulseOutput CreateIoOutput(IoPointTable ioPoints, string pointName)
        {
            var card = ServiceCardManage.Instance.GetIControlCard();
            if (card == null)
            {
                Log4netRecord.ErrorLog.Error("五金模切未获取到控制卡，IO 信号不发送：" + pointName);
                return null;
            }

            IoPoint point = ioPoints.Get(pointName);
            IOPulseOutput output = new IOPulseOutput(Project.Instance.M_ParamControlCord.CardNo, point.BitNo,
                card, point.PulseWidthMs)
                .SetIsSaveLog(point.SaveLog);

            工位信号表.Add(output);
            return output;
        }

        /// <summary>
        /// 尺寸工位结果回传：OK / NG 二选一，另外维护连续 NG。
        /// PLC 收到这一片的结果才放下一个触发，这是"三路不串片"的实际保证。
        /// </summary>
        /// <returns>本次是否触发了连续 NG</returns>
        private bool 尺寸工位_发送结果(bool isNG, string 说明)
        {
            if (isNG)
            {
                SizeStation_NGSignal?.Trigger("尺寸工位——发送_NG信号:" + 说明);
                尺寸连续NG计数++;
            }
            else
            {
                SizeStation_OKSignal?.Trigger("尺寸工位——发送_OK信号:" + 说明);
                尺寸连续NG计数 = 0;
            }

            bool continuousNG = false;
            int 阈值 = Data.Craft?.Get_GlobalVariableItem("SizeStation_ContinuousCount")?.Value.ToInt() ?? 0;
            if (阈值 > 0 && 尺寸连续NG计数 >= 阈值)
            {
                尺寸连续NG计数 = 0;
                continuousNG = true;
                SizeStation_ContinuousNGSignal?.Trigger("尺寸工位——发送_连续NG信号:" + 说明);
            }

            return continuousNG;
        }

        /// <summary>
        /// 缺陷工位结果回传：OK / 轻度NG / 重度NG 三选一，另外维护连续 NG。
        /// A/B 合片后每片只调一次。
        /// </summary>
        /// <returns>本次是否触发了连续 NG</returns>
        private bool 缺陷工位_发送结果(bool isNG, bool isSevere, string 说明)
        {
            if (isNG)
            {
                if (isSevere)
                {
                    DefectStation_SevereNGSignal?.Trigger("缺陷工位——发送_重度NG信号:" + 说明);
                }
                else
                {
                    DefectStation_MinorNGSignal?.Trigger("缺陷工位——发送_轻度NG信号:" + 说明);
                }
                Data.DefectStation_ContinuousCount++;
            }
            else
            {
                DefectStation_OKSignal?.Trigger("缺陷工位——发送_OK信号:" + 说明);
                Data.DefectStation_ContinuousCount = 0;
            }

            bool continuousNG = false;
            int 阈值 = Data.Craft?.Get_GlobalVariableItem("DefectStation_ContinuousCount")?.Value.ToInt() ?? 0;
            if (阈值 > 0 && Data.DefectStation_ContinuousCount >= 阈值)
            {
                Data.DefectStation_ContinuousCount = 0;
                continuousNG = true;
                DefectStation_ContinuousNGSignal?.Trigger("缺陷工位——发送_连续NG信号:" + 说明);
            }

            return continuousNG;
        }

        /// <summary>
        /// 通讯线程：心跳按配方节拍触发，所有脉冲输出每 5ms 推进一次状态机。
        /// </summary>
        public override void Communication()
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                int 心跳周期 = Data.Craft?.Get_GlobalVariableItem("HeartbeatPollingTime")?.Value.ToInt() ?? 500;

                if (Project.Instance.Is_Run)
                {
                    if (CutStation_Heartbeat != null && CutStation_Heartbeat.GetIsArrivalTime(心跳周期))
                    {
                        CutStation_Heartbeat.Trigger("冲模工位——心跳");
                    }
                    if (DefectStation_Heartbeat != null && DefectStation_Heartbeat.GetIsArrivalTime(心跳周期))
                    {
                        DefectStation_Heartbeat.Trigger("缺陷工位——心跳");
                    }
                    if (SizeStation_Heartbeat != null && SizeStation_Heartbeat.GetIsArrivalTime(心跳周期))
                    {
                        SizeStation_Heartbeat.Trigger("尺寸工位——心跳");
                    }
                }

                foreach (IOPulseOutput output in 工位信号表)
                {
                    output.OnTimerTick();
                }
            });
        }

        /// <summary>脉宽下发：心跳用 HeartbeatPulseWidth，结果信号用 PulseWidth。</summary>
        public override void Udpate()
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                if (Data.Craft == null) return;

                int 心跳脉宽 = Data.Craft.Get_GlobalVariableItem("HeartbeatPulseWidth")?.Value.ToInt() ?? 100;
                int 信号脉宽 = Data.Craft.Get_GlobalVariableItem("PulseWidth")?.Value.ToInt() ?? 10;

                CutStation_Heartbeat?.SetPulseWidth(心跳脉宽);

                DefectStation_Heartbeat?.SetPulseWidth(心跳脉宽);
                DefectStation_MinorNGSignal?.SetPulseWidth(信号脉宽);
                DefectStation_SevereNGSignal?.SetPulseWidth(信号脉宽);
                DefectStation_OKSignal?.SetPulseWidth(信号脉宽);
                DefectStation_ContinuousNGSignal?.SetPulseWidth(信号脉宽);

                SizeStation_Heartbeat?.SetPulseWidth(心跳脉宽);
                SizeStation_NGSignal?.SetPulseWidth(信号脉宽);
                SizeStation_OKSignal?.SetPulseWidth(信号脉宽);
                SizeStation_ContinuousNGSignal?.SetPulseWidth(信号脉宽);
            });
        }
    }
}