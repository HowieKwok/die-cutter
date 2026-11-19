namespace Frame_UI
{
    /// <summary>
    /// 设计令牌：只定义语义角色，不定义具体控件。
    /// 屏（XAML / WinForm 窗体）只允许引用这里的角色名，不允许出现具体色值。
    /// 需要新增颜色时先加角色，再在 DefaultDarkTheme 里赋值。
    /// </summary>
    public sealed class UiTokens
    {
        // ---- 表面 ----
        public UiColor Canvas { get; set; }          // 窗口最底层背景
        public UiColor Panel { get; set; }           // 面板 / 卡片底
        public UiColor PanelDeep { get; set; }       // 更深一级的嵌套面板
        public UiColor GridLine { get; set; }        // 背景网格线

        // ---- 线条 ----
        public UiColor Line { get; set; }            // 面板边框
        public UiColor Divider { get; set; }         // 分隔线
        public UiColor LineSubtle { get; set; }      // 弱化边框

        // ---- 文本 ----
        public UiColor TextPrimary { get; set; }
        public UiColor TextSecondary { get; set; }
        public UiColor TextHint { get; set; }        // 禁用 / 占位

        // ---- 强调 ----
        public UiColor AccentPrimary { get; set; }    // 主强调（强调标题、选中描边）
        public UiColor AccentSecondary { get; set; }  // 次强调（选中卡片）
        public UiColor AccentMuted { get; set; }      // 强调弱化（副标题）

        // ---- 状态 ----
        public UiColor StateOk { get; set; }
        public UiColor StateFail { get; set; }
        public UiColor StateWarn { get; set; }
        public UiColor StateIdle { get; set; }

        // ---- 交互态 ----
        public UiColor SelectionBg { get; set; }
        public UiColor SelectionBorder { get; set; }
        public UiColor ButtonBg { get; set; }
        public UiColor ButtonBorder { get; set; }
        public UiColor ButtonHoverBg { get; set; }

        // ---- 字体 ----
        public string FontFamily { get; set; }
        public float FontSizeTitle { get; set; }
        public float FontSizeBody { get; set; }
        public float FontSizeCaption { get; set; }

        // ---- 尺寸（逻辑单位，适配器按 DPI 缩放）----
        public int SpacingXs { get; set; }
        public int SpacingSm { get; set; }
        public int SpacingMd { get; set; }
        public int SpacingLg { get; set; }
        public int RadiusCard { get; set; }
        public int RadiusInput { get; set; }
        public int ControlHeight { get; set; }

        /// <summary>
        /// 分类色：插件分类标识色。键缺失时调用方回退到 AccentMuted。
        /// </summary>
        public System.Collections.Generic.Dictionary<string, UiColor> CategoryColors { get; set; }
    }
}