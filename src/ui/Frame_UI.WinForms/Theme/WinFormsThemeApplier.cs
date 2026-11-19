using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Frame_UI.WinForms
{
    /// <summary>
    /// 把令牌下发到整棵 WinForm 控件树。
    ///
    /// 存在理由：WinForm 没有样式系统，而 20 个插件的 Designer 里已有 600 余处
    /// BackColor/ForeColor。逐处删除既不可维护也无法覆盖第三方控件，
    /// 因此统一在窗体加载后做一次后置覆盖，Designer 保持原样。
    ///
    /// 幂等要求：可被重复调用（主题切换时会再次下发），不得订阅事件或累积状态。
    /// </summary>
    public static class WinFormsThemeApplier
    {
        /// <summary>以当前令牌重新下发整棵树。</summary>
        public static void Apply(Control root)
        {
            if (root == null) return;
            ApplyCore(root, ThemeService.Current);
        }

        private static void ApplyCore(Control control, UiTokens tokens)
        {
            if (control == null) return;
            if (control.Site != null && control.Site.DesignMode) return;

            TryStyleScrollbars(control);
            ApplyOwnStyle(control, tokens);
            ApplyDisabledInk(control);

            // 禁用态是运行期状态（按钮随业务被 Enabled=false），构造期下发不到，
            // 因此挂 EnabledChanged：禁用时压成灰、启用时按令牌复原。
            // 先减后加保证幂等，与 ControlAdded 同一套写法。
            control.EnabledChanged -= OnEnabledChanged;
            control.EnabledChanged += OnEnabledChanged;

            // 懒创建兜底：Apply 之后才 add 进来的控件（第三方面板构造后补的子控件、
            // 数据加载后才 new 出来的树/表）本身不会被这次递归看到，
            // 挂 ControlAdded 让它们一进树就拿到令牌，而不是等到下一次主题切换。
            // 先减后加保证幂等（Apply 会被重复调用）。
            control.ControlAdded -= OnControlAdded;
            control.ControlAdded += OnControlAdded;

            Control[] children = new Control[control.Controls.Count];
            control.Controls.CopyTo(children, 0);
            foreach (Control child in children)
            {
                ApplyCore(child, tokens);
            }

            ApplyVirtualChildren(control, tokens);
        }

        /// <summary>
        /// 虚拟容器补递归：AntdUI.Tabs 把页签挂在 Pages 集合而不是 Controls，
        /// 常规递归够不到页签里的原生控件（DataGridView 等），导致“外框深色、页内还是白”。
        /// 鸭子类型识别 Pages：属性存在、可枚举、元素是 Control 即视为子级。
        /// </summary>
        private static void ApplyVirtualChildren(Control control, UiTokens tokens)
        {
            PropertyInfo pages = control.GetType().GetProperty("Pages", BindingFlags.Public | BindingFlags.Instance);
            if (pages == null) return;

            System.Collections.IEnumerable items = pages.GetValue(control, null) as System.Collections.IEnumerable;
            if (items == null) return;

            foreach (object item in items)
            {
                Control child = item as Control;
                if (child != null) ApplyCore(child, tokens);
            }
        }

        private static void ApplyOwnStyle(Control control, UiTokens tokens)
        {
            // 带边框色的自绘控件（Halcon HWindowControl 等，多为 UserControl 派生）：
            // 必须先于 UserControl 分支，只统一外框线色，不碰图像区。
            // 鸭子类型识别 BorderColor 属性，避免适配器引用 HalconDotNet。
            if (TryApplyBorderColor(control)) return;

            var form = control as Form;
            if (form != null)
            {
                form.BackColor = ColorBridge.Canvas;
                form.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            var userControl = control as UserControl;
            if (userControl != null)
            {
                userControl.BackColor = ColorBridge.Canvas;
                userControl.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            // StatusStrip 必须先于 ToolStrip 判定：它继承自 ToolStrip，
            // 顺序反了会永远走不到这个分支（颜色与对齐都和普通工具条不同）。
            var statusStrip = control as StatusStrip;
            if (statusStrip != null)
            {
                statusStrip.BackColor = ColorBridge.PanelDeep;
                statusStrip.ForeColor = ColorBridge.TextSecondary;
                return;
            }

            var toolStrip = control as ToolStrip;
            if (toolStrip != null)
            {
                toolStrip.Renderer = new SciToolStripRenderer(tokens);
                toolStrip.BackColor = ColorBridge.PanelDeep;
                toolStrip.ForeColor = ColorBridge.TextPrimary;
                toolStrip.GripStyle = ToolStripGripStyle.Hidden;
                return;
            }

            var grid = control as DataGridView;
            if (grid != null)
            {
                ApplyGrid(grid);
                return;
            }

            var tab = control as TabControl;
            if (tab != null)
            {
                ApplyTabControl(tab);
                return;
            }

            // TabPage 是 Panel 派生，但 UseVisualStyleBackColor 为 true 时它按视觉样式
            // 画系统底色、BackColor 完全无效——不单独处理就会出现“页签深色、页内全白”。
            var tabPage = control as TabPage;
            if (tabPage != null)
            {
                tabPage.UseVisualStyleBackColor = false;
                tabPage.BackColor = ColorBridge.Panel;
                tabPage.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            // CheckedListBox 继承自 ListBox，必须先匹配子类型，否则永远走不到。
            var checkList = control as CheckedListBox;
            if (checkList != null)
            {
                checkList.BackColor = ColorBridge.PanelDeep;
                checkList.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            var listBox = control as ListBox;
            if (listBox != null)
            {
                listBox.BackColor = ColorBridge.PanelDeep;
                listBox.ForeColor = ColorBridge.TextPrimary;
                listBox.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            var textBox = control as TextBoxBase;
            if (textBox != null)
            {
                textBox.BackColor = ColorBridge.PanelDeep;
                textBox.ForeColor = ColorBridge.TextPrimary;
                // 边框形状是设计器的选择，主题只管颜色：
                // 相机标题栏的灰度信息（R/C/Value）是只读展示，Designer 里就是 None，
                // 在这里强制 FixedSingle 会平白给它加一圈框。
                return;
            }

            var combo = control as ComboBox;
            if (combo != null)
            {
                combo.BackColor = ColorBridge.PanelDeep;
                combo.ForeColor = ColorBridge.TextPrimary;
                // 开了视觉样式时 ComboBox 会忽略 BackColor（真机表现：只有下拉列表深色、输入框仍是白底）。
                // FlatStyle.Flat 才让属性色生效。
                combo.FlatStyle = FlatStyle.Flat;

                // 下拉列表默认是系统绘制：深色界面下会弹出一块白底黑字。
                // 统一改成自绘，字段底/列表底、选中项都走令牌，和 WPF 的下拉是同一套观感。
                // 已经是自绘的（第三方面板自己画图标/多列）一律不动，否则两套 DrawItem 会互相覆盖。
                if (combo.DrawMode == DrawMode.Normal)
                {
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    if (combo.ItemHeight < 22) combo.ItemHeight = 22;
                    combo.DrawItem -= ComboBoxDrawItem;
                    combo.DrawItem += ComboBoxDrawItem;
                }
                return;
            }

            // UpDownBase 同时覆盖 NumericUpDown 与 DomainUpDown。
            var upDown = control as UpDownBase;
            if (upDown != null)
            {
                upDown.BackColor = ColorBridge.PanelDeep;
                upDown.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            var treeView = control as TreeView;
            if (treeView != null)
            {
                treeView.BackColor = ColorBridge.PanelDeep;
                treeView.ForeColor = ColorBridge.TextPrimary;
                treeView.LineColor = ColorBridge.LineSubtle;
                return;
            }

            var listView = control as ListView;
            if (listView != null)
            {
                listView.BackColor = ColorBridge.PanelDeep;
                listView.ForeColor = ColorBridge.TextPrimary;
                // ListView 的列头由系统绘制，属性改不了色：
                // Win10+ 通过 uxtheme 的 DarkMode_Explorer 让列头与滚动条跟随深色。
                // 老系统上调用失败也无副作用（保持原样）。
                TryApplyDarkExplorerTheme(listView);
                return;
            }

            // 选择类控件与 Label 同理：背景透出父容器，避免出现一块面板色补丁。
            var checkBox = control as CheckBox;
            if (checkBox != null)
            {
                checkBox.BackColor = Color.Transparent;
                checkBox.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            var radioButton = control as RadioButton;
            if (radioButton != null)
            {
                radioButton.BackColor = Color.Transparent;
                radioButton.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            // 图像显示区：按“外框统一、内部保留”约定，只统一底色，不碰图像内容。
            var pictureBox = control as PictureBox;
            if (pictureBox != null)
            {
                pictureBox.BackColor = ColorBridge.Canvas;
                return;
            }

            // 双态按钮（如 Frame_Core.ControlButton）：选中/未选中两色是控件的自定义属性，
            // 普通 Button 分支改不到——UpdateState 会把 BackColor 刷回自定义色。
            // 用鸭子类型识别，避免适配器反向依赖 Frame_Core。
            if (TryStyleTwoStateButton(control)) return;

            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = ColorBridge.ButtonBg;
                button.ForeColor = ColorBridge.TextPrimary;
                button.FlatAppearance.BorderColor = ColorBridge.ButtonBorder;
                button.FlatAppearance.MouseOverBackColor = ColorBridge.ButtonHoverBg;
                button.UseVisualStyleBackColor = false;
                return;
            }

            // LinkLabel 继承自 Label，必须先匹配子类型：链接色走强调色，而不是普通文本色。
            var linkLabel = control as LinkLabel;
            if (linkLabel != null)
            {
                linkLabel.BackColor = Color.Transparent;
                linkLabel.ForeColor = ColorBridge.TextPrimary;
                linkLabel.LinkColor = ColorBridge.AccentPrimary;
                linkLabel.VisitedLinkColor = ColorBridge.AccentMuted;
                linkLabel.ActiveLinkColor = ColorBridge.AccentPrimary;
                return;
            }

            var label = control as Label;
            if (label != null)
            {
                label.BackColor = Color.Transparent;
                label.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            var groupBox = control as GroupBox;
            if (groupBox != null)
            {
                groupBox.BackColor = Color.Transparent;
                groupBox.ForeColor = ColorBridge.AccentMuted;
                return;
            }

            var panel = control as Panel;
            if (panel != null)
            {
                panel.BackColor = ColorBridge.Canvas;
                panel.ForeColor = ColorBridge.TextPrimary;
                return;
            }

            // 兜底：第三方控件（含 AntdUI）只下发基类颜色。
            // AntdUI 的模板色需要其自身的颜色配置，属于后续适配项，
            // 这里不做猜测性设置，避免把控件画坏。
            control.BackColor = ColorBridge.Panel;
            control.ForeColor = ColorBridge.TextPrimary;
        }

        /// <summary>
        /// 给暴露可写 Color 类型 BorderColor 属性的控件下发令牌线色。
        /// 深色主题下相机画面（黑）与面板（近黑）融为一体，边框是唯一的视觉分界。
        /// 注意 Halcon HWindowControl 默认 BorderStyle=None / BorderWidth=0，只设颜色画不出来，
        /// 必须把边框样式与宽度一并补上（已有边框的控件不受影响）。
        /// </summary>
        private static bool TryApplyBorderColor(Control control)
        {
            Type type = control.GetType();
            PropertyInfo borderColor = type.GetProperty("BorderColor", BindingFlags.Public | BindingFlags.Instance);
            if (borderColor == null || !borderColor.CanWrite || borderColor.PropertyType != typeof(Color)) return false;

            borderColor.SetValue(control, ColorBridge.Line, null);

            PropertyInfo borderStyle = type.GetProperty("BorderStyle", BindingFlags.Public | BindingFlags.Instance);
            if (borderStyle != null && borderStyle.CanWrite && borderStyle.PropertyType == typeof(BorderStyle)
                && (BorderStyle)borderStyle.GetValue(control, null) == BorderStyle.None)
            {
                borderStyle.SetValue(control, BorderStyle.FixedSingle, null);
            }

            PropertyInfo borderWidth = type.GetProperty("BorderWidth", BindingFlags.Public | BindingFlags.Instance);
            if (borderWidth != null && borderWidth.CanWrite && borderWidth.PropertyType == typeof(int)
                && (int)borderWidth.GetValue(control, null) < 1)
            {
                borderWidth.SetValue(control, 1, null);
            }

            control.BackColor = ColorBridge.Canvas;
            return true;
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int LVM_GETHEADER = 0x1000 + 31;

        /// <summary>
        /// 句柄未创建时 SetWindowTheme 无法生效，等句柄就绪后补一次。
        /// 列头是 ListView 内部的 SysHeader32 子窗口，必须单独下发深色主题。
        /// </summary>
        private static void TryApplyDarkExplorerTheme(ListView listView)
        {
            try
            {
                if (listView.IsHandleCreated)
                {
                    ApplyDarkExplorerThemeCore(listView);
                }
                else
                {
                    listView.HandleCreated += delegate
                    {
                        try { ApplyDarkExplorerThemeCore(listView); }
                        catch { }
                    };
                }
            }
            catch
            {
                // 主题化失败不影响业务：老 Windows 上没有 DarkMode_Explorer。
            }
        }

        private static void ApplyDarkExplorerThemeCore(ListView listView)
        {
            SetWindowTheme(listView.Handle, "DarkMode_Explorer", null);
            IntPtr header = SendMessage(listView.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (header != IntPtr.Zero)
            {
                SetWindowTheme(header, "DarkMode_ItemsView", null);
            }
        }

        /// <summary>
        /// 识别并统一样式化“双态按钮”：带有可写 CheckedBackColor / UnCheckedBackColor
        /// 两个 Color 属性的 Button 派生类。命中后完成全部样式下发并返回 true。
        /// </summary>
        private static bool TryStyleTwoStateButton(Control control)
        {
            Button button = control as Button;
            if (button == null) return false;

            PropertyInfo checkedProp = control.GetType().GetProperty(
                "CheckedBackColor", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo unCheckedProp = control.GetType().GetProperty(
                "UnCheckedBackColor", BindingFlags.Public | BindingFlags.Instance);

            if (checkedProp == null || unCheckedProp == null
                || !checkedProp.CanWrite || !unCheckedProp.CanWrite
                || checkedProp.PropertyType != typeof(Color)
                || unCheckedProp.PropertyType != typeof(Color))
            {
                return false;
            }

            // 业务语义色（启动=绿 / 运行中=红等）由控件自己声明（KeepStateColors），
            // 主题不得覆盖；其余双态按钮（自动/仿真、开始/结束膜卷）跟随主题。
            PropertyInfo keepProp = control.GetType().GetProperty(
                "KeepStateColors", BindingFlags.Public | BindingFlags.Instance);
            bool keepStateColors = keepProp != null
                && keepProp.PropertyType == typeof(bool)
                && keepProp.CanRead
                && (bool)keepProp.GetValue(control, null);

            if (!keepStateColors)
            {
                // 属性 setter 内部会回写 BackColor，两个都设完后控件即处于正确状态色。
                unCheckedProp.SetValue(control, ColorBridge.ButtonBg, null);
                checkedProp.SetValue(control, ColorBridge.SelectionBg, null);
            }

            button.FlatStyle = FlatStyle.Flat;
            button.ForeColor = ColorBridge.TextPrimary;
            button.FlatAppearance.BorderColor = ColorBridge.ButtonBorder;
            button.FlatAppearance.MouseOverBackColor = ColorBridge.ButtonHoverBg;
            button.UseVisualStyleBackColor = false;
            return true;
        }

        private static void ApplyGrid(DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BorderStyle = BorderStyle.None;
            // BackColor 是“单元格之外”的底（空白区、滚动条轨道边缘），
            // 只设 BackgroundColor 时这层仍是 SystemColors.Control 的浅灰，
            // 深色界面里就是一块白斑（图片管理里的两张目录表格即为此）。
            grid.BackColor = ColorBridge.Panel;
            grid.BackgroundColor = ColorBridge.Panel;
            grid.GridColor = ColorBridge.LineSubtle;
            grid.ForeColor = ColorBridge.TextPrimary;
            grid.DefaultCellStyle.BackColor = ColorBridge.Panel;
            grid.DefaultCellStyle.ForeColor = ColorBridge.TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = ColorBridge.SelectionBg;
            grid.DefaultCellStyle.SelectionForeColor = ColorBridge.TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.BackColor = ColorBridge.PanelDeep;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ColorBridge.AccentMuted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = ColorBridge.PanelDeep;
            grid.RowHeadersDefaultCellStyle.BackColor = ColorBridge.PanelDeep;
            grid.RowHeadersDefaultCellStyle.ForeColor = ColorBridge.TextSecondary;
            grid.AlternatingRowsDefaultCellStyle.BackColor = ColorBridge.PanelDeep;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = ColorBridge.TextPrimary;

            // 列级 HeaderCell.Style 的优先级高于 ColumnHeadersDefaultCellStyle，
            // Designer 或三方封装设过列头样式的表格必须逐列兜底，否则表头残留白色。
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.BackColor = ColorBridge.PanelDeep;
                column.HeaderCell.Style.ForeColor = ColorBridge.AccentMuted;
                column.HeaderCell.Style.SelectionBackColor = ColorBridge.PanelDeep;
            }
        }

        private static void OnControlAdded(object sender, ControlEventArgs e)
        {
            if (e == null || e.Control == null) return;
            ApplyCore(e.Control, ThemeService.Current);
        }

        private static void OnEnabledChanged(object sender, EventArgs e)
        {
            Control control = sender as Control;
            if (control == null || control.IsDisposed) return;

            // 整棵子树重走一遍：禁用 Label 的兄弟控件不跟着变，但被禁用的容器
            // （GroupBox / Panel）里的子控件要靠递归才会一起压灰。
            ApplyCore(control, ThemeService.Current);
        }

        /// <summary>
        /// 禁用态文字统一压灰。
        ///
        /// 只处理“文字是主体”的控件：按钮、标签、输入类、选择类。
        /// 容器与表格不参与——容器的 ForeColor 会被子控件继承，
        /// 表格整体变灰会让禁用页的数据彻底不可读。
        /// </summary>
        private static void ApplyDisabledInk(Control control)
        {
            if (IsEffectivelyEnabled(control))
            {
                RestoreImage(control);
                return;
            }

            if (control is Button || control is Label || control is LinkLabel
                || control is TextBoxBase || control is ComboBox || control is UpDownBase
                || control is CheckBox || control is RadioButton)
            {
                control.ForeColor = ColorBridge.TextHint;
            }

            GrayOutImage(control);
        }

        /// <summary>禁用按钮的图标备份：原图 + 生成出来的灰图，成对存放。</summary>
        private sealed class DisabledImages
        {
            public Image Original;
            public Image Gray;
        }

        private static readonly ConditionalWeakTable<Control, DisabledImages> DisabledImageBackup =
            new ConditionalWeakTable<Control, DisabledImages>();

        /// <summary>
        /// 框架只给“文字”做禁用变灰，图标仍按原色绘制：
        /// 工具栏上的上一片/下一片/启动这类纯图标按钮，禁用后看着和可用状态一样。
        /// 这里把当前图按灰度+半透明另存一份换上，启用时再换回原图。
        /// </summary>
        private static void GrayOutImage(Control control)
        {
            Button button = control as Button;
            if (button == null || button.Image == null) return;

            DisabledImages backup;
            if (!DisabledImageBackup.TryGetValue(button, out backup))
            {
                backup = new DisabledImages();
                DisabledImageBackup.Add(button, backup);
            }

            // 换过图（例如双态按钮切了状态）就重新生成，不能沿用上一张的灰图。
            if (!ReferenceEquals(button.Image, backup.Gray))
            {
                backup.Original = button.Image;
                backup.Gray = ToDisabledImage(backup.Original);
            }

            button.Image = backup.Gray;
        }

        private static void RestoreImage(Control control)
        {
            Button button = control as Button;
            if (button == null) return;

            DisabledImages backup;
            if (!DisabledImageBackup.TryGetValue(button, out backup)) return;

            // 只有“当前显示的正是我们换上去的灰图”才恢复，
            // 否则说明控件自己已经换了新图，不能拿旧图盖掉。
            if (ReferenceEquals(button.Image, backup.Gray)) button.Image = backup.Original;
        }

        /// <summary>灰度 + 55% 不透明度：既去掉颜色，又和可用态拉开亮度差。</summary>
        private static Image ToDisabledImage(Image source)
        {
            Bitmap gray = new Bitmap(source.Width, source.Height);
            using (Graphics g = Graphics.FromImage(gray))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix(new[]
                {
                    new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
                    new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
                    new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 0.55f, 0f },
                    new[] { 0f, 0f, 0f, 0f, 1f },
                });
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(source,
                    new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height,
                    GraphicsUnit.Pixel, attributes);
            }
            return gray;
        }

        /// <summary>
        /// 有效启用态：Control.Enabled 只返回自身的属性值，
        /// 父容器被禁用时子控件仍然读得到 true，必须逐级上溯。
        /// </summary>
        private static bool IsEffectivelyEnabled(Control control)
        {
            for (Control current = control; current != null; current = current.Parent)
            {
                if (!current.Enabled) return false;
            }
            return true;
        }

        /// <summary>
        /// 滚动条统一交给 uxtheme：WinForms 的滚动条是非客户区，属性改不了色，
        /// 只能按窗口句柄换主题。漏掉这一条就会出现“内容深色、滚动条白亮”的割裂
        /// （面板、日志、表格、树、多行文本都在此列）。
        /// </summary>
        private static void TryStyleScrollbars(Control control)
        {
            if (!IsScrollbarBearing(control)) return;

            // 句柄还没建好：构造期下发很常见（窗体 Load 之前 Handle 是 0）。
            // 这里不能直接 return——那样这条滚动条永远停在系统浅色，
            // 等句柄建好再补一次即可（先减后加，重复下发不会累积）。
            if (!control.IsHandleCreated)
            {
                control.HandleCreated -= OnHandleCreatedForScrollbars;
                control.HandleCreated += OnHandleCreatedForScrollbars;
                return;
            }

            try
            {
                // DarkMode_Explorer 只在 Win10 1809+ 生效；旧系统调用失败即保持原样。
                SetWindowTheme(control.Handle, IsDarkTheme() ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch
            {
                // 换主题失败不影响业务。
            }
        }

        private static void OnHandleCreatedForScrollbars(object sender, EventArgs e)
        {
            Control control = sender as Control;
            if (control == null) return;

            control.HandleCreated -= OnHandleCreatedForScrollbars;
            TryStyleScrollbars(control);
        }


        private static bool IsScrollbarBearing(Control control)
        {
            // TabPage 自身不带滚动条，但它继承 Panel：用 AutoScroll 判定即可。
            if (control is TextBoxBase || control is ListBox || control is TreeView
                || control is DataGridView || control is TabControl)
            {
                return true;
            }

            ScrollableControl scrollable = control as ScrollableControl;
            return scrollable != null && scrollable.AutoScroll;
        }

        /// <summary>
        /// 深色/浅色判定：直接看当前令牌的画布亮度，不依赖主题名常量，
        /// 新增主题（如高对比）时无需再改这里。
        /// </summary>
        private static bool IsDarkTheme()
        {
            Color canvas = ColorBridge.Canvas;
            return canvas.R + canvas.G + canvas.B < 3 * 128;
        }

        /// <summary>
        /// TabControl 的页签由系统绘制，属性改不了色，DarkMode_Explorer 也只影响客户区。
        /// 因此改成自绘：页签底色/文字全部走令牌，深浅主题都能跟随。
        /// </summary>
        private static void ApplyTabControl(TabControl tab)
        {
            tab.BackColor = ColorBridge.Panel;
            tab.ForeColor = ColorBridge.TextPrimary;
            tab.DrawMode = TabDrawMode.OwnerDrawFixed;
            if (tab.ItemSize.Height < 26) tab.ItemSize = new Size(tab.ItemSize.Width, 26);

            // 页体（TabPage）由 ApplyOwnStyle 的 TabPage 分支处理：
            // 那里同时负责“后来才加上的页”，这里不重复设置。

            // 页签自绘只管页签矩形本身，标题条的空白段仍是系统白底，
            // 交给 TabStripPainter 在 WM_PAINT 之后整条重填。
            TabStripPainter.Attach(tab);

            // 静态处理器 + 先减后加：主题会重复下发，不能累积订阅。
            tab.DrawItem -= TabControlDrawItem;
            tab.DrawItem += TabControlDrawItem;
            tab.Invalidate();
        }

        /// <summary>
        /// ComboBox 下拉项自绘：系统绘制会把列表刷成白底黑字。
        /// 左内边距与 WPF 的下拉项一致（6px），选中项用 Selection 令牌。
        /// </summary>
        private static void ComboBoxDrawItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null || e.Index < 0 || e.Index >= combo.Items.Count) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            using (SolidBrush back = new SolidBrush(selected ? ColorBridge.SelectionBg : ColorBridge.PanelDeep))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }

            Rectangle textBounds = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 6), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), combo.Font, textBounds,
                selected ? ColorBridge.TextPrimary : ColorBridge.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static void TabControlDrawItem(object sender, DrawItemEventArgs e)
        {            TabControl tab = sender as TabControl;
            if (tab == null || e.Index < 0 || e.Index >= tab.TabPages.Count) return;

            bool selected = e.Index == tab.SelectedIndex;
            Rectangle bounds = e.Bounds;

            using (SolidBrush back = new SolidBrush(selected ? ColorBridge.Panel : ColorBridge.PanelDeep))
            {
                e.Graphics.FillRectangle(back, bounds);
            }

            TextRenderer.DrawText(e.Graphics, tab.TabPages[e.Index].Text, tab.Font, bounds,
                selected ? ColorBridge.TextPrimary : ColorBridge.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}