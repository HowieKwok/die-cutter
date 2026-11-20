using System;
using System.Threading;
using System.Windows.Forms;
using Frame_Core;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 启动初始化阶段：参数、设备、仿真服务端，以及加载进度条。
    ///
    /// 之所以不在 Form_Main 里做，是因为 WinForms 的事件顺序是 Load → Shown：
    /// 以前用"阻断后 Showned 里 Close()"来中止启动，Load 早就跑完并装配了 Server，
    /// 于是失败变成远处的空引用。初始化与 UI 分开后，"能不能进主界面"只有这里一个判据，
    /// 失败就退出进程，不构造任何业务对象。
    /// </summary>
    internal static class StartupBootstrap
    {
        /// <summary>返回 true 才能创建主窗体。</summary>
        public static bool Run()
        {
            Form_LoadProgressBar bar = new Form_LoadProgressBar();
            Exception unhandled = null;

            Thread thBar = new Thread(delegate()
            {
                bar.SendToBack();
                bar.ShowDialog();
            });
            thBar.IsBackground = true;
            thBar.Start();

            try
            {
                // 仿真钩子已由 Program.Main 在调用本方法前挂好，
                // INIProject 内部会读运行模式并据此起仿真服务端。
                Project.Instance.INIProject();
            }
            catch (Exception ex)
            {
                // INIProject 自己会兜异常，这里只防"兜底本身也失败"的极端情况。
                unhandled = ex;
            }
            finally
            {
                bar.closingFlag = true;
            }

            Exception error = Project.Instance.InitError ?? unhandled;
            if (error != null)
            {
                // 初始化异常是程序问题，用户改配置救不回来，因此这是全流程仅剩的一个提示框。
                MessageBox.Show(
                    "项目初始化失败，请把日志文件交给维护人员。" + Environment.NewLine
                    + Environment.NewLine + error.Message,
                    "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return !Simulation.SimBootstrap.Blocked || RepairSimulation();
        }

        /// <summary>
        /// 仿真阻断后的唯一界面：直接打开已有的相机参数窗口（WpfCameraDevWindow）。
        /// 不新建"仿真配置"窗体——选目录、绑定 EmulationPath、保存回参数的控件本来就在那里，
        /// 缺的只是"启动阶段也能进去"这个入口。关窗即重新体检；通过就继续启动，不重启进程。
        /// </summary>
        private static bool RepairSimulation()
        {
            // 相机参数没读出来就无处可改，直接放弃，避免打开一个空窗口。
            if (Project.Instance.M_CamParam == null) return false;

            // 只走一轮：关窗即"改完了"，重新体检；仍不过就是用户放弃了，直接结束启动。
            // 不做自动重开——否则用户想退出时会被反复弹回同一个窗口。
            DevMgmt.Cameras.View.WpfCameraDevWindow window =
                new DevMgmt.Cameras.View.WpfCameraDevWindow(Project.Instance.M_CamParam);
            window.Title = "仿真配置（关闭本窗口重新体检）—— " + ShortReason();

            window.ShowDialog();

            if (!Simulation.SimBootstrap.Recheck()) return false;

            // 体检通过后才起 PLC 服务端、注册模拟控制卡等带副作用的步骤。
            return Simulation.SimBootstrap.Initialize();
        }

        /// <summary>
        /// 标题只放一条阻断项：完整报告有多行，塞进标题等于不可读。
        /// 用户需要的是"哪台相机不对"，其余明细留在日志里。
        /// </summary>
        private static string ShortReason()
        {
            string reason = Simulation.SimBootstrap.LastBlockReason;
            if (string.IsNullOrWhiteSpace(reason)) return "需要处理仿真配置";

            string[] lines = reason.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("[阻断]", StringComparison.Ordinal) >= 0)
                {
                    return lines[i].Replace("[阻断]", "").Trim();
                }
            }

            return lines.Length > 0 ? lines[0].Trim() : "需要处理仿真配置";
        }
    }
}