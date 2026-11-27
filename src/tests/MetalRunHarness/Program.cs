using Frame_Core.业务参数类;
using Frame_MetalRun;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MetalRunHarness
{
    /// <summary>
    /// 五金运行控制器的脱离界面测试。
    /// 覆盖：状态迁移、暂停不硬停、结束确认、换卷、单步门禁、PLC 发布顺序、暂停帧门控、尺寸手动帧号。
    /// 不启动 Halcon/相机/主界面。
    /// </summary>
    internal static class Program
    {
        private static int _failed;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("== Frame_MetalRun Harness ==");

            TestStateMachine();
            TestStartRoll();
            TestPauseResume();
            TestEndRoll();
            TestEndRollFailure();
            TestEndRollDegraded();
            TestSwitchRoll();
            TestSwitchRollRejects();
            TestSwitchRollConfirmAndFailures();
            TestSwitchRollDuringStep();
            TestPendingMergeQueue();
            TestStep();
            TestStepDiscardInFlight();
            TestPublisher();
            TestFrameGate();
            TestSizeManualFrameId();
            TestManualStepClampSuppression();

            Console.WriteLine(_failed == 0 ? "ALL PASS" : ("FAILED: " + _failed));
            return _failed == 0 ? 0 : 1;
        }

        private static void Check(bool condition, string name)
        {
            if (condition)
            {
                Console.WriteLine("[PASS] " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name);
            }
        }

        private static void TestStateMachine()
        {
            FakeHost host = new FakeHost();
            MetalRunController run = new MetalRunController(host);

            Check(run.State == MetalRunState.Idle, "初始 Idle");
            MetalRunButtons buttons = run.GetButtons();
            Check(buttons.CanStartRoll && !buttons.CanEndRoll && !buttons.CanTogglePause, "Idle 按钮矩阵");
            Check(!buttons.CanStepPrev && !buttons.CanStepNext, "Idle 上下片禁用");
            Check(buttons.CanEditRollName && !buttons.CanSwitchRoll, "Idle 卷号可编辑、换卷禁用");
        }

        private static void TestStartRoll()
        {
            FakeHost host = new FakeHost();
            MetalRunController run = new MetalRunController(host);

            host.RecipeOk = false;
            string error;
            Check(!run.StartFilmRoll("R1", out error) && run.State == MetalRunState.Idle, "配方校验失败不改变状态");
            Check(!host.Calls.Contains("prepare") && !host.Calls.Contains("start"), "配方校验失败无副作用");

            host.RecipeOk = true;
            host.Calls.Clear();
            Check(run.StartFilmRoll("R1", out error), "开始膜卷成功");
            Check(run.State == MetalRunState.Running, "开始后 Running");
            Check(string.Join(">", host.Calls.ToArray()) == "validateRecipe>validateRoll>prepare>clear>start",
                "开始膜卷调用顺序");
            Check(host.Started, "宿主标记已启动");
        }

        private static void TestPauseResume()
        {
            FakeHost host = new FakeHost();
            MetalRunController run = new MetalRunController(host);
            string error;
            run.StartFilmRoll("R2", out error);

            Check(run.Pause(out error), "暂停成功");
            Check(run.State == MetalRunState.Paused, "暂停后 Paused");
            Check(host.Paused, "宿主已挂起");
            Check(!host.Calls.Contains("stop"), "暂停不调用硬停");

            Check(run.Resume(out error), "继续成功");
            Check(run.State == MetalRunState.Running, "继续后 Running");
            Check(!host.Paused, "宿主已恢复");
        }

        private static void TestEndRoll()
        {
            FakeHost host = new FakeHost();
            MetalRunController run = new MetalRunController(host);
            string error;
            MetalRunPending pending;

            run.StartFilmRoll("R3", out error);
            host.Calls.Clear();
            host.Pending = new MetalRunPending();
            Check(run.EndFilmRoll(false, out pending, out error) == EndRollResult.Completed, "无未处理项直接结束");
            Check(run.State == MetalRunState.Idle, "结束后 Idle");
            Check(host.Calls.Contains("stop"), "结束调用硬停清场");

            run.StartFilmRoll("R4", out error);
            host.Pending = new MetalRunPending { FrameQueueCount = 2, PlcResultCount = 1 };
            host.Calls.Clear();
            Check(run.EndFilmRoll(false, out pending, out error) == EndRollResult.NeedsConfirm, "有未处理项需要确认");
            Check(run.State == MetalRunState.Paused, "取消前保持 Paused");
            Check(!host.Calls.Contains("stop"), "未确认不硬停");
            Check(pending.Total == 3, "未处理项计数");

            Check(run.EndFilmRoll(true, out pending, out error) == EndRollResult.Completed, "确认后结束");
            Check(run.State == MetalRunState.Idle, "确认后 Idle");

            run.StartFilmRoll("R7", out error);
            host.Pending = new MetalRunPending { DroppedFrameCount = 2 };
            Check(run.EndFilmRoll(false, out pending, out error) == EndRollResult.NeedsConfirm, "暂停丢弃数也需确认");
            Check(pending.Total == 2, "丢弃数计入未处理总数");
            Check(run.EndFilmRoll(true, out pending, out error) == EndRollResult.Completed, "丢弃确认后结束");
        }

        private static void TestEndRollFailure()
        {
            FakeHost host = new FakeHost();
            MetalRunController run = new MetalRunController(host);
            string error;
            MetalRunPending pending;

            run.StartFilmRoll("R6", out error);
            host.Pending = new MetalRunPending();
            host.StopOk = false;

            Check(run.EndFilmRoll(true, out pending, out error) == EndRollResult.Failed, "清场失败返回失败");
            Check(run.State == MetalRunState.Paused, "清场失败保持 Paused");

            Check(run.Resume(out error) && run.State == MetalRunState.Running, "清场失败后可继续");

            Check(run.Pause(out error), "再次暂停");
            host.StopOk = true;
            Check(run.EndFilmRoll(true, out pending, out error) == EndRollResult.Completed, "重试结束成功");
            Check(run.State == MetalRunState.Idle, "重试后 Idle");
        }

        private static void TestEndRollDegraded()
        {
            FakeHost host = new FakeHost();
            MetalRunController run = new MetalRunController(host);
            string error;
            MetalRunPending pending;

            run.StartFilmRoll("R8", out error);
            host.Pending = new MetalRunPending();
            host.StopOk = false;
            host.StopDegraded = true;

            Check(run.EndFilmRoll(true, out pending, out error) == EndRollResult.Failed, "复位失败返回失败");
            Check(run.State == MetalRunState.Idle, "复位失败降级 Idle");
            Check(!string.IsNullOrEmpty(error), "复位失败给出原因");
        }

        private static void TestSwitchRoll()
        {
            FakeHost host = new FakeHost();
            MetalRunController run = new MetalRunController(host);
            string error;
            MetalRunPending pending;

            run.StartFilmRoll("S1", out error);
            Check(run.Pause(out error), "换卷前暂停");

            host.Calls.Clear();
            host.Pending = new MetalRunPending();
            Check(run.SwitchFilmRoll("S2", false, out pending, out error) == SwitchRollResult.Completed,
                "暂停态换卷成功");
            Check(run.State == MetalRunState.Paused, "换卷后仍 Paused");
            Check(string.Join(">", host.Calls.ToArray()) == "validateRoll>wait>switch:S2",
                "换卷调用顺序");
            Check(!host.Calls.Contains("start"), "换卷不重启流程");
            Check(!host.Calls.Contains("stop"), "换卷不硬停");

            MetalRunButtons buttons = run.GetButtons();
            Check(buttons.CanSwitchRoll && buttons.CanEditRollName, "暂停态换卷与卷号可编辑");
        }

        private static void TestSwitchRollRejects()
        {
            string error;
            MetalRunPending pending;

            FakeHost idleHost = new FakeHost();
            MetalRunController idleRun = new MetalRunController(idleHost);
            Check(idleRun.SwitchFilmRoll("X1", false, out pending, out error) == SwitchRollResult.Failed
                  && idleRun.State == MetalRunState.Idle, "Idle 下换卷拒绝");
            Check(!string.IsNullOrEmpty(error) && error.Contains("开始膜卷"), "Idle 换卷提示先开始");
            Check(!idleHost.Calls.Contains("switch:X1"), "Idle 拒绝无宿主副作用");

            FakeHost host = new FakeHost();
            MetalRunController run = new MetalRunController(host);
            run.StartFilmRoll("X2", out error);
            host.Calls.Clear();
            Check(run.SwitchFilmRoll("X3", false, out pending, out error) == SwitchRollResult.Failed
                  && run.State == MetalRunState.Running, "Running 下换卷拒绝");
            Check(!string.IsNullOrEmpty(error) && error.Contains("暂停"), "Running 换卷提示先暂停");
            Check(!host.Calls.Contains("switch:X3"), "Running 拒绝无宿主副作用");

            MetalRunButtons runningButtons = run.GetButtons();
            Check(!runningButtons.CanEditRollName && !runningButtons.CanSwitchRoll, "Running 卷号锁定、换卷禁用");
        }

        private static void TestSwitchRollConfirmAndFailures()
        {
            FakeHost host = new FakeHost();
            MetalRunController run = new MetalRunController(host);
            string error;
            MetalRunPending pending;

            run.StartFilmRoll("C1", out error);
            run.Pause(out error);
            host.Pending = new MetalRunPending { FrameQueueCount = 2, MergeQueueCount = 1 };
            host.Calls.Clear();

            Check(run.SwitchFilmRoll("C2", false, out pending, out error) == SwitchRollResult.NeedsConfirm,
                "有未处理项需要确认");
            Check(run.State == MetalRunState.Paused, "确认前保持 Paused");
            Check(pending.Total == 3, "未处理项含合片在途");
            Check(!host.Calls.Contains("switch:C2"), "未确认不执行换卷");

            Check(run.SwitchFilmRoll("C2", true, out pending, out error) == SwitchRollResult.Completed,
                "确认后换卷成功");
            Check(run.State == MetalRunState.Paused, "确认换卷后仍 Paused");

            host.Pending = new MetalRunPending();
            host.SwitchResult = SwitchRollResult.Failed;
            Check(run.SwitchFilmRoll("C3", true, out pending, out error) == SwitchRollResult.Failed,
                "宿主失败原样返回");
            Check(run.State == MetalRunState.Paused, "宿主失败保持 Paused");

            host.SwitchResult = SwitchRollResult.DegradedToIdle;
            Check(run.SwitchFilmRoll("C4", true, out pending, out error) == SwitchRollResult.DegradedToIdle,
                "宿主降级返回 DegradedToIdle");
            Check(run.State == MetalRunState.Idle, "降级后进入 Idle");

            FakeHost failHost = new FakeHost();
            MetalRunController failRun = new MetalRunController(failHost);
            failRun.StartFilmRoll("C5", out error);
            failRun.Pause(out error);
            failHost.Pending = new MetalRunPending();
            failHost.SwitchResult = SwitchRollResult.DegradeFailed;
            failHost.SwitchReason = "降级结束也失败。请重启软件后再生产。";
            Check(failRun.SwitchFilmRoll("C6", true, out pending, out error) == SwitchRollResult.DegradeFailed,
                "降级失败原样返回");
            Check(failRun.State == MetalRunState.Paused, "降级失败保持 Paused");
            Check(error != null && error.Contains("重启"), "降级失败提示重启软件");
        }

        private static void TestSwitchRollDuringStep()
        {
            FakeHost host = new FakeHost { IsSimulation = true, StepBlocks = true };
            MetalRunController run = new MetalRunController(host);
            string error;
            run.StartFilmRoll("D1", out error);
            run.Pause(out error);

            Task<bool> stepTask = Task.Run(() =>
            {
                string stepError;
                return run.Step(true, false, out stepError);
            });
            Check(host.StepStarted.Wait(1000), "单步已进入宿主");

            MetalRunPending pending;
            string switchError;
            Check(run.SwitchFilmRoll("D2", false, out pending, out switchError) == SwitchRollResult.Failed,
                "单步执行中拒绝换卷");
            Check(!string.IsNullOrEmpty(switchError) && switchError.Contains("单步"), "单步中换卷提示单步");

            host.StepRelease.Set();
            Check(stepTask.Wait(3000), "单步完成");
        }

        private static void TestPendingMergeQueue()
        {
            MetalRunPending pending = new MetalRunPending { MergeQueueCount = 3 };
            Check(pending.Total == 3, "合片在途计入未处理总数");
            Check(pending.ToDisplayText().Contains("合片在途"), "确认文案包含合片在途");
        }

        private static void TestStep()
        {
            FakeHost host = new FakeHost { IsSimulation = true };
            MetalRunController run = new MetalRunController(host);
            string error;

            run.StartFilmRoll("R5", out error);
            run.Pause(out error);

            host.Calls.Clear();
            Check(run.Step(true, false, out error), "仿真暂停单步成功");
            Check(host.Calls.Contains("step"), "调用宿主单步");
            Check(run.State == MetalRunState.Paused, "单步后仍 Paused");

            host.IsSimulation = false;
            Check(!run.Step(true, false, out error), "自动模式拒绝单步");
        }

        private static void TestStepDiscardInFlight()
        {
            FakeHost host = new FakeHost { IsSimulation = true, SimInFlight = 5 };
            MetalRunController run = new MetalRunController(host);
            string error;

            run.StartFilmRoll("S9", out error);
            run.Pause(out error);

            Check(run.GetSimInFlightCount() == 5, "读取仿真在途数");
            host.Calls.Clear();
            Check(run.Step(true, true, out error), "确认丢弃后单步成功");
            Check(host.LastStepDiscard, "丢弃标志已传给宿主");

            host.Calls.Clear();
            Check(run.Step(true, false, out error), "普通单步成功");
            Check(!host.LastStepDiscard, "普通单步不丢弃在途");
        }

        private static void TestPublisher()
        {
            PlcResultChannel channel = new PlcResultChannel("harness", 4);
            List<int> writes = new List<int>();
            PlcResultPublisher publisher = new PlcResultPublisher(
                new[] { channel },
                (ch, item) =>
                {
                    lock (writes) { writes.Add((int)item.Value); }
                    return true;
                });

            channel.Enqueue(7, PlcResult.Pending);
            channel.Enqueue(7, PlcResult.Ok);
            publisher.Start();
            Thread.Sleep(300);
            publisher.Dispose();

            Check(channel.Count == 0, "PLC 发布器排空队列");
            Check(writes.Count == 2 && writes[0] == 3 && writes[1] == 1, "Pending→Result 顺序");
        }

        private static void TestFrameGate()
        {
            五金模切帧门控.ResetForRun();
            五金模切帧门控.Accepting = false;

            bool firstDrop;
            Check(!五金模切帧门控.Allow(0, out firstDrop) && !firstDrop, "清场期间拒绝新帧且不告警");
            Check(五金模切帧门控.DropCount == 0, "清场拒绝不计入丢弃数");

            五金模切帧门控.Accepting = true;
            五金模切帧门控.ResetForRun();
            Check(五金模切帧门控.Allow(0, out firstDrop), "运行时允许入队");

            五金模切帧门控.SetPaused(true);
            Check(!五金模切帧门控.Allow(8, out firstDrop) && firstDrop, "暂停积压上限拒绝");
            Check(!五金模切帧门控.Allow(8, out firstDrop) && !firstDrop, "按相机只告警一次");
            Check(五金模切帧门控.DropCount == 2, "暂停丢弃计数");

            五金模切帧门控.SetPaused(false);
            Check(五金模切帧门控.Allow(8, out firstDrop), "继续后恢复入队");
            五金模切帧门控.ResetDropCount();
        }

        private static void TestSizeManualFrameId()
        {
            SizeFrameSequencer sequencer = new SizeFrameSequencer();
            sequencer.PushManualFrameId(42);

            Check(sequencer.Acquire() == 42, "手动帧号第一路");
            Check(sequencer.Acquire() == 42, "同窗口第二路同号");
            Check(sequencer.Acquire() == 42, "同窗口第三路同号");
            Check(sequencer.Acquire() == 43, "下一窗口回到自增");
        }

        private static void TestManualStepClampSuppression()
        {
            LineScanFrameSequencer sequencer = new LineScanFrameSequencer();
            int frameId;
            FrameRejectReason reason;

            sequencer.NextUp();   // 上线=1
            Check(sequencer.TryNextDown(out frameId, out reason) && frameId == 1, "下线正常接受");

            sequencer.SuppressLeadClampOnce();
            Check(!sequencer.TryNextDown(out frameId, out reason) &&
                  reason == FrameRejectReason.单步恢复, "单步后首次钳位不告警");
            Check(!sequencer.TryNextDown(out frameId, out reason) &&
                  reason == FrameRejectReason.下线超前, "后续钳位恢复正常告警");
        }

        /// <summary>假宿主：只记录调用，不碰真实流程/队列/PLC。</summary>
        private sealed class FakeHost : IMetalRunHost
        {
            public bool IsSimulation { get; set; }
            public bool RecipeOk = true;
            public bool RollOk = true;
            public bool PrepareOk = true;
            public bool ClearOk = true;
            public bool StartOk = true;
            public bool PauseOk = true;
            public bool ResumeOk = true;
            public bool WaitIdleOk = true;
            public bool StepOk = true;
            public bool StopOk = true;
            public bool StopDegraded = false;
            public int SimInFlight = 0;
            public bool LastStepDiscard;

            public SwitchRollResult SwitchResult = SwitchRollResult.Completed;
            public string SwitchReason;
            public bool StepBlocks;
            public readonly ManualResetEventSlim StepStarted = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim StepRelease = new ManualResetEventSlim(false);

            public bool Started;
            public bool Paused;
            public MetalRunPending Pending;
            public readonly List<string> Calls = new List<string>();

            public bool ValidateRecipe(out string reason) { Calls.Add("validateRecipe"); reason = null; return RecipeOk; }
            public bool ValidateFilmRoll(string name, out string reason) { Calls.Add("validateRoll"); reason = null; return RollOk; }
            public bool PrepareFilmRoll(string name, out string reason) { Calls.Add("prepare"); reason = null; return PrepareOk; }
            public bool ClearBeforeStart(out string reason) { Calls.Add("clear"); reason = null; return ClearOk; }
            public bool StartProduction(out string reason) { Calls.Add("start"); Started = true; reason = null; return StartOk; }
            public bool PauseProduction(out string reason) { Calls.Add("pause"); Paused = true; reason = null; return PauseOk; }
            public bool ResumeProduction(out string reason) { Calls.Add("resume"); Paused = false; reason = null; return ResumeOk; }
            public bool WaitAllIdle(int timeoutMs) { Calls.Add("wait"); return WaitIdleOk; }
            public MetalRunPending InspectPending() { return Pending ?? new MetalRunPending(); }
            public StopClearResult StopAndClear(out string reason)
            {
                Calls.Add("stop");
                Started = false;
                reason = null;
                if (StopOk) return StopClearResult.Completed;
                return StopDegraded ? StopClearResult.DegradedToIdle : StopClearResult.StopFailed;
            }
            public SwitchRollResult SwitchFilmRoll(string name, out string reason)
            {
                Calls.Add("switch:" + name);
                reason = SwitchReason;
                return SwitchResult;
            }
            public int GetSimInFlightCount() { Calls.Add("inFlight"); return SimInFlight; }

            public bool StepSimulation(bool forward, bool discardInFlight, out string reason)
            {
                Calls.Add("step");
                LastStepDiscard = discardInFlight;
                reason = null;
                if (StepBlocks)
                {
                    StepStarted.Set();
                    StepRelease.Wait(3000);
                }
                return StepOk;
            }
            public void RollbackStart() { Calls.Add("rollback"); Started = false; }
        }
    }
}
