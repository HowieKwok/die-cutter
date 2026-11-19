using System;

namespace Frame_UI
{
    /// <summary>
    /// 主题唯一入口。
    /// 说明：当前应用没有 WPF Application 对象（启动链是 WinForms 消息循环），
    /// 因此主题不能依赖 Application.Current.Resources，必须由两个适配器主动订阅本服务。
    /// </summary>
    public static class ThemeService
    {
        private static UiTokens current = UiThemeCatalog.Create(UiThemeCatalog.DarkThemeName);
        private static string currentName = UiThemeCatalog.DarkThemeName;
        private static bool changeEnabled;

        /// <summary>
        /// 启动即恢复上次使用的主题。
        ///
        /// 放在静态构造里而不是 Program.Main：AntdUI 适配器、WPF 字典、窗体下发
        /// 全都读 ThemeService.Current，只要它们跑起来，静态构造一定已经完成，
        /// 不会出现“先按深色建了一半、再切浅色”的中间态。
        /// 这里直接改状态而不走 ApplyCore：切换开关此时还没启用。
        /// </summary>
        static ThemeService()
        {
            string saved = ThemeSettings.Load();
            if (string.IsNullOrWhiteSpace(saved)) return;

            current = UiThemeCatalog.Create(saved);
            currentName = saved;
        }

        /// <summary>当前生效的令牌。</summary>
        public static UiTokens Current
        {
            get { return current; }
        }

        /// <summary>当前主题在 UiThemeCatalog 中的登记名。</summary>
        public static string CurrentThemeName
        {
            get { return currentName; }
        }

        /// <summary>已登记、可切换的主题名列表。</summary>
        public static string[] AvailableThemes
        {
            get { return UiThemeCatalog.Names; }
        }

        /// <summary>
        /// 运行期切换开关。本期只做静态主题，因此默认关闭：
        /// 调试窗口是 Hide 复用的单例，卡片画刷在首次构建时固化，
        /// 若此时打开切换会出现“令牌变了但界面没变”的半成品状态。
        /// 待卡片构建改为按令牌重建后再启用。
        /// </summary>
        public static bool RuntimeSwitchEnabled
        {
            get { return changeEnabled; }
        }

        /// <summary>主题切换通知。参数为新的令牌实例。</summary>
        public static event EventHandler<UiTokens> ThemeChanged;

        /// <summary>
        /// 替换当前令牌。RuntimeSwitchEnabled 为 false 时直接忽略，
        /// 避免出现部分界面已换、部分界面未换的中间态。
        /// </summary>
        public static bool Apply(UiTokens tokens)
        {
            return ApplyCore(tokens, null);
        }

        /// <summary>
        /// 按登记名切换主题（例如 UiThemeCatalog.DarkThemeName / LightThemeName）。
        /// 与 Apply 受同一切换开关约束：RuntimeSwitchEnabled 为 false 时直接忽略。
        /// </summary>
        public static bool ApplyTheme(string name)
        {
            UiTokens tokens = UiThemeCatalog.Create(name);
            return ApplyCore(tokens, name.Trim());
        }

        /// <summary>
        /// 统一切换通道：先更新令牌与主题名，再广播 ThemeChanged——
        /// 订阅方（如 AntdUiThemeAdapter）在事件里会回读 CurrentThemeName，
        /// 顺序反了会拿到旧主题名（真机事故：切浅色后 AntdUI 仍停在深色）。
        /// </summary>
        private static bool ApplyCore(UiTokens tokens, string themeName)
        {
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            if (!changeEnabled) return false;

            current = tokens;

            // Apply(UiTokens) 走的是“直接给一份令牌”，没有名字可言：
            // 此时保留原主题名，否则订阅方回读 CurrentThemeName 会拿到 null，
            // 于是 AntdUI/图标墨色判定全部落到“非浅色”分支。
            if (!string.IsNullOrWhiteSpace(themeName))
            {
                currentName = themeName.Trim();
                ThemeSettings.Save(currentName);
            }

            EventHandler<UiTokens> handler = ThemeChanged;
            if (handler != null) handler(null, tokens);
            return true;
        }

        /// <summary>启用运行期主题切换。启用前必须确认所有视图都能重绘。</summary>
        public static void EnableRuntimeSwitch()
        {
            changeEnabled = true;
        }

        /// <summary>按插件分类取标识色，类别未登记时回退到 AccentMuted。</summary>
        public static UiColor ResolveCategoryColor(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return current.AccentMuted;

            foreach (var pair in current.CategoryColors)
            {
                if (category.IndexOf(pair.Key, StringComparison.Ordinal) >= 0) return pair.Value;
            }
            return current.AccentMuted;
        }
    }
}