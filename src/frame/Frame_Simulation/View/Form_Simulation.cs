using System;
using System.Drawing;
using System.Windows.Forms;
using Frame_Simulation;

namespace Frame_Simulation.View
{
    /// <summary>
    /// 仿真监控界面。非模态，可与主界面同时操作。
    ///
    /// 布局：单页三列（上线扫 / 下线扫 / 尺寸），每列上方是该工位的帧ID处理状态，
    /// 下方是它对应的 PLC 发送队列状态。三列同屏是为了横向对比——
    /// 分页时只能看到一路，"上线第 12 片、下线第 12 片、尺寸第 10 片"这种对照做不出来。
    ///
    /// 心跳不再写日志：顶部一个闪烁点，收包计数一变就翻转一次；
    /// 长时间不翻转即通讯断开，比翻滚动日志快得多。
    /// </summary>
    public partial class Form_Simulation : Form
    {
        private readonly SimulationRuntime _runtime;

        private NumericUpDown _numCapacity;
        private Label _labInterval;
        private Label _labPlc;
        private Panel _heartbeat;
        private Label _labCommon;

        // 三列固定顺序：0=上线 1=下线 2=尺寸
        private readonly Label[] _stageLabels = new Label[3];
        private readonly Label[] _queueLabels = new Label[3];
        private readonly Label[] _titleLabels = new Label[3];
        private readonly string[] _fallbackTitles = { "上线扫", "下线扫", "尺寸工位" };

        public Form_Simulation(SimulationRuntime runtime)
        {
            _runtime = runtime;

            Text = "仿真监控";
            StartPosition = FormStartPosition.CenterParent;
            Width = 1040;
            Height = 620;
            MinimizeBox = false;
            ShowInTaskbar = false;

            BuildLayout();
            BindEvents();
        }

        private void BuildLayout()
        {
            Controls.Add(BuildStationGrid());
            Controls.Add(BuildTopBar());
            Controls.Add(BuildBottomBar());
        }

        private Panel BuildTopBar()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(12, 12, 12, 6) };

            Label labCap = new Label { Text = "仿真产能（片/分）：", AutoSize = true, Location = new Point(12, 17) };
            _numCapacity = new NumericUpDown
            {
                Location = new Point(150, 13),
                Width = 90,
                Minimum = 1,
                Maximum = 6000,
                Value = _runtime == null ? 30 : Math.Max(1, _runtime.Config.CapacityPerMinute)
            };

            Button btnApply = new Button { Text = "应用", Location = new Point(250, 12), Width = 70 };
            btnApply.Click += (s, e) => ApplyCapacity();

            _labInterval = new Label { AutoSize = true, Location = new Point(336, 17), ForeColor = Color.DimGray };
            _labPlc = new Label { AutoSize = true, Location = new Point(480, 17), ForeColor = Color.DimGray };

            Label labBeat = new Label { Text = "心跳", AutoSize = true, Location = new Point(700, 17), ForeColor = Color.DimGray };
            _heartbeat = new Panel
            {
                Location = new Point(744, 12),
                Size = new Size(16, 16),
                BackColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle,
            };

            UpdateIntervalLabel();
            UpdatePlcLabel();

            top.Controls.AddRange(new Control[]
            {
                labCap, _numCapacity, btnApply, _labInterval, _labPlc, labBeat, _heartbeat
            });
            return top;
        }

        private Control BuildStationGrid()
        {
            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(8, 2, 8, 2),
            };
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 58));

            for (int i = 0; i < 3; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));

                _stageLabels[i] = CreateBody("—");
                _queueLabels[i] = CreateBody("—");

                // 标题先给兜底工位名，RefreshStations 再用状态源自报的 Name 覆盖。
                // 这里刻意不拼相机名：相机创建顺序是 下线→上线→尺寸，按索引取名字会让
                // 列标题变成"上线扫 · 下线扫"这种看起来像两个工位的错位文本。
                _titleLabels[i] = CreateTitle(_fallbackTitles[i] + " · 帧处理");
                grid.Controls.Add(WrapWithTitle(_titleLabels[i], _stageLabels[i]), i, 0);
                grid.Controls.Add(WrapWithTitle(CreateTitle("PLC 发送队列"), _queueLabels[i]), i, 1);
            }

            return grid;
        }

        private Panel BuildBottomBar()
        {
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(12, 4, 12, 4) };
            _labCommon = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Text = "等待仿真运行…",
            };
            bottom.Controls.Add(_labCommon);
            return bottom;
        }

        private static Label CreateBody(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Font = new Font("Consolas", 10F),
                Padding = new Padding(10),
                TextAlign = ContentAlignment.TopLeft,
                Text = text,
            };
        }

        private static Label CreateTitle(string title)
        {
            return new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                BackColor = Color.FromArgb(240, 240, 240),
                ForeColor = Color.FromArgb(64, 64, 64),
            };
        }

        private static Control WrapWithTitle(Label lab, Control body)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };

            body.Dock = DockStyle.Fill;
            panel.Controls.Add(body);
            panel.Controls.Add(lab);
            return panel;
        }
    }
}