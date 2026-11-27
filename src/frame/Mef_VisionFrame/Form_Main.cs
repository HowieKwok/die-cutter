using ControlBoard.服务类;
using DevMgmt.Cameras.View;
using DevMgmt.Light.View;
using Eholly.ZChart;
using EHolly.ImageTool;

using Frame_Core;
using Frame_MetalRun;
using FrameCommunity.DataOperation;
using HalconDotNet;
using LogTool;
using Mef_VisionFrame.调试设置.WPF;
using NPOI.SS.Formula.Functions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace Mef_VisionFrame
{
    public partial class Form_Main : Form
    {
        /// <summary>
        /// 获取软件编译时间，作用：用作软件界面版本时间
        /// </summary>
        public string Str_LatestTimeProgram = "20200202-xxxx";

        static Form_Main form_Main;

        /// <summary>
        /// 在菜单条末尾追加“切换主题”入口（深色/浅色互切）。
        /// 切换由 ThemeService 广播：WinForms 窗体、WPF 窗口、AntdUI 控件三方各自订阅重刷。
        /// 已知边界：个别把画刷固化进字段的视图需要重新打开才能完全跟随新主题。
        /// </summary>
        private void AppendThemeSwitchMenuItem()
        {
            var themeItem = new ToolStripMenuItem("切换主题");
            themeItem.ToolTipText = "深色 / 浅色主题互切";
            themeItem.Click += delegate
            {
                bool toLight = Frame_UI.ThemeService.CurrentThemeName != Frame_UI.UiThemeCatalog.LightThemeName;
                Frame_UI.ThemeService.ApplyTheme(toLight
                    ? Frame_UI.UiThemeCatalog.LightThemeName
                    : Frame_UI.UiThemeCatalog.DarkThemeName);
            };
            menuStrip1.Items.Add(themeItem);
        }

        /// <summary>
        /// 路径设置入口：塞进“系统设置”菜单，排在“磁盘设置”前面。
        /// 用代码追加而不是改设计器，避免生成文件反复冲突。
        /// </summary>
        private void AppendPathSettingMenuItem()
        {
            ToolStripMenuItem item = new ToolStripMenuItem("路径设置");
            item.ToolTipText = "配置生产数据、标定、点检、点检图像、日志、MES 的落盘目录";
            item.Click += delegate
            {
                if (!Project.Instance.purviewManage.GetWhetherPurview(
                        Project.Instance.userManage.userData.CurrentUser, ModelName.路径设置)) return;

                using (Form_PathSetting form = new Form_PathSetting(Project.Instance.pathParameter))
                {
                    form.StartPosition = FormStartPosition.CenterScreen;
                    form.ShowDialog(this);
                }
            };

            int index = 系统设置ToolStripMenuItem.DropDownItems.IndexOf(磁盘设置ToolStripMenuItem);
            if (index >= 0)
                系统设置ToolStripMenuItem.DropDownItems.Insert(index, item);
            else
                系统设置ToolStripMenuItem.DropDownItems.Add(item);
        }

        /// <summary>
        /// 仿真模式下追加"仿真"菜单入口。必须在 INIProject 之后调用——
        /// RunMode 是 INIProject 里读的，构造函数开头调用会把入口漏掉。
        /// </summary>
        private void AppendSimulationMenuItem()
        {
            if (!Simulation.SimBootstrap.IsSimulation) return;

            ToolStripMenuItem simItem = new ToolStripMenuItem("仿真");
            simItem.ToolTipText = "设置仿真产能、查看相机/PLC 仿真记录";
            simItem.Click += delegate
            {
                if (Simulation.SimBootstrap.Runtime == null) return;

                // 非模态：可与主界面同时操作
                Frame_Simulation.View.Form_Simulation form =
                    new Frame_Simulation.View.Form_Simulation(Simulation.SimBootstrap.Runtime);
                Frame_Core.FormStylingHook.Style(form);   // 与主界面同主题
                form.Show(this);
            };

            menuStrip1.Items.Add(simItem);
        }

        /// <summary>
        /// 配置缺失时在状态 chip 行末尾（与"磁盘剩余/内存"同一个父容器）追加一个红色条目，
        /// 启动弹窗被点掉后仍然可见，点击可重新查看明细。配置完整时不添加任何控件。
        /// </summary>
        private void AppendConfigWarningBar()
        {
            // 仿真图片目录这类问题不阻断启动，但必须让用户看见——它就在同一个提示条里。
            List<string> problems = new List<string>(StartupPreflight.LastProblems ?? new List<string>());
            problems.AddRange(Simulation.SimBootstrap.StartupWarnings);
            if (problems.Count == 0) return;

            uC_StateView1.SetWarning("配置不完整（" + problems.Count + " 项）", delegate
            {
                MessageBox.Show(string.Join(Environment.NewLine, problems.ToArray()),
                    "配置不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        }

        public Form_Main()
        {
            InitializeComponent();
            Frame_Core.FormStylingHook.Style(this);
            AppendThemeSwitchMenuItem();
            AppendConfigWarningBar();
            AppendPathSettingMenuItem();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            form_Main = this;

            // 主容器承载整块业务视图（含多路相机画面），开双缓冲，
            // 首次装载与后续重排都不会先露白底再画内容。
            typeof(Panel)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(panel_Main, true, null);

            // 项目初始化已前移到 Program.Main 的启动阶段（StartupBootstrap）：
            // 构造函数只组装界面，不再读配置、不再碰设备。
            AppendSimulationMenuItem();

            // 五金改由【开始膜卷】显式启动；不再在 Shown 时自动开跑。
                //CraftSetting=new Form_CraftSetting();
                //MainDetection = new UC_MainDetection();
                //BaseSetting = new UC_BaseSetting();
                //BIZ_Switchover.InitBizDataAndForm();

                ////初始化流程
                ////Project.Instance.IFactory.INIPro();
                ////Project.Instance.IFactory.INIProduct();

                //////主副机通讯回调函数注册
                ////Project.Instance.m_SlaveMachine.BeginEvent -= MainDetection.Begin;
                ////Project.Instance.m_SlaveMachine.BeginEvent += MainDetection.Begin;

                ////软件版本显示
                //string MainSlaveMachine = "MainMachine";
                //if (!Project.Instance.systemInfo.IsHostMachine)
                //    MainSlaveMachine = "SlaveMachine";
                //ComStationsanwahlEnum comStationsanwahlEnum = (ComStationsanwahlEnum)Project.Instance.systemInfo.ComStationsanwahl;
                //Init_LatestTimeProgram(comStationsanwahlEnum.ToString());

                ////翻译
                //MenuStripTranslate();



                ////初始化状态
                //InitStateBar_Flag = true;
                //form_LoadProgressBar.closingFlag = true;

        }

        public static Form_Main FormMain
        {
            get
            {
                if (form_Main == null)
                {
                    form_Main = new Form_Main();
                }
                return form_Main;
            }
        }
        private void Form_Main_Load(object sender, EventArgs e)
        {
            new UIService(85, this, ThreadPriority.Normal);

            EHollyImageTool.Init();
            EHollyImageTool.StartDeleteService();
            EHollyImageTool.Settings.ImageSaveTimeOut = 4000;


            ProjectFactory.Load();
            uC_StateView1.LoadItems(ProjectFactory.F_Server?.WorkServerBase?.GetStatusBarInfo());
            RefreshRunStateChip();

            panel_Main.SuspendLayout();
            try
            {
                panel_Main.Controls.Clear();
                panel_Main.Controls.Add(ProjectFactory.F_View.ToolView);   //添加窗体
            }
            finally
            {
                panel_Main.ResumeLayout(true);
            }


            //软件版本显示
            string MainSlaveMachine = "HSVISONMAIN";
            if (!Project.Instance.systemInfo.IsHostMachine)
                MainSlaveMachine = "HSVISONSLAVE";

            Init_LatestTimeProgram("");
            RefreshSimulationImagePaths();

            // 登录态只由 UserManage 持有，徽标订阅事件做渲染（可能来自通讯线程，内部已做 Invoke 保护）
            Project.Instance.userManage.LoginEvent -= UserManage_LoginEvent;
            Project.Instance.userManage.LoginEvent += UserManage_LoginEvent;
            RefreshUserBadge();

            //初始化状态
            InitStateBar_Flag = true;

        }


        /// <summary>
        /// 禁用窗体双击放大缩小
        /// </summary>
        /// <param name="m"></param>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg != 0xA3 && m.Msg != 0x0003 && m.WParam != (IntPtr)0xF012)
            {
                base.WndProc(ref m);
            }
        }
        private void Form_Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (Project.Instance.Is_MojuanStart)
                {
                    System.Windows.Forms.MessageBox.Show("请结束膜卷再关闭软件");
                    e.Cancel = true;
                    return;
                }

                DialogResult result = System.Windows.Forms.MessageBox.Show("是否关闭软件？", "询问", MessageBoxButtons.YesNo);
                if (result == DialogResult.No)
                {

                    e.Cancel = true;
                    return;
                }
                //Project.Instance.systemInfo.CurrentCraft = Project.Instance.paramPathSet.CurrentCraft;
                //Project.Instance.systemInfo.CurrentMaterial = Project.Instance.paramPathSet.CurrentMaterial;
                //Project.Instance.systemInfo.IsHostMachine = Project.Instance.paramPathSet.IsHostMachine;

                Project.Instance.systemInfo.Save();
                //释放硬件资源
                Project.Instance.Close_Dev();


            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(ex);
            }
            close();
        }
        /// <summary>
        /// 关闭程序，释放资源
        /// </summary>
        public void close()
        {
            this.Dispose(true);
            this.Close();
            // Application.Exit();
            Environment.Exit(0);
            System.Diagnostics.Process[] objProcess = System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName);
            if (objProcess != null)
            {
                for (int i = 0; i < objProcess.Length; i++)
                {
                    if (objProcess[i].Id != System.Diagnostics.Process.GetCurrentProcess().Id)
                    {
                        //double T = objProcess[i].TotalProcessorTime.TotalMilliseconds;
                        //System.Windows.Forms.MessageBox.Show("关闭进程,id:" + objProcess[i].Id.ToString() + ",T:" + T.ToString("F3"));
                        objProcess[i].Kill();
                    }
                }
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            //System.Environment.Exit(System.Environment.ExitCode);

        }
        ///<summary>
        ///窗体切换，防止闪烁
        ///</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        #region 窗体控件重绘，防止闪烁
        [System.Runtime.InteropServices.DllImport("user32")]
        private static extern int SendMessage(IntPtr hwnd, int wMsg, int wParam, IntPtr lParam);
        private const int WM_SETREDRAW = 0xB;
        #endregion

        #region 底部状态栏

        bool InitStateBar_Flag = false;
        /// <summary>
        /// 五金三态 chip：状态由控制器唯一真源给出；激光不显示。
        /// 500ms 状态栏定时器调用，暂停/继续最多滞后半秒。
        /// </summary>
        private void RefreshRunStateChip()
        {
            if (!Project.Instance.Is五金模切)
            {
                uC_StateView1.SetRunState(null);
                SetCraftMenusEnabled(true);
                return;
            }

            Server_五金模切 metal = ProjectFactory.F_Server == null
                ? null
                : ProjectFactory.F_Server.WorkServerBase as Server_五金模切;
            MetalRunController run = metal == null ? null : metal.RunControl;
            if (run == null)
            {
                uC_StateView1.SetRunState(null);
                SetCraftMenusEnabled(true);
                return;
            }

            RunStateChipState state = run.State == MetalRunState.Running ? RunStateChipState.Running
                : run.State == MetalRunState.Paused ? RunStateChipState.Paused
                : RunStateChipState.Idle;
            uC_StateView1.SetRunState(state);

            // 运行中禁止进入调试设置/工艺标准；暂停与空闲保持可调参
            SetCraftMenusEnabled(run.State != MetalRunState.Running);
        }

        /// <summary>运行中禁止进入调试设置/工艺标准；暂停与空闲保持可调参。</summary>
        private void SetCraftMenusEnabled(bool enabled)
        {
            if (调试设置ToolStripMenuItem.Enabled != enabled) 调试设置ToolStripMenuItem.Enabled = enabled;
            if (工艺标准ToolStripMenuItem.Enabled != enabled) 工艺标准ToolStripMenuItem.Enabled = enabled;
        }

        /// <summary>
        /// 状态栏刷新
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void timer_StatusBarUpdate_Tick(object sender, EventArgs e)
        {
            if (InitStateBar_Flag)
            {
                try
                {



                    if (uC_StateView1.IsHandleCreated)
                    {
                        uC_StateView1.RefreshStatus(ProjectFactory.F_Server.WorkServerBase.GetStatusBarInfo());
                        RefreshRunStateChip();
                    }
                }
                catch (Exception)
                {

                }

            }
        }
        #endregion

        private void 系统设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void 外联设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        WPFDebugProcessWindow _wpfDebugWindow;
        private void 调试设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.调试设置)) { return; }

            //WPFDebugProcessWindow wpfDebugWindow = new WPFDebugProcessWindow
            //{
            //    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
            //};
            //wpfDebugWindow.ShowDialog();
            //return;

            // 1. 首次初始化窗体 + 拦截关闭行为（关键：关闭改为隐藏，不销毁）
            if (_wpfDebugWindow == null)
            {
                _wpfDebugWindow = new WPFDebugProcessWindow
                {
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
                };


                // 拦截Closing事件：取消原生关闭，改为隐藏窗口（两种关闭方式都会进入该事件）
                _wpfDebugWindow.Closing += (sender1, cancelArgs) =>
                {
                    // 取消系统默认关闭销毁逻辑
                    cancelArgs.Cancel = true;
                    // 隐藏窗口，保留实例
                    _wpfDebugWindow.Hide();
                    this.Enabled = true;
                };
            }

            // 2. 窗口隐藏状态直接唤起弹窗
            if (!_wpfDebugWindow.IsVisible)
            {
                _wpfDebugWindow.Show();
                this.Enabled = false;
            }


            //// 3. 最小化窗口恢复正常状态
            //if (_wpfDebugWindow.WindowState == System.Windows.WindowState.Minimized)
            //{
            //    _wpfDebugWindow.WindowState = System.Windows.WindowState.Normal;
            //}

            //// 4. 前置激活置顶
            //_wpfDebugWindow.Activate();

        }

        private void 工艺标准ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.工艺设置)) { return; }
            //panel_Main.Controls.Clear();
            //CraftSetting.FormBorderStyle = FormBorderStyle.None;
            //CraftSetting.Dock = DockStyle.Fill;
            //CraftSetting.TopLevel = false;
            //panel_Main.Controls.Add(CraftSetting);   //添加窗体

            // CraftSetting.Show();
            using (Form_CraftFrame form = new Form_CraftFrame(Project.Instance.systemInfo, Project.Instance.craftBase, Project.Instance.paramPathSet.CraftPath))
            {
                form.StartPosition = FormStartPosition.CenterParent;
                form.WindowState = FormWindowState.Maximized;
                form.ShowDialog();
            }
            ProjectFactory.F_Server.WorkServerBase.Udpate();

        }



        /// <summary>
        /// 顶部右侧用户徽标：点击打开登录中心
        /// </summary>
        private void tsUserBadge_Click(object sender, EventArgs e)
        {
            ShowLoginCenter();
        }

        private void ShowLoginCenter()
        {
            using (UserLogionForm userLogionForm = new UserLogionForm(Project.Instance.userManage))
            {
                userLogionForm.StartPosition = FormStartPosition.CenterScreen;
                userLogionForm.ShowDialog();
            }
        }

        /// <summary>
        /// 仅渲染登录态：未登录红色，已登录绿色 + 用户名 + 用户类型
        /// </summary>
        private void RefreshUserBadge()
        {
            if (tsUserBadge == null) return;

            var data = Project.Instance.userManage?.userData;
            if (data == null) return;

            bool logged = data.CurrentUser != UserType.Null;
            tsUserBadge.Text = logged
                ? string.Format("● {0} / {1}", data.CurrentUserID, LanguageSwitch.ToUserLanguageg(data.CurrentUser, UserLanguageg.中文))
                : "● 未登录";
            tsUserBadge.ForeColor = logged ? Color.Green : Color.Red;
        }

        private void UserManage_LoginEvent(UserType userType)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)RefreshUserBadge);
                return;
            }
            RefreshUserBadge();
        }

        /// <summary>
        /// 相机设置变更后刷新两处取图来源：
        ///   1. ImageSimManage —— 标定/点检界面的一次性仿真取图仍在用它；
        ///   2. SimCamera —— 仿真运行期的取图，改路径后必须重扫，否则要重启才生效。
        /// </summary>
        public void RefreshSimulationImagePaths()
        {
            try
            {
                foreach (var item in Project.Instance.M_CameraRelevancies)
                {
                    Service_Camera.Get_CameraRelevancy(item.Key).m_ImageSimManage.ReadImageSimPath(Service_Camera.Get_CameraDev(item.Key).CameraParameter.EmulationPath);
                    Service_Camera.Get_CameraRelevancy(item.Key).m_ImageSimManage.InitSimFlag();
                }

                if (Simulation.SimBootstrap.IsSimulation && Simulation.SimBootstrap.Runtime != null)
                {
                    Simulation.SimBootstrap.Runtime.ReloadImageSets(
                        new Func<DevMgmt.Cameras.Models.CameraDev, Frame_Simulation.SimCameraSpec>(dev =>
                        {
                            if (dev == null || dev.CameraParameter == null) return null;
                            return new Frame_Simulation.SimCameraSpec
                            {
                                Name = dev.CameraParameter.CameraName,
                                ImageDir = Simulation.SimBootstrap.ResolveImageDir(dev.CameraParameter)
                            };
                        }),
                        Project.Instance.M_CamParam.l_CameraDevs.Cast<DevMgmt.Cameras.Models.CameraDev>());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

        }
        private void 相机toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            try
            {
                if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.相机设置)) { return; }
                WpfCameraDevWindow window = new WpfCameraDevWindow(Project.Instance.M_CamParam);
                window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
                window.HideSomeControls();
                window.ShowDialog();

                RefreshSimulationImagePaths();
                // 仿真相机不重建，只把新目录指过去：改完路径立刻能跑，不用重启。
                Simulation.SimBootstrap.ReloadImageDirs();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

        }

        private void 光源ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.光源设置)) { return; }
            WpfLightDevWindow window = new WpfLightDevWindow(Project.Instance.M_LightControllerParam);
            window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            window.ShowDialog();

        }

        private void pLC通讯ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.主设备通讯)) { return; }

            // 按机型选对应的 PLC 服务实例：五金是 CIP 符号表，激光是 Fins 的 D 寄存器表，
            // 两者各有一份参数。传错了会出现"参数存下来了但连的是另一台的对象"。
            PLCCommunicationBase plc = Project.Instance.Is五金模切
                ? (PLCCommunicationBase)Project.Instance.server_五金模切PLC
                : Project.Instance.server_DiecuttingCutPLC;

            using (Form_DieCuttingCutPLC form = new Form_DieCuttingCutPLC(plc))
            {
                form.StartPosition = FormStartPosition.CenterScreen;
                form.ShowDialog();
            }
        }

        private void mark设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.Mark设置)) { return; }

            //using (Form_MarkSet form = new Form_MarkSet())
            //{
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}
        }

        private void 相机标定ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.相机标定)) { return; }
            //if (Project.Instance.systemInfo.CurrentMaterial == MaterialCrafEnum.叠片阳极单面制片CS)
            //{
            //    using (Form_SelectCamCalibrationPlate form = new Form_SelectCamCalibrationPlate(Project.Instance.M_CameraRelevancies))
            //    {
            //        form.StartPosition = FormStartPosition.CenterScreen;
            //        form.ShowDialog();
            //    }
            //}
            //else if (Project.Instance.systemInfo.CurrentMaterial == MaterialCrafEnum.叠片阳极双面制片CD)
            //{
            //    using (Form_SelectCamCalibrationPlate form = new Form_SelectCamCalibrationPlate(Project.Instance.M_CameraRelevancies))
            //    {
            //        form.StartPosition = FormStartPosition.CenterScreen;
            //        form.ShowDialog();
            //    }
            //}
            //else if (Project.Instance.systemInfo.CurrentMaterial == MaterialCrafEnum.叠片阴极双面制片AD)
            //{
            //    using (Form_SelectCamCalibrationPlate form = new Form_SelectCamCalibrationPlate(Project.Instance.M_CameraRelevancies))
            //    {
            //        form.StartPosition = FormStartPosition.CenterScreen;
            //        form.ShowDialog();
            //    }
            //}
            //else if (Project.Instance.systemInfo.CurrentMaterial == MaterialCrafEnum.叠片预定位PP)
            //{
            //    using (Form_SelectCamCheckerboard form = new Form_SelectCamCheckerboard(Project.Instance.M_CameraRelevancies))
            //    {
            //        form.StartPosition = FormStartPosition.CenterScreen;
            //        form.ShowDialog();
            //    }
            //}
            //else if (Project.Instance.systemInfo.CurrentMaterial == MaterialCrafEnum.叠片对齐度OVH)
            //{
            //    using (Form_SelectCamCheckerboard form = new Form_SelectCamCheckerboard(Project.Instance.M_CameraRelevancies))
            //    {
            //        form.StartPosition = FormStartPosition.CenterScreen;
            //        form.ShowDialog();
            //    }
            //}

        }

        private void 预定位平台标定ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.相机标定)) { return; }
            //if (Project.Instance.systemInfo.CurrentMaterial == MaterialCrafEnum.叠片预定位PP)
            //{
            //    using (Form_PretargetingCalibration form = new Form_PretargetingCalibration(Project.Instance.M_CameraRelevancies))
            //    {
            //        form.StartPosition = FormStartPosition.CenterScreen;
            //        form.ShowDialog();
            //    }

            //    //Form_PretargetingCalibration form = new Form_PretargetingCalibration(Project.Instance.M_CameraRelevancies);
            //    // form.StartPosition = FormStartPosition.CenterScreen;
            //    //form.Show();
            //}

        }

        private void 打标机软件通讯ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.控制卡)) { return; }
            ServiceCardManage.Instance.ShowWindow();
            //using (DBJCardForm dBJCardForm = new DBJCardForm(Project.Instance.dbjCardManager))
            //{
            //    dBJCardForm.StartPosition = FormStartPosition.CenterScreen;
            //    dBJCardForm.ShowDialog();
            //}

            //using (TCPClientForm dBJCardForm = new TCPClientForm(Project.Instance.dbjCardManager))
            //{
            //    dBJCardForm.StartPosition = FormStartPosition.CenterScreen;
            //    dBJCardForm.ShowDialog();
            //}
        }

        private void 生产统计ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.数据分析)) { return; }
            // “生产统计”= 打开当前膜卷的生产数据目录。
            // 旧实现直接 Process.Start(ProductMojuanPath)，该值是目录而非可执行文件，
            // 目录尚未生成（未开卷/换了存储盘）时 Win32Exception：系统找不到指定的文件。
            // 目录校验、启动与失败提示统一收敛到 ShellPathLauncher。
            Frame_Core.ShellPathLauncher.OpenFolder(Project.Instance.paramPathSet.ProductMojuanPath, "生产统计");
        }

        private void 纠偏设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.纠偏器)) { return; }
            //using (Form_Thick3DSet form = new Form_Thick3DSet(Project.Instance.thick3DManages))
            //{
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}

        }

        private void 保存设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.保存设置)) { return; }
            //using (SaveSetingForm form = new SaveSetingForm())
            //{
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}

            WindowImageSave window = new WindowImageSave(Project.Instance.M_NewImageSaveParamer);
            window.ShowDialog();
        }

        private void 仿真设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void 尺寸点检ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.点检)) { return; }
            //using (FormPointInspection form = new FormPointInspection(Project.Instance.M_param_PointInspection))
            //{
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}
        }

        private void chineseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Project.Instance.systemInfo.m_CurrentLanguageType = LanguageEnum.Chinese;
            Project.Instance.systemInfo.Save();

        }

        private void englishToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Project.Instance.systemInfo.m_CurrentLanguageType = LanguageEnum.English;
            Project.Instance.systemInfo.Save();

        }



        private void pC通讯设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.PC通讯设置)) { return; }
            //using (MainSlaveMachineForm form = new MainSlaveMachineForm())
            //{
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}
        }

        /// <summary>
        /// 仿真加载图像锁标志
        /// </summary>
        // 旧仿真取图定时器（timer_SimLoadImage）已移除：仿真取图下沉到 SimCamera 的采集线程。

        private void 磁盘设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.磁盘设置)) { return; }
            if (!Project.Instance.driveParameter.IsShowedWarning)
            {
                Project.Instance.driveParameter.IsShowedWarning = true;
                using (Drive_Form disk_Form = new Drive_Form(Project.Instance.driveParameter))
                {
                    disk_Form.StartPosition = FormStartPosition.CenterScreen;
                    disk_Form.ShowDialog();


                }
            }
        }



        /// <summary>
        /// 获取软件编译时间，作用：用作软件界面版本时间显示  
        /// </summary>
        public void Init_LatestTimeProgram(string millType)
        {
            try
            {
                //===============================================================================================================
                //==========【】获取最后编译时间：
                Str_LatestTimeProgram = System.IO.File.GetLastWriteTime(this.GetType().Assembly.Location).ToString("yyyyMMdd-HHmm");

                Str_LatestTimeProgram = System.IO.File.GetLastWriteTime(this.GetType().Assembly.Location).ToString("yyyyMMdd-HHmm");

                // 标题统一为「HSVISION - 机型」，机型来自 SystemParam.json 的 CraftType。
                // 枚举成员名与 Description 一致（激光模切一体机 / 五金模切），也与配置目录同名。
                MaterialCrafEnum craftType = Project.Instance != null && Project.Instance.systemInfo != null
                    ? Project.Instance.systemInfo.CraftType
                    : MaterialCrafEnum.激光模切一体机;
                this.Text = "HSVISION - " + craftType.ToString();
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error(ex);
            }
        }

        private void 控制卡ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.控制卡)) { return; }

            //using (Form_ControlCard form = new Form_ControlCard(Project.Instance.bIZ_ControlCard))
            //{
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}       
        }

        private void 点检校验ToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void mSA圆点设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.MSA圆点设置)) { return; }
            //using (Form_MsaCircle form = new Form_MsaCircle())
            //{
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}
        }

        private void mSA线宽设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.MSA线宽设置)) { return; }
            //using (Form_MsaLineWidth form = new Form_MsaLineWidth())
            //{
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}
        }

        private void 数据分析ToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void menuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {


        }

        private void mSA棋盘格线宽设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.MSA棋盘格线宽设置)) { return; }
            //using (Form_MsaCheckBoardLine form = new Form_MsaCheckBoardLine(Project.Instance.paramPathSet.MsaLineCheckBoradParamPath))
            //{
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}
        }

        private void mSA3D测厚ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.MSA测厚3D设置)) { return; }

        }

        private void 基础设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.基础设置)) { return; }
            using (Form_BaseSetting form = new Form_BaseSetting(Project.Instance.M_param_PointInspection))
            {
                form.StartPosition = FormStartPosition.CenterScreen;
                form.ShowDialog();
            }
        }

        private void 主副机通讯ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.主副机通讯)) { return; }
            //using (SlaveMachineForm form = new SlaveMachineForm(Project.Instance.slaveMachineManager))
            //{
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}
        }

        private void 图片管理ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.图片管理)) { return; }
            using (Form_ImageManage form = new Form_ImageManage())
            {
                form.StartPosition = FormStartPosition.CenterScreen;
                form.ShowDialog();
            }
        }

        private void 数据查询ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.数据分析)) { return; }
            // 生产数据根目录由 Project.Init / 磁盘设置维护，不能写死 E:\生产数据；
            // 写死时换盘或开发机没有 E: 盘会导致数据查询永远为空。
            string root = Project.Instance.paramPathSet.initProdDataPath;
            using (FrmCsvViewer form = new FrmCsvViewer(root))
            {
                form.StartPosition = FormStartPosition.CenterScreen;
                form.ShowDialog();
            }
        }

        private void 灰度自适应ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!Project.Instance.purviewManage.GetWhetherPurview(Project.Instance.userManage.userData.CurrentUser, ModelName.灰度自适应)) { return; }
            //using (FormGrayScaleAdaptive form = new FormGrayScaleAdaptive())
            //{
            //    LanguageProject.ChangeLanguage(Project.Instance.systemInfo.m_CurrentLanguageType, form);
            //    form.StartPosition = FormStartPosition.CenterScreen;
            //    form.ShowDialog();
            //}
        }

        private void timer_DJ_Tick(object sender, EventArgs e)
        {
            try
            {
                timer_DJ.Enabled = false;
                PointInspectionWorks.PointInspectionHint();
                timer_DJ.Enabled = true;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error(ex);
            }
        }


        bool backuppState = false;
        private void timer_CraftBack_Tick(object sender, EventArgs e)
        {
            if (backuppState) return;
            backuppState = true;
            Task.Run(() => { BackCraftParam(); });
         
        }

        public void BackCraftParam()
        {
            try
            {

                string path = CraftExtension.GetRecipeFilePath(Project.Instance.paramPathSet.CraftPath, Project.Instance.systemInfo.CraftType, Project.Instance.systemInfo.RecipeName);
                string NewPath = Path.Combine(Project.Instance.paramPathSet.CaraftBackPath, Project.Instance.systemInfo.CraftType.ToString());
                Directory.CreateDirectory(NewPath);
                NewPath = Path.Combine(NewPath, $"{Project.Instance.systemInfo.RecipeName}.json");
                File.Copy(path, NewPath, true);

                string DirectoryPath = CraftExtension.GetRecipeFilePath(Project.Instance.paramPathSet.CraftPath, Project.Instance.systemInfo.CraftType, Project.Instance.systemInfo.RecipeName, false);
                string NewDirectoryPath = Path.Combine(Project.Instance.paramPathSet.CaraftBackPath, Project.Instance.systemInfo.CraftType.ToString(), $"{Project.Instance.systemInfo.RecipeName}");
                CopyDirectory(DirectoryPath, NewDirectoryPath);


            }
            catch (Exception ex)
            {
                MessageNotify.AddErrorLog(ex.Message + "\r\n" + ex.StackTrace);
            }
            backuppState = false;
        }
        /// <summary>
        /// 递归复制目录及其所有子文件/文件夹
        /// </summary>
        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }
}
