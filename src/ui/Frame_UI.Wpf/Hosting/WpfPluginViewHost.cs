using System;
using System.Windows;
using System.Windows.Controls;

namespace Frame_UI.Wpf
{
    /// <summary>
    /// 插件编辑器宿主外壳：内容区 + 底部动作条（执行 / 保存 / 关闭 + 耗时 + 状态）。
    ///
    /// 刻意不认识 IPlugin：UI 层只负责“长什么样、按钮在哪”，
    /// 点击后做什么由业务层（Mef_VisionFrame 的插件基类）通过事件接收。
    /// 这样 Frame_Core 依然可以不引用 UI 层。
    /// </summary>
    public class WpfPluginViewHost : ThemedWindow
    {
        private ContentControl contentHost;
        private TextBlock costTimeText;
        private TextBlock statusText;

        public WpfPluginViewHost()
        {
            Width = 1008;
            Height = 811;
            MinWidth = 860;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

            contentHost = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };

            var contentBorder = new Border
            {
                Margin = new Thickness(10, 10, 10, 6),
                BorderThickness = new Thickness(1),
                BorderBrush = ThemeScope.Brush("Line.Normal"),
                Background = ThemeScope.Brush("Surface.PanelDeep"),
                Child = contentHost
            };
            Grid.SetRow(contentBorder, 0);
            root.Children.Add(contentBorder);

            root.Children.Add(BuildActionBar());
            Content = root;
        }

        /// <summary>业务层点击“执行”。</summary>
        public event EventHandler RunRequested;

        /// <summary>业务层点击“保存”。</summary>
        public event EventHandler SaveRequested;

        /// <summary>业务层点击“关闭”。</summary>
        public event EventHandler CloseRequested;

        /// <summary>设置插件页面内容。传入前请确保已绑定插件对象。</summary>
        public void SetContent(object content)
        {
            contentHost.Content = content;
        }

        protected void SetStatus(string text)
        {
            if (statusText != null) statusText.Text = text ?? string.Empty;
        }

        protected void SetCostTime(string text)
        {
            if (costTimeText != null) costTimeText.Text = text ?? string.Empty;
        }

        private Border BuildActionBar()
        {
            var bar = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = ThemeScope.Brush("Line.Divider"),
                Background = ThemeScope.Brush("Surface.Panel"),
                Padding = new Thickness(10, 0, 10, 0)
            };
            Grid.SetRow(bar, 1);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            costTimeText = new TextBlock
            {
                Text = "耗时：00ms",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeScope.Brush("Accent.Muted")
            };
            Grid.SetColumn(costTimeText, 0);
            grid.Children.Add(costTimeText);

            statusText = new TextBlock
            {
                Text = "等待操作",
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeScope.Brush("Text.Secondary")
            };
            Grid.SetColumn(statusText, 1);
            grid.Children.Add(statusText);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            buttons.Children.Add(CreateButton("执行", delegate { Raise(RunRequested); }));
            buttons.Children.Add(CreateButton("保存", delegate { Raise(SaveRequested); }));
            buttons.Children.Add(CreateButton("关闭", delegate { Raise(CloseRequested); }));
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);

            bar.Child = grid;
            return bar;
        }

        private Button CreateButton(string text, Action onClick)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 92,
                Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource("Button.Primary")
            };
            button.Click += delegate { onClick(); };
            return button;
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}