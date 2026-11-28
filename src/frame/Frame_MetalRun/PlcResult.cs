namespace Frame_MetalRun
{
    /// <summary>回传 PLC 的三路结果值。0 停止 / 1 OK / 2 NG / 3 待检。</summary>
    public enum PlcResult : short
    {
        Stopped = 0,
        Ok = 1,
        Ng = 2,
        /// <summary>
        /// 人工复检：既不是 OK 也不是 NG，需要人工确认。
        /// 只在结算出结果时写入，帧号分配阶段不得写本值。
        /// </summary>
        Review = 3,
    }
}
