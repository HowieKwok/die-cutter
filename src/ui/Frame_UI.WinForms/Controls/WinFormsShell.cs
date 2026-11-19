using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Frame_UI.WinForms
{
    /// <summary>
    /// 窗体外壳装配器：给任意 Form 加上深色标题栏与底部动作条。
    ///
    /// 为什么是“装配”而不是“继承”：
    ///   插件窗体类必须继承插件基类。若基类继承 ThemedForm，插件编译期就必须能看到
    ///   Frame_UI.WinForms（C# 需要解析整条基类链），插件编译面被迫暴露 UI 适配器。
    ///   改为组合后，插件基类的基类回到 System.Windows.Forms.Form，插件只需引用 Frame_PluginHost。
    ///
    /// 布局要点：不使用 Padding，也不假设控件的添加顺序。
    ///   两条栏是 Dock=Top/Bottom，插件的 Fill 内容可能晚于外壳添加；
    ///   因此 ApplyTheme 在加载后把两条栏移到 z-order 末尾，让它们先参与 Dock 布局。
    /// 形态要点：窗口能否缩放、最大化是否让开任务栏不属于本类，
    ///   由 ShellMode 与 WindowResizeBehavior 承担；本类只负责视觉与事件转发。
    /// </summary>
    public sealed class WinFormsShell
    {
        private const int WmNcLButtonDown = 0xA1;
        private const int HtCaption = 0x2;
        private const int TitleBarHeight = 36;
        private const int ActionBarHeight = 44;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly Form form;
        private readonly ShellMode mode;
        private Panel titleBar;
        private Label titleLabel;
        private Panel actionBar;
        private Label costTimeLabel;
        private Label statusLabel;

        private WinFormsShell(Form form, ShellMode mode)
        {
            this.form = form;
            this.mode = mode;
        }

        public event EventHandler RunRequested;
        public event EventHandler SaveRequested;
        public event EventHandler CloseRequested;

        /// <summary>
        /// 给窗体装配外壳。应在窗体构造函数中、子类 InitializeComponent 之前调用。
        /// 形态是构造参数而非可变属性：运行期改形态会让已装配的窗口句柄与视觉脱节。
        /// </summary>
        public static WinFormsShell Attach(Form form, ShellMode mode)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));

            var shell = new WinFormsShell(form, mode);
            shell.Build();
            return shell;
        }

        public void SetStatus(string text)
        {
            if (statusLabel != null) statusLabel.Text = text ?? string.Empty;
        }

        public void SetCostTime(string text)
        {
            if (costTimeLabel != null) costTimeLabel.Text = text ?? string.Empty;
        }

        /// <summary>下发主题。必须在窗体 Load 之后调用（施加器需要已创建的句柄）。</summary>
        public void ApplyTheme()
        {
            // Dock 按 z-order 逆序布局：把两条栏移到末尾，使其先于插件内容布局。
            // 否则子类后添加到 Fill 区域的控件会先占满客户区，导致两条栏被盖住。
            if (titleBar != null && form.Controls.Contains(titleBar))
            {
                form.Controls.SetChildIndex(titleBar, form.Controls.Count - 1);
            }
            if (actionBar != null && form.Controls.Contains(actionBar))
            {
                form.Controls.SetChildIndex(actionBar, form.Controls.Count - 1);
            }

            WinFormsThemeApplier.Apply(form);

            if (titleBar != null)
            {
                titleBar.BackColor = ColorBridge.PanelDeep;
                titleLabel.ForeColor = ColorBridge.AccentPrimary;
                titleLabel.Font = new Font(
                    ThemeService.Current.FontFamily,
                    ThemeService.Current.FontSizeBody,
                    FontStyle.Regular);
            }

            if (actionBar != null)
            {
                actionBar.BackColor = ColorBridge.Panel;
                costTimeLabel.ForeColor = ColorBridge.AccentMuted;
                statusLabel.ForeColor = ColorBridge.TextSecondary;
            }
        }

        private void Build()
        {
            // 两种形态都不使用原生边框：Standalone 由 WindowResizeBehavior 补缩放，
            // Embedded 不需要缩放，因此统一 None，避免出现双标题栏。
            form.FormBorderStyle = FormBorderStyle.None;
            form.StartPosition = FormStartPosition.CenterParent;

            BuildTitleBar();
            BuildActionBar();

            if (mode == ShellMode.Standalone)
            {
                // 句柄可能尚未创建，无法此时挂 WndProc，延迟到 HandleCreated。
                if (form.IsHandleCreated) WindowResizeBehavior.Attach(form);
                else form.HandleCreated += delegate { WindowResizeBehavior.Attach(form); };
            }
            else if (titleBar != null)
            {
                titleBar.Visible = false;
            }

            form.TextChanged += delegate
            {
                if (titleLabel != null) titleLabel.Text = form.Text;
            };
        }

        private void BuildTitleBar()
        {
            titleBar = new Panel { Dock = DockStyle.Top, Height = TitleBarHeight };
            titleBar.MouseDown += DragWindow;

            titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            };
            titleLabel.MouseDown += DragWindow;
            titleBar.Controls.Add(titleLabel);

            // Dock=Right 按 z-order 逆序布局：最后添加的最靠左。
            // 因此按 关闭 → 最大化 → 最小化 添加，视觉顺序才是 [─][□][✕]。
            titleBar.Controls.Add(CreateCaptionButton("✕", delegate { Raise(CloseRequested); }));
            titleBar.Controls.Add(CreateCaptionButton("□", ToggleMaximize));
            titleBar.Controls.Add(CreateCaptionButton("─", delegate { form.WindowState = FormWindowState.Minimized; }));

            form.Controls.Add(titleBar);
        }

        private void BuildActionBar()
        {
            actionBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = ActionBarHeight,
                Padding = new Padding(10, 0, 10, 0)
            };

            costTimeLabel = new Label
            {
                Dock = DockStyle.Left,
                Width = 180,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "耗时：00ms"
            };
            actionBar.Controls.Add(costTimeLabel);

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Text = "等待操作"
            };
            actionBar.Controls.Add(statusLabel);

            // 同上：按 关闭 → 保存 → 执行 添加，视觉顺序才是 [执行][保存][关闭]。
            actionBar.Controls.Add(CreateActionButton("关闭", delegate { Raise(CloseRequested); }));
            actionBar.Controls.Add(CreateActionButton("保存", delegate { Raise(SaveRequested); }));
            actionBar.Controls.Add(CreateActionButton("执行", delegate { Raise(RunRequested); }));

            form.Controls.Add(actionBar);
        }

        private Button CreateCaptionButton(string text, Action onClick)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Right,
                Width = 38,
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += delegate { onClick(); };
            return button;
        }

        private Button CreateActionButton(string text, Action onClick)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Right,
                Width = 92,
                FlatStyle = FlatStyle.Flat
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += delegate { onClick(); };
            return button;
        }

        private void ToggleMaximize()
        {
            form.WindowState = form.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || mode == ShellMode.Embedded) return;
            ReleaseCapture();
            SendMessage(form.Handle, WmNcLButtonDown, HtCaption, 0);
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}