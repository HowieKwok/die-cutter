using System.Drawing;

namespace Frame_UI.WinForms
{
    /// <summary>
    /// 令牌色 → System.Drawing.Color 的唯一转换点。
    /// 适配器内所有控件颜色都必须经过这里，避免在各处散落 Color.FromArgb。
    /// </summary>
    public static class ColorBridge
    {
        public static Color ToColor(UiColor color)
        {
            return Color.FromArgb(color.R, color.G, color.B);
        }

        public static Color Canvas
        {
            get { return ToColor(ThemeService.Current.Canvas); }
        }

        public static Color Panel
        {
            get { return ToColor(ThemeService.Current.Panel); }
        }

        public static Color PanelDeep
        {
            get { return ToColor(ThemeService.Current.PanelDeep); }
        }

        public static Color Line
        {
            get { return ToColor(ThemeService.Current.Line); }
        }

        public static Color Divider
        {
            get { return ToColor(ThemeService.Current.Divider); }
        }

        public static Color LineSubtle
        {
            get { return ToColor(ThemeService.Current.LineSubtle); }
        }

        public static Color TextPrimary
        {
            get { return ToColor(ThemeService.Current.TextPrimary); }
        }

        public static Color TextSecondary
        {
            get { return ToColor(ThemeService.Current.TextSecondary); }
        }

        public static Color TextHint
        {
            get { return ToColor(ThemeService.Current.TextHint); }
        }

        public static Color AccentPrimary
        {
            get { return ToColor(ThemeService.Current.AccentPrimary); }
        }

        public static Color AccentMuted
        {
            get { return ToColor(ThemeService.Current.AccentMuted); }
        }

        public static Color StateOk
        {
            get { return ToColor(ThemeService.Current.StateOk); }
        }

        public static Color StateFail
        {
            get { return ToColor(ThemeService.Current.StateFail); }
        }

        public static Color StateWarn
        {
            get { return ToColor(ThemeService.Current.StateWarn); }
        }

        public static Color StateIdle
        {
            get { return ToColor(ThemeService.Current.StateIdle); }
        }

        public static Color ButtonBg
        {
            get { return ToColor(ThemeService.Current.ButtonBg); }
        }

        public static Color ButtonBorder
        {
            get { return ToColor(ThemeService.Current.ButtonBorder); }
        }

        public static Color ButtonHoverBg
        {
            get { return ToColor(ThemeService.Current.ButtonHoverBg); }
        }

        public static Color SelectionBg
        {
            get { return ToColor(ThemeService.Current.SelectionBg); }
        }

        public static Color SelectionBorder
        {
            get { return ToColor(ThemeService.Current.SelectionBorder); }
        }
    }
}