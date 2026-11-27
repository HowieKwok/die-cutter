using Frame_Core;
using Frame_MetalRun;
using LogTool;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevMgmt.Light.Models;

namespace Mef_VisionFrame
{
    public partial class UC_ToolView : UCViewBase
    {
        VMBase VM;
        private readonly ToolTip toolTips = new ToolTip
        {
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = 6000,
        };

        public UC_ToolView(VMBase _VM)
        {
            InitializeComponent();
            VM = _VM;

            // 工具栏图标按视觉尺寸缩放：Button.Image 按原始像素绘制，
            // 资源里的图标比按钮本身还大，会顶到边框、压住圆角。
            btn_Reset.Image = Frame_Core.ImageScaler.Fit(Properties.Resources.icon_reset, 20, 20);

            ApplyToolGlyphs();
            ApplyToolTips();
            ApplyAutoModeVisibility();
            Frame_UI.ThemeService.ThemeChanged += OnThemeChanged;
            // 设计器已重写 Dispose(bool)，这里不再重复重写，用 Disposed 事件解订阅。
            Disposed += delegate
            {
                Frame_UI.ThemeService.ThemeChanged -= OnThemeChanged;
                toolTips.Dispose();
            };

            VM.BaseServer.RefreshToolView -= RefreshView;
            VM.BaseServer.RefreshToolView += RefreshView;
        }

        /// <summary>
        /// 五金自动模式不提供人工启停：按钮留着会让人以为可以手动停，
        /// 实际停机只能由复位/换卷/停线等入口触发。
        ///
        /// 仿真保留按钮——仿真本来就要人为开始和观察。
        /// </summary>
        private void ApplyAutoModeVisibility()
        {
            // 五金已改为“继续/暂停”，不再按运行模式隐藏；激光原本就始终显示。
            btn_run.Visible = true;
        }

        /// <summary>
        /// 图标按钮没有文字，鼠标悬停必须能说明用途。
        /// 只给“纯图标/语义不明”的按钮挂提示，带文字的按钮不重复一遍。
        /// </summary>
        private void ApplyToolTips()
        {
            toolTips.SetToolTip(btn_Prev, "上一片");
            toolTips.SetToolTip(btn_run, Project.Instance.Is五金模切
                ? "继续 / 暂停（只暂停视觉流程，PLC 结果发布不停止）"
                : "启动 / 停止");
            toolTips.SetToolTip(btn_next, "下一片");
            toolTips.SetToolTip(btn_Reset, "CCD 软件复位");
            toolTips.SetToolTip(btn_AutoSim, "运行模式（启动时由配置决定，运行期不可切换）");
            toolTips.SetToolTip(btn_ClearCutData, "清除纠偏队列");
            toolTips.SetToolTip(btn_FilmRollSwitch, "开始 / 结束膜卷");
            toolTips.SetToolTip(input_FilmRoll, "当前膜卷号");
        }

