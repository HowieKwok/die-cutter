using Frame_Core;
using Frame_MetalRun;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金换卷按钮与确认流程。放在独立 partial，避免改动设计器生成的代码。
    /// 可用性只读 MetalRunButtons；“卷号是否已改”由这里比较输入框与当前卷号。
    /// </summary>
    public partial class UC_ToolView
    {
        private ControlButton btn_SwitchRoll;

        /// <summary>懒创建换卷按钮，插到“开始/结束膜卷”左侧。</summary>
        private void EnsureSwitchRollButton()
        {
            if (btn_SwitchRoll != null) return;

            btn_SwitchRoll = new ControlButton
            {
                Name = "btn_SwitchRoll",
                Size = new Size(100, 34),   // 与“开始/结束膜卷”一致：图标 + 文本
                TabIndex = 15,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                UnCheckedText = "换卷",
                CheckedText = "换卷",
                UnCheckedBackColor = Color.FromArgb(30, 110, 220),
                CheckedBackColor = Color.FromArgb(30, 110, 220),
                KeepStateColors = true,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                ForeColor = Frame_UI.WinForms.ColorBridge.TextPrimary,
            };
            btn_SwitchRoll.FlatAppearance.BorderColor = Frame_UI.WinForms.ColorBridge.ButtonBorder;
            btn_SwitchRoll.FlatAppearance.MouseOverBackColor = Frame_UI.WinForms.ColorBridge.ButtonHoverBg;
            ApplySwitchRollGlyph();
            btn_SwitchRoll.Click += btn_SwitchRoll_Click;

            flowLayoutPanel1.Controls.Add(btn_SwitchRoll);
            // RightToLeft 布局：index 越大越靠左，放在开始/结束膜卷左侧
            flowLayoutPanel1.Controls.SetChildIndex(btn_SwitchRoll, 2);
            toolTips.SetToolTip(btn_SwitchRoll, "换卷（暂停态可用；流程不停，发布器短暂停止）");

            // 输入卷号时实时判断“卷号已改”，否则按钮要等下一次状态变化才解禁
            input_FilmRoll.TextChanged += delegate { RefreshSwitchRollButton(); };
        }

        /// <summary>换卷按钮的图标与文字色跟随主题；图标墨色按状态底色反差。</summary>
        private void ApplySwitchRollGlyph()
        {
            if (btn_SwitchRoll == null) return;

            Color back = btn_SwitchRoll.Checked ? btn_SwitchRoll.CheckedBackColor : btn_SwitchRoll.UnCheckedBackColor;
            Color ink = Frame_Core.ToolGlyph.ContrastInk(back);
            btn_SwitchRoll.UnCheckedImage = Frame_Core.ToolGlyph.SwitchRoll(18, ink);
            btn_SwitchRoll.CheckedImage = Frame_Core.ToolGlyph.SwitchRoll(18, ink);
            btn_SwitchRoll.ForeColor = Frame_UI.WinForms.ColorBridge.TextPrimary;
            btn_SwitchRoll.FlatAppearance.BorderColor = Frame_UI.WinForms.ColorBridge.ButtonBorder;
            btn_SwitchRoll.FlatAppearance.MouseOverBackColor = Frame_UI.WinForms.ColorBridge.ButtonHoverBg;
        }

        /// <summary>输入框内容变化后按控制器状态重算按钮可用性。</summary>
        private void RefreshSwitchRollButton()
        {
            MetalRunController run = MetalRun;
            if (run == null) return;
            UpdateSwitchRollButton(run.GetButtons(), run.State != MetalRunState.Idle);
        }

        /// <summary>工具栏刷新：可用性来自控制器，卷号未改时禁用。</summary>
        private void UpdateSwitchRollButton(MetalRunButtons buttons, bool started)
        {
            EnsureSwitchRollButton();
            btn_SwitchRoll.Visible = started;
            btn_SwitchRoll.Enabled = buttons.CanSwitchRoll &&
                !string.Equals(input_FilmRoll.Text, Project.Instance.systemInfo.currentMojuanhao);
        }

        private void btn_SwitchRoll_Click(object sender, EventArgs e)
        {
            MetalRunController run = MetalRun;
            if (run == null) return;

            string error;
            MetalRunPending pending;
            SwitchRollResult result = run.SwitchFilmRoll(input_FilmRoll.Text, false, out pending, out error);

            if (result == SwitchRollResult.NeedsConfirm)
            {
                string message =
                    "换卷会先把旧卷统计落盘，再丢弃以下未处理项；" + Environment.NewLine +
                    "新卷从第 1 片重新计数。" + Environment.NewLine + Environment.NewLine +
                    pending.ToDisplayText() + Environment.NewLine + Environment.NewLine +
                    "说明：" + Environment.NewLine +
                    "· PLC 待发送结果中的“含待检”可能已在发布器停止前发出，其余随清场丢弃；" + Environment.NewLine +
                    "· 合片在途的片不会产生完整统计，也不会再发 PLC 结果。" + Environment.NewLine + Environment.NewLine +
                    "是否切换到膜卷「" + input_FilmRoll.Text + "」？";

                if (MessageBox.Show(message, "换卷", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                result = run.SwitchFilmRoll(input_FilmRoll.Text, true, out pending, out error);
            }

            if (result == SwitchRollResult.Failed)
            {
                if (!string.IsNullOrEmpty(error))
                    MessageBox.Show(error, "换卷", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (result == SwitchRollResult.DegradedToIdle)
            {
                MessageBox.Show("换卷失败，已结束当前膜卷，请重新开始。" +
                                (string.IsNullOrEmpty(error) ? "" : Environment.NewLine + error),
                                "换卷", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (result == SwitchRollResult.DegradeFailed)
            {
                MessageBox.Show(string.IsNullOrEmpty(error)
                    ? "换卷失败且降级失败，请重启软件后再生产。"
                    : error, "换卷", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            RefreshView();
        }
    }
}
