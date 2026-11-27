using Frame_Core;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>状态栏三态 chip 的状态。与 Frame_MetalRun.MetalRunState 一一对应，避免 UI 直接依赖控制器。</summary>
    public enum RunStateChipState
    {
        Idle = 0,
        Paused = 1,
        Running = 2,
    }

    /// <summary>
    /// 底部状态栏的空闲/暂停/运行指示器。
    /// 自绘而不是复用 StatusLabelItem：需要图标 + 状态色 + Tooltip，而现有 Label chip 只有文字和底色。
    /// </summary>
    internal sealed class RunStateChip : Control
    {
        private RunStateChipState _state = RunStateChipState.Idle;
        private string _tooltip = string.Empty;

        public RunStateChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            Size = new Size(84, 22);
            Margin = new Padding(3, 4, 6, 4);
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            Cursor = Cursors.Default;
            _tooltip = BuildTooltip(_state);
        }

        public RunStateChipState State
        {
            get { return _state; }
        }

        public string TooltipText
        {
            get { return _tooltip; }
        }

        public void SetState(RunStateChipState state)
        {
            if (_state == state) return;
            _state = state;
            _tooltip = BuildTooltip(state);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Color back = StateColor(_state);
            string text = StateText(_state);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2f))
            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillPath(brush, path);
            }

            int iconSize = 13;
            int iconTop = (Height - iconSize) / 2;
            using (Image icon = StateIcon(_state, iconSize))
            {
                if (icon != null) e.Graphics.DrawImage(icon, 8, iconTop, iconSize, iconSize);
            }

            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                new Rectangle(27, 0, Width - 31, Height),
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static string StateText(RunStateChipState state)
        {
            switch (state)
            {
                case RunStateChipState.Running: return "运行";
                case RunStateChipState.Paused: return "暂停";
                default: return "空闲";
            }
        }

        private static Color StateColor(RunStateChipState state)
        {
            switch (state)
            {
                case RunStateChipState.Running: return UC_StateView.NormalBackColor;
                case RunStateChipState.Paused: return Frame_UI.WinForms.ColorBridge.StateWarn;
                default: return Frame_UI.WinForms.ColorBridge.StateIdle;
            }
        }

        private static Image StateIcon(RunStateChipState state, int size)
        {
            switch (state)
            {
                case RunStateChipState.Running: return ToolGlyph.Play(size, Color.White);
                case RunStateChipState.Paused: return ToolGlyph.Pause(size, Color.White);
                default: return ToolGlyph.IdleDot(size, Color.White);
            }
        }

        private static string BuildTooltip(RunStateChipState state)
        {
            switch (state)
            {
                case RunStateChipState.Running:
                    return "运行中：视觉流程正在处理帧。点【暂停】只暂停视觉，PLC 结果发布不停止。";
                case RunStateChipState.Paused:
                    return "已暂停：视觉流程暂停。自动模式帧仍会入队（每相机上限 8 帧），PLC 继续发布；仿真模式不读取本地图片。";
                default:
                    return "空闲：未开始膜卷。PLC 触发不会产生结果，请先点【开始膜卷】。";
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(bounds.Left, bounds.Top, d, d, 180f, 90f);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270f, 90f);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0f, 90f);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }
}
