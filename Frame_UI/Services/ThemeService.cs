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
        private static UiTokens current = DefaultDarkTheme.Create();
        private static bool changeEnabled;

        /// <summary>当前生效的令牌。</summary>
        public static UiTokens Current
        {
            get { return current; }
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
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            if (!changeEnabled) return false;

            current = tokens;
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