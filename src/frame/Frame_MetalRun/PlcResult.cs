namespace Frame_MetalRun
{
    /// <summary>回传 PLC 的三路结果值。0 停止 / 1 OK / 2 NG / 3 待检。</summary>
    public enum PlcResult : short
    {
        Stopped = 0,
        Ok = 1,
        Ng = 2,
        Pending = 3,
    }
}
