using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Frame_Core
{
    /// <summary>
    /// 路径设置：集中配置所有落盘根目录，保存后立即生效，无需重启。
    ///
    /// 生产数据根在“下一次开卷/换卷”时使用；膜卷运行或暂停期间禁止修改。
    /// MES 文件根为预留项，当前版本没有写入方。
    /// </summary>
    public class Form_PathSetting : Form
    {
        private readonly PathParameter _param;
        private readonly List<PathRow> _rows = new List<PathRow>();

        private TextBox _txtData;
        private TextBox _txtCalib;
        private TextBox _txtCheck;
        private TextBox _txtImage;
        private TextBox _txtLog;
        private TextBox _txtMes;

        private sealed class PathRow
        {
            public string Title;
            public TextBox Box;
            public Label Status;
        }

        public Form_PathSetting(PathParameter param)
        {
            _param = param ?? new PathParameter();
            if (string.IsNullOrWhiteSpace(_param.DataRoot)) _param.Init();

            Text = "路径设置";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(920, 460);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;

            BuildUi();
            FormStylingHook.Style(this);
        }

        private void BuildUi()
        {
            Label hint = new Label
            {
                Location = new Point(12, 10),
                Size = new Size(896, 38),
                Text = "所有路径保存后立即生效，无需重启。\r\n" +
                       "生产数据根在“下一次开卷/换卷”时使用；膜卷运行或暂停期间不能修改。",
            };
            Controls.Add(hint);

            TableLayoutPanel table = new TableLayoutPanel
            {
                Location = new Point(12, 54),
                Size = new Size(896, 240),
                ColumnCount = 5,
                RowCount = 6,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            for (int i = 0; i < 6; i++)
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            AddRow(table, 0, "生产数据根", out _txtData);
            AddRow(table, 1, "标定目录", out _txtCalib);
            AddRow(table, 2, "点检数据根", out _txtCheck);
            AddRow(table, 3, "点检图像根", out _txtImage);
            AddRow(table, 4, "日志目录", out _txtLog);
            AddRow(table, 5, "MES 文件根（预留）", out _txtMes);
            Controls.Add(table);

            Label mesNote = new Label
            {
                Location = new Point(12, 300),
                Size = new Size(896, 36),
                ForeColor = Color.Gray,
                Text = "MES 文件根为预留项：当前版本没有 MES 落盘实现，配置会保存，后续接入时直接使用。",
            };
            Controls.Add(mesNote);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 12, 8),
            };

            Button btnCancel = new Button { Text = "取消", Size = new Size(80, 30) };
            Button btnSave = new Button { Text = "保存", Size = new Size(80, 30) };
            Button btnDefault = new Button { Text = "恢复默认", Size = new Size(90, 30) };
            Button btnDataRoot = new Button { Text = "按数据根重置", Size = new Size(110, 30) };

            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            btnSave.Click += delegate { SaveAndApply(); };
            btnDefault.Click += delegate { ApplyDefaults(); };
            btnDataRoot.Click += delegate { ResetFromDataRoot(); };

            AcceptButton = btnSave;
            CancelButton = btnCancel;

            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnSave);
            buttons.Controls.Add(btnDefault);
            buttons.Controls.Add(btnDataRoot);
            Controls.Add(buttons);

            _txtData.Text = _param.DataRoot;
            _txtCalib.Text = _param.CalibrationRoot;
            _txtCheck.Text = _param.PointCheckRoot;
            _txtImage.Text = _param.PointCheckImageRoot;
            _txtLog.Text = Project.Instance.logParam.LogSavePath;
            _txtMes.Text = _param.MesRoot;
        }

        private void AddRow(TableLayoutPanel table, int row, string title, out TextBox box)
        {
            Label label = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
            };

            TextBox input = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 4, 8),
            };

            Button browse = new Button
            {
                Text = "浏览…",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 4, 5),
            };

            Button open = new Button
            {
                Text = "打开",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 4, 5),
            };

            Label status = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                ForeColor = Color.Gray,
            };

            browse.Click += delegate { BrowseFolder(input); };
            open.Click += delegate { OpenFolder(input.Text); };
            input.TextChanged += delegate { UpdateStatus(input, status); };

            table.Controls.Add(label, 0, row);
            table.Controls.Add(input, 1, row);
            table.Controls.Add(browse, 2, row);
            table.Controls.Add(open, 3, row);
            table.Controls.Add(status, 4, row);

            _rows.Add(new PathRow { Title = title, Box = input, Status = status });
            box = input;
        }

        private void BrowseFolder(TextBox box)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择目录";
                if (Directory.Exists(box.Text.Trim()))
                    dialog.SelectedPath = box.Text.Trim();

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    box.Text = dialog.SelectedPath;
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                string full = path == null ? "" : path.Trim();
                if (string.IsNullOrEmpty(full) || !Directory.Exists(full))
                {
                    MessageBox.Show("目录不存在或尚未创建。", "路径设置",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                System.Diagnostics.Process.Start("explorer.exe", "\"" + full + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "路径设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void UpdateStatus(TextBox box, Label status)
        {
            // 用户逐字输入时路径随时可能非法（引号、竖线、控制字符），
            // Path.IsPathRooted/GetFullPath 都会抛异常，这里必须全程兜住。
            try
            {
                string path = box.Text.Trim();
                if (string.IsNullOrEmpty(path))
                {
                    SetStatus(status, "未填写", Color.Gray);
                    return;
                }

                bool rooted;
                try { rooted = Path.IsPathRooted(path); }
                catch
                {
                    SetStatus(status, "含非法字符", Color.Red);
                    return;
                }
                if (!rooted)
                {
                    SetStatus(status, "需要绝对路径", Color.Red);
                    return;
                }

                string root;
                try { root = Path.GetPathRoot(Path.GetFullPath(path)); }
                catch
                {
                    SetStatus(status, "路径无效", Color.Red);
                    return;
                }

                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    SetStatus(status, "磁盘不存在", Color.Red);
                    return;
                }
                if (!Directory.Exists(path))
                {
                    SetStatus(status, "保存时新建", Color.DarkOrange);
                    return;
                }

                try
                {
                    DriveInfo info = new DriveInfo(root);
                    double freeGb = info.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                    SetStatus(status, "就绪，剩余 " + freeGb.ToString("F1") + " GB", Color.Green);
                }
                catch
                {
                    SetStatus(status, "就绪", Color.Green);
                }
            }
            catch
            {
                SetStatus(status, "路径无效", Color.Red);
            }
        }

        private static void SetStatus(Label status, string text, Color color)
        {
            status.Text = text;
            status.ForeColor = color;
        }

        private void SaveAndApply()
        {
            if (Project.Instance.Is_MojuanStart || Project.Instance.Is_Run)
            {
                MessageBox.Show("膜卷运行或暂停中，不能修改路径；请先结束膜卷。", "路径设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string error;
            foreach (PathRow row in _rows)
            {
                if (!PathConfigService.CheckWritable(row.Box.Text, row.Title, out error))
                {
                    MessageBox.Show(error, "路径设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    row.Box.Focus();
                    return;
                }
            }

            string oldImageRoot = _param.PointCheckImageRoot;

            // 用临时对象提交：校验或写盘失败时，内存里的当前配置不受影响
            PathParameter next = new PathParameter
            {
                filePath = _param.filePath,
                DataRoot = _txtData.Text,
                CalibrationRoot = _txtCalib.Text,
                PointCheckRoot = _txtCheck.Text,
                PointCheckImageRoot = _txtImage.Text,
                MesRoot = _txtMes.Text,
            };

            error = PathConfigService.Apply(next, _txtLog.Text, oldImageRoot);
            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show(error, "路径设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void ResetFromDataRoot()
        {
            string dataRoot = _txtData.Text.Trim();
            if (string.IsNullOrEmpty(dataRoot))
            {
                MessageBox.Show("请先填写生产数据根。", "路径设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                _txtCalib.Text = Path.Combine(dataRoot, "标定");
                _txtCheck.Text = Path.Combine(dataRoot, "点检数据");
                _txtImage.Text = Path.Combine(dataRoot, "点检图像");
                _txtMes.Text = Path.Combine(dataRoot, "MES文件");
            }
            catch
            {
                MessageBox.Show("生产数据根含非法字符，无法派生其它目录。", "路径设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ApplyDefaults()
        {
            string dataRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "生产数据");
            _txtData.Text = dataRoot;
            _txtCalib.Text = Path.Combine(dataRoot, "标定");
            _txtCheck.Text = Path.Combine(dataRoot, "点检数据");
            _txtImage.Text = Path.Combine(dataRoot, "点检图像");
            _txtLog.Text = PathParameter.DefaultLogPath();
            _txtMes.Text = Path.Combine(dataRoot, "MES文件");
        }
    }
}
