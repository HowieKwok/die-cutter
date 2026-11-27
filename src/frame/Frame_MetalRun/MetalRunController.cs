using System;

namespace Frame_MetalRun
{
    /// <summary>
    /// 五金运行控制器：三态迁移与动作编排。
    ///
    /// 这里不碰图像队列、PLC、流程线程；所有副作用通过 IMetalRunHost 完成。
    /// 好处是：暂停=不调 StopProduction / 不清队列 / 不写 0 是结构性保证，
    /// 而不是靠调用方自觉。
    /// </summary>
    public sealed class MetalRunController
    {
        private readonly object _sync = new object();
        private readonly IMetalRunHost _host;

        private MetalRunState _state = MetalRunState.Idle;
        private bool _stepBusy;

        public MetalRunController(IMetalRunHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            _host = host;
        }

        /// <summary>状态变化后触发。界面只订阅这一个事件刷新工具栏/状态栏。</summary>
        public event Action StateChanged;

        public MetalRunState State
        {
            get { lock (_sync) { return _state; } }
        }

        public bool IsSimulation
        {
            get { return _host.IsSimulation; }
        }

        public bool StepBusy
        {
            get { lock (_sync) { return _stepBusy; } }
        }

        /// <summary>开始膜卷：校验配方/膜卷 → 清场 → 启动发布器与全部流程。</summary>
        public bool StartFilmRoll(string filmRollName, out string error)
        {
            lock (_sync)
            {
                error = null;
                if (_state != MetalRunState.Idle)
                {
                    error = "当前已开始膜卷，请先结束当前膜卷。";
                    return false;
                }
                if (_stepBusy)
                {
                    error = "单步执行中，请稍后再试。";
                    return false;
                }

                string reason;
                if (!_host.ValidateRecipe(out reason))
                {
                    error = string.IsNullOrEmpty(reason) ? "配方校验失败。" : reason;
                    return false;
                }
                if (!_host.ValidateFilmRoll(filmRollName, out reason))
                {
                    error = string.IsNullOrEmpty(reason) ? "膜卷号校验失败。" : reason;
                    return false;
                }

                try
                {
                    if (!_host.PrepareFilmRoll(filmRollName, out reason))
                    {
                        error = string.IsNullOrEmpty(reason) ? "建立膜卷生产路径失败。" : reason;
                        RollbackLocked();
                        return false;
                    }
                    if (!_host.ClearBeforeStart(out reason))
                    {
                        error = string.IsNullOrEmpty(reason) ? "开始前清场失败。" : reason;
                        RollbackLocked();
                        return false;
                    }
                    if (!_host.StartProduction(out reason))
                    {
                        error = string.IsNullOrEmpty(reason) ? "启动视觉流程失败。" : reason;
                        RollbackLocked();
                        return false;
                    }

                    _state = MetalRunState.Running;
                }
                catch (Exception ex)
                {
                    error = "开始膜卷异常：" + ex.Message;
                    RollbackLocked();
                    return false;
                }
            }

            RaiseStateChanged();
            return true;
        }

