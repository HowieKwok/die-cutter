using Frame_Core;
using Frame_UI;
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

        private RunStateChip _runChip;
        private ToolTip _runTip;

        public UC_StateView()
        {
            InitializeComponent();
            Init();
        }

        public void Init()
        {
            _runTip = new ToolTip
            {
                InitialDelay = 300,
                ReshowDelay = 100,
                AutoPopDelay = 8000,
            };

            ThemeService.ThemeChanged += OnThemeChanged;
            Disposed += delegate
            {
                ThemeService.ThemeChanged -= OnThemeChanged;
                if (_runTip != null) _runTip.Dispose();
            };

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
            AppendRunChip();
            CenterContent();
        }

        /// <summary>
        /// 五金运行态 chip。传 null 表示不显示（激光）；同一状态重复调用不会重绘。
        /// chip 与 _warningLabel 一样在 LoadItems 清空后补回，不会被状态栏刷新冲掉。
        /// </summary>
        public void SetRunState(RunStateChipState? state)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetRunState(state)));
                return;
            }

            if (!state.HasValue)
            {
                if (_runChip == null) return;
                _content.Controls.Remove(_runChip);
                _runChip.Dispose();
                _runChip = null;
                CenterContent();
                return;
            }

            if (_runChip != null && _runChip.State == state.Value)
            {
                AppendRunChip();
                return;
            }

            if (_runChip == null)
            {
                _runChip = new RunStateChip();
            }

            _runChip.SetState(state.Value);
            _runTip.SetToolTip(_runChip, _runChip.TooltipText);
            AppendRunChip();
            CenterContent();
        }

        private void AppendRunChip()
        {
            if (_runChip == null) return;
            if (!_content.Controls.Contains(_runChip))
            {
                _content.Controls.Add(_runChip);
            }
            if (_content.Controls.GetChildIndex(_runChip) != 0)
            {
                _content.Controls.SetChildIndex(_runChip, 0);
            }
        }

        private void OnThemeChanged(object sender, UiTokens tokens)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => { if (_runChip != null) _runChip.Invalidate(); }));
                return;
            }
            if (_runChip != null) _runChip.Invalidate();
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

        /// <summary>状态栏 Normal 底色；运行态 chip 复用同一绿色，避免两个绿不一致。</summary>
        internal static readonly Color NormalBackColor = Color.FromArgb(0, 180, 120);

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
                    return NormalBackColor;
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
