namespace Frame_MetalRun
{
    /// <summary>五金运行态。唯一真源：UI 与宿主都只读控制器状态。</summary>
    public enum MetalRunState
    {
        /// <summary>未开始膜卷。流程线程停止，帧触发不产生结果。</summary>
        Idle = 0,

        /// <summary>运行中。流程消费帧，结果正常发布。</summary>
        Running = 1,

        /// <summary>已暂停。流程线程存活但跳过执行；自动模式帧继续入队，PLC 发布继续。</summary>
        Paused = 2,
    }

    /// <summary>结束膜卷的三种结果。</summary>
    public enum EndRollResult
    {
        /// <summary>已经结束并清场。</summary>
        Completed = 0,

        /// <summary>存在未处理项，需要界面确认是否丢弃。</summary>
        NeedsConfirm = 1,

        /// <summary>操作失败（等待超时、宿主异常、复位失败等）；复位失败时状态已降级 Idle。</summary>
        Failed = 2,
    }

    /// <summary>
    /// 换卷结果。NeedsConfirm 只由控制器产出；
    /// DegradedToIdle / DegradeFailed 只由宿主产出，控制器只做状态映射。
    /// </summary>
    public enum SwitchRollResult
    {
        /// <summary>已切到新卷，状态仍为 Paused。</summary>
        Completed = 0,

        /// <summary>存在未处理项，需要界面确认是否丢弃。仅控制器使用。</summary>
        NeedsConfirm = 1,

        /// <summary>失败且未动旧卷：仍 Paused，旧卷可继续。</summary>
        Failed = 2,

        /// <summary>清场中途失败，已 StopAndClear 降级进入 Idle。</summary>
        DegradedToIdle = 3,

        /// <summary>清场失败且降级也失败：状态不确定，必须重启软件。</summary>
        DegradeFailed = 4,
    }

    /// <summary>
    /// 结束膜卷清场结果。StopFailed 表示生产可能还在跑；
    /// DegradedToIdle 表示线程已停但复位失败，调用方应降级到 Idle 并提示重启。
    /// </summary>
    public enum StopClearResult
    {
        /// <summary>已停并完成清场。</summary>
        Completed = 0,

        /// <summary>流程/发布器没停住：保持 Paused，可继续或重试结束。</summary>
        StopFailed = 1,

        /// <summary>生产已停止，但 INIProduct/清场失败：控制器降级到 Idle。</summary>
        DegradedToIdle = 2,
    }
}
