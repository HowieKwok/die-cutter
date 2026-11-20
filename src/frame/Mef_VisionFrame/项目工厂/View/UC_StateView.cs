using Frame_Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    public partial class UC_StateView : UserControl
    {
        private readonly Dictionary<string, Label> _labels = new Dictionary<string, Label>();
        private Label _warningLabel;
        public UC_StateView()
        {
            InitializeComponent();
            Init();
        }

        public void Init()
        {
            _content.AutoSize = true;
            _content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _content.FlowDirection = FlowDirection.LeftToRight;
            _content.WrapContents = false;
            _content.BackColor = Color.Transparent;
            _content.Anchor = AnchorStyles.None;
            
        }
        public void LoadItems(IEnumerable<StatusLabelItem> items)
        {
            if (items == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => LoadItems(items)));
                return;
            }

            _content.SuspendLayout();

            _content.Controls.Clear();
            _labels.Clear();

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                    continue;

                var label = CreateLabel(item);

                _labels[item.Key] = label;
                _content.Controls.Add(label);
            }

            _content.ResumeLayout();
            AppendWarningLabel();
            CenterContent();
        }

        /// <summary>
        /// 常驻警示条目：与设备状态 chip 同一行、同一父容器，但不会被 LoadItems 的
        /// Clear() 冲掉（清空后由 AppendWarningLabel 补回）。传空文本即移除。
        /// </summary>
        public void SetWarning(string text, Action onClick)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetWarning(text, onClick)));
                return;
            }

            if (_warningLabel != null)
            {
                _content.Controls.Remove(_warningLabel);
                _warningLabel.Dispose();
                _warningLabel = null;
            }

            if (string.IsNullOrWhiteSpace(text))
                return;

            _warningLabel = new Label
            {
                AutoSize = true,
                Text = text,
                ForeColor = Color.White,
                BackColor = GetBackColor(StatusLevel.Alarm),
                Padding = new Padding(12, 6, 12, 6),
                Margin = new Padding(4),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = onClick == null ? Cursors.Default : Cursors.Hand
            };

            if (onClick != null)
            {
                _warningLabel.Click += delegate { onClick(); };
            }

            _content.Controls.Add(_warningLabel);
            CenterContent();
        }

        private void AppendWarningLabel()
        {
            if (_warningLabel != null && !_content.Controls.Contains(_warningLabel))
            {
                _content.Controls.Add(_warningLabel);
            }
        }

        public void BuildLabels(int count)
        {
            var items = Enumerable.Range(0, count)
                .Select(i => new StatusLabelItem
                {
                    Key = i.ToString(),
                    Text = $"状态{i + 1}：--",
                    Status = StatusLevel.Normal
                });

            LoadItems(items);
        }

        public void SetText(string key, string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetText(key, text)));
                return;
            }

            if (!_labels.TryGetValue(key, out var label))
                return;

            label.Text = text;
            CenterContent();
        }

        public void SetStatus(string key, string text, StatusLevel status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetStatus(key, text, status)));
                return;
            }

            if (!_labels.TryGetValue(key, out var label))
                return;

            label.Text = text;
            label.BackColor = GetBackColor(status);
            label.ForeColor = GetForeColor(status);

            CenterContent();
        }

        public void SetStatus(int index, string text, StatusLevel status)
        {
            SetStatus(index.ToString(), text, status);
        }

        public void RefreshStatus(IEnumerable<StatusLabelItem> items)
        {
            if (items == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => RefreshStatus(items)));
                return;
            }

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                    continue;

                if (!_labels.TryGetValue(item.Key, out var label))
                    continue;

                label.Text = item.Text;
                label.BackColor = GetBackColor(item.Status);
                label.ForeColor = GetForeColor(item.Status);
            }

            CenterContent();
        }

        public bool ContainsKey(string key)
        {
            return _labels.ContainsKey(key);
        }

        public void ClearItems()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ClearItems));
                return;
            }

            _content.Controls.Clear();
            _labels.Clear();
            CenterContent();
        }

        private Label CreateLabel(StatusLabelItem item)
        {
            return new Label
            {
                AutoSize = true,
                Text = item.Text,
                ForeColor = GetForeColor(item.Status),
                BackColor = GetBackColor(item.Status),
                Padding = new Padding(12, 6, 12, 6),
                Margin = new Padding(4),
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private Color GetBackColor(StatusLevel status)
        {
            switch (status)
            {
                case StatusLevel.Warning:
                    return Color.FromArgb(255, 170, 0);

                case StatusLevel.Alarm:
                    return Color.FromArgb(220, 45, 45);

                case StatusLevel.Normal:
                default:
                    return Color.FromArgb(0, 180, 120);
            }
        }

        private Color GetForeColor(StatusLevel status)
        {
            return Color.White;
        }

        private void CenterContent()
        {
            if (_content == null)
                return;

            _content.PerformLayout();

            _content.Left = Math.Max(0, (Width - _content.Width) / 2);
            _content.Top = Math.Max(0, (Height - _content.Height) / 2);
        }
    }
  
}
