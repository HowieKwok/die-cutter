using Frame_Core;
using Frame_Core.流程汇总文件.产品类;
using HalconDotNet;
using LogTool;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切的极片尺寸工位：3 路尺寸相机各判自己的尺寸项，再按「同 SN 等齐」合成工位结果。
    ///
    /// 数据流：
    ///   每路尺寸流程产出一张图 → 先判这一路的单相机尺寸项（CameraId1 == CameraId2，不等齐）
    ///   → 登记到该 SN 的组里 → 最后到齐的那一路用三路结果算跨相机尺寸项
    ///   → 统计良率 → 三路各自显示。
    ///
    /// 与激光模切同一套语义：单路项在各自相机里判、跨相机项由最后到齐的一路判，
    /// 一颗 AutoResetEvent 通知"到齐"、超时按 NG 处理、同一个 SN 只算一次。
    /// 差别只在相机下标（2/3/4 而不是 3/4/5）——合并公式按相机下标查数据源，不再写死 3/4/5。
    /// </summary>
    public partial class Server_五金模切
    {
        /// <summary>尺寸工位占用的相机数。</summary>
        private const int SizeStationCamCount = 3;

        /// <summary>组内默认等待时间(ms)。配方里有 SizeStationTimeout 时以配方为准。</summary>
        private const int SizeDefaultWaitMs = 1500;

        /// <summary>
        /// 未合算的组最长存活时间(ms)。正常一片几百毫秒就凑齐，超过这个时间说明有一路根本没出图
        /// （没有等待者也就不会触发超时），下一帧到来时顺手清掉，避免字典无限增长。
        /// </summary>
        private const int SizeGroupTtlMs = 10000;

        private readonly object SizeSyncLock = new object();
        private readonly Dictionary<string, SizeStationGroup> _sizePendingGroups = new Dictionary<string, SizeStationGroup>();
        private readonly AutoResetEvent _sizeAllReadyEvent = new AutoResetEvent(false);

        /// <summary>
        /// 已判超时/异常的 SN。等齐失败后迟到的那几路不能再建组，
        /// 否则会留下一个永远没人消费的孤儿组（SN 每片唯一，字典只增不减），
        /// 仿真里还会让 <see cref="IsSizeStationIdle"/> 永远为 false、从此不再喂图。
        ///
        /// 只保留一小段时间：同一 SN 在 TTL 内再次出现就是迟到的帧，TTL 之后按新片处理。
        /// </summary>
        private readonly Dictionary<string, DateTime> _abandonedSizeSn = new Dictionary<string, DateTime>();

        /// <summary>废弃标记的存活时间，覆盖最慢一路的迟到窗口。</summary>
        private const int SizeAbandonTtlMs = 5000;

        /// <summary>等齐失败在统计表里的归类项。配方没有这项时 ApplyCounts 会追加到表尾，占比看得见。</summary>
        private const string SizeAbnormalName = "等齐超时/组异常";

        /// <summary>曲线未建页签的尺寸项名，每项只报一次，避免每片刷一条错误日志。</summary>
        private readonly HashSet<string> ChartMissReported = new HashSet<string>();

        /// <summary>
        /// 一个 SN 的三路产品。Products 的键是物理相机下标，所以机型换下标不用改这里。
        /// </summary>
        private sealed class SizeStationGroup
        {
            /// <summary>建组时刻，用来清理"有一路根本没出图"的残留组。</summary>
            public readonly DateTime CreatedAt = DateTime.Now;

            /// <summary>本组帧号。三路必须同号，否则这三份数据不是同一片的。</summary>
            public int FrameId;

            public readonly Dictionary<int, SizeDectectProduct> Products = new Dictionary<int, SizeDectectProduct>();

            /// <summary>三路里任何一路检出缺陷，这一片就是缺陷 NG（与尺寸超规一起决定工位结论）。</summary>
            public bool IsDefectNG;

            public bool IsComplete
            {
                get { return Products.Count >= SizeStationCamCount; }
            }
        }

        #region 相机回调

        public void SizeCam1Process(ProductBase productBase)
        {
            ProcessSizeStation(CamRole.尺寸检测, 0, productBase);
        }

        public void SizeCam2Process(ProductBase productBase)
        {
            ProcessSizeStation(CamRole.尺寸检测, 1, productBase);
        }

        public void SizeCam3Process(ProductBase productBase)
        {
            ProcessSizeStation(CamRole.尺寸检测, 2, productBase);
        }

        #endregion

        /// <summary>
        /// 一路尺寸相机的处理：登记 → 等三路到齐 → 由最后到齐的一路算工位尺寸 → 显示本路。
        /// 三路各自的显示互不等待，只有"合算"那一步等齐。
        /// </summary>
        private void ProcessSizeStation(CamRole role, int occurrence, ProductBase productBase)
        {
            SizeDectectProduct product = productBase as SizeDectectProduct;
            if (product == null)
            {
                AppLog.Error("尺寸工位收到非尺寸产品，无法结算，请检查流程回调绑定");
                return;
            }

            // 帧号先取出来：早返回与异常路径都要靠它补结算
            int frameId = product.FrameId;
            if (frameId <= 0 && product.showHalconGroup != null && product.showHalconGroup.Count > 0 &&
                product.showHalconGroup[0] != null)
            {
                frameId = (int)product.showHalconGroup[0].m_nImageFrameID;
            }

            if (product.showHalconGroup == null || product.showHalconGroup.Count == 0)
            {
                SettleAndEnqueue(frameId, Frame_Core.业务参数类.五金模切帧序号.FrameStation.尺寸, true,
                    Frame_Core.业务参数类.SettlementSource.ServerJudge, "尺寸产品缺少显示窗口");
                return;
            }

            int camIndex = ResolveCamIndex(role, occurrence);
            if (camIndex < 0)
            {
                SettleAndEnqueue(frameId, Frame_Core.业务参数类.五金模切帧序号.FrameStation.尺寸, true,
                    Frame_Core.业务参数类.SettlementSource.ServerJudge, "尺寸相机下标解析失败");
                return;
            }

            // 第一路是尺寸工位的顺序结算线程：顺手清掉第一路从未到齐的残留组，
            // 否则那些帧的待检永远不会再变成 1/2。巡检失败不能影响本帧判定
            if (occurrence == 0)
            {
                try { SweepStaleSizes(); }
                catch (Exception sweepEx) { AppLog.Error("尺寸过期组清扫异常:" + sweepEx); }
            }

            ShowHalconWindow window = product.showHalconGroup[0];
            try
            {
                HOperatorSet.CountSeconds(out product.detectionTime.Data_StartTime);

                window.m_messageShowInfos.Add(new MessageShowInfo() { m_strMessage = $"帧号：{window.m_nImageFrameID}" });
                product.CamName = Service_Camera.GetCam_Name(camIndex);

                // 单路尺寸先判：只依赖本路结果的那些尺寸项（配方里 CameraId1 == CameraId2），
                // 不等齐、不参与合成，与激光模切的 SizeCam4/5/6_Process 一一对应。
                bool singleCamOk = JudgeSingleCamSize(camIndex, product, window);

                // 缺陷判定：结论要随入组一起被"最后到齐"的那一路看到
                DefectFrameResult defect = JudgeSizeDefects(camIndex, product, window);
                // 片级统计按这一项归类（三路里优先级最高的那个缺陷）
                product.productDataResult.highestPriorityNgDefect = defect.HighestPriority;

                // 尺寸超规也要让这一路画面标 NG：截图与 NG 存图按 m_bIsNG 判断，
                // 只在缺陷判定里置位会让超规片的 NG 图不出。
                window.m_bIsNG = !(singleCamOk && !defect.IsNG) || product.ForceNg;

                // 缺陷明细落盘（与外观工位同一套写法，只是表头第二列是相机名）
                FileOperationCSV.WriteData_CSV(Data.Station3_Path_Defect, Data.Station3_DefectTitle.ToArray(),
                    product.productDataResult.DefectData, product.CamName);

                foreach (List<RowData> row in product.productDataResult.DefectData)
                {
                    Data.Station3_DefectShowQueue.Enqueue(row, 200);
                }

                // 帧号是本片的唯一身份：三路同号才进同一组
                if (frameId <= 0) frameId = (int)window.m_nImageFrameID;
                AddToSizeGroup(camIndex, product, defect.IsNG, frameId);

                // 只有第一路尺寸相机负责等齐与判工位。
                // 三路都等的时候，同一个"到齐"信号只能唤醒一个等待者，另外两路必然超时；
                // 激光模切也是 SizeCam4 单路 WaitAndTake、另两路只 Submit。
                if (occurrence != 0) return;

                SizeStationGroup group;
                ProcessExecutionResult result = WaitForSizeGroup(frameId, ReadSizeWaitMs(), out group);

                if (result == ProcessExecutionResult.Success)
                {
                    bool sizeOk = JudgeStationSize(group, window);
                    // 三路里任何一路在流程层失败都会置 ForceNg：数据不完整不能算合格
                    bool ok = sizeOk && !group.IsDefectNG && !GroupForceNg(group);
                    window.m_bIsNG = !ok;

                    // 三路到齐即本片成立：结论入队，一个 SN 只入一次。
                    // 与外观不同，尺寸三路共用 Size 一个点位，所以必须等到齐才入队。
                    // 结算被拒说明这一帧已由别的路径结案，统计与日志也不能再记一次
                    if (EnqueueSizeResult(frameId, !ok))
                    {
                        WriteSizeSendLog(frameId, !ok);

                        DieCutJudge.DataStatistical(Data.Station3_StatisticalData, (long)window.m_nProductID, !ok);
                        CountSizeDistribution(group);
                        Data.SizeStation_BindingDatasSave.Enqueue(
                            Model_五金模切.Snapshot(Data.SizeDefectCounts, Data.Station3_StatisticalData), 1);
                    }
                }
                else
                {
                    window.m_bIsNG = true;
                    window.m_messageShowInfos.Add(new MessageShowInfo()
                    {
                        m_strMessage = result == ProcessExecutionResult.Overtime ? "尺寸工位等齐超时" : "尺寸工位组异常",
                        m_stuGetColor = HalconColor.red,
                    });
                    AppLog.Error($"五金模切尺寸工位{(result == ProcessExecutionResult.Overtime ? "超时" : "异常")}，相机：{product.CamName}");

                    // 这一片已按 NG 结案：清掉残留组并打废弃标记，
                    // 迟到的那几路图不再入组，否则会在字典里留下一个永远没人消费的孤儿组
                    AbandonSizeGroup(frameId);

                    // 等不齐也算一片 NG，立即入队，机构不会一直等。
                    // 已经由别的路径结案时不再重复记账
                    if (EnqueueSizeResult(frameId, true))
                    {
                        WriteSizeSendLog(frameId, true);

                        // 界面这一片不能丢：良率、分布、过程数据三处都要留痕，
                        // 否则 PLC 已经判了这一片 NG，界面良率却比实际高。
                        // SumupCount 的口径是 productID，超时那路的 ID 可能比已记账的小，
                        // 直接写会让总数回退，所以取一次单调递增值。
                        long timeoutProductId = Math.Max((long)window.m_nProductID,
                            (long)Data.Station3_StatisticalData.SumupCount + 1);
                        DieCutJudge.DataStatistical(Data.Station3_StatisticalData, timeoutProductId, true);
                        Data.SizeDefectCounts.AddOrUpdate(SizeAbnormalName, 1, (k, v) => v + 1);
                        Data.SizeStation_BindingDatasSave.Enqueue(
                            Model_五金模切.Snapshot(Data.SizeDefectCounts, Data.Station3_StatisticalData), 1);
                        EnqueueInvalidSizeRow(window);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("五金模切尺寸工位异常:" + ex);
                if (window != null) window.m_bIsNG = true;

                // 只有第一路是顺序结算线程，可以直接写结论；
                // 另外两路不在这里入队——第一路会因组里缺一路在等齐超时后统一补 NG，
                // 由它写保证同一帧只有一条、且不会越过更早的帧
                if (occurrence == 0)
                {
                    SettleAndEnqueue(frameId, Frame_Core.业务参数类.五金模切帧序号.FrameStation.尺寸, true,
                        Frame_Core.业务参数类.SettlementSource.ServerJudge,
                        "尺寸工位异常：" + ex.Message);
                }
                else
                {
                    AppLog.Error(
                        $"尺寸工位第 {occurrence + 1} 路异常，交由第一路等齐超时统一结算，帧号 {frameId}");
                }
            }
            finally
            {
                // 每路都显示自己的图，不依赖其它两路是否到齐；
                // 放在 finally 里，非等待路 return 之后也照样出图。
                HOperatorSet.CountSeconds(out product.detectionTime.Data_EndTime);
                WriteDetectionTime(camIndex, 0, window, product.detectionTime);
                DeliverImage(camIndex, window, "原图", Data.Cams[camIndex].MainShow);
            }
        }

        /// <summary>
        /// 等不齐也要在"极片尺寸结果"面板占一行：列数与配方一致，值全写 -999，
        /// 这样切到该页看得出来"这一片没检成"，而不是表格里凭空少一行。
        /// </summary>
        private void EnqueueInvalidSizeRow(ShowHalconWindow window)
        {
            List<string> titles = Data.Station3_Title_Size;
            List<RowData> row = new List<RowData>
            {
                new RowData { dataStr = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff"), resultType = ResultType.NG },
                new RowData { dataStr = (window?.m_nImageFrameID ?? 0).ToString(), resultType = ResultType.NG },
            };

            for (int i = 2; i < titles.Count; i++)
            {
                row.Add(new RowData { dataStr = "-999", resultType = ResultType.NG });
            }

            Data.Station3_SizeShowQueue.Enqueue(row, 200);
        }

        #region 等齐

        /// <summary>组内是否有任一路在流程层失败（ForceNg）。数据不完整不能算合格。</summary>
        private static bool GroupForceNg(SizeStationGroup group)
        {
            if (group == null) return false;

            foreach (SizeDectectProduct product in group.Products.Values)
            {
                if (product != null && product.ForceNg) return true;
            }
            return false;
        }

        /// <summary>
        /// 第一路入口的超时清扫：第一路从未到齐的组没有等待者，不会自己超时，
        /// 不清掉这些帧的待检就永远停在 3。
        /// 只由第一路（顺序结算线程）调用，补 NG 不会越过更早的帧。
        /// </summary>
        private void SweepStaleSizes()
        {
            List<int> stale = null;
            DateTime now = DateTime.Now;

            lock (SizeSyncLock)
            {
                foreach (KeyValuePair<string, SizeStationGroup> item in _sizePendingGroups)
                {
                    if ((now - item.Value.CreatedAt).TotalMilliseconds < SizeGroupTtlMs) continue;
                    if (stale == null) stale = new List<int>();
                    stale.Add(item.Value.FrameId);
                }
            }

            if (stale == null) return;

            foreach (int frameId in stale)
            {
                AbandonSizeGroup(frameId);
                SettleAndEnqueue(frameId, Frame_Core.业务参数类.五金模切帧序号.FrameStation.尺寸, true,
                    Frame_Core.业务参数类.SettlementSource.Route, "尺寸组超时未凑齐");
            }
        }

        private void AddToSizeGroup(int camIndex, SizeDectectProduct product, bool isDefectNG, int frameId)
        {
            // 用帧号作键：三路同号才进同一组。原先用 SN，而 SN 从未赋值、恒为空串，
            // 等于"任意三路各来一帧就凑齐一片"，这正是需求第 5 条要拦住的错配。
            string key = frameId.ToString();

            lock (SizeSyncLock)
            {
                // 有一路长期没出图的组先清掉：没有等待者就没人会超时，不清会一直留在字典里
                RemoveStaleSizeGroups(DateTime.Now);

                // 这一片已经按超时/异常结案，迟到的图不再入组
                if (IsAbandonedSize(key)) return;

                // 已有别的帧号在凑组：说明三路处理的不是同一片。
                // 不收这一帧——收进来会把两片的数据拼成一个"看起来正常"的结论。
                foreach (KeyValuePair<string, SizeStationGroup> item in _sizePendingGroups)
                {
                    if (item.Value.FrameId == frameId) continue;

                    Frame_Core.业务类.五金模切告警.Raise(
                        Frame_Core.业务类.五金模切告警.尺寸帧号错位,
                        "尺寸帧号错位",
                        $"尺寸相机帧号不一致：正在凑组帧 {item.Value.FrameId}，新到帧 {frameId}。" +
                        "该帧不入组，请检查三路触发时序与帧号计数。");
                    return;
                }

                SizeStationGroup group;
                if (!_sizePendingGroups.TryGetValue(key, out group))
                {
                    group = new SizeStationGroup { FrameId = frameId };
                    _sizePendingGroups.Add(key, group);
                }

                // 同一窗口内同一路再次到图：保留先入组的数据，不覆盖。
                // 覆盖会静默换掉等待方正在用的那一份，判定结果对不上原始触发
                if (group.Products.ContainsKey(camIndex))
                {
                    Frame_Core.业务类.五金模切告警.Raise(
                        "尺寸重复帧",
                        "尺寸重复触发",
                        $"尺寸相机下标 {camIndex} 在帧 {frameId} 的组窗口内重复到图，已保留先入组的数据。");
                    return;
                }

                group.Products[camIndex] = product;
                if (isDefectNG) group.IsDefectNG = true;

                // 最后到达的一路负责唤醒等齐的一方
                if (group.IsComplete) _sizeAllReadyEvent.Set();
            }
        }

        /// <summary>
        /// 本片已按超时/组异常结案：移除残留组并打废弃标记，挡住之后迟到的帧。
        /// 由等待方（第一路尺寸流程）在 else 分支调用。
        /// </summary>
        private void AbandonSizeGroup(int frameId)
        {
            string key = frameId.ToString();

            lock (SizeSyncLock)
            {
                _sizePendingGroups.Remove(key);
                _abandonedSizeSn[key] = DateTime.Now;
            }
        }

        /// <summary>复位/换卷/清数据：丢掉未合算的组与废弃标记，新卷从空表开始。</summary>
        private void ClearSizeGroups()
        {
            lock (SizeSyncLock)
            {
                _sizePendingGroups.Clear();
                _abandonedSizeSn.Clear();
            }
        }

        /// <summary>
        /// 清掉长时间没凑齐的组（有一路根本没出图时，没有等待者也就没人会超时）。
        /// 调用方必须持有 <see cref="SizeSyncLock"/>。
        /// </summary>
        private void RemoveStaleSizeGroups(DateTime now)
        {
            if (_sizePendingGroups.Count == 0) return;

            List<string> stale = null;
            foreach (KeyValuePair<string, SizeStationGroup> item in _sizePendingGroups)
            {
                if ((now - item.Value.CreatedAt).TotalMilliseconds < SizeGroupTtlMs) continue;
                if (stale == null) stale = new List<string>();
                stale.Add(item.Key);
            }

            if (stale == null) return;

            foreach (string key in stale)
            {
                _sizePendingGroups.Remove(key);
                // 打废弃标记：迟到的那几路不用再凑一个没人消费的组
                _abandonedSizeSn[key] = now;
            }
        }

        /// <summary>调用方必须持有 <see cref="SizeSyncLock"/>。顺带清掉过期标记，避免字典只增不减。</summary>
        private bool IsAbandonedSize(string key)
        {
            if (_abandonedSizeSn.Count == 0) return false;

            DateTime now = DateTime.Now;
            List<string> expired = null;
            foreach (KeyValuePair<string, DateTime> item in _abandonedSizeSn)
            {
                if ((now - item.Value).TotalMilliseconds < SizeAbandonTtlMs) continue;
                if (expired == null) expired = new List<string>();
                expired.Add(item.Key);
            }

            if (expired != null)
            {
                foreach (string stale in expired) _abandonedSizeSn.Remove(stale);
            }

            return _abandonedSizeSn.ContainsKey(key);
        }

        /// <summary>
        /// 等同一个 SN 的三路到齐。无论成功、超时还是异常，组都会被移除，保证一个 SN 只合算一次。
        /// 超时/异常时由调用方再补一个废弃标记（<see cref="AbandonSizeGroup"/>），
        /// 挡住之后迟到的帧，否则它们会重新建出一个没人消费的组。
        ///
        /// 用"检查是否到齐 → 等信号 → 再看一遍"的循环，而不是单次 WaitOne：
        /// 到齐信号是共享的，可能被上一片残留的信号提前唤醒，单次等待会拿不完整的三路去合算。
        /// </summary>
        private ProcessExecutionResult WaitForSizeGroup(int frameId, int timeoutMs, out SizeStationGroup group)
        {
            string key = frameId.ToString();
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                lock (SizeSyncLock)
                {
                    if (!_sizePendingGroups.TryGetValue(key, out group)) return ProcessExecutionResult.Exception;

                    if (group.IsComplete)
                    {
                        _sizePendingGroups.Remove(key);
                        return ProcessExecutionResult.Success;
                    }
                }

                int remaining = timeoutMs - (int)watch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    lock (SizeSyncLock) _sizePendingGroups.Remove(key);
                    return ProcessExecutionResult.Overtime;
                }

                _sizeAllReadyEvent.WaitOne(remaining);
            }
        }

        /// <summary>
        /// 尺寸工位是否空闲（没有还没合算的片）。
        /// 仿真是开环喂图，连续跑时必须等上一片合算完，否则新的三路会覆盖组里的旧内容。
        /// </summary>
        private bool IsSizeStationIdle
        {
            get
            {
                lock (SizeSyncLock) return _sizePendingGroups.Count == 0;
            }
        }

        private int ReadSizeWaitMs()
        {
            GlobalVariableItem item = Data.Craft?.Get_GlobalVariableItem("SizeStationTimeout");
            if (item == null) return SizeDefaultWaitMs;

            int timeout = item.Value.ToInt();
            return timeout > 0 ? timeout : SizeDefaultWaitMs;
        }

        #endregion

        #region 工位判定

        /// <summary>
        /// 用三路结果算工位尺寸。每一项的 DataSource1/2 通过 CameraId1/2 落到具体某一路，
        /// 所以三路各测一段、再合成一个整片尺寸，是配方的事，不是代码的事。
        /// </summary>
        private bool JudgeStationSize(SizeStationGroup group, ShowHalconWindow window)
        {
            CraftParam_五金模切 craft = Data.Craft;
            if (craft == null)
            {
                Frame_Core.业务类.五金模切告警.Raise(
                    "尺寸工艺缺失_工位",
                    "尺寸配方缺失",
                    "尺寸工位未加载工艺配方，本片按 NG 结算。");
                return false;
            }

            // 必须取全部尺寸项：配方里既有"跨相机合成"项（CameraId1 != CameraId2），
            // 也有"单路测量"项（CameraId1 == CameraId2）。只取前者会让后者永远不参与出表。
            List<SizeCraftStation_DieCuttingCut> items = craft.Get_Station3_SizeCrafts_Runtime();
            if (items == null || items.Count == 0)
            {
                // 工位级没有任何尺寸项：没有规则就证明不了合格，不能直接判 OK
                Frame_Core.业务类.五金模切告警.Raise(
                    "尺寸工艺缺失_工位",
                    "尺寸工艺项为空",
                    "尺寸工位没有配置任何尺寸项，本片按 NG 结算。");
                return false;
            }

            bool isNG = false;

            List<RowData> sizeRow = new List<RowData>
            {
                new RowData { dataStr = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff"), resultType = ResultType.OK },
                new RowData { dataStr = (window?.m_nImageFrameID ?? 0).ToString(), resultType = ResultType.OK },
            };

            foreach (SizeCraftStation_DieCuttingCut item in items)
            {
                // 跨相机项在这里合成；单相机项的结果已经在 判单路尺寸 里算过，按名取回即可，
                // 不能重算——重算会多记一次统计计数，也会把同一项判两遍。
                SizeInfo sizeInfo = item.CameraId1 == item.CameraId2
                    ? GetSingleCamSizeResult(group, item)
                    : JudgeCrossCamSize(item, group, window);

                isNG |= sizeInfo.IsNG;

                sizeRow.Add(new RowData { dataStr = sizeInfo.DataResult.ToString(), resultType = sizeInfo.resultType });
            }

            // 一片一行：表头与行同源，用同一份 items 生成，避免开关切换时列数不一致
            List<string> titles = new List<string> { "时间", "帧号" };
            foreach (SizeCraftStation_DieCuttingCut item in items) titles.Add(item.Name);
            FileOperationCSV.WriteData_CSV(Data.Station3_Path_Size, titles.ToArray(), sizeRow.ToArray(), "极片尺寸工位");
            Data.Station3_SizeShowQueue.Enqueue(sizeRow, 200);

            return !isNG;
        }

        /// <summary>
        /// 记一次尺寸工位的片分布：三路尺寸项各自可能超规、三路缺陷也各自可能 NG，
        /// 但统计口径是"这一片归到哪个项上"——取全组优先级最高的那一个项，记 1 次。
        ///
        /// 与激光模切 Station3_StatisticalData 一致：先在三路里挑最高优先级的缺陷和尺寸项，
        /// 缺陷优先（有缺陷就不再看尺寸），这样"占比"是"这一类占了多少片"。
        /// </summary>
        private void CountSizeDistribution(SizeStationGroup group)
        {
            if (group == null) return;

            List<DefectSummary> defectItems = new List<DefectSummary>();
            List<SizeInfo> sizeItems = new List<SizeInfo>();

            foreach (SizeDectectProduct product in group.Products.Values)
            {
                if (product?.productDataResult == null) continue;

                if (product.productDataResult.highestPriorityNgDefect != null)
                {
                    defectItems.Add(product.productDataResult.highestPriorityNgDefect);
                }
                if (product.productDataResult.highestPriorityNgSize != null)
                {
                    // 只有真正 NG 的项才参与统计；超规格但未判 NG 的项不该占良率
                    if (product.productDataResult.highestPriorityNgSize.IsNG)
                    {
                        sizeItems.Add(product.productDataResult.highestPriorityNgSize);
                    }
                }
            }

            DefectSummary topDefect = defectItems.Count > 0
                ? DieCutJudge.GetHighestPriority(defectItems.ToArray()) : null;
            SizeInfo topSize = sizeItems.Count > 0
                ? DieCutJudge.GetHighestPriority(sizeItems.ToArray()) : null;

            string name = topDefect?.T_Name ?? topSize?.Name;

            if (string.IsNullOrEmpty(name)) return;

            Data.SizeDefectCounts.AddOrUpdate(name, 1, (key, count) => count + 1);
        }

        /// <summary>
        /// 一路相机自己的尺寸项（CameraId1 == CameraId2）。只依赖本路结果，不等齐，
        /// 结果写进该产品的 sizeInfos，供工位出表时按名取回。返回 false 表示这一路超规。
        /// </summary>
        private bool JudgeSingleCamSize(int camIndex, SizeDectectProduct product, ShowHalconWindow window)
        {
            CraftParam_五金模切 craft = Data.Craft;
            if (craft == null)
            {
                Frame_Core.业务类.五金模切告警.Raise(
                    "尺寸工艺缺失_单路",
                    "尺寸配方缺失",
                    "尺寸工位未加载工艺配方，本片按 NG 结算。");
                return false;
            }

            // 单路没有配置尺寸项是合法的（该路只做缺陷检测），不能因此判 NG；
            // 工位级一项都没有时由 JudgeStationSize 兜底判 NG
            List<SizeCraftStation_DieCuttingCut> items = craft.Get_Station3_SizeCrafts_Runtime(camIndex);
            if (items == null || items.Count == 0) return true;

            bool isNG = false;
            foreach (SizeCraftStation_DieCuttingCut item in items)
            {
                SizeInfo sizeInfo = item.SizeCraftStation_DieCuttingCutToSizeInfo();
                try
                {
                    ExecuteResult<double> executeResult =
                        DieCutJudge.SizeCam_SizeComputationalLogic(camIndex, item, product);
                    sizeInfo.DataResult = (executeResult != null && executeResult.IsSuccess) ? executeResult.Value : -999;

                    ApplySizeJudgment(sizeInfo, item, executeResult, window);
                }
                catch (Exception ex)
                {
                    sizeInfo.DataResult = -999;
                    sizeInfo.IsOverspecification = true;
                    sizeInfo.IsNG = true;
                    sizeInfo.resultType = ResultType.NG;
                    AppLog.Error($"{product?.CamName} 尺寸项[{item.Name}]计算异常:{ex}");
                }

                isNG |= sizeInfo.IsNG;
                product.productDataResult.sizeInfos.Add(sizeInfo);
            }

            // 与激光模切一致：这一路"最该关心的超规项"，工位统计按它归类
            product.productDataResult.highestPriorityNgSize =
                DieCutJudge.GetHighestPriority(product.productDataResult.sizeInfos.ToArray());

            return !isNG;
        }

        /// <summary>跨相机项：用组内三路结果合成，判定/曲线/统计都在这里做。</summary>
        private SizeInfo JudgeCrossCamSize(SizeCraftStation_DieCuttingCut item, SizeStationGroup group, ShowHalconWindow window)
        {
            SizeInfo sizeInfo = item.SizeCraftStation_DieCuttingCutToSizeInfo();
            ExecuteResult<double> executeResult =
                DieCutJudge.Station3_SizeComputationalLogic(item, group.Products);

            sizeInfo.DataResult = (executeResult != null && executeResult.IsSuccess) ? executeResult.Value : -999;
            ApplySizeJudgment(sizeInfo, item, executeResult, window);

            return sizeInfo;
        }

        /// <summary>
        /// 单相机项的结果按名从对应相机的产品里取。取不到说明那一路没出结果（超时/异常），
        /// 按无效值 + NG 输出，占住这一列，避免整行左移。
        /// </summary>
        private static SizeInfo GetSingleCamSizeResult(SizeStationGroup group, SizeCraftStation_DieCuttingCut item)
        {
            SizeDectectProduct product;
            if (group.Products.TryGetValue(item.CameraId1, out product) && product?.productDataResult?.sizeInfos != null)
            {
                SizeInfo found = product.productDataResult.sizeInfos.Find(p => p.Name == item.Name);
                if (found != null) return found;
            }

            SizeInfo sizeInfo = item.SizeCraftStation_DieCuttingCutToSizeInfo();
            sizeInfo.DataResult = -999;
            sizeInfo.IsOverspecification = true;
            sizeInfo.IsNG = true;
            sizeInfo.resultType = ResultType.NG;
            return sizeInfo;
        }

        /// <summary>
        /// 尺寸项的公共判定：超规格 → DBEnable 决定是否 NG，然后记统计、写图上消息、更新曲线。
        /// 单路项与跨相机项共用，保证两种项的判定和统计口径完全一样。
        /// </summary>
        private void ApplySizeJudgment(SizeInfo sizeInfo, SizeCraftStation_DieCuttingCut item,
            ExecuteResult<double> executeResult, ShowHalconWindow window)
        {
            sizeInfo.IsOverspecification =
                !(sizeInfo.DataResult <= sizeInfo.upperLimitValue && sizeInfo.DataResult >= sizeInfo.lowerLimitValue);
            sizeInfo.IsNG = sizeInfo.IsOverspecification && item.DBEnable;
            sizeInfo.resultType = sizeInfo.IsNG ? ResultType.NG
                : sizeInfo.IsOverspecification ? ResultType.Warn : ResultType.OK;

            // 逐项连续 NG：按该项工艺里的"连续数量"计数，满阈值立刻置多次NG信号。
            // 单路项每片只判一次（只属于一路相机），跨相机项每片也只判一次（最后到齐的那路），
            // 所以这里累加不会出现同一片同一项被记多次。
            if (RecordSizeConsecutiveNg(sizeInfo.Name, sizeInfo.IsNG, item.ContinuousCount))
            {
                AppLog.Error(
                    $"五金模切尺寸项[{sizeInfo.Name}]连续NG达到阈值{item.ContinuousCount}");
                TriggerSizeNg();
            }

            // 这里不记统计：单路项在每一路都会判一次，跨相机项只在最后到齐的一路判，
            // 逐项累加会让同一片的同一项记 2~3 次。片级统计统一在 计尺寸片分布 里按"一片一项"记。

            if (window != null)
            {
                window.m_messageShowInfos.Add(new MessageShowInfo()
                {
                    m_strMessage = $"{sizeInfo.Name}：{sizeInfo.DataResult:0.000}({sizeInfo.lowerLimitValue}_{sizeInfo.upperLimitValue}) {executeResult?.Message}",
                    m_stuGetColor = sizeInfo.IsNG ? HalconColor.red
                        : sizeInfo.IsOverspecification ? HalconColor.yellow : HalconColor.green,
                });
            }

            ScotPotUIUser chart;
            if (Data.chartUIUser.TryGetValue(sizeInfo.Name, out chart) && chart != null)
            {
                chart.UpdateCurve("当前值", sizeInfo.DataResult);
            }
            else
            {
                // 曲线页签没建（配方为空 / 项名对不上）时数据会被静默丢弃，
                // 界面上只看到一条标准值平线，不报出来根本查不到。
                lock (ChartMissReported)
                {
                    if (ChartMissReported.Add(sizeInfo.Name))
                    {
                        AppLog.Error($"五金模切曲线未建立页签，尺寸项[{sizeInfo.Name}]的曲线数据被丢弃");
                    }
                }
            }
        }

        #endregion
    }
}
