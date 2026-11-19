using Frame_Core;
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
        /// 图标按钮没有文字，鼠标悬停必须能说明用途。
        /// 只给“纯图标/语义不明”的按钮挂提示，带文字的按钮不重复一遍。
        /// </summary>
        private void ApplyToolTips()
        {
            toolTips.SetToolTip(btn_Prev, "上一片");
            toolTips.SetToolTip(btn_run, "启动 / 停止");
            toolTips.SetToolTip(btn_next, "下一片");
            toolTips.SetToolTip(btn_Reset, "CCD 软件复位");
            toolTips.SetToolTip(btn_AutoSim, "自动模式 / 仿真模式切换");
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

            // 启动键：图标墨色按当前状态底色反差推算（绿底深箭头 / 红底白方块）
            btn_run.UnCheckedText = string.Empty;
            btn_run.CheckedText = string.Empty;
            btn_run.UnCheckedImage = Frame_Core.ToolGlyph.Play(20, Frame_Core.ToolGlyph.ContrastInk(btn_run.UnCheckedBackColor));
            btn_run.CheckedImage = Frame_Core.ToolGlyph.Stop(20, Frame_Core.ToolGlyph.ContrastInk(btn_run.CheckedBackColor));

            btn_Prev.Text = string.Empty;
            btn_Prev.Image = Frame_Core.ToolGlyph.SkipPrev(18, ink);

            btn_next.Text = string.Empty;
            btn_next.Image = Frame_Core.ToolGlyph.SkipNext(18, ink);

            // 模式键：未选中=自动（齿轮/绿），选中=仿真（锥形瓶/琥珀）。
            // 墨色由各自底色反差推算，换配色不用再改这里。
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

        private void btn_AutoSim_Click(object sender, EventArgs e)
        {
            DialogResult result = System.Windows.Forms.MessageBox.Show("切换运行模式？", "询问", MessageBoxButtons.YesNo);
            if (result == DialogResult.No) return;

            VM.BaseServer.RunModelSwitch();
            RefreshView();
        }

        private void btn_Prev_Click(object sender, EventArgs e)
        {
            if (Project.Instance.Is_simulation)
                VM.BaseData.M_SimRunCommand = SimRunCommand.SimPrev;
        }

        private void btn_run_Click(object sender, EventArgs e)
        {
            ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                if (Project.Instance.Is_Run)
                {
                    VM.BaseServer.StopRun();
                }
                else
                {
                    //自动
                    if (!Project.Instance.Is_simulation)
                    {
                        VM.BaseServer.StartRun();
                    }
                    //仿真
                    else
                    {

                        foreach (ProcessServices item in Project.Instance.m_ProcessList)
                        {
                            Project.Instance.StartRun(item.process.processInfo.ProcessID);
                        }

                        VM.BaseData.M_SimRunCommand = SimRunCommand.SimRun;
                        RefreshView();
                    }
                }


            });

        }

        private void btn_next_Click(object sender, EventArgs e)
        {
            if (Project.Instance.Is_simulation)
                VM.BaseData.M_SimRunCommand = SimRunCommand.SimNext;
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
                    btn_FilmRollSwitch.Checked = Project.Instance.Is_MojuanStart;
                    switch_LightOnOff.Checked = Project.Instance.LightOnOff;

                    if (Project.Instance.Is_simulation)
                    {
                        if (Project.Instance.Is_MojuanStart)
                        {
                            input_FilmRoll.Text = Project.Instance.systemInfo.currentMojuanhao;
                            btn_AutoSim.Enabled = false;
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
                            btn_AutoSim.Enabled = false;
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
                            btn_AutoSim.Enabled = false;
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
