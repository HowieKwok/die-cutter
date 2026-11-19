using System.Drawing;
using System.Windows.Forms;

namespace Frame_UI.WinForms
{
    /// <summary>
    /// 把令牌下发到整棵 WinForm 控件树。
    ///
    /// 存在理由：WinForm 没有样式系统，而 20 个插件的 Designer 里已有 600 余处
    /// BackColor/ForeColor。逐处删除既不可维护也无法覆盖第三方控件，
    /// 因此统一在窗体加载后做一次后置覆盖，Designer 保持原样。
    ///
    /// 幂等要求：可被重复调用（主题切换时会再次下发），不得订阅事件或累积状态。
    /// </summary>
    public static class WinFormsThemeApplier
    {
        /// <summary>以当前令牌重新下发整棵树。</summary>
        public static void Apply(Control root)
        {
            if (root == null) return;
            ApplyCore(root, ThemeService.Current);
        }

        private static void ApplyCore(Control control, UiTokens tokens)
        {
            if (control == null) return;
            if (control.Site != null && control.Site.DesignMode) return;

            ApplyOwnStyle(control, tokens);

            Control[] children = new Control[control.Controls.Count];
            control.Controls.CopyTo(children, 0);
            foreach (Control child in children)
            {
                ApplyCore(child, tokens);
            }
        }

        private static void ApplyOwnStyle(Control control, UiTokens tokens)
        {
            var form = control as Form;
            if (form != null)
            {
                form.BackColor = ColorBridge.Canvas;
                form.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            var userControl = control as UserControl;
            if (userControl != null)
            {
                userControl.BackColor = ColorBridge.Canvas;
                userControl.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            // StatusStrip 必须先于 ToolStrip 判定：它继承自 ToolStrip，
            // 顺序反了会永远走不到这个分支（颜色与对齐都和普通工具条不同）。
            var statusStrip = control as StatusStrip;
            if (statusStrip != null)
            {
                statusStrip.BackColor = ColorBridge.PanelDeep;
                statusStrip.ForeColor = ColorBridge.TextSecondary;
                return;
            }

            var toolStrip = control as ToolStrip;
            if (toolStrip != null)
            {
                toolStrip.Renderer = new SciToolStripRenderer(tokens);
                toolStrip.BackColor = ColorBridge.PanelDeep;
                toolStrip.ForeColor = ColorBridge.TextPrimary;
                toolStrip.GripStyle = ToolStripGripStyle.Hidden;
                return;
            }

            var grid = control as DataGridView;
            if (grid != null)
            {
                ApplyGrid(grid);
                return;
            }

            var tab = control as TabControl;
            if (tab != null)
            {
                tab.BackColor = ColorBridge.Panel;
                tab.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            // CheckedListBox 继承自 ListBox，必须先匹配子类型，否则永远走不到。
            var checkList = control as CheckedListBox;
            if (checkList != null)
            {
                checkList.BackColor = ColorBridge.PanelDeep;
                checkList.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            var listBox = control as ListBox;
            if (listBox != null)
            {
                listBox.BackColor = ColorBridge.PanelDeep;
                listBox.ForeColor = ColorBridge.TextPrimary;
                listBox.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            var textBox = control as TextBoxBase;
            if (textBox != null)
            {
                textBox.BackColor = ColorBridge.PanelDeep;
                textBox.ForeColor = ColorBridge.TextPrimary;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            var combo = control as ComboBox;
            if (combo != null)
            {
                combo.BackColor = ColorBridge.PanelDeep;
                combo.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = ColorBridge.ButtonBg;
                button.ForeColor = ColorBridge.TextPrimary;
                button.FlatAppearance.BorderColor = ColorBridge.ButtonBorder;
                button.FlatAppearance.MouseOverBackColor = ColorBridge.ButtonHoverBg;
                button.UseVisualStyleBackColor = false;
                return;
            }

            var label = control as Label;
            if (label != null)
            {
                label.BackColor = Color.Transparent;
                label.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            var groupBox = control as GroupBox;
            if (groupBox != null)
            {
                groupBox.BackColor = Color.Transparent;
                groupBox.ForeColor = ColorBridge.AccentMuted;
                return;
            }

            var panel = control as Panel;
            if (panel != null)
            {
                panel.BackColor = ColorBridge.Canvas;
                panel.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            // 兜底：第三方控件（含 AntdUI）只下发基类颜色。
            // AntdUI 的模板色需要其自身的颜色配置，属于后续适配项，
            // 这里不做猜测性设置，避免把控件画坏。
            control.BackColor = ColorBridge.Panel;
            control.ForeColor = ColorBridge.TextPrimary;
        }

        private static void ApplyGrid(DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = ColorBridge.Panel;
            grid.GridColor = ColorBridge.LineSubtle;
            grid.ForeColor = ColorBridge.TextPrimary;
            grid.DefaultCellStyle.BackColor = ColorBridge.Panel;
            grid.DefaultCellStyle.ForeColor = ColorBridge.TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = ColorBridge.SelectionBg;
            grid.DefaultCellStyle.SelectionForeColor = ColorBridge.TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.BackColor = ColorBridge.PanelDeep;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ColorBridge.AccentMuted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = ColorBridge.PanelDeep;
            grid.RowHeadersDefaultCellStyle.BackColor = ColorBridge.PanelDeep;
            grid.RowHeadersDefaultCellStyle.ForeColor = ColorBridge.TextSecondary;
            grid.AlternatingRowsDefaultCellStyle.BackColor = ColorBridge.PanelDeep;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = ColorBridge.TextPrimary;
        }
    }
}