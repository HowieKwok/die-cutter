using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金帧与发布状态板。
    ///
    /// 做成 UserControl 而不是 Form：既能被主界面嵌进去，也能单独托在一个窗口里，
    /// 不需要先知道主界面的 Tab 结构。
    ///
    /// 只读展示，不提供任何操作——所有处置都在告警窗或配置里做，
    /// 避免这里长出第二个能改运行时状态的地方。
    /// </summary>
    public sealed class UC_五金状态板 : UserControl
    {
        private readonly Label _body;
        private readonly Timer _timer;

        public UC_五金状态板()
        {
            BackColor = Color.White;

            _body = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Font = new Font("Consolas", 10F),
                Padding = new Padding(14),
                TextAlign = ContentAlignment.TopLeft,
                BackColor = Color.White,
            };

            Controls.Add(_body);

            _timer = new Timer { Interval = 500 };
            _timer.Tick += (s, e) => RefreshBoard();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshBoard();
            _timer.Start();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _timer.Stop();
            base.OnHandleDestroyed(e);
        }

        public void RefreshBoard()
        {
            StringBuilder sb = new StringBuilder();

            var lineScan = Frame_Core.业务参数类.五金模切帧序号.LineScan;

            sb.AppendLine("── 帧号 ───────────────────────");
            sb.AppendLine($"  上线最新帧号        {lineScan.UpFrameId}");
            sb.AppendLine($"  下线最新帧号        {lineScan.DownFrameId}    （下线偏移 {lineScan.DownOffset}）");
            sb.AppendLine($"  尺寸最新帧号        {Frame_Core.业务参数类.五金模切帧序号.Size.CurrentFrameId}    （上组缺帧 {Frame_Core.业务参数类.五金模切帧序号.Size.LastWindowMissing}）");
            sb.AppendLine($"  代际                {Frame_Core.业务参数类.五金模切帧序号.Generation}");
            sb.AppendLine();

            sb.AppendLine("── 发布通道 ───────────────────");
            sb.AppendLine("  通道            深度/容量  暂停  被拒  队首           最近完成");
            AppendChannel(sb, Server_五金模切.UpResultChannel);
            AppendChannel(sb, Server_五金模切.DownResultChannel);
            AppendChannel(sb, Server_五金模切.SizeResultChannel);
            sb.AppendLine();
            sb.AppendLine("  （深度持续不降 = 发布器没跑或 PLC 收不到）");

            _body.Text = sb.ToString();
        }

        private static void AppendChannel(StringBuilder sb, PlcResultChannel channel)
        {
            // 队首显示帧号 + 值 + 已重试次数：卡住时能一眼看出停在哪一片、试了几次
            PlcResultItem head;
            string headText = channel.TryPeek(out head)
                ? $"帧{head.FrameId}/{head.Value}/试{head.Attempts}"
                : "—";

            sb.AppendLine(
                $"  {channel.Name,-12}{channel.Count,4}/{channel.Capacity,-5}" +
                $"{(channel.IsPaused ? "是" : "否")}    {channel.RejectedCount,4}  " +
                $"{headText,-16}{channel.LastCompletedFrameId}");
        }

        /// <summary>独立窗口形式打开，供没有预留 Tab 的场景临时查看。</summary>
        public static void ShowWindow()
        {
            Form host = new Form
            {
                Text = "五金模切 · 状态板",
                Size = new Size(520, 400),
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.White,
            };
            host.Controls.Add(new UC_五金状态板 { Dock = DockStyle.Fill });
            host.Show();
        }
    }
}