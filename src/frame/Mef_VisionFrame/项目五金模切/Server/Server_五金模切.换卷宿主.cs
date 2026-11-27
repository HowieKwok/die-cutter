using Frame_Core;
using Frame_Core.业务参数类;
using Frame_MetalRun;
using System;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 换卷宿主实现：暂停态下结束当前卷数据代际并切到新卷。
    ///
    /// 与结束膜卷的差别：不停流程线程、不重建 PLC 会话；
    /// 只短暂停发布器，保证清场时写 0 不被在途旧结果覆盖。
    /// 阶段划分与失败语义见 docs/五金模切_换卷设计.md。
    /// </summary>
    public partial class Server_五金模切
    {
        public SwitchRollResult SwitchFilmRoll(string filmRollName, out string reason)
        {
            reason = null;

            // ── 阶段 0：无副作用校验
            if (string.IsNullOrEmpty(filmRollName))
            {
                reason = "膜卷号不能为空。";
                return SwitchRollResult.Failed;
            }
            if (filmRollName == Project.Instance.systemInfo.currentMojuanhao)
            {
                reason = "新卷号不能与当前卷号相同。";
                return SwitchRollResult.Failed;
            }

            string oldName = Project.Instance.systemInfo.currentMojuanhao;

            // ── 阶段 1：旧卷收尾（任何一步失败都可原样恢复）
            五金模切帧门控.Accepting = false;   // 先断新帧：期间拒绝不计数、不告警

            try
            {
                if (!WaitAllIdle(3000))
                {
                    reason = "流程仍在处理当前帧，换卷已中止。";
                    五金模切帧门控.Accepting = true;
                    return SwitchRollResult.Failed;
                }

                // 换卷不停线程：缺一条线程就是脏数据，必须拒绝
                if (!AllThreadsAlive(out reason))
                {
                    五金模切帧门控.Accepting = true;
                    return SwitchRollResult.Failed;
                }

                // 停发布器：保证写 0 是终值；只停线程，不动 PLC 会话
                StopResultPublisher();

                // Is_MojuanStart 仍为 true，统计落旧卷路径
                DrainStatsOfCurrentRoll();

                // ── 阶段 2：换目录（失败回滚）
                if (!PrepareFilmRoll(filmRollName, out reason))
                {
                    string prepareError = reason;
                    return RollbackToOldRoll(oldName, prepareError, out reason);
                }
            }
            catch (Exception ex)
            {
                return RollbackToOldRoll(oldName, "换卷异常：" + ex.Message, out reason);
            }

            // ── 阶段 3：数据层清场（不可逆，失败由宿主降级）
            try
            {
                if (!ClearDataGeneration(out reason))
                {
                    string clearError = reason;
                    return DegradeToIdle(clearError, out reason);
                }
            }
            catch (Exception ex)
            {
                return DegradeToIdle("换卷清场异常：" + ex.Message, out reason);
            }

            // ResetForRun 会清 Paused，必须补回；顺序见设计 6.4
            五金模切帧门控.SetPaused(true);
            五金模切帧门控.Accepting = true;
            Project.Instance.Is_MojuanStart = true;
            StartResultPublisher();
            return SwitchRollResult.Completed;
        }

        /// <summary>阶段 1/2 失败：还原旧卷并恢复发布器；还原失败则降级。</summary>
        private SwitchRollResult RollbackToOldRoll(string oldName, string cause, out string reason)
        {
            string restoreError;
            if (!RestoreFilmRollAfterPrepareFailure(oldName, out restoreError))
            {
                return DegradeToIdle(cause + "；旧卷回滚失败：" + restoreError, out reason);
            }

            Project.Instance.Is_MojuanStart = true;
            五金模切帧门控.Accepting = true;
            StartResultPublisher();   // 旧队列照常继续发
            reason = cause;
            return SwitchRollResult.Failed;
        }

        /// <summary>
        /// PrepareFilmRoll 失败后还原旧卷：卷号、systemInfo、路径指针、PLC 日志路径，并删掉新建空目录。
        /// PrepareFilmRoll 在 NewMujuanhao 之前就改了卷号与路径，只有用旧卷名重跑一次才能还原。
        /// </summary>
        private bool RestoreFilmRollAfterPrepareFailure(string oldName, out string reason)
        {
            reason = null;
            try
            {
                if (!Project.Instance.NewMujuanhao(Project.Instance.systemInfo.RecipeName, oldName,
                                                   Project.Instance.systemInfo.CraftType, out reason))
                {
                    reason = "恢复旧卷路径失败：" + reason;
                    return false;
                }

                Project.Instance.systemInfo.currentMojuanhao = oldName;
                Project.Instance.systemInfo.Save();
                CleanupPreparedFilmRoll();
                return true;
            }
            catch (Exception ex)
            {
                reason = "恢复旧卷状态异常：" + ex.Message;
                return false;
            }
        }

        /// <summary>阶段 3 失败：停流程/发布器并清场进 Idle；降级本身失败则要求重启软件。</summary>
        private SwitchRollResult DegradeToIdle(string cause, out string reason)
        {
            string stopReason;
            if (StopAndClear(out stopReason) == StopClearResult.Completed)
            {
                reason = cause;
                return SwitchRollResult.DegradedToIdle;
            }

            // 状态不确定时先关帧准入，避免新帧落进半清空的队列
            五金模切帧门控.Accepting = false;
            reason = cause + "；降级结束也失败：" +
                     (string.IsNullOrEmpty(stopReason) ? "未知原因" : stopReason) +
                     "。请重启软件后再生产。";
            return SwitchRollResult.DegradeFailed;
        }

        /// <summary>任一流程线程已停止即返回 false。方向与 ClearBeforeStart 的 HasLiveProcesses 相反。</summary>
        private bool AllThreadsAlive(out string reason)
        {
            reason = null;
            foreach (ProcessServices service in Project.Instance.m_ProcessList)
            {
                if (service == null || service.GetThreadStatus()) continue;
                reason = "流程线程已停止：" +
                         (service.process != null && service.process.processInfo != null
                             ? service.process.processInfo.ProcessName
                             : "未知流程") +
                         "，换卷已中止。";
                return false;
            }
            return true;
        }

        /// <summary>排空旧卷统计。Is_MojuanStart 必须仍为 true，路径仍是旧卷。</summary>
        private void DrainStatsOfCurrentRoll()
        {
            // thd_DataSave 每 500ms 也在排同一队列；两边都出队不会重复写。
            // guard 防止磁盘故障时死循环。
            int guard = 0;
            while ((Data.DefectStation_BindingDatasSave.Count > 0 ||
                    Data.SizeStation_BindingDatasSave.Count > 0) && guard++ < 50)
            {
                DataSave();
            }
        }

        /// <summary>
        /// 数据层清场。取自 ClearBeforeStart，去掉 HasLiveProcesses 检查与发布器停/起。
        /// 必须在发布器已停止、流程线程已挂起的状态下调用。
        /// </summary>
        private bool ClearDataGeneration(out string reason)
        {
            reason = null;
            try
            {
                五金模切帧门控.ResetForRun();   // 清 Paused/丢弃计数（Paused=false 是已知副作用）
                Frame_Core.业务类.五金模切告警.Dismiss("暂停积压已满");

                Data.Init();                    // 按新卷重建 CSV 路径、清显示/统计/保存队列
                ClearConsecutiveCounts();
                ClearCumulativeNgCounts();
                ResetAllResults();              // 三路写 0（发布器已停，0 是终值）+ 清多次NG
                ClearSizeGroups();

                Project.Instance.baseProDevice.INIPro();
                if (!Project.Instance.baseProDevice.INIProduct())
                {
                    reason = "复位相机参数/序号失败。";
                    return false;
                }
                // INIProduct 内：Dispose 各相机算子参数 → 帧序号.Reset()（代际 +1、序号归 1、清在途）

                foreach (PlcResultChannel channel in AllResultChannels) channel.Clear();

                Data.DefectStation_BindingDatasSave.Clear();
                Data.SizeStation_BindingDatasSave.Clear();

                foreach (var item in Project.Instance.M_CameraRelevancies)
                {
                    item.Value.Item1.Camera.GrabCount = 0;
                    item.Value.Item2.m_ImageSimManage.InitSimFlag();
                    item.Value.Item2.m_SizePointInspectionManage.sizePointInspectionParam
                        .sizePointInspectionWorkParam.ResetStartDJ();
                }

                if (IsSimulation && Simulation.SimBootstrap.Runtime != null)
                {
                    Simulation.SimBootstrap.Runtime.Orchestrator.ClearProducts();
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = "换卷清场异常：" + ex.Message;
                return false;
            }
        }
    }
}
