using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Frame_Simulation;
using Frame_Simulation.Log;

namespace Frame_Simulation.View
{
    /// <summary>
    /// 仿真监控界面。非模态，可与主界面同时操作。
    /// 只做两件事：调产能、看记录。所有数据都来自 SimulationRuntime，
    /// 界面本身不持有任何采集状态，关掉窗口也不影响仿真运行。
    /// </summary>
    public class Form_Simulation : Form
    {
        private readonly SimulationRuntime _runtime;

        private NumericUpDown _numCapacity;
        private Label _labInterval;
        private Label _labPlc;
        private ListBox _listCam;
        private ListBox _listPlc;
        private Timer _flushTimer;

        private readonly List<SimRecordItem> _pendingCam = new List<SimRecordItem>();
        private readonly List<SimRecordItem> _pendingPlc = new List<SimRecordItem>();
        private readonly object _sync = new object();

        public Form_Simulation(SimulationRuntime runtime)
        {
            _runtime = runtime;

            Text = "仿真监控";
            StartPosition = FormStartPosition.CenterParent;
            Width = 900;
            Height = 560;
            MinimizeBox = false;
            ShowInTaskbar = false;

            BuildLayout();
            BindEvents();
        }

        private void BuildLayout()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(12, 16, 12, 8) };

            Label labCap = new Label { Text = "仿真产能（片/分）：", AutoSize = true, Location = new Point(12, 20) };
            _numCapacity = new NumericUpDown
            {
                Location = new Point(150, 16),
                Width = 90,
                Minimum = 1,
                Maximum = 6000,
                Value = _runtime == null ? 30 : Math.Max(1, _runtime.Config.CapacityPerMinute)
            };

            Button btnApply = new Button { Text = "应用", Location = new Point(250, 15), Width = 70 };
            btnApply.Click += (s, e) => ApplyCapacity();

            _labInterval = new Label { AutoSize = true, Location = new Point(336, 20), ForeColor = Color.DimGray };
            _labPlc = new Label { AutoSize = true, Location = new Point(560, 20), ForeColor = Color.DimGray };

            UpdateIntervalLabel();
            UpdatePlcLabel();

            top.Controls.AddRange(new Control[] { labCap, _numCapacity, btnApply, _labInterval, _labPlc });

            SplitContainer split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 440
            };

            _listCam = CreateList("相机仿真记录");
            _listPlc = CreateList("PLC 仿真记录");

            split.Panel1.Controls.Add(WrapWithTitle("相机仿真记录", _listCam));
            split.Panel2.Controls.Add(WrapWithTitle("PLC 仿真记录", _listPlc));

            Controls.Add(split);
            Controls.Add(top);
        }

        private static Control WrapWithTitle(string title, Control body)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill };
            Label lab = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                ForeColor = Color.FromArgb(64, 64, 64)
            };
            body.Dock = DockStyle.Fill;
            panel.Controls.Add(body);
            panel.Controls.Add(lab);
            return panel;
        }

        private static ListBox CreateList(string name)
        {
            return new ListBox
            {
                Name = name,
                IntegralHeight = false,
                HorizontalScrollbar = true,
                Font = new Font("Consolas", 9f)
            };
        }

        private void BindEvents()
        {
            if (_runtime == null) return;

            // 记录可能来自采集线程：只入待处理队列，UI 线程定时批量刷，
            // 否则高产能下每帧一次 Invoke 会把界面拖死。
            _runtime.Recorder.Recorded += OnRecorded;

            _flushTimer = new Timer { Interval = 200 };
            _flushTimer.Tick += (s, e) => Flush();
            _flushTimer.Start();
        }

        private void OnRecorded(SimRecordItem item)
        {
            if (item == null) return;
            lock (_sync)
            {
                if (item.Channel == SimRecordChannel.Camera) _pendingCam.Add(item);
                else _pendingPlc.Add(item);
            }
        }

        private void Flush()
        {
            List<SimRecordItem> cam = null, plc = null;
            lock (_sync)
            {
                if (_pendingCam.Count > 0) { cam = new List<SimRecordItem>(_pendingCam); _pendingCam.Clear(); }
                if (_pendingPlc.Count > 0) { plc = new List<SimRecordItem>(_pendingPlc); _pendingPlc.Clear(); }
            }

            if (cam != null) Append(_listCam, cam);
            if (plc != null) Append(_listPlc, plc);
            UpdatePlcLabel();
        }

        private static void Append(ListBox list, List<SimRecordItem> items)
        {
            list.BeginUpdate();
            foreach (SimRecordItem item in items) list.Items.Add(item.ToString());
            while (list.Items.Count > 2000) list.Items.RemoveAt(0);
            list.TopIndex = Math.Max(0, list.Items.Count - 1);
            list.EndUpdate();
        }

        private void ApplyCapacity()
        {
            if (_runtime == null) return;
            _runtime.SetCapacity((int)_numCapacity.Value);
            UpdateIntervalLabel();
        }

        private void UpdateIntervalLabel()
        {
            if (_runtime == null) { _labInterval.Text = ""; return; }
            _labInterval.Text = string.Format("≈ 每 {0} ms 一张", _runtime.Pacer.IntervalMs);
        }

        private void UpdatePlcLabel()
        {
            if (_runtime == null || _runtime.PlcServer == null) { _labPlc.Text = "PLC：未启动"; return; }

            _labPlc.Text = _runtime.PlcServer.IsStarted
                ? string.Format("PLC：监听 {0}", _runtime.PlcServer.Port)
                : "PLC：未启动";
            _labPlc.ForeColor = _runtime.PlcServer.IsStarted ? Color.SeaGreen : Color.Firebrick;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_flushTimer != null) _flushTimer.Stop();
            if (_runtime != null) _runtime.Recorder.Recorded -= OnRecorded;
            base.OnFormClosed(e);
        }
    }
}