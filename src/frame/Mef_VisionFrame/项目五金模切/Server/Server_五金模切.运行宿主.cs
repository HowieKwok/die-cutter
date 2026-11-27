using Frame_Core;
using Frame_Core.业务参数类;
using Frame_MetalRun;
using Frame_Simulation;
using Frame_Simulation.Capture;
using LogTool;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mef_VisionFrame
{
    /// <summary>
    /// MetalRunController 的宿主实现：真正操作流程线程、帧缓存、PLC 与仿真。
    /// 控制器只做状态编排；暂停不调 StopRun、不清队列、不写 0 由这里保证。
    /// </summary>
    public partial class Server_五金模切 : IMetalRunHost
    {
        public bool IsSimulation
        {
            get { return Project.Instance.Is_simulation; }
        }

        #region 校验与膜卷准备

        public bool ValidateRecipe(out string reason)
        {
            reason = null;

            string recipe = Project.Instance.systemInfo == null ? null : Project.Instance.systemInfo.RecipeName;
            if (string.IsNullOrWhiteSpace(recipe))
            {
                reason = "当前未选择配方。";
                return false;
            }

            if (Project.Instance.craftBase == null)
            {
                reason = "配方参数未加载，无法开始生产。";
                return false;
            }

            if (!string.Equals(Project.Instance.craftBase.RecipeName, recipe, StringComparison.Ordinal))
            {
                reason = string.Format("配方参数与系统配方名不一致（参数：{0}，系统：{1}）。",
                    Project.Instance.craftBase.RecipeName, recipe);
                return false;
            }

            string craftFile = Project.Instance.paramPathSet.craftParamPath;
            if (string.IsNullOrEmpty(craftFile) || !File.Exists(craftFile))
            {
                reason = "配方文件不存在：" + craftFile;
                return false;
            }

            string instanceFile = Project.Instance.paramPathSet.InstanceParamPath;
            if (string.IsNullOrEmpty(instanceFile) || !File.Exists(instanceFile))
            {
                reason = "流程配置不存在：" + instanceFile;
                return false;
            }

            return true;
        }

        public bool ValidateFilmRoll(string filmRollName, out string reason)
        {
            reason = null;

            if (string.IsNullOrEmpty(filmRollName) ||
                !Regex.IsMatch(filmRollName, "^[a-zA-Z0-9-]+$"))
            {
                reason = "膜卷号不合法，只能包含字母、数字、-，不能有空格。";
                return false;
            }

            // 不能复用 ProductRootPath：它只在 NewMujuanhao 时更新，
            // 换配方或跨天后仍是上一次的路径，会把同名膜卷误判为可用。
            string path = Project.Instance.paramPathSet.BuildProductRootPath(
                Project.Instance.systemInfo.RecipeName,
                Project.Instance.systemInfo.CraftType,
                Project.Instance.systemInfo.IsHostMachine) + filmRollName;
            if (Directory.Exists(path))
            {
                reason = "膜卷号重复：" + path;
                return false;
            }

            return true;
        }

        /// <summary>本次 PrepareFilmRoll 新建的膜卷目录；回滚时仅在仍为空时删除。</summary>
        private string _preparedFilmRollPath;
        private bool _preparedFilmRollCreated;

        public bool PrepareFilmRoll(string filmRollName, out string reason)
        {
            reason = null;
            _preparedFilmRollPath = null;
            _preparedFilmRollCreated = false;
            try
            {
                // 先落 false：清场期间 DataSave 不能把上一卷数据写进新路径
                Project.Instance.Is_MojuanStart = false;
                Project.Instance.systemInfo.currentMojuanhao = filmRollName;
                Project.Instance.systemInfo.Save();

                // 记录本次新建的目录：开始失败回滚时删掉，否则同名膜卷重试会被判重拒绝
                string root = Project.Instance.paramPathSet.BuildProductRootPath(
                    Project.Instance.systemInfo.RecipeName,
                    Project.Instance.systemInfo.CraftType,
                    Project.Instance.systemInfo.IsHostMachine);
                _preparedFilmRollPath = root + filmRollName + "\\";
                _preparedFilmRollCreated = !Directory.Exists(_preparedFilmRollPath);

                string mujuanError;
                if (!Project.Instance.NewMujuanhao(Project.Instance.systemInfo.RecipeName,
                        filmRollName, Project.Instance.systemInfo.CraftType, out mujuanError))
                {
                    reason = "建立膜卷生产路径失败" +
                             (string.IsNullOrEmpty(mujuanError) ? "。" : "：" + mujuanError);
                    return false;
                }

                UIDelegate.LightParamChange();
                return true;
            }
            catch (Exception ex)
            {
                reason = "建立膜卷异常：" + ex.Message;
                return false;
            }
        }

        /// <summary>是否还有流程线程处于运行状态；用于开始前拦截上一次回滚遗留的线程。</summary>
        private bool HasLiveProcesses(out string detail)
        {
            List<string> names = new List<string>();
            foreach (ProcessServices service in Project.Instance.m_ProcessList)
            {
                if (service == null || !service.GetThreadStatus()) continue;
                names.Add(service.process != null && service.process.processInfo != null
                    ? service.process.processInfo.ProcessName
                    : "未知流程");
            }
            detail = string.Join("、", names.ToArray());
            return names.Count > 0;
        }

        #endregion

        #region 开始 / 暂停 / 继续

        public bool ClearBeforeStart(out string reason)
        {
            reason = null;
            try
            {
                // 上一次回滚若未能停干净，必须先拒绝：新卷与旧线程叠加会双份取图
                string liveDetail;
                if (HasLiveProcesses(out liveDetail))
                {
                    reason = "检测到遗留流程线程仍在运行：" + liveDetail + "，请重启软件后再开始。";
                    return false;
                }

                五金模切帧门控.Accepting = false;
                五金模切帧门控.ResetForRun();
                Frame_Core.业务类.五金模切告警.Dismiss("暂停积压已满");

                // 上一次异常可能留下发布器实例，先幂等停掉，避免第二份发布线程
                StopResultPublisher();

                Data.Init();
                ClearConsecutiveCounts();
                ClearCumulativeNgCounts();
                ResetAllResults();
                ClearSizeGroups();

                Project.Instance.baseProDevice.INIPro();
                if (!Project.Instance.baseProDevice.INIProduct())
                {
                    reason = "复位相机参数/序号失败。";
                    return false;
                }

                foreach (PlcResultChannel channel in AllResultChannels) channel.Clear();

                // Data.Init 已重建路径并清显示/统计；这里兜底清统计快照保存队列
                Data.DefectStation_BindingDatasSave.Clear();
                Data.SizeStation_BindingDatasSave.Clear();

                foreach (var item in Project.Instance.M_CameraRelevancies)
                {
                    item.Value.Item1.Camera.GrabCount = 0;
                    item.Value.Item2.m_ImageSimManage.InitSimFlag();
                    item.Value.Item2.m_SizePointInspectionManage.sizePointInspectionParam
                        .sizePointInspectionWorkParam.ResetStartDJ();
                }

                // INIProduct -> 帧序号.Reset -> OnProductsCleared 已清仿真在途；
                // 这里再显式清一次，覆盖非仿真/异常残留
                if (IsSimulation && Simulation.SimBootstrap.Runtime != null)
                {
                    Simulation.SimBootstrap.Runtime.Orchestrator.ClearProducts();
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "开始前清场异常：" + ex.Message;
                return false;
            }
        }

        public bool StartProduction(out string reason)
        {
            reason = null;
            try
            {
                // 异常残留的发布器先停掉，避免第二份发布线程
                StopResultPublisher();

                if (!StartRun())
                {
                    reason = "启动流程线程失败。";
                    return false;
                }

                // GetStates 只是“任一”；逐条确认 7 条流程都起来了
                foreach (ProcessServices service in Project.Instance.m_ProcessList)
                {
                    if (!service.GetThreadStatus())
                    {
                        reason = "流程线程未能启动：" + service.process.processInfo.ProcessName;
                        return false;
                    }
                }

                五金模切帧门控.ResetForRun();
                五金模切帧门控.Accepting = true;

                // 解冻放到开准入之后：StartRun 已把 Is_Run 打开，过早解冻的触发会被 !Accepting 丢弃
                if (IsSimulation && Simulation.SimBootstrap.Runtime != null)
                {
                    Simulation.SimBootstrap.Runtime.ResumeTriggers();
                }

                Project.Instance.Is_MojuanStart = true;

                InitView?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                reason = "启动生产异常：" + ex.Message;
                return false;
            }
        }

        public bool PauseProduction(out string reason)
        {
            reason = null;
            try
            {
                if (IsSimulation && Simulation.SimBootstrap.Runtime != null)
                {
                    Simulation.SimBootstrap.Runtime.PauseTriggers();
                }

                五金模切帧门控.SetPaused(true);

                foreach (ProcessServices service in Project.Instance.m_ProcessList)
                {
                    if (service != null) service.Suspend();
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "暂停视觉流程异常：" + ex.Message;
                return false;
            }
        }

        public bool ResumeProduction(out string reason)
        {
            reason = null;
            try
            {
                // 清场中途会关闭帧准入；未重新开始膜卷就恢复，会变成“Running 但永不接帧”
                if (!五金模切帧门控.Accepting)
                {
                    reason = "帧准入已关闭，请结束膜卷后重新开始。";
                    return false;
                }

                // 暂停期间若有流程线程退出（异常/部分停止），继续运行只会得到不完整的检测结果
                foreach (ProcessServices service in Project.Instance.m_ProcessList)
                {
                    if (service == null || service.GetThreadStatus()) continue;
                    reason = "流程线程已停止：" +
                             (service.process != null && service.process.processInfo != null
                                 ? service.process.processInfo.ProcessName : "未知流程") +
                             "，请结束膜卷后重新开始。";
                    return false;
                }

                五金模切帧门控.SetPaused(false);
                Frame_Core.业务类.五金模切告警.Dismiss("暂停积压已满");

                foreach (ProcessServices service in Project.Instance.m_ProcessList)
                {
                    if (service != null) service.Resume();
                }

                if (IsSimulation && Simulation.SimBootstrap.Runtime != null)
                {
                    Simulation.SimBootstrap.Runtime.ResumeTriggers();
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "恢复视觉流程异常：" + ex.Message;
                return false;
            }
        }

        public bool WaitAllIdle(int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            foreach (ProcessServices service in Project.Instance.m_ProcessList)
            {
                if (service == null) continue;
                int remaining = timeoutMs - (int)sw.ElapsedMilliseconds;
                if (remaining < 0) remaining = 0;
                if (!service.WaitIdle(remaining)) return false;
            }
            return true;
        }

        #endregion

        #region 结束与回滚

        public MetalRunPending InspectPending()
        {
            MetalRunPending pending = new MetalRunPending();
            List<string> details = new List<string>();
            int frames = 0;

            foreach (KeyValuePair<int, BaseProParam> pair in Project.Instance.baseProDevice.m_DicCamOperaParam)
            {
                BaseProParam param = pair.Value;
                if (param == null) continue;

                int count = param.QueueGrabImage.Count + param.alreadyDealGrabImage.Count;
                if (count > 0)
                {
                    frames += count;
                    details.Add(param.strCamreaName + " " + count);
                }

                DieCutParam cut = param as DieCutParam;
                if (cut != null) pending.EaQueueCount += cut.imageEAInfos.Count;
            }

            pending.FrameQueueCount = frames;
            pending.FrameDetail = string.Join(" / ", details.ToArray());

            lock (SizeSyncLock)
            {
                pending.SizeGroupCount = _sizePendingGroups.Count;
            }

            pending.PlcResultCount = AllResultChannels.Sum(c => c.Count);

            // 合片在途：已判定、等待另一面配对。Data.Init 会丢弃，必须计入确认快照
            pending.MergeQueueCount = Data.ALine_DieCutEAProduct.Count + Data.BLine_DieCutEAProduct.Count;

            if (IsSimulation && Simulation.SimBootstrap.Runtime != null &&
                Simulation.SimBootstrap.Runtime.Orchestrator != null)
            {
                pending.SimInFlightCount = Simulation.SimBootstrap.Runtime.Orchestrator.PendingDeliveryCount;
            }

            pending.DroppedFrameCount = 五金模切帧门控.DropCount;
            return pending;
        }

        public StopClearResult StopAndClear(out string reason)
        {
            reason = null;

            // 阶段一：先确认能停下来。失败就恢复帧准入，保持 Paused，允许继续或重试结束；
            // 只有停止成功才进入不可逆的清场，避免“流程还在跑、相机已被 Dispose”。
            五金模切帧门控.Accepting = false;

            // 双保险：控制器结束前已 WaitAllIdle，这里再等一次，防止直接调用时清场撞上在途帧
            if (!WaitAllIdle(3000))
            {
                reason = "流程仍在处理当前帧，清场已中止。";
                五金模切帧门控.Accepting = true;
                return StopClearResult.StopFailed;
            }

            string stopError;
            if (!StopProductionOnly(out stopError))
            {
                reason = string.IsNullOrEmpty(stopError) ? "停止流程/发布器失败。" : stopError;
                五金模切帧门控.Accepting = true;
                return StopClearResult.StopFailed;
            }

            // 生产已停后的复位；失败也不恢复 Accepting，交由控制器降级 Idle。
            bool resetOk = ResetProductionAfterStop(out string resetError);

            // 阶段二：生产已停止，后续清理尽力完成；单项失败只记日志，不回退状态。
            try
            {
                // 确认弹窗停留期间仍可能按暂停上限入队；清场前留痕，避免“静默丢弃”
                int framesAtClear = 0;
                foreach (KeyValuePair<int, BaseProParam> pair in Project.Instance.baseProDevice.m_DicCamOperaParam)
                {
                    BaseProParam param = pair.Value;
                    if (param != null)
                        framesAtClear += param.QueueGrabImage.Count + param.alreadyDealGrabImage.Count;
                }
                if (framesAtClear > 0)
                {
                    AppLog.Info("结束膜卷清场：帧队列仍有 " + framesAtClear + " 帧未处理，随清场丢弃");
                }

                if (IsSimulation && Simulation.SimBootstrap.Runtime != null)
                {
                    Simulation.SimBootstrap.Runtime.PauseTriggers();
                }

                // 显示/保存队列：结束也清，避免上一卷画面残留；统计表保留
                for (int i = 0; i < Data.Cams.Length; i++)
                {
                    Data.Cams[i].Dispose();
                }

                // Is_MojuanStart 仍为 true 时排空统计 CSV 保存队列
                int guard = 0;
                while ((Data.DefectStation_BindingDatasSave.Count > 0 ||
                        Data.SizeStation_BindingDatasSave.Count > 0) && guard++ < 50)
                {
                    DataSave();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("结束膜卷清理异常（流程已停止）", ex);
            }
            finally
            {
                Project.Instance.Is_MojuanStart = false;
                五金模切帧门控.ResetForRun();
                Frame_Core.业务类.五金模切告警.Dismiss("暂停积压已满");
            }

            if (!resetOk)
            {
                reason = string.IsNullOrEmpty(resetError)
                    ? "生产已停止，但清场复位失败，请重启软件。"
                    : resetError;
                return StopClearResult.DegradedToIdle;
            }

            return StopClearResult.Completed;
        }

        public void RollbackStart()
        {
            try
            {
                五金模切帧门控.Accepting = false;

                // 启动失败时可能已有流程在跑；先挂起再等，WaitIdle 才能等到线程真正停泊
                foreach (ProcessServices service in Project.Instance.m_ProcessList)
                {
                    if (service != null && service.GetThreadStatus()) service.Suspend();
                }
                if (!WaitAllIdle(3000))
                {
                    AppLog.Error("五金开始回滚：等待流程空闲超时");
                }

                if (!StopRun())
                {
                    AppLog.Error("五金开始回滚：停止流程/发布器失败");
                }

                if (IsSimulation && Simulation.SimBootstrap.Runtime != null)
                {
                    Simulation.SimBootstrap.Runtime.PauseTriggers();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("五金开始回滚失败", ex);
            }
            finally
            {
                Project.Instance.Is_MojuanStart = false;
                五金模切帧门控.ResetForRun();
                Frame_Core.业务类.五金模切告警.Dismiss("暂停积压已满");
                CleanupPreparedFilmRoll();
            }
        }

        /// <summary>删除本次 PrepareFilmRoll 新建且仍为空的膜卷目录，避免同名重试被“目录已存在”拒绝。</summary>
        private void CleanupPreparedFilmRoll()
        {
            string path = _preparedFilmRollPath;
            bool created = _preparedFilmRollCreated;
            _preparedFilmRollPath = null;
            _preparedFilmRollCreated = false;

            if (!created || string.IsNullOrEmpty(path)) return;
            try
            {
                if (Directory.Exists(path) &&
                    Directory.GetFileSystemEntries(path).Length == 0)
                {
                    Directory.Delete(path, false);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("回滚清理膜卷目录失败：" + path, ex);
            }
        }

        #endregion

        #region 仿真单步

        public int GetSimInFlightCount()
        {
            if (!IsSimulation) return 0;
            SimulationRuntime runtime = Simulation.SimBootstrap.Runtime;
            return runtime != null && runtime.Orchestrator != null
                ? runtime.Orchestrator.PendingDeliveryCount
                : 0;
        }

        public bool StepSimulation(bool forward, bool discardInFlight, out string reason)
        {
            reason = null;
            try
            {
                SimulationRuntime runtime = Simulation.SimBootstrap.Runtime;
                if (runtime == null || runtime.Orchestrator == null)
                {
                    reason = "仿真运行时未初始化。";
                    return false;
                }

                if (!WaitAllIdle(3000))
                {
                    reason = "流程仍在处理当前帧，请稍后重试。";
                    return false;
                }

                // 在途令牌是“已下线、尚未到尺寸工位”的片：
                // 单步会立即推进下线帧号，旧令牌恢复后才送达，尺寸帧号会回退。
                int inFlight = runtime.Orchestrator.PendingDeliveryCount;
                if (inFlight > 0)
                {
                    if (!discardInFlight)
                    {
                        reason = "仿真仍有 " + inFlight + " 片在途，单步会让尺寸帧号回退；请先继续运行或结束膜卷。";
                        return false;
                    }

                    // 操作员已确认：仿真令牌是人工产生的，丢弃不会造成真实数据损失
                    runtime.Orchestrator.ClearProducts();
                    AppLog.Info("五金单步：已丢弃 " + inFlight + " 片仿真在途令牌。");
                }

                // 上一步的残留组先清掉，否则新帧号会被“尺寸帧号错位”挡住
                ClearSizeGroups();

                bool backlog = CountFrameBacklog() > 0;

                if (!backlog)
                {
                    int upCam, downCam;
                    if (!Project.Instance.baseProDevice.TryGetCamIndex(CamRole.模切主相机, 0, out upCam) ||
                        !Project.Instance.baseProDevice.TryGetCamIndex(CamRole.模切从相机, 0, out downCam))
                    {
                        reason = "缺少上线/下线相机槽位。";
                        return false;
                    }

                    SimCamera up = GetSimCamera(upCam);
                    SimCamera down = GetSimCamera(downCam);
                    if (up == null || down == null)
                    {
                        reason = "仿真相机未创建。";
                        return false;
                    }

                    BaseProParam downParam = null;
                    Project.Instance.baseProDevice.m_DicCamOperaParam.TryGetValue(downCam, out downParam);
                    int downQueueBefore = downParam == null ? 0 : downParam.alreadyDealGrabImage.Count;

                    runtime.Orchestrator.ManualStep = true;
                    try
                    {
                        if (!up.StepPush(forward))
                        {
                            reason = "上线推图失败（相机队列未清空或图片读取失败）。";
                            return false;
                        }
                        if (!down.StepPush(forward))
                        {
                            reason = "下线推图失败（相机队列未清空或图片读取失败）。";
                            return false;
                        }
                    }
                    finally
                    {
                        runtime.Orchestrator.ManualStep = false;
                    }

                    // 不能用 DownFrameId 判断是否接受：下线超前时它会被钳位，
                    // 看起来“变了”但这一帧其实已被丢弃。看下线帧队列是否真的多了一帧。
                    int downAcceptedId = 0;
                    if (downParam != null)
                    {
                        var downFrames = downParam.alreadyDealGrabImage.ToList();
                        if (downFrames.Count > downQueueBefore)
                        {
                            downAcceptedId = (int)downFrames[downFrames.Count - 1].image_ID;
                        }
                    }

                    if (downAcceptedId <= 0)
                    {
                        // 下线没拿到新号：尺寸没有合法新帧号，绝不能复用旧号
                        AppLog.Info("五金单步：下线未接受新帧号，本步跳过尺寸。");
                    }
                    else
                    {
                        五金模切帧序号.Size.PushManualFrameId(downAcceptedId);

                        int sizePushed = 0;
                        for (int occurrence = 0; occurrence < 3; occurrence++)
                        {
                            int camIndex;
                            if (!Project.Instance.baseProDevice.TryGetCamIndex(CamRole.尺寸检测, occurrence, out camIndex))
                                continue;

                            SimCamera camera = GetSimCamera(camIndex);
                            if (camera == null || !camera.StepPush(forward))
                            {
                                五金模切帧序号.Size.PushManualFrameId(0);
                                reason = "尺寸推图失败（相机队列未清空或图片读取失败）。";
                                return false;
                            }
                            sizePushed++;
                        }

                        if (sizePushed != 3)
                        {
                            // 一路都没推时手动帧号不会被 Acquire 消费，必须清掉，否则恢复后误用旧号
                            五金模切帧序号.Size.PushManualFrameId(0);
                            reason = $"尺寸相机只推图 {sizePushed}/3 路，单步中止。";
                            return false;
                        }
                    }
                }

                // 按依赖分组执行：裁图 -> EA -> 尺寸；尺寸三路必须并发
                if (!StepGroup(new ProcessKind[] { ProcessKind.相机裁图 }, 5000, out reason)) return false;
                if (!StepGroup(new ProcessKind[] { ProcessKind.极片EA }, 5000, out reason)) return false;
                if (!StepGroup(new ProcessKind[] { ProcessKind.极片尺寸检测, ProcessKind.极片尺寸带极耳检测 }, 5000, out reason)) return false;

                // 单步后恢复运行会有一次正常的下线钳位；标记为静默，避免弹“线扫帧号错位”
                五金模切帧序号.LineScan.SuppressLeadClampOnce();
                return true;
            }
            catch (Exception ex)
            {
                reason = "仿真单步异常：" + ex.Message;
                return false;
            }
        }

        private int CountFrameBacklog()
        {
            int count = 0;
            foreach (KeyValuePair<int, BaseProParam> pair in Project.Instance.baseProDevice.m_DicCamOperaParam)
            {
                BaseProParam param = pair.Value;
                if (param != null) count += param.QueueGrabImage.Count + param.alreadyDealGrabImage.Count;
            }
            return count;
        }

        private static SimCamera GetSimCamera(int camIndex)
        {
            var devs = Project.Instance.M_CamParam == null ? null : Project.Instance.M_CamParam.l_CameraDevs;
            if (devs == null || camIndex < 0 || camIndex >= devs.Count) return null;
            return devs[camIndex] == null ? null : devs[camIndex].Camera as SimCamera;
        }

        private bool StepGroup(ProcessKind[] kinds, int timeoutMs, out string reason)
        {
            reason = null;

            BaseProDevice device = Project.Instance.baseProDevice;
            if (device == null)
            {
                reason = "五金流程设备未初始化。";
                return false;
            }

            List<ProcessServices> services = new List<ProcessServices>();
            foreach (ProcessServices service in Project.Instance.m_ProcessList)
            {
                if (service == null || service.process == null || service.process.processInfo == null) continue;

                ProcessKind kind;
                if (!device.TryGetProcessKind(service.process.processInfo.ProcessID, out kind)) continue;
                if (!kinds.Contains(kind)) continue;

                services.Add(service);
            }

            if (services.Count == 0)
            {
                reason = "没有找到可单步的流程：" + string.Join("/", kinds.Select(k => k.ToString()).ToArray());
                return false;
            }

            Task<bool>[] tasks = services
                .Select(service => Task.Run(() => service.StepOnce(timeoutMs)))
                .ToArray();

            try
            {
                Task.WaitAll(tasks, timeoutMs + 1000);
            }
            catch (Exception ex)
            {
                reason = "单步执行异常：" + ex.Message;
                return false;
            }

            for (int i = 0; i < tasks.Length; i++)
            {
                if (!tasks[i].IsCompleted)
                {
                    reason = "单步等待超时：" + services[i].process.processInfo.ProcessName;
                    return false;
                }
                if (!tasks[i].Result)
                {
                    reason = "单步执行失败：" + services[i].process.processInfo.ProcessName;
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
