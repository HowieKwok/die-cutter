using System;
using System.Drawing;
using System.Reflection;

namespace Frame_UI.WinForms
{
    /// <summary>
    /// AntdUI 主题适配器。
    ///
    /// 存在理由：工艺视图链（FormCraftBase : AntdUI.Window）用的是 AntdUI 自绘控件，
    /// 它们不吃 WinFormsThemeApplier 下发的 BackColor/ForeColor —— 真机表现为
    /// "背景变了、控件还是原样"。AntdUI 必须走它自己的全局开关。
    ///
    /// 为什么用反射而不是直接引用 AntdUI：
    ///   1. AntdUI 是第三方 DLL（DLL\UiDLL\AntdUI.dll），只有部分工程引用它；
    ///      加编译期依赖会把这份耦合扩散到整个 UI 层。
    ///   2. 我们只用到 Config.IsDark / Config.Font 两个成员，
    ///      代价远小于一处强类型依赖。
    ///
    /// 能力边界（实测 API，见 AntdUI 1.6.13 的 AntdUI.Config）：
    ///   Config 只暴露 IsDark / IsLight / Font / Animation / Dpi / ScrollBarHide 等开关，
    ///   不接受自定义颜色。因此 AntdUI 控件只能"切到它自己的深色板"，
    ///   无法逐色对齐 UiTokens —— 这是本适配器的能力上限，不是实现缺陷。
    /// </summary>
    public static class AntdUiThemeAdapter
    {
        /// <summary>是否已找到 AntdUI（未引用该库的工程里为 false，调用即静默跳过）。</summary>
        public static bool IsAvailable
        {
            get { return ResolveConfigType() != null; }
        }

        /// <summary>把 AntdUI 切到深色板并统一字体。幂等，可在任意时刻重复调用。</summary>
        public static void Apply(UiTokens tokens)
        {
            Type config = ResolveConfigType();
            if (config == null) return;

            try
            {
                PropertyInfo isDark = config.GetProperty("IsDark", BindingFlags.Public | BindingFlags.Static);
                if (isDark != null && isDark.CanWrite && isDark.PropertyType == typeof(bool))
                {
                    isDark.SetValue(null, true, null);
                }

                PropertyInfo font = config.GetProperty("Font", BindingFlags.Public | BindingFlags.Static);
                if (font != null && font.CanWrite && tokens != null && !string.IsNullOrEmpty(tokens.FontFamily))
                {
                    font.SetValue(null, new Font(tokens.FontFamily, 9f), null);
                }
            }
            catch (Exception)
            {
                // 外观适配失败不应影响业务启动：AntdUI 版本差异时不抛到调用方。
            }
        }

        /// <summary>
        /// 定位 AntdUI.Config 类型。
        ///
        /// 不能用 AppDomain.CurrentDomain.GetAssemblies() 遍历：
        /// 程序集是按需加载的，而本方法在 Program.cs 启动阶段调用，
        /// 此时还没有任何代码触碰过 AntdUI 的类型，它压根不在已加载列表里，
        /// 结果恒为 null、适配器静默跳过（真机表现就是"背景变了、控件没变"）。
        /// 因此必须按程序集名解析，由运行时自行加载。
        /// </summary>
        private static Type ResolveConfigType()
        {
            // 优先按"类型, 程序集"全名解析；AntdUI.dll 已随宿主输出，可被加载。
            Type resolved = Type.GetType("AntdUI.Config, AntdUI", false);
            if (resolved != null) return resolved;

            // 兜底：显式按名称加载（未引用 AntdUI 的工程会抛，直接放弃）。
            try
            {
                Assembly assembly = Assembly.Load("AntdUI");
                return assembly == null ? null : assembly.GetType("AntdUI.Config", false);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}