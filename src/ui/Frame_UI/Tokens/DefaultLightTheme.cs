using System.Collections.Generic;

namespace Frame_UI
{
    /// <summary>
    /// 浅色主题骨架：与 DefaultDarkTheme 语义角色一一对应。
    ///
    /// 定位说明：本期视觉基准仍是深色主题（DefaultDarkTheme），
    /// 浅色仅用于验证“一套令牌、多主题可换”的结构完整性。
    /// 色值按工业现场可读性取保守中性色，后续如需正式启用浅色，
    /// 应先由视觉走查再精调，并调用 ThemeService.EnableRuntimeSwitch()。
    /// </summary>
    public static class DefaultLightTheme
    {
        public static UiTokens Create()
        {
            return new UiTokens
            {
                Canvas = UiColor.FromHex("#F2F5F8"),
                Panel = UiColor.FromHex("#FFFFFF"),
                PanelDeep = UiColor.FromHex("#E9EEF3"),
                GridLine = UiColor.FromHex("#DCE4EB"),

                Line = UiColor.FromHex("#B8C6D2"),
                Divider = UiColor.FromHex("#CBD6E0"),
                LineSubtle = UiColor.FromHex("#E2E9EF"),

                TextPrimary = UiColor.FromHex("#1F2933"),
                TextSecondary = UiColor.FromHex("#52606D"),
                TextHint = UiColor.FromHex("#9AA5B1"),

                AccentPrimary = UiColor.FromHex("#0891B2"),
                AccentSecondary = UiColor.FromHex("#C2278E"),
                AccentMuted = UiColor.FromHex("#0E7490"),

                StateOk = UiColor.FromHex("#1F9D55"),
                StateFail = UiColor.FromHex("#D64545"),
                StateWarn = UiColor.FromHex("#D97706"),
                StateIdle = UiColor.FromHex("#9AA5B1"),

                SelectionBg = UiColor.FromHex("#E0F2FE"),
                SelectionBorder = UiColor.FromHex("#0891B2"),
                ButtonBg = UiColor.FromHex("#FFFFFF"),
                ButtonBorder = UiColor.FromHex("#B8C6D2"),
                ButtonHoverBg = UiColor.FromHex("#F0F9FF"),

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
                    { "相机", UiColor.FromHex("#1F9D55") },
                    { "测量", UiColor.FromHex("#0891B2") },
                    { "尺寸", UiColor.FromHex("#0891B2") },
                    { "检测", UiColor.FromHex("#C2278E") },
                    { "瑕疵", UiColor.FromHex("#C2278E") },
                    { "定位", UiColor.FromHex("#D97706") },
                    { "标定", UiColor.FromHex("#7C3AED") },
                }
            };
        }
    }
}
