using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 非模态告警窗。
    ///
    /// 为什么不用 MessageBox：模态框会卡住发起调用的那条链路，
    /// 现场正在跑料时弹一个模态框等于让设备停在那里等人点。
    ///
    /// 同一 key 同时只允许一个窗（由 五金模切告警 的 pending 集合先挡一层），
    /// 这里再加一层：重复调用会把已有窗口提到前面，而不是再开一个。
    /// </summary>
    public sealed class Form_五金告警 : Form
    {
        private static readonly Dictionary<string, Form_五金告警> _open =
            new Dictionary<string, Form_五金告警>();

        private readonly string _key;
        private readonly Action _retry;
        private bool _closingByUser;

        private Form_五金告警(string key, string title, string message, Action retry)
        {
            _key = key;
            _retry = retry;

            Text = "五金模切 · " + title;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(560, 230);
            MinimumSize = new Size(420, 200);
            TopMost = true;
            ShowInTaskbar = true;
            BackColor = Color.White;

            Label caption = new Label
            {
                Text = title,
                ForeColor = Color.Firebrick,
                Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold),
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(16, 12, 16, 0),
            };

            TextBox body = new TextBox
            {
                Text = message,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei", 9.5F),
                BackColor = Color.White,
                // 只读但允许复制：现场要把告警内容贴给工程师
                TabStop = true,
            };

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 48 };

            Button ok = new Button
            {
                Text = "知道了",
                DialogResult = DialogResult.OK,
                Size = new Size(96, 32),
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                    Location = new Point(444, 8),
            };
            ok.Click += (s, e) => Close();

            footer.Controls.Add(ok);

            // "重试"与"关闭"是两个语义：关闭只是不再提示，通道仍然是暂停的；
            // 只有点重试才会把通道从 Paused 拉回 Idle。合成一个按钮会让人以为关掉就恢复了。
            if (retry != null)
            {
                Button again = new Button
                {
                    Text = "重试",
                    Size = new Size(96, 32),
                    Anchor = AnchorStyles.Right | AnchorStyles.Top,
                    Location = new Point(340, 8),
                };
                again.Click += (s, e) =>
                {
                    Action action = _retry;
                    Close();          // 先关窗：失败会重新 Raise，不该叠一个旧窗
                    if (action != null) action();
                };

                footer.Controls.Add(again);
            }

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(caption);

            FormClosing += (s, e) =>
            {
                // 用户关闭 = 确认：撤销 pending，同一故障再次出现才会再弹
                // 程序关闭 = 不撤销：进程退出时 pending 无意义，避免误清别的窗口状态
                _closingByUser = true;
            };
            FormClosed += (s, e) =>
            {
                lock (_open) { _open.Remove(_key); }
                if (_closingByUser) Frame_Core.业务类.五金模切告警.Dismiss(_key);
            };
        }

        /// <summary>由主窗口的 UI 线程调用。非 UI 线程请先 BeginInvoke。</summary>
        /// <param name="retry">非空时显示"重试"按钮；区别见按钮处的注释。</param>
        public static void ShowAlert(string key, string title, string message, Action retry = null)
        {
            Form_五金告警 existing;
            lock (_open)
            {
                if (_open.TryGetValue(key, out existing) && !existing.IsDisposed)
                {
                    existing.WindowState = FormWindowState.Normal;
                    existing.BringToFront();
                    existing.Activate();
                    return;
                }

                existing = new Form_五金告警(key, title, message, retry);
                _open[key] = existing;
            }

            existing.Show();
        }
    }
}