        /// <summary>暂停：运行中才允许。只挂流程，不清队列、不写 PLC。</summary>
        public bool Pause(out string error)
        {
            lock (_sync)
            {
                error = null;
                if (_state != MetalRunState.Running)
                {
                    error = _state == MetalRunState.Paused ? "当前已经是暂停状态。" : "未开始膜卷，无法暂停。";
                    return false;
                }
                if (_stepBusy)
                {
                    error = "单步执行中，请稍后再试。";
                    return false;
                }

                string reason;
                try
                {
                    if (!_host.PauseProduction(out reason))
                    {
                        error = string.IsNullOrEmpty(reason) ? "暂停失败。" : reason;
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    error = "暂停异常：" + ex.Message;
                    return false;
                }

                _state = MetalRunState.Paused;
            }

            RaiseStateChanged();
            return true;
        }

        /// <summary>继续：已暂停才允许。</summary>
        public bool Resume(out string error)
        {
            lock (_sync)
            {
                error = null;
                if (_state != MetalRunState.Paused)
                {
                    error = _state == MetalRunState.Running ? "当前已经是运行状态。" : "未开始膜卷，无法继续。";
                    return false;
                }
                if (_stepBusy)
                {
                    error = "单步执行中，请稍后再试。";
                    return false;
                }

                string reason;
                try
                {
                    if (!_host.ResumeProduction(out reason))
                    {
                        error = string.IsNullOrEmpty(reason) ? "继续失败。" : reason;
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    error = "继续异常：" + ex.Message;
                    return false;
                }

                _state = MetalRunState.Running;
            }

            RaiseStateChanged();
            return true;
        }

        /// <summary>
        /// 结束膜卷。运行中会先自动暂停；检查到未处理项且未确认时返回 NeedsConfirm，
        /// 状态保持 Paused，不会自动恢复运行。
        /// </summary>
        public EndRollResult EndFilmRoll(bool confirmDiscard, out MetalRunPending pending, out string error)
        {
            pending = new MetalRunPending();
            error = null;
            bool notify = false;

            try
            {
                lock (_sync)
                {
                    if (_state == MetalRunState.Idle)
                    {
                        error = "未开始膜卷，无需结束。";
                        return EndRollResult.Failed;
                    }
                    if (_stepBusy)
                    {
                        error = "单步执行中，请稍后再试。";
                        return EndRollResult.Failed;
                    }

                    if (_state == MetalRunState.Running)
                    {
                        string pauseReason;
                        try
                        {
                            if (!_host.PauseProduction(out pauseReason))
                            {
                                error = string.IsNullOrEmpty(pauseReason) ? "结束前暂停失败。" : pauseReason;
                                return EndRollResult.Failed;
                            }
                        }
                        catch (Exception ex)
                        {
                            error = "结束前暂停异常：" + ex.Message;
                            return EndRollResult.Failed;
                        }

                        _state = MetalRunState.Paused;
                        notify = true;   // 锁外统一通知，避免事件处理器回调取同一把锁
                    }

                    try
                    {
                        if (!_host.WaitAllIdle(3000))
                        {
                            error = "流程仍在处理当前帧，请稍后重试。";
                            return EndRollResult.Failed;
                        }

                        MetalRunPending snapshot = _host.InspectPending();
                        pending = snapshot ?? new MetalRunPending();

                        if (pending.Total > 0 && !confirmDiscard)
                        {
                            return EndRollResult.NeedsConfirm;
                        }

                        string stopReason;
                        StopClearResult stopResult = _host.StopAndClear(out stopReason);
                        if (stopResult == StopClearResult.StopFailed)
                        {
                            error = string.IsNullOrEmpty(stopReason) ? "结束膜卷清场失败。" : stopReason;
                            return EndRollResult.Failed;
                        }

                        // 生产已停：即使复位失败也必须离开 Paused，否则继续/再次结束都会被拒
                        _state = MetalRunState.Idle;
                        notify = true;

                        if (stopResult == StopClearResult.DegradedToIdle)
                        {
                            error = string.IsNullOrEmpty(stopReason)
                                ? "生产已停止，但清场复位失败，请重启软件。"
                                : stopReason;
                            return EndRollResult.Failed;
                        }
                    }
                    catch (Exception ex)
                    {
                        error = "结束膜卷异常：" + ex.Message;
                        return EndRollResult.Failed;
                    }

                    return EndRollResult.Completed;
                }
            }
            finally
            {
                if (notify) RaiseStateChanged();
            }
        }

        /// <summary>
        /// 换卷：暂停态下结束当前卷数据代际并切到新卷，完成后仍为 Paused。
        /// 失败语义完全由宿主的 SwitchRollResult 决定，控制器不做阶段推断。
        /// </summary>
        public SwitchRollResult SwitchFilmRoll(string filmRollName, bool confirmDiscard,
                                               out MetalRunPending pending, out string error)
        {
            pending = new MetalRunPending();
            error = null;

            SwitchRollResult result;
            lock (_sync)
            {
                if (_state != MetalRunState.Paused)
                {
                    error = _state == MetalRunState.Running
                        ? "请先暂停后再换卷。"
                        : "请先开始膜卷。";
                    return SwitchRollResult.Failed;
                }
                if (_stepBusy)
                {
                    error = "单步执行中，请稍后再试。";
                    return SwitchRollResult.Failed;
                }

                string reason;
                try
                {
                    if (!_host.ValidateFilmRoll(filmRollName, out reason))
                    {
                        error = string.IsNullOrEmpty(reason) ? "膜卷号校验失败。" : reason;
                        return SwitchRollResult.Failed;
                    }
                    if (!_host.WaitAllIdle(3000))
                    {
                        error = "流程仍在处理当前帧，请稍后重试。";
                        return SwitchRollResult.Failed;
                    }

                    MetalRunPending snapshot = _host.InspectPending();
                    pending = snapshot ?? new MetalRunPending();
                    if (pending.Total > 0 && !confirmDiscard)
                    {
                        return SwitchRollResult.NeedsConfirm;
                    }

                    result = _host.SwitchFilmRoll(filmRollName, out reason);
                    if (!string.IsNullOrEmpty(reason)) error = reason;
                }
                catch (Exception ex)
                {
                    error = "换卷异常：" + ex.Message;
                    return SwitchRollResult.Failed;
                }

                // 只有确定进入 Idle 才改状态；DegradeFailed 时线程/数据不确定，保持 Paused
                if (result == SwitchRollResult.DegradedToIdle)
                {
                    _state = MetalRunState.Idle;
                }
            }

            RaiseStateChanged();
            return result;
        }

        /// <summary>仿真在途产品数；自动模式返回 0。界面在单步前用它决定是否弹确认。</summary>
        public int GetSimInFlightCount()
        {
            if (!_host.IsSimulation) return 0;
            try { return _host.GetSimInFlightCount(); }
            catch { return 0; }
        }

        /// <summary>仿真单步。仅仿真 + 已暂停；执行期间 StepBusy 阻止其它状态迁移。</summary>
        public bool Step(bool forward, bool discardInFlight, out string error)
        {
            error = null;

            lock (_sync)
            {
                if (!_host.IsSimulation)
                {
                    error = "仅仿真模式支持单步。";
                    return false;
                }
                if (_state != MetalRunState.Paused)
                {
                    error = "请先暂停后再单步。";
                    return false;
                }
                if (_stepBusy)
                {
                    error = "单步执行中，请稍后再试。";
                    return false;
                }
                _stepBusy = true;
            }

            RaiseStateChanged();
            bool ok;
            string reason;
            try
            {
                ok = _host.StepSimulation(forward, discardInFlight, out reason);
                if (!ok) error = string.IsNullOrEmpty(reason) ? "单步执行失败。" : reason;
            }
            catch (Exception ex)
            {
                ok = false;
                error = "单步异常：" + ex.Message;
            }
            finally
            {
                lock (_sync) { _stepBusy = false; }
                RaiseStateChanged();
            }

            return ok;
        }

        /// <summary>界面按钮矩阵。</summary>
        public MetalRunButtons GetButtons()
        {
            lock (_sync)
            {
                bool started = _state != MetalRunState.Idle;
                bool canOperate = started && !_stepBusy;
                return new MetalRunButtons
                {
                    CanStartRoll = _state == MetalRunState.Idle && !_stepBusy,
                    CanEndRoll = canOperate,
                    CanTogglePause = canOperate,
                    PauseText = _state == MetalRunState.Running ? "暂停" : "继续",
                    CanStepPrev = _host.IsSimulation && _state == MetalRunState.Paused && !_stepBusy,
                    CanStepNext = _host.IsSimulation && _state == MetalRunState.Paused && !_stepBusy,
                    StepBusy = _stepBusy,
                    CanEditRollName = (_state == MetalRunState.Idle || _state == MetalRunState.Paused) && !_stepBusy,
                    CanSwitchRoll = _state == MetalRunState.Paused && !_stepBusy,
                };
            }
        }

        /// <summary>锁内调用：失败回滚到 Idle。宿主回滚自身不再抛异常。</summary>
        private void RollbackLocked()
        {
            try { _host.RollbackStart(); }
            catch { }
            _state = MetalRunState.Idle;
        }

        private void RaiseStateChanged()
        {
            Action handler = StateChanged;
            if (handler == null) return;
            try { handler(); }
            catch { }
        }
    }
}
