using System;
using System.Windows;

namespace Frame_Core
{
    /// <summary>
    /// WPF 统一样式的接线点（与 FormStylingHook 同理）。
    ///
    /// 存在理由：Frame_Core 下的 WPF 窗口（如 WindowImageSave）没有公共基类，
    /// 而样式实现位于 Frame_UI.Wpf —— Frame_Core 不能引用它（分层门禁禁止）。
    /// 因此这里只暴露一个 Action&lt;Window&gt;，由宿主在启动时接到 ThemeScope.Ensure。
    /// 两边互不认识，依赖方向保持：Frame_Core ← 宿主 → Frame_UI.Wpf。
    ///
    /// 调用方约定：窗口在 InitializeComponent() 之后调用一次 Style(this)。
    /// 资源键使用 DynamicResource 的窗口，主题切换时会自动跟随。
    /// </summary>
    public static class WpfStylingHook
    {
        /// <summary>宿主注册的样式下发器。未注册时为 null，调用即静默跳过。</summary>
        public static Action<Window> OnWindowCreated;

        /// <summary>对单个窗口下发统一样式。未接线时为无操作。</summary>
        public static void Style(Window window)
        {
            if (window == null) return;
            var handler = OnWindowCreated;
            if (handler == null) return;
            handler(window);
        }
    }
}