        /// <summary>
        /// 启动/上一片/下一片改用几何图标重绘。
        ///
        /// 不继续用 ▶ ■ ◀◀ 这类字符的原因：字形取决于系统字体收录情况
        /// （真机表现为上一片只剩一条竖杠、下一片与启动键同形），也不能跟随状态色。
        /// </summary>
        private void ApplyToolGlyphs()
        {
            bool dark = Frame_UI.ThemeService.CurrentThemeName == Frame_UI.UiThemeCatalog.DarkThemeName;
            Color ink = dark ? Color.FromArgb(232, 251, 255) : Color.FromArgb(20, 32, 44);

            // 五金启动键改为文字“继续/暂停”，始终显示；激光保留绿/红图标。
            if (Project.Instance.Is五金模切)
            {
                // 五金：继续/暂停只显示图标（Checked=运行中→暂停图标），语义交给悬停提示
                btn_run.UnCheckedText = string.Empty;
                btn_run.CheckedText = string.Empty;
                btn_run.UnCheckedImage = Frame_Core.ToolGlyph.Play(20,
                    Frame_Core.ToolGlyph.ContrastInk(btn_run.UnCheckedBackColor));
                btn_run.CheckedImage = Frame_Core.ToolGlyph.Pause(20,
                    Frame_Core.ToolGlyph.ContrastInk(btn_run.CheckedBackColor));
                btn_run.Size = new Size(34, 34);
            }
            else
            {
                // 启动键：图标墨色按当前状态底色反差推算（绿底深箭头 / 红底白方块）
                btn_run.UnCheckedText = string.Empty;
                btn_run.CheckedText = string.Empty;
                btn_run.UnCheckedImage = Frame_Core.ToolGlyph.Play(20, Frame_Core.ToolGlyph.ContrastInk(btn_run.UnCheckedBackColor));
                btn_run.CheckedImage = Frame_Core.ToolGlyph.Stop(20, Frame_Core.ToolGlyph.ContrastInk(btn_run.CheckedBackColor));
            }

            if (btn_run.Parent != null) btn_run.Parent.PerformLayout();

            btn_Prev.Text = string.Empty;
            btn_Prev.Image = Frame_Core.ToolGlyph.SkipPrev(18, ink);

            btn_next.Text = string.Empty;
            btn_next.Image = Frame_Core.ToolGlyph.SkipNext(18, ink);

            // 模式键：未选中=自动（齿轮/绿），选中=仿真（锥形瓶/琥珀）。
            // 墨色由各自底色反差推算，换配色不用再改这里。只显示图标，不带文字。
            btn_AutoSim.UnCheckedText = string.Empty;
            btn_AutoSim.CheckedText = string.Empty;
            btn_AutoSim.UnCheckedImage = Frame_Core.ToolGlyph.AutoMode(18,
                Frame_Core.ToolGlyph.ContrastInk(btn_AutoSim.UnCheckedBackColor));
            btn_AutoSim.CheckedImage = Frame_Core.ToolGlyph.SimMode(18,
                Frame_Core.ToolGlyph.ContrastInk(btn_AutoSim.CheckedBackColor));

            // 膜卷键：图标固定是卷轴，状态差异由底色（开始绿 / 结束蓝）承载。
            btn_FilmRollSwitch.UnCheckedImage = Frame_Core.ToolGlyph.FilmRoll(18,
                Frame_Core.ToolGlyph.ContrastInk(btn_FilmRollSwitch.UnCheckedBackColor));
            btn_FilmRollSwitch.CheckedImage = Frame_Core.ToolGlyph.FilmRoll(18,
                Frame_Core.ToolGlyph.ContrastInk(btn_FilmRollSwitch.CheckedBackColor));

            // 普通主题按钮：底色是 ButtonBg，墨色直接用正文色。
            btn_ClearCutData.Image = Frame_Core.ToolGlyph.ClearData(18, ink);

            // 换卷按钮可能尚未创建（Idle 且未进入工具栏刷新），存在时同步图标与文字色
            if (btn_SwitchRoll != null) ApplySwitchRollGlyph();
        }

        private void OnThemeChanged(object sender, Frame_UI.UiTokens tokens)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(ApplyToolGlyphs)); return; }
            ApplyToolGlyphs();
        }
        private void UC_MainView_Load(object sender, EventArgs e)
        {
            RefreshView();
            panel_DataShow.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(this.panel_DataShow, true);
        }

        /// <summary>
        /// 模式按钮只读：保持 Enabled 才能显示 ToolTip，因此用点击事件把选中态拉回配置值，
        /// 抵消控件自身的 Checked 翻转，保证图标始终等于当前运行模式。
        /// </summary>
        private void btn_AutoSim_Click(object sender, EventArgs e)
        {
            btn_AutoSim.Checked = Project.Instance.Is_simulation;
        }

        private void btn_Prev_Click(object sender, EventArgs e)
        {
            if (StartMetalStep(false)) return;

            // 激光保留原空实现
        }

        private void btn_run_Click(object sender, EventArgs e)
        {
            MetalRunController run = MetalRun;
            if (run != null)
            {
                string error;
                if (run.State == MetalRunState.Running)
                {
                    if (!run.Pause(out error)) MessageBox.Show(error ?? "暂停失败");
                }
                else if (run.State == MetalRunState.Paused)
                {
                    if (!run.Resume(out error)) MessageBox.Show(error ?? "继续失败");
                }
                return;
            }

            ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                if (Project.Instance.Is_Run)
                {
                    VM.BaseServer.StopRun();
                }
                else
                {
                    // 自动/仿真共用同一条启动路径：差异只存在于相机与 PLC 的底层实现。
                    VM.BaseServer.StartRun();
                }
            });
        }

        private void btn_next_Click(object sender, EventArgs e)
        {
            if (StartMetalStep(true)) return;

            if (Project.Instance.Is_simulation)
                VM.BaseData.M_SimRunCommand = SimRunCommand.SimNext;
        }

        /// <summary>
        /// 单步会阻塞到尺寸等齐最长 1.5s；放后台线程，避免主界面假死。
        /// 控制器 StepBusy 与按钮禁用共同防重入。
        /// </summary>
        private bool StartMetalStep(bool forward)
        {
            MetalRunController run = MetalRun;
            if (run == null) return false;

            // 仿真在途令牌会与单步手动帧号冲突；先让操作员确认丢弃，再清空执行
            bool discardInFlight = false;
            int inFlight = run.GetSimInFlightCount();
            if (inFlight > 0)
            {
                string message = "仿真有 " + inFlight + " 片在途，单步将丢弃这些仿真令牌，是否继续？";
                if (MessageBox.Show(message, "单步", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return true;
                discardInFlight = true;
            }

            btn_Prev.Enabled = false;
            btn_next.Enabled = false;

            Task.Run(() =>
            {
                string error = null;
                bool ok = false;
                try
                {
                    ok = run.Step(forward, discardInFlight, out error);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (!ok) MessageBox.Show(error ?? "单步执行失败");
                        RefreshView();   // 成功也要刷新：任一事件订阅者异常时按钮不能卡死
                    }));
                }
                catch (ObjectDisposedException) { }
            });

            return true;
        }

        private void switch_LightOnOff_Click(object sender, EventArgs e)
        {
            Project.Instance.LightOnOff = switch_LightOnOff.Checked;
            VM.BaseServer?.thd_LightControl.ReleaseSemaphore();
        }

        private void btn_Reset_Click(object sender, EventArgs e)
        {
            DialogResult result = System.Windows.Forms.MessageBox.Show("CCD软件复位？", "询问", MessageBoxButtons.YesNo);
            if (result == DialogResult.No) return;
            VM.BaseServer.Reset();
        }

        private void btn_FilmRollSwitch_Click(object sender, EventArgs e)
        {
            MetalRunController run = MetalRun;
            if (run != null)
            {
                if (run.State == MetalRunState.Idle)
                {
                    string startError;
                    if (!run.StartFilmRoll(input_FilmRoll.Text, out startError))
                        MessageBox.Show(startError ?? "开始膜卷失败");
                    return;
                }

                MetalRunPending pending;
                string error;
                EndRollResult result = run.EndFilmRoll(false, out pending, out error);

                if (result == EndRollResult.NeedsConfirm)
                {
                    string message = "当前还有未处理项：" + Environment.NewLine + Environment.NewLine +
                                     pending.ToDisplayText() + Environment.NewLine + Environment.NewLine +
                                     "结束膜卷将丢弃这些数据，是否继续？";
                    if (MessageBox.Show(message, "结束膜卷", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;

                    result = run.EndFilmRoll(true, out pending, out error);
                }

                if (result == EndRollResult.Failed)
                    MessageBox.Show(error ?? "结束膜卷失败");
                return;
            }

            if (!Project.Instance.Is_MojuanStart)
            {
                //开始膜卷
                VM.BaseServer.StartFilmRoll(input_FilmRoll.Text);
            }
            else
            {
                //结束膜卷
                VM.BaseServer.StopFilmRoll();
            }
        }


        public override void RefreshView()
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                this.Invoke((EventHandler)delegate
                {
                    btn_run.Checked = Project.Instance.Is_Run;
                    btn_AutoSim.Checked = Project.Instance.Is_simulation;
                    // 模式只读：不能用点击切换，但必须保持 Enabled —— WinForms 的 ToolTip 不会为禁用控件弹出。
                    // 因此这里不 Disabled，改由 btn_AutoSim_Click 把选中态拉回配置值。
                    btn_AutoSim.Enabled = true;
                    toolTips.SetToolTip(btn_AutoSim, Project.Instance.Is_simulation ? "仿真模式" : "自动模式");
                    btn_FilmRollSwitch.Checked = Project.Instance.Is_MojuanStart;
                    switch_LightOnOff.Checked = Project.Instance.LightOnOff;

                    MetalRunController run = MetalRun;
                    if (run != null)
                    {
                        UpdateMetalToolbar(run);
                        return;
                    }

                    if (Project.Instance.Is_simulation)
                    {
                        if (Project.Instance.Is_MojuanStart)
                        {
                            input_FilmRoll.Text = Project.Instance.systemInfo.currentMojuanhao;
                            btn_AutoSim.Enabled = true;
                            btn_Prev.Enabled = false;
                            btn_next.Enabled = false;
                            input_FilmRoll.Enabled = false;
                            btn_Reset.Enabled = false;
                        }
                        else
                        {
                            btn_AutoSim.Enabled = true;
                            btn_Prev.Enabled = true;
                            btn_next.Enabled = true;
                            input_FilmRoll.Enabled = true;
                            btn_Reset.Enabled = true;
                        }

                        if (Project.Instance.Is_Run)
                        {
                            switch_LightOnOff.Enabled = false;
                            btn_AutoSim.Enabled = true;
                            btn_Prev.Enabled = false;
                            btn_next.Enabled = false;
                        }
                        else
                        {
                            switch_LightOnOff.Enabled = true;
                            btn_AutoSim.Enabled = true;
                            btn_Prev.Enabled = true;
                            btn_next.Enabled = true;
                        }
                    }
                    else
                    {
                        if (Project.Instance.Is_MojuanStart)
                        {
                            input_FilmRoll.Text = Project.Instance.systemInfo.currentMojuanhao;
                            btn_AutoSim.Enabled = true;
                            btn_Prev.Enabled = false;
                            btn_next.Enabled = false;
                            input_FilmRoll.Enabled = false;
                            btn_Reset.Enabled = false;
                        }
                        else
                        {
                            btn_AutoSim.Enabled = true;
                            btn_Prev.Enabled = true;
                            btn_next.Enabled = true;
                            input_FilmRoll.Enabled = true;
                            btn_Reset.Enabled = true;
                        }

                        if (Project.Instance.Is_Run)
                        {
                            switch_LightOnOff.Enabled = false;                        
                        }
                        else
                        {
                            switch_LightOnOff.Enabled = true;
                        }
                    }
                
                });

            });
        }

        private MetalRunController MetalRun
        {
            get
            {
                Server_五金模切 metal = VM.BaseServer as Server_五金模切;
                return metal == null ? null : metal.RunControl;
            }
        }

        /// <summary>
        /// 五金工具栏：按钮可用性完全来自控制器，不在界面里重新推断运行状态。
        /// </summary>
        private void UpdateMetalToolbar(MetalRunController run)
        {
            MetalRunButtons buttons = run.GetButtons();
            bool started = run.State != MetalRunState.Idle;

            btn_run.Visible = true;
            btn_run.Enabled = buttons.CanTogglePause;
            btn_run.Checked = run.State == MetalRunState.Running;

            btn_FilmRollSwitch.Visible = true;
            btn_FilmRollSwitch.Enabled = buttons.CanStartRoll || buttons.CanEndRoll;
            btn_FilmRollSwitch.Checked = started;
            if (started && !input_FilmRoll.Focused) input_FilmRoll.Text = Project.Instance.systemInfo.currentMojuanhao;

            input_FilmRoll.Enabled = buttons.CanEditRollName;
            switch_LightOnOff.Enabled = !started;

            // 自动模式没有“上一片/下一片”语义，直接隐藏；仿真暂停时才显示
            bool showStep = Project.Instance.Is_simulation;
            btn_Prev.Visible = showStep;
            btn_next.Visible = showStep;
            btn_Prev.Enabled = showStep && buttons.CanStepPrev;
            btn_next.Enabled = showStep && buttons.CanStepNext;

            // 五金没有裁切纠偏/软复位入口；隐藏而不是留着误点
            btn_Reset.Visible = false;
            btn_ClearCutData.Visible = false;

            UpdateSwitchRollButton(buttons, started);

            btn_AutoSim.Enabled = true;
            btn_AutoSim.Checked = Project.Instance.Is_simulation;
            toolTips.SetToolTip(btn_AutoSim, Project.Instance.Is_simulation ? "仿真模式" : "自动模式");
            toolTips.SetToolTip(btn_run, run.State == MetalRunState.Running
                ? "暂停（只暂停视觉流程，PLC 结果发布不停止）"
                : "继续");
        }

        private void btn_ClearCutData_Click(object sender, EventArgs e)
        {
            DialogResult result = System.Windows.Forms.MessageBox.Show("是否清除纠偏队列？", "询问", MessageBoxButtons.YesNo);
            if (result == DialogResult.No)
            {       
                return;
            }
            VM.BaseServer.ClearCutData(true);
        }
    }


}
