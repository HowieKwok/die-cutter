using Frame_Core;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切的业务服务：线程管理、运行控制、报警与状态栏。
    /// 工艺处理（各流程的取图/算法/判定/PLC/IO）尚未实现，方法体保持为空壳，
    /// 需要时按激光模切的 Server_DieCuttingCut 逐个补齐。
    /// </summary>
    public class Server_五金模切 : ServerBase
    {
        public Model_五金模切 Data;

        public override event Action RefreshToolView;
        public override event Action InitView;
        public override event Action RefreshView;

        public Server_五金模切(Model_五金模切 modelBase)
            : base(modelBase)
        {
            Data = modelBase;
            InitThread();
        }

        /// <summary>报警与光源线程沿用基类，保证工具条上的光源开关可用。</summary>
        public override void InitThread()
        {
            base.InitThread();
        }

        #region 运行控制

        public override bool StartRun()
        {
            return ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                if (Project.Instance.Is_simulation)
                {
                    MessageBox.Show(" “仿真运行”状态禁止自动运行。");
                    return false;
                }

                foreach (ProcessServices item in Project.Instance.m_ProcessList)
                {
                    Project.Instance.StartRun(item.process.processInfo.ProcessID);
                }

                RefreshToolView?.Invoke();
                return true;
            });
        }

        public override bool StopRun()
        {
            return ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                Data.M_SimRunCommand = SimRunCommand.Stop;

                foreach (ProcessServices item in Project.Instance.m_ProcessList)
                {
                    Project.Instance.StopRun(item.process.processInfo.ProcessID);
                }

                RefreshToolView?.Invoke();
                return true;
            });
        }

        public override void Reset()
        {
            ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                Project.Instance.baseProDevice.INIProduct();
            });
        }

        public override void RunModelSwitch()
        {
            ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                if (Project.Instance.Is_simulation)
                {
                    Project.Instance.Is_simulation = false;
                    Project.Instance.baseProDevice.InitProduct_Sim();
                }
                else
                {
                    Project.Instance.Is_simulation = true;
                }

                RefreshToolView?.Invoke();
            });
        }

        #endregion

        #region 膜卷

        public override bool StartFilmRoll(string filmRollName)
        {
            return ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                Project.Instance.systemInfo.currentMojuanhao = filmRollName;
                Project.Instance.Is_MojuanStart = true;
                RefreshToolView?.Invoke();
                return true;
            });
        }

        public override bool StopFilmRoll()
        {
            return ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                Project.Instance.Is_MojuanStart = false;
                RefreshToolView?.Invoke();
                return true;
            });
        }

        #endregion

        #region 待补齐的工艺部分

        public override void SimLoadImage()
        {
        }

        /// <summary>数据保存线程内容待实现。</summary>
        public override void DataSave()
        {
        }

        /// <summary>数据处理线程内容待实现。</summary>
        public override void DataProcess()
        {
        }

        /// <summary>PLC 通讯线程内容待实现（点位先沿用激光模切）。</summary>
        public override void Communication()
        {
        }

        /// <summary>IO 脉宽等参数下发待实现。</summary>
        public override void Udpate()
        {
        }

        public override void ClearCutData(bool isSendPLC)
        {
        }

        public override void AlarmReset()
        {
        }

        public override void SendStopAlarm(int index)
        {
        }

        public override void SendStopAlarm(string msg)
        {
        }

        public override void SendAlarm(int index)
        {
        }

        public override void SendAlarm(string msg)
        {
        }

        public override List<StatusLabelItem> GetStatusBarInfo()
        {
            return new List<StatusLabelItem>();
        }

        #endregion
    }
}