namespace Frame_MetalRun
{
    /// <summary>工具栏按钮状态。界面只负责把这里的值贴到控件上，不再自己推断。</summary>
    public sealed class MetalRunButtons
    {
        public bool CanStartRoll { get; set; }
        public bool CanEndRoll { get; set; }
        public bool CanTogglePause { get; set; }

        /// <summary>运行中显示“暂停”，其余显示“继续”。</summary>
        public string PauseText { get; set; }

        public bool CanStepPrev { get; set; }
        public bool CanStepNext { get; set; }

        /// <summary>单步执行中：上一片/下一片/暂停/结束都应拒绝或禁用。</summary>
        public bool StepBusy { get; set; }

        /// <summary>Idle 与 Paused 可编辑；Running 锁定。</summary>
        public bool CanEditRollName { get; set; }

        /// <summary>暂停态可用；“卷号已改”由界面判定。</summary>
        public bool CanSwitchRoll { get; set; }
    }
}
