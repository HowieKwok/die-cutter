using System.Collections.Generic;

namespace Frame_UI
{
    /// <summary>
    /// 默认深色主题：取自现有 WPF 调试界面的实际视觉基准
    /// （Mef_VisionFrame/4调试设置/WPF/WPFDebugProcessView.xaml）。
    /// 这里是整个仓库唯一允许出现十六进制色值的地方之一，
    /// 另一个允许位置是两个适配器的 Theme 资源文件。
    /// </summary>
    public static class DefaultDarkTheme
    {
        public static UiTokens Create()
        {
            return new UiTokens
            {
                Canvas = UiColor.FromHex("#061018"),
                Panel = UiColor.FromHex("#0D1723"),
                PanelDeep = UiColor.FromHex("#081522"),
                GridLine = UiColor.FromHex("#0B2A36"),

                Line = UiColor.FromHex("#1C6E86"),
                Divider = UiColor.FromHex("#255F72"),
                LineSubtle = UiColor.FromHex("#143847"),

                TextPrimary = UiColor.FromHex("#E8FBFF"),
                TextSecondary = UiColor.FromHex("#9FC7D8"),
                TextHint = UiColor.FromHex("#6A8794"),

                AccentPrimary = UiColor.FromHex("#00E5FF"),
                AccentSecondary = UiColor.FromHex("#FF2E9A"),
                AccentMuted = UiColor.FromHex("#7EE6FF"),

                StateOk = UiColor.FromHex("#42FF9E"),
                StateFail = UiColor.FromHex("#FF7777"),
                StateWarn = UiColor.FromHex("#FFC457"),
                StateIdle = UiColor.FromHex("#6A8794"),

                SelectionBg = UiColor.FromHex("#102131"),
                SelectionBorder = UiColor.FromHex("#00E5FF"),
                ButtonBg = UiColor.FromHex("#0D2230"),
                ButtonBorder = UiColor.FromHex("#15748A"),
                ButtonHoverBg = UiColor.FromHex("#2A4058"),

                FontFamily = "Microsoft YaHei UI",
                FontSizeTitle = 15f,
                FontSizeBody = 13f,
                FontSizeCaption = 11f,

                SpacingXs = 4,
                SpacingSm = 8,
                SpacingMd = 14,
                SpacingLg = 20,
                RadiusCard = 0,
                RadiusInput = 2,
                ControlHeight = 32,

                CategoryColors = new Dictionary<string, UiColor>
                {
                    { "相机", UiColor.FromHex("#42FF9E") },
                    { "测量", UiColor.FromHex("#00E5FF") },
                    { "尺寸", UiColor.FromHex("#00E5FF") },
                    { "检测", UiColor.FromHex("#FF2E9A") },
                    { "瑕疵", UiColor.FromHex("#FF2E9A") },
                    { "定位", UiColor.FromHex("#FFC457") },
                    { "标定", UiColor.FromHex("#B181FF") },
                }
            };
        }
    }
}