using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Frame_Simulation.Log;

namespace Frame_Simulation.View
{
    /// <summary>
    /// Form_Simulation 的刷新部分。
    ///
    /// 状态全部靠"定时拉快照"，不靠事件推送：宿主那边是三条不同线程写的队列，
    /// 推事件过来还要在这里排队、去重，反而不如每 200ms 读一次快照简单可靠。
    /// 界面关掉不影响仿真，因为这里从不持有任何业务对象。
    /// </summary>
    public partial class Form_Simulation
    {
        private Timer _flushTimer;

        private long _lastBeatCount = -1;
        private bool _beatOn;
        private DateTime _lastBeatAt = DateTime.MinValue;

        private void BindEvents()
        {
            if (_runtime == null) return;

            if (_runtime.Recorder != null) _runtime.Recorder.Recorded += OnRecorded;

            _flushTimer = new Timer { Interval = 200 };
            _flushTimer.Tick += (s, e) => { RefreshStations(); RefreshHeartbeat(); };
            _flushTimer.Start();
        }

        /// <summary>
        /// 记录只保留"最后一条"做单行提示：心跳已改为闪烁点，其余记录的价值在于
        /// 让人知道最近发生了什么，不需要历史列表。
        /// </summary>
        private void OnRecorded(SimRecordItem item)
        {
            if (item == null || item.Channel == SimRecordChannel.Plc) return;
            if (!IsHandleCreated || _labCommon == null) return;

            string text = item.ToString();
            try { BeginInvoke(new Action(() => { if (_labCommon != null) _labCommon.Text = text; })); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void RefreshStations()
        {
            IReadOnlyList<SimStationState> states = null;
            if (_runtime != null && _runtime.StationStateProvider != null)
            {
                try { states = _runtime.StationStateProvider(); }
                catch { states = null; }
            }

            for (int i = 0; i < 3; i++)
            {
                SimStationState s = states != null && i < states.Count ? states[i] : null;

                // 标题以状态源自报的工位名为准（上线扫/下线扫/尺寸工位），相机名放在后面，
                // 这样即使相机创建顺序与工位顺序不一致，标题也不会和内容错位。
                if (s != null && !string.IsNullOrEmpty(s.Name) && _titleLabels[i] != null)
                    _titleLabels[i].Text = s.Name + " · " + CamNameAt(i, _fallbackTitles[i]);

                _stageLabels[i].Text = s == null ? "未接入状态源" : FormatStage(s);
                _queueLabels[i].Text = s == null ? "—" : FormatQueue(s);

                // 不能写死 Color.Black：窗体已被 WinFormsThemeApplier 下发深色主题，
                // 黑字落在深色面板上等于看不见。非暂停态交回主题（父容器的 ForeColor），
                // 只有暂停才覆盖成深浅背景都可见的橙色。
                if (s != null && s.QueueState == "已暂停") _queueLabels[i].ForeColor = Color.OrangeRed;
                else _queueLabels[i].ResetForeColor();
            }

            UpdatePlcLabel();
        }

        private static string FormatStage(SimStationState s)
        {
            return "帧号      " + s.FrameId + Environment.NewLine +
                   "状态      " + (string.IsNullOrEmpty(s.Stage) ? "—" : s.Stage);
        }

        private static string FormatQueue(SimStationState s)
        {
            return "深度      " + s.QueueDepth + " / " + s.QueueCapacity + Environment.NewLine +
                   "状态      " + (string.IsNullOrEmpty(s.QueueState) ? "—" : s.QueueState) + Environment.NewLine +
                   "队首帧号  " + (s.QueueHeadFrameId == 0 ? "—" : s.QueueHeadFrameId.ToString()) + Environment.NewLine +
                   "重试次数  " + s.QueueHeadAttempts + Environment.NewLine +
                   "最近发出  " + (s.LastSentFrameId == 0 ? "—" : s.LastSentFrameId.ToString());
        }

        /// <summary>
        /// 心跳点：收包计数一变就翻转一次颜色，频率即心跳频率；
        /// 超过 3 秒没变化说明通讯断了，置灰——"不闪"本身就是要传达的信息。
        /// </summary>
        private void RefreshHeartbeat()
        {
            if (_runtime == null || _runtime.PlcServer == null)
            {
                _heartbeat.BackColor = Color.Gainsboro;
                return;
            }

            long count = _runtime.PlcServer.ReceivedCount;
            if (count != _lastBeatCount)
            {
                _lastBeatCount = count;
                _lastBeatAt = DateTime.Now;
                _beatOn = !_beatOn;
                _heartbeat.BackColor = _beatOn ? Color.SeaGreen : Color.LightGreen;
            }
            else if ((DateTime.Now - _lastBeatAt).TotalSeconds > 3)
            {
                _heartbeat.BackColor = Color.Gainsboro;
            }
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
            if (_flushTimer != null) { _flushTimer.Stop(); _flushTimer.Dispose(); }
            if (_runtime != null && _runtime.Recorder != null) _runtime.Recorder.Recorded -= OnRecorded;
            base.OnFormClosed(e);
        }
    }
}