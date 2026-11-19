using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Frame_UI.Wpf
{
    /// <summary>
    /// 统一窗口外壳：无边框 + WindowChrome + 自绘标题栏。
    ///
    /// 刻意不拦截 Closing：调试窗口由 Form_Main 缓存并「关闭即隐藏」，
    /// 关闭语义必须留给宿主决定，否则两个 Closing 处理器会互相覆盖。
    /// 因此本类只提供最小化 / 最大化 / 关闭三个命令与视觉，不改关闭行为。
    /// </summary>
    public class ThemedWindow : Window
    {
        public ThemedWindow()
        {
            ThemeScope.Ensure(this);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Background = ThemeScope.Brush("Surface.Canvas");
            BorderThickness = new Thickness(0);
            UseLayoutRounding = true;

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 36,
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            });

            SetResourceReference(StyleProperty, "Window.Themed");

            MinimizeCommand = new RelayCommand(delegate { WindowState = WindowState.Minimized; });
            ToggleMaximizeCommand = new RelayCommand(ToggleMaximize);
            CloseCommand = new RelayCommand(delegate { Close(); });
        }

        public ICommand MinimizeCommand { get; private set; }
        public ICommand ToggleMaximizeCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action execute;

            public RelayCommand(Action execute)
            {
                this.execute = execute;
            }

            public bool CanExecute(object parameter)
            {
                return true;
            }

            public void Execute(object parameter)
            {
                execute();
            }

            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }
    }
}