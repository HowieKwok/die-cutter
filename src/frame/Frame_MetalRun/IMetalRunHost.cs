namespace Frame_MetalRun
{
    /// <summary>
    /// 五金运行控制器的宿主端口。控制器只负责状态编排，
    /// 真正的流程、队列、PLC、仿真操作全部由 Server_五金模切 实现。
    /// 所有方法返回 bool + 原因，不在这里弹窗、不吞异常。
    /// </summary>
    public interface IMetalRunHost
    {
        bool IsSimulation { get; }

        /// <summary>配方 JSON + 流程 Instance.xml 等校验。</summary>
        bool ValidateRecipe(out string reason);

        /// <summary>膜卷号合法性与目录重复校验。</summary>
        bool ValidateFilmRoll(string filmRollName, out string reason);

        /// <summary>写膜卷号、生产路径、光源参数；Is_MojuanStart 保持在 false。</summary>
        bool PrepareFilmRoll(string filmRollName, out string reason);

        /// <summary>清场：显示/统计/帧/EA/尺寸/结果/仿真在途，序号归 1，三路写 0。</summary>
        bool ClearBeforeStart(out string reason);

        /// <summary>解除仿真冻结 + 启动发布器 + 启动全部流程 + 逐条校验线程已启动。</summary>
        bool StartProduction(out string reason);

        /// <summary>暂停视觉流程：仿真冻结触发，流程线程挂起；不清队列、不写 PLC、不停发布器。</summary>
        bool PauseProduction(out string reason);

        /// <summary>恢复视觉流程：流程恢复、仿真解除冻结。</summary>
        bool ResumeProduction(out string reason);

        /// <summary>等待所有流程线程退出当前 ExecuteProcess。超时返回 false。</summary>
        bool WaitAllIdle(int timeoutMs);

        /// <summary>结束前未处理项快照；失败返回空快照而不是抛异常。</summary>
        MetalRunPending InspectPending();

        /// <summary>
        /// 硬停并清场：停流程→停发布器→写0→INIProduct→清结果/尺寸/仿真/显示→排空保存队列。
        /// StopFailed 时生产可能仍在跑，调用方保持 Paused；DegradedToIdle 时线程已停但复位失败。
        /// </summary>
        StopClearResult StopAndClear(out string reason);

        /// <summary>
        /// 换卷：结束当前卷的数据代际并切到新卷。流程线程保持挂起，PLC 会话不重建。
        /// 内部按阶段执行并在失败时自行回滚或降级；调用方只按返回值改状态，不做阶段推断。
        /// 前置：控制器已确认 Paused、WaitAllIdle 成功。
        /// </summary>
        SwitchRollResult SwitchFilmRoll(string filmRollName, out string reason);

        /// <summary>仿真在途产品数（已放行、尚未到尺寸工位）；非仿真返回 0。</summary>
        int GetSimInFlightCount();

        /// <summary>
        /// 仿真单步：推图/手动帧号/分组 StepOnce。仅控制器在 Paused 且仿真时调用。
        /// discardInFlight=true 时先丢弃仿真在途令牌，消除尺寸帧号回退风险。
        /// </summary>
        bool StepSimulation(bool forward, bool discardInFlight, out string reason);

        /// <summary>开始失败时回滚到 Idle：停流程/发布器、写0、清场、标志复位。</summary>
        void RollbackStart();
    }
}
