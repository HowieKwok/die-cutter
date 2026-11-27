using System.Text;

namespace Frame_MetalRun
{
    /// <summary>
    /// 结束膜卷前的未处理项快照。只统计会影响“这一片是否已被检”的数据；
    /// CSV 保存/统计合片属于统计输出，不纳入这里（见设计文档）。
    /// </summary>
    public sealed class MetalRunPending
    {
        /// <summary>帧队列总帧数（各相机 alreadyDeal + 相机队列）。</summary>
        public int FrameQueueCount { get; set; }

        /// <summary>按相机展开的帧队列明细，例如“上线扫 2 / 下线扫 1”。</summary>
        public string FrameDetail { get; set; }

        /// <summary>极片 EA 等待队列张数。</summary>
        public int EaQueueCount { get; set; }

        /// <summary>尺寸工位未凑齐的组数。</summary>
        public int SizeGroupCount { get; set; }

        /// <summary>三条 PLC 结果队列待发送条数（含待检）。</summary>
        public int PlcResultCount { get; set; }

        /// <summary>仿真在途产品（含尚未送达的令牌）。</summary>
        public int SimInFlightCount { get; set; }

        /// <summary>A/B 合片在途片数（已判定、等待另一面配对）。</summary>
        public int MergeQueueCount { get; set; }

        /// <summary>暂停期间因超过每相机上限被丢弃的帧数（诊断用）。</summary>
        public int DroppedFrameCount { get; set; }

        /// <summary>全部未处理项总数（含暂停期间丢弃的帧）。</summary>
        public int Total
        {
            get
            {
                return FrameQueueCount + EaQueueCount + SizeGroupCount + PlcResultCount +
                       SimInFlightCount + MergeQueueCount + DroppedFrameCount;
            }
        }

        /// <summary>给结束确认弹窗用的分类文本。</summary>
        public string ToDisplayText()
        {
            StringBuilder sb = new StringBuilder();
            if (FrameQueueCount > 0)
            {
                sb.AppendLine("· 待处理图像：" + FrameQueueCount + " 帧" +
                              (string.IsNullOrEmpty(FrameDetail) ? "" : "（" + FrameDetail + "）"));
            }
            if (EaQueueCount > 0) sb.AppendLine("· 极片 EA 待判定：" + EaQueueCount + " 张");
            if (SizeGroupCount > 0) sb.AppendLine("· 尺寸未凑齐组：" + SizeGroupCount + " 组");
            if (PlcResultCount > 0) sb.AppendLine("· PLC 待发送结果：" + PlcResultCount + " 条（含待检）");
            if (SimInFlightCount > 0) sb.AppendLine("· 仿真在途产品：" + SimInFlightCount + " 片");
            if (MergeQueueCount > 0) sb.AppendLine("· 合片在途：" + MergeQueueCount + " 片");
            if (DroppedFrameCount > 0) sb.AppendLine("· 暂停期间已丢弃：" + DroppedFrameCount + " 帧");

            if (sb.Length == 0) return "当前没有未处理项。";
            return sb.ToString().TrimEnd();
        }
    }
}
