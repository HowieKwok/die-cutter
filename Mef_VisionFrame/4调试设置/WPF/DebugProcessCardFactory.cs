using Frame_Core;
using Frame_UI.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapeLine = System.Windows.Shapes.Line;

namespace Mef_VisionFrame.调试设置.WPF
{
    /// <summary>
    /// 调试界面的卡片构建器。
    ///
    /// 本类只描述结构与层级，颜色一律通过 ThemeScope 取令牌键，
    /// 不允许出现 Color.FromRgb —— 否则“调试壳统一皮肤”会在卡片这一层漏掉。
    /// </summary>
    internal sealed class DebugProcessCardFactory
    {
        private readonly ModuleRunStateReader stateReader;

        public DebugProcessCardFactory(ModuleRunStateReader stateReader)
        {
            this.stateReader = stateReader;
        }

        public Button CreateProcessCard(Frame_Core.Process proc, bool selected)
        {
            StackPanel stack = new StackPanel();
            StackPanel header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(CreateBadge("相机", selected ? ThemeScope.Brush("State.Ok") : ThemeScope.Brush("State.Idle")));
            header.Children.Add(CreateText(proc.processInfo.ProcessName, 15, ThemeScope.Brush("Text.Primary")));
            stack.Children.Add(header);
            stack.Children.Add(CreateText(
                "流程 " + proc.processInfo.ProcessID + " / 模块 " + proc.m_ModuleObjList.Count,
                12,
                ThemeScope.Brush("Accent.Muted")));

            return CreateCardButton(
                stack,
                selected ? ThemeScope.Brush("Accent.Primary") : ThemeScope.Brush("Line.Normal"),
                selected ? ThemeScope.Brush("Selection.Background") : ThemeScope.Brush("Surface.PanelDeep"));
        }

        public Button CreatePluginTile(string name, string category, bool selected)
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border icon = CreateModuleIcon(category, false);
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            StackPanel text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(CreateText(name, 13, ThemeScope.Brush("Text.Primary")));
            text.Children.Add(CreateText(
                string.IsNullOrWhiteSpace(category) ? "未分类" : category,
                11,
                ThemeScope.Brush("Accent.Muted")));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            return CreateCardButton(
                grid,
                selected ? ThemeScope.Brush("Accent.Secondary") : ThemeScope.Brush("Line.Normal"),
                selected ? ThemeScope.Brush("Selection.Background") : ThemeScope.Brush("Surface.PanelDeep"));
        }

        public Button CreateSelectedModuleCard(IPlugin module, bool selected)
        {
            string pluginName = module.moduleInfo.PluginName;
            PluginsInfo info;
            string category = PluginService.pluginsDictionary.TryGetValue(pluginName, out info)
                ? info.PluginsCategory
                : null;

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });

            Border icon = CreateModuleIcon(category, true);
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            StackPanel stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(CreateText(module.moduleInfo.ModuleName, 15, ThemeScope.Brush("Text.Primary")));
            stack.Children.Add(CreateText("插件：" + pluginName, 12, ThemeScope.Brush("Accent.Muted")));
            stack.Children.Add(CreateText(
                "编号：" + module.moduleInfo.ModuleEncode + " / 启用：" + module.moduleInfo.IsUse,
                12,
                ThemeScope.Brush("Text.Secondary")));
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            StackPanel runPanel = CreateRunStatusPanel(module);
            Grid.SetColumn(runPanel, 2);
            grid.Children.Add(runPanel);

            return CreateCardButton(
                grid,
                selected ? ThemeScope.Brush("Accent.Secondary") : ThemeScope.Brush("Line.Normal"),
                selected ? ThemeScope.Brush("Selection.Background") : ThemeScope.Brush("Surface.PanelDeep"));
        }

        public TextBlock CreateText(string text, double size, Brush brush)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 1)
            };
        }

        private Button CreateCardButton(object content, Brush border, Brush background)
        {
            return new Button
            {
                Content = content,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                BorderBrush = border,
                Background = background,
                Foreground = ThemeScope.Brush("Text.Primary"),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private Border CreateModuleIcon(string category, bool moduleInstance)
        {
            Brush color = ThemeScope.CategoryBrush(category);
            Grid iconGrid = new Grid { Width = 30, Height = 30 };
            iconGrid.Children.Add(new Ellipse
            {
                Stroke = color,
                StrokeThickness = 1.6,
                Fill = ThemeScope.Brush("Surface.PanelDeep"),
                Width = 30,
                Height = 30
            });

            FrameworkElement shape = CreateCategoryShape(category, moduleInstance, color);
            shape.HorizontalAlignment = HorizontalAlignment.Center;
            shape.VerticalAlignment = VerticalAlignment.Center;
            iconGrid.Children.Add(shape);

            return new Border
            {
                Width = 38,
                Height = 38,
                Margin = new Thickness(0, 0, 8, 0),
                BorderBrush = color,
                BorderThickness = new Thickness(1),
                Background = ThemeScope.Brush("Surface.Canvas"),
                CornerRadius = new CornerRadius(8),
                Child = iconGrid
            };
        }

        private FrameworkElement CreateCategoryShape(string category, bool moduleInstance, Brush brush)
        {
            string c = category ?? string.Empty;
            if (c.Contains("定位") || c.Contains("找") || c.Contains("匹配"))
            {
                Grid target = new Grid { Width = 18, Height = 18 };
                target.Children.Add(new Ellipse { Stroke = brush, StrokeThickness = 1.8, Width = 18, Height = 18 });
                target.Children.Add(new Ellipse { Stroke = brush, StrokeThickness = 1.4, Width = 8, Height = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                target.Children.Add(new ShapeLine { X1 = 9, Y1 = 0, X2 = 9, Y2 = 18, Stroke = brush, StrokeThickness = 1 });
                target.Children.Add(new ShapeLine { X1 = 0, Y1 = 9, X2 = 18, Y2 = 9, Stroke = brush, StrokeThickness = 1 });
                return target;
            }

            if (c.Contains("测量") || c.Contains("尺寸") || c.Contains("角度"))
            {
                Grid ruler = new Grid { Width = 20, Height = 16 };
                ruler.Children.Add(new ShapeLine { X1 = 2, Y1 = 12, X2 = 18, Y2 = 4, Stroke = brush, StrokeThickness = 2 });
                ruler.Children.Add(new ShapeLine { X1 = 5, Y1 = 10, X2 = 5, Y2 = 15, Stroke = brush, StrokeThickness = 1.2 });
                ruler.Children.Add(new ShapeLine { X1 = 10, Y1 = 8, X2 = 10, Y2 = 13, Stroke = brush, StrokeThickness = 1.2 });
                ruler.Children.Add(new ShapeLine { X1 = 15, Y1 = 5, X2 = 15, Y2 = 10, Stroke = brush, StrokeThickness = 1.2 });
                return ruler;
            }

            if (c.Contains("检测") || c.Contains("缺陷") || c.Contains("Blob"))
            {
                Grid lens = new Grid { Width = 20, Height = 20 };
                lens.Children.Add(new Ellipse { Stroke = brush, StrokeThickness = 1.8, Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
                lens.Children.Add(new ShapeLine { X1 = 13, Y1 = 13, X2 = 19, Y2 = 19, Stroke = brush, StrokeThickness = 2 });
                lens.Children.Add(new Ellipse { Fill = brush, Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                return lens;
            }

            if (c.Contains("标定"))
            {
                Grid check = new Grid { Width = 18, Height = 18 };
                for (int r = 0; r < 3; r++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        check.Children.Add(new Rectangle
                        {
                            Width = 4,
                            Height = 4,
                            Fill = (r + col) % 2 == 0 ? brush : Brushes.Transparent,
                            Stroke = brush,
                            StrokeThickness = 0.8,
                            Margin = new Thickness(col * 6, r * 6, 0, 0),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        });
                    }
                }
                return check;
            }

            Grid blocks = new Grid { Width = 18, Height = 18 };
            blocks.Children.Add(new Rectangle { Width = 7, Height = 7, RadiusX = 1, RadiusY = 1, Fill = brush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
            blocks.Children.Add(new Rectangle { Width = 7, Height = 7, RadiusX = 1, RadiusY = 1, Fill = brush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
            blocks.Children.Add(new Rectangle { Width = 7, Height = 7, RadiusX = 1, RadiusY = 1, Fill = brush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom });
            blocks.Children.Add(new Rectangle { Width = 7, Height = 7, RadiusX = 1, RadiusY = 1, Fill = moduleInstance ? brush : Brushes.Transparent, Stroke = brush, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
            return blocks;
        }

        private StackPanel CreateRunStatusPanel(IPlugin module)
        {
            StackPanel panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(CreateRunStatusBadge(module));
            panel.Children.Add(new TextBlock
            {
                Text = "耗时：" + stateReader.GetElapsedText(module),
                FontSize = 11,
                Foreground = ThemeScope.Brush("Text.Secondary"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            });

            return panel;
        }

        private Border CreateRunStatusBadge(IPlugin module)
        {
            ModuleRunState state = stateReader.GetState(module);
            string text = state == ModuleRunState.Success ? "成功" : (state == ModuleRunState.Failed ? "失败" : "未执行");
            Brush accent = state == ModuleRunState.Success
                ? ThemeScope.Brush("State.Ok")
                : state == ModuleRunState.Failed
                    ? ThemeScope.Brush("State.Fail")
                    : ThemeScope.Brush("State.Idle");

            Grid content = new Grid { Height = 22 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            TextBlock label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = ThemeScope.Brush("Text.Primary"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };
            Grid.SetColumn(label, 1);
            content.Children.Add(label);

            return new Border
            {
                MinWidth = 70,
                Height = 26,
                Padding = new Thickness(8, 1, 8, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(13),
                BorderThickness = new Thickness(1),
                BorderBrush = accent,
                Background = ThemeScope.Brush("Surface.Canvas"),
                Child = content,
                ToolTip = stateReader.GetToolTip(module, state)
            };
        }

        private Border CreateBadge(string text, Brush brush)
        {
            return new Border
            {
                Height = 22,
                MinWidth = 38,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(6, 1, 6, 1),
                BorderThickness = new Thickness(1),
                BorderBrush = brush,
                Background = ThemeScope.Brush("Surface.Canvas"),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    Foreground = brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
    }
}