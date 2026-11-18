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
        private const int 尺寸工位相机数 = 3;

        /// <summary>组内默认等待时间(ms)。配方里有 SizeStationTimeout 时以配方为准。</summary>
        private const int 尺寸默认等待时间 = 1500;

        private readonly object 尺寸同步锁 = new object();
        private readonly Dictionary<string, 尺寸工位组> 尺寸待判组 = new Dictionary<string, 尺寸工位组>();
        private readonly AutoResetEvent 尺寸到齐信号 = new AutoResetEvent(false);

        /// <summary>
        /// 一个 SN 的三路产品。Products 的键是物理相机下标，所以机型换下标不用改这里。
        /// </summary>
        private sealed class 尺寸工位组
        {
            public readonly Dictionary<int, SizeDectectProduct> Products = new Dictionary<int, SizeDectectProduct>();

            /// <summary>三路里任何一路检出缺陷，这一片就是缺陷 NG（与尺寸超规一起决定工位结论）。</summary>
            public bool IsDefectNG;

            public bool IsComplete
            {
                get { return Products.Count >= 尺寸工位相机数; }
            }
        }

        #region 相机回调

        public void 尺寸相机1_Process(ProductBase productBase)
        {
            尺寸工位_Process(CamRole.尺寸检测, 0, productBase);
        }

        public void 尺寸相机2_Process(ProductBase productBase)
        {
            尺寸工位_Process(CamRole.尺寸检测, 1, productBase);
        }

        public void 尺寸相机3_Process(ProductBase productBase)
        {
            尺寸工位_Process(CamRole.尺寸检测, 2, productBase);
        }

        #endregion

        /// <summary>
        /// 一路尺寸相机的处理：登记 → 等三路到齐 → 由最后到齐的一路算工位尺寸 → 显示本路。
        /// 三路各自的显示互不等待，只有"合算"那一步等齐。
        /// </summary>
        private void 尺寸工位_Process(CamRole role, int occurrence, ProductBase productBase)
        {
            SizeDectectProduct product = productBase as SizeDectectProduct;
            if (product?.showHalconGroup == null || product.showHalconGroup.Count == 0) return;

            int camIndex = ResolveCamIndex(role, occurrence);
            if (camIndex < 0) return;

            ShowHalconWindow window = product.showHalconGroup[0];
            try
            {
                HOperatorSet.CountSeconds(out product.detectionTime.Data_StartTime);

                window.m_messageShowInfos.Add(new MessageShowInfo() { m_strMessage = $"帧号：{window.m_nImageFrameID}" });
                product.CamName = Service_Camera.GetCam_Name(camIndex);

                // 单路尺寸先判：只依赖本路结果的那些尺寸项（配方里 CameraId1 == CameraId2），
                // 不等齐、不参与合成，与激光模切的 SizeCam4/5/6_Process 一一对应。
                bool 单路尺寸OK = 判单路尺寸(camIndex, product, window);

                // 缺陷判定：结论要随入组一起被"最后到齐"的那一路看到
                缺陷帧结论 缺陷 = 尺寸缺陷判定(camIndex, product, window);
                // 片级统计按这一项归类（三路里优先级最高的那个缺陷）
                product.productDataResult.highestPriorityNgDefect = 缺陷.HighestPriority;

                // 尺寸超规也要让这一路画面标 NG：截图与 NG 存图按 m_bIsNG 判断，
                // 只在缺陷判定里置位会让超规片的 NG 图不出。
                window.m_bIsNG = !(单路尺寸OK && !缺陷.IsNG);

                // 缺陷明细落盘（与外观工位同一套写法，只是表头第二列是相机名）
                FileOperationCSV.WriteData_CSV(Data.Station3_Path_Defect, Data.Station3_DefectTitle.ToArray(),
                    product.productDataResult.DefectData, product.CamName);

                foreach (List<RowData> row in product.productDataResult.DefectData)
                {
                    Data.Station3_DefectShowQueue.Enqueue(row, 200);
                }

                尺寸入组(camIndex, product, 缺陷.IsNG);

                // 只有第一路尺寸相机负责等齐与判工位。
                // 三路都等的时候，同一个"到齐"信号只能唤醒一个等待者，另外两路必然超时；
                // 激光模切也是 SizeCam4 单路 WaitAndTake、另两路只 Submit。
                if (occurrence != 0) return;

                尺寸工位组 group;
                ProcessExecutionResult result = 尺寸等待(product.SN, 读取尺寸等待时间(), out group);

                if (result == ProcessExecutionResult.Success)
                {
                    bool 尺寸OK = 判工位尺寸(group, window);
                    bool ok = 尺寸OK && !group.IsDefectNG;
                    window.m_bIsNG = !ok;

                    Server_DieCuttingCut.DataStatistical(Data.Station3_StatisticalData, (long)window.m_nProductID, !ok);
                    计尺寸片分布(group);
                    Data.SizeStation_BindingDatasSave.Enqueue(
                        Model_五金模切.Snapshot(Data.SizeDefectCounts, Data.Station3_StatisticalData), 1);

                    // 只记录结果：由尺寸相机"下一帧开始"回调发给 PLC，保证发出去的是已到位的上一片
                    Data.Station3_LastResult.SetValue(product.SN, !ok,
                        组内帧号(group, 0), 组内帧号(group, 1), 组内帧号(group, 2));

                    // 仿真没有帧开始节拍：判定完成就发这一片，避免同一片被连发多次
                    if (Project.Instance.Is_simulation)
                    {
                        DateTime 发送开始 = DateTime.Now;
                        bool 连续NG = 尺寸工位_发送结果(!ok, $"仿真#{window.m_nProductID}");
                        写尺寸发送日志(window.m_nImageFrameID + 1, 组内帧号(group, 0), !ok, 连续NG, 发送开始);
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
                    Log4netRecord.ErrorLog.Error($"五金模切尺寸工位{(result == ProcessExecutionResult.Overtime ? "超时" : "异常")}，相机：{product.CamName}");

                    // 等不齐也算一片 NG，下一帧开始回调会把 NG 发给 PLC，机构不会一直等
                    Data.Station3_LastResult.SetValue(product.SN, true,
                        (long)window.m_nImageFrameID, (long)window.m_nImageFrameID, (long)window.m_nImageFrameID);

                    if (Project.Instance.Is_simulation)
                    {
                        DateTime 发送开始 = DateTime.Now;
                        bool 连续NG = 尺寸工位_发送结果(true, "仿真-等齐失败");
                        写尺寸发送日志((long)window.m_nImageFrameID + 1, (long)window.m_nImageFrameID, true, 连续NG, 发送开始);
                    }                }
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error("五金模切尺寸工位异常:" + ex);
                window.m_bIsNG = true;
            }
            finally
            {
                // 每路都显示自己的图，不依赖其它两路是否到齐；
                // 放在 finally 里，非等待路 return 之后也照样出图。
                HOperatorSet.CountSeconds(out product.detectionTime.Data_EndTime);
                写检测耗时(camIndex, 0, window, product.detectionTime);
                投递图像(camIndex, window, "原图", Data.Cams[camIndex].MainShow);
            }
        }

        #region 等齐

        private void 尺寸入组(int camIndex, SizeDectectProduct product, bool isDefectNG)
        {
            string key = product.SN ?? string.Empty;

            lock (尺寸同步锁)
            {
                尺寸工位组 group;
                if (!尺寸待判组.TryGetValue(key, out group))
                {
                    group = new 尺寸工位组();
                    尺寸待判组.Add(key, group);
                }

                group.Products[camIndex] = product;
                if (isDefectNG) group.IsDefectNG = true;

                // 最后到达的一路负责唤醒等齐的一方
                if (group.IsComplete) 尺寸到齐信号.Set();
            }
        }

        /// <summary>
        /// 等同一个 SN 的三路到齐。无论成功、超时还是异常，组都会被移除，保证一个 SN 只合算一次。
        ///
        /// 用"检查是否到齐 → 等信号 → 再看一遍"的循环，而不是单次 WaitOne：
        /// 到齐信号是共享的，可能被上一片残留的信号提前唤醒，单次等待会拿不完整的三路去合算。
        /// </summary>
        private ProcessExecutionResult 尺寸等待(string sn, int timeoutMs, out 尺寸工位组 group)
        {
            string key = sn ?? string.Empty;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                lock (尺寸同步锁)
                {
                    if (!尺寸待判组.TryGetValue(key, out group)) return ProcessExecutionResult.Exception;

                    if (group.IsComplete)
                    {
                        尺寸待判组.Remove(key);
                        return ProcessExecutionResult.Success;
                    }
                }

                int 剩余 = timeoutMs - (int)watch.ElapsedMilliseconds;
                if (剩余 <= 0)
                {
                    lock (尺寸同步锁) 尺寸待判组.Remove(key);
                    return ProcessExecutionResult.Overtime;
                }

                尺寸到齐信号.WaitOne(剩余);
            }
        }

        /// <summary>
        /// 尺寸工位是否空闲（没有还没合算的片）。
        /// 仿真是开环喂图，连续跑时必须等上一片合算完，否则新的三路会覆盖组里的旧内容。
        /// </summary>
        private bool 尺寸工位空闲
        {
            get
            {
                lock (尺寸同步锁) return 尺寸待判组.Count == 0;
            }
        }

        /// <summary>取组内第 occurrence 路尺寸相机的图像帧号（按角色解析，不写死 2/3/4）。</summary>
        private static long 组内帧号(尺寸工位组 group, int occurrence)
        {
            int camIndex;
            if (!Project.Instance.baseProDevice.TryGetCamIndex(CamRole.尺寸检测, occurrence, out camIndex)) return 0;

            SizeDectectProduct product;
            if (!group.Products.TryGetValue(camIndex, out product) || product == null) return 0;

            ShowHalconWindow window = product.showHalconGroup != null && product.showHalconGroup.Count > 0
                ? product.showHalconGroup[0] : null;
            return window?.m_nImageFrameID ?? 0;
        }

        private int 读取尺寸等待时间()
        {
            GlobalVariableItem item = Data.Craft?.Get_GlobalVariableItem("SizeStationTimeout");
            if (item == null) return 尺寸默认等待时间;

            int timeout = item.Value.ToInt();
            return timeout > 0 ? timeout : 尺寸默认等待时间;
        }

        #endregion

        #region 工位判定

        /// <summary>
        /// 用三路结果算工位尺寸。每一项的 DataSource1/2 通过 CameraId1/2 落到具体某一路，
        /// 所以三路各测一段、再合成一个整片尺寸，是配方的事，不是代码的事。
        /// </summary>
        private bool 判工位尺寸(尺寸工位组 group, ShowHalconWindow window)
        {
            CraftParam_五金模切 craft = Data.Craft;
            if (craft == null) return false;

            // 必须取全部尺寸项：配方里既有"跨相机合成"项（CameraId1 != CameraId2），
            // 也有"单路测量"项（CameraId1 == CameraId2）。只取前者会让后者永远不参与出表。
            List<SizeCraftStation_DieCuttingCut> items = craft.Get_Station3_SizeCrafts();
            if (items == null || items.Count == 0) return true;

            bool isNG = false;

            List<RowData> 尺寸行 = new List<RowData>
            {
                new RowData { dataStr = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff"), resultType = ResultType.OK },
                new RowData { dataStr = (window?.m_nImageFrameID ?? 0).ToString(), resultType = ResultType.OK },
            };

            foreach (SizeCraftStation_DieCuttingCut item in items)
            {
                // 跨相机项在这里合成；单相机项的结果已经在 判单路尺寸 里算过，按名取回即可，
                // 不能重算——重算会多记一次统计计数，也会把同一项判两遍。
                SizeInfo sizeInfo = item.CameraId1 == item.CameraId2
                    ? 取单路尺寸结果(group, item)
                    : 判跨相机尺寸(item, group, window);

                isNG |= sizeInfo.IsNG;

                尺寸行.Add(new RowData { dataStr = sizeInfo.DataResult.ToString(), resultType = sizeInfo.resultType });
            }

            // 一片一行：列顺序必须与 Station3_Title_Size 一致（时间/帧号/各尺寸项）
            FileOperationCSV.WriteData_CSV(Data.Station3_Path_Size, Data.Station3_Title_Size.ToArray(), 尺寸行.ToArray(), "极片尺寸工位");
            Data.Station3_SizeShowQueue.Enqueue(尺寸行, 200);

            return !isNG;
        }

        /// <summary>
        /// 记一次尺寸工位的片分布：三路尺寸项各自可能超规、三路缺陷也各自可能 NG，
        /// 但统计口径是"这一片归到哪个项上"——取全组优先级最高的那一个项，记 1 次。
        ///
        /// 与激光模切 Station3_StatisticalData 一致：先在三路里挑最高优先级的缺陷和尺寸项，
        /// 缺陷优先（有缺陷就不再看尺寸），这样"占比"是"这一类占了多少片"。
        /// </summary>
        private void 计尺寸片分布(尺寸工位组 group)
        {
            if (group == null) return;

            List<DefectSummary> 缺陷项 = new List<DefectSummary>();
            List<SizeInfo> 尺寸项 = new List<SizeInfo>();

            foreach (SizeDectectProduct product in group.Products.Values)
            {
                if (product?.productDataResult == null) continue;

                if (product.productDataResult.highestPriorityNgDefect != null)
                {
                    缺陷项.Add(product.productDataResult.highestPriorityNgDefect);
                }
                if (product.productDataResult.highestPriorityNgSize != null)
                {
                    // 只有真正 NG 的项才参与统计；超规格但未判 NG 的项不该占良率
                    if (product.productDataResult.highestPriorityNgSize.IsNG)
                    {
                        尺寸项.Add(product.productDataResult.highestPriorityNgSize);
                    }
                }
            }

            DefectSummary 最高缺陷 = 缺陷项.Count > 0
                ? Server_DieCuttingCut.GetHighestPriority(缺陷项.ToArray()) : null;
            SizeInfo 最高尺寸 = 尺寸项.Count > 0
                ? Server_DieCuttingCut.GetHighestPriority(尺寸项.ToArray()) : null;

            string 名称 = 最高缺陷?.T_Name ?? 最高尺寸?.Name;
            if (string.IsNullOrEmpty(名称)) return;

            Data.SizeDefectCounts.AddOrUpdate(名称, 1, (key, count) => count + 1);
        }

        /// <summary>
        /// 一路相机自己的尺寸项（CameraId1 == CameraId2）。只依赖本路结果，不等齐，
        /// 结果写进该产品的 sizeInfos，供工位出表时按名取回。返回 false 表示这一路超规。
        /// </summary>
        private bool 判单路尺寸(int camIndex, SizeDectectProduct product, ShowHalconWindow window)
        {
            CraftParam_五金模切 craft = Data.Craft;
            if (craft == null) return true;

            List<SizeCraftStation_DieCuttingCut> items = craft.Get_Station3_SizeCrafts(camIndex);
            if (items == null || items.Count == 0) return true;

            bool isNG = false;
            foreach (SizeCraftStation_DieCuttingCut item in items)
            {
                SizeInfo sizeInfo = item.SizeCraftStation_DieCuttingCutToSizeInfo();
                try
                {
                    ExecuteResult<double> executeResult =
                        Server_DieCuttingCut.SizeCam_SizeComputationalLogic(camIndex, item, product);
                    sizeInfo.DataResult = (executeResult != null && executeResult.IsSuccess) ? executeResult.Value : -999;

                    应用尺寸判定(sizeInfo, item, executeResult, window);
                }
                catch (Exception ex)
                {
                    sizeInfo.DataResult = -999;
                    sizeInfo.IsOverspecification = true;
                    sizeInfo.IsNG = true;
                    sizeInfo.resultType = ResultType.NG;
                    Log4netRecord.ErrorLog.Error($"{product?.CamName} 尺寸项[{item.Name}]计算异常:{ex}");
                }

                isNG |= sizeInfo.IsNG;
                product.productDataResult.sizeInfos.Add(sizeInfo);
            }

            // 与激光模切一致：这一路"最该关心的超规项"，工位统计按它归类
            product.productDataResult.highestPriorityNgSize =
                Server_DieCuttingCut.GetHighestPriority(product.productDataResult.sizeInfos.ToArray());

            return !isNG;
        }

        /// <summary>跨相机项：用组内三路结果合成，判定/曲线/统计都在这里做。</summary>
        private SizeInfo 判跨相机尺寸(SizeCraftStation_DieCuttingCut item, 尺寸工位组 group, ShowHalconWindow window)
        {
            SizeInfo sizeInfo = item.SizeCraftStation_DieCuttingCutToSizeInfo();
            ExecuteResult<double> executeResult =
                Server_DieCuttingCut.Station3_SizeComputationalLogic(item, group.Products);

            sizeInfo.DataResult = (executeResult != null && executeResult.IsSuccess) ? executeResult.Value : -999;
            应用尺寸判定(sizeInfo, item, executeResult, window);

            return sizeInfo;
        }

        /// <summary>
        /// 单相机项的结果按名从对应相机的产品里取。取不到说明那一路没出结果（超时/异常），
        /// 按无效值 + NG 输出，占住这一列，避免整行左移。
        /// </summary>
        private static SizeInfo 取单路尺寸结果(尺寸工位组 group, SizeCraftStation_DieCuttingCut item)
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
        private void 应用尺寸判定(SizeInfo sizeInfo, SizeCraftStation_DieCuttingCut item,
            ExecuteResult<double> executeResult, ShowHalconWindow window)
        {
            sizeInfo.IsOverspecification =
                !(sizeInfo.DataResult <= sizeInfo.upperLimitValue && sizeInfo.DataResult >= sizeInfo.lowerLimitValue);
            sizeInfo.IsNG = sizeInfo.IsOverspecification && item.DBEnable;
            sizeInfo.resultType = sizeInfo.IsNG ? ResultType.NG
                : sizeInfo.IsOverspecification ? ResultType.Warn : ResultType.OK;

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
        }

        #endregion
    }
}