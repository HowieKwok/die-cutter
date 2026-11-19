using System.Windows.Controls;

namespace Frame_UI.Wpf
{
    /// <summary>
    /// 插件 WPF 视图的载体基类。
    ///
    /// 存在理由：令牌字典是注入到“承载元素自身”的 Resources 里的，
    /// 没有这个载体，UserControl 内的 DynamicResource 永远解析不到键，
    /// 会静默回退成默认值（透明底、黑字），而且不会报任何错。
    ///
    /// 约定：插件视图可以继续用硬编码色值（当前多数如此），
    /// 但一旦改用 DynamicResource，就必须继承本类，否则该文件会“看起来生效、实际回退”。
    /// </summary>
    public class ThemedUserControl : UserControl
    {
        public ThemedUserControl()
        {
            // 不在此处设置 Style：控件模板由使用方按语义显式引用，
            // 载体只负责让令牌键在整棵可视树里可达。
            ThemeScope.Ensure(this);
        }
    }
}