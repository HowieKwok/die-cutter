using Frame_UI.Wpf;
using Mef_VisionFrame._4调试设置.WPF.ViewModel;
using System.Windows.Input;

namespace Mef_VisionFrame.调试设置.WPF
{
    /// <summary>
    /// 调试流程窗口。
    /// 外壳（无边框、标题栏、三个窗口按钮）由 ThemedWindow 提供，
    /// 本类只负责装配 ViewModel 与标题栏双击最大化。
    /// 关闭行为不在这里拦截：Form_Main 需要「关闭即隐藏」的复用语义。
    /// </summary>
    public partial class WPFDebugProcessWindow : ThemedWindow
    {
        public WPFDebugProcessViewModel ViewModel { get; }

        public WPFDebugProcessWindow()
        {
            // ThemedWindow 构造期已注入主题，这里可以直接解析 XAML 里的 StaticResource。
            InitializeComponent();

            ViewModel = new WPFDebugProcessViewModel();
            DataContext = ViewModel;

            MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (ToggleMaximizeCommand != null && ToggleMaximizeCommand.CanExecute(null))
            {
                ToggleMaximizeCommand.Execute(null);
            }
        }
    }
}