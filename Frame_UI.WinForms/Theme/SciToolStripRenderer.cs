using System.Drawing;
using System.Windows.Forms;

namespace Frame_UI.WinForms
{
    /// <summary>
    /// ToolStrip / MenuStrip / StatusStrip 的统一渲染器。
    /// WinForm 的 ToolStrip 只能通过 Renderer 改变外观，无法用属性完成，
    /// 因此这是唯一允许自绘工具栏的地方。
    /// </summary>
    public sealed class SciToolStripRenderer : ToolStripProfessionalRenderer
    {
        public SciToolStripRenderer(UiTokens tokens)
            : base(new SciColorTable(tokens))
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? ColorBridge.TextPrimary : ColorBridge.TextHint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = ColorBridge.TextSecondary;
            base.OnRenderArrow(e);
        }

        private sealed class SciColorTable : ProfessionalColorTable
        {
            public SciColorTable(UiTokens tokens)
            {
                UseSystemColors = false;
                _panel = ColorBridge.ToColor(tokens.Panel);
                _panelDeep = ColorBridge.ToColor(tokens.PanelDeep);
                _line = ColorBridge.ToColor(tokens.Line);
                _lineSubtle = ColorBridge.ToColor(tokens.LineSubtle);
                _hover = ColorBridge.ToColor(tokens.ButtonHoverBg);
                _selection = ColorBridge.ToColor(tokens.SelectionBg);
                _accent = ColorBridge.ToColor(tokens.AccentPrimary);
            }

            private readonly Color _panel;
            private readonly Color _panelDeep;
            private readonly Color _line;
            private readonly Color _lineSubtle;
            private readonly Color _hover;
            private readonly Color _selection;
            private readonly Color _accent;

            public override Color ToolStripGradientBegin { get { return _panelDeep; } }
            public override Color ToolStripGradientMiddle { get { return _panelDeep; } }
            public override Color ToolStripGradientEnd { get { return _panelDeep; } }
            public override Color ToolStripBorder { get { return _lineSubtle; } }

            public override Color ButtonSelectedHighlight { get { return _hover; } }
            public override Color ButtonSelectedHighlightBorder { get { return _accent; } }
            public override Color ButtonSelectedGradientBegin { get { return _hover; } }
            public override Color ButtonSelectedGradientMiddle { get { return _hover; } }
            public override Color ButtonSelectedGradientEnd { get { return _hover; } }

            public override Color ButtonPressedHighlight { get { return _selection; } }
            public override Color ButtonPressedHighlightBorder { get { return _accent; } }
            public override Color ButtonPressedGradientBegin { get { return _selection; } }
            public override Color ButtonPressedGradientMiddle { get { return _selection; } }
            public override Color ButtonPressedGradientEnd { get { return _selection; } }

            public override Color ButtonCheckedGradientBegin { get { return _selection; } }
            public override Color ButtonCheckedGradientMiddle { get { return _selection; } }
            public override Color ButtonCheckedGradientEnd { get { return _selection; } }
            public override Color ButtonCheckedHighlight { get { return _selection; } }
            public override Color ButtonCheckedHighlightBorder { get { return _accent; } }

            public override Color SeparatorDark { get { return _line; } }
            public override Color SeparatorLight { get { return _lineSubtle; } }

            public override Color MenuItemSelected { get { return _hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return _hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return _hover; } }
            public override Color MenuItemBorder { get { return _accent; } }
            public override Color MenuBorder { get { return _line; } }
            public override Color MenuItemPressedGradientBegin { get { return _selection; } }
            public override Color MenuItemPressedGradientEnd { get { return _selection; } }

            public override Color ImageMarginGradientBegin { get { return _panel; } }
            public override Color ImageMarginGradientMiddle { get { return _panel; } }
            public override Color ImageMarginGradientEnd { get { return _panel; } }

            public override Color OverflowButtonGradientBegin { get { return _panelDeep; } }
            public override Color OverflowButtonGradientMiddle { get { return _panelDeep; } }
            public override Color OverflowButtonGradientEnd { get { return _panelDeep; } }

            public override Color CheckBackground { get { return _selection; } }
            public override Color CheckSelectedBackground { get { return _accent; } }
            public override Color CheckPressedBackground { get { return _accent; } }
        }
    }
}