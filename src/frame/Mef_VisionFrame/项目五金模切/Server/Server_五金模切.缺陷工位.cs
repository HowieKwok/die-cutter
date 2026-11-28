using DefectRuleClassifier.Core.Models;
using Frame_Core;
using Frame_Core.流程汇总文件.产品类;
using LogTool;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切的缺陷判定：外观（EA 图）与尺寸工位上的缺陷。
    ///
    /// 判定规则来自共享算法 <see cref="DieCutJudge"/>（激光与五金同一份），
    /// 五金这边只负责"哪一路相机、用哪一组工艺项、结论往哪里写"。
    ///
    /// 结论去处：
    ///   1. 图上的 NG 标记 + 缺陷区域 + 缺陷小图进该路相机的 NG 轮播；
    ///   2. 缺陷明细行进 productDataResult.DefectData，由调用方落 CSV；
    ///   3. 计数进 Model 的分布表，界面线程每 500ms 刷新到右侧统计表；
    ///   4. 外观工位的结果进 A/B 队列参与合片；尺寸工位的结论并入"这一片"。
    /// </summary>
    public partial class Server_五金模切
    {
        /// <summary>一帧图的缺陷结论。</summary>
        private struct DefectFrameResult
        {
            public bool IsNG;
            public bool IsSevere;
            public int NgCount;

            /// <summary>本帧存在待检项且没有 NG：需要人工复检。</summary>
            public bool IsReview;

            /// <summary>
            /// 这一帧里优先级最高的 NG 项。片级判定与统计都靠它，
            /// 与激光模切的 productDataResult.highestPriorityNgDefect 同义。
            /// </summary>
            public DefectSummary HighestPriority;
        }

        private DefectFrameResult JudgeAppearanceDefects(int camIndex, DieCutEAProduct product, ShowHalconWindow window)
        {
            CraftParam_五金模切 craft = Data.Craft;
            if (craft == null)
            {
                // 没有工艺规则就不能证明产品合格：判 NG 并告警，不能静默给 OK
                Frame_Core.业务类.五金模切告警.Raise(
                    "缺陷工艺缺失_外观",
                    "外观配方缺失",
                    "外观工位未加载工艺配方，本片按 NG 结算。");
                return new DefectFrameResult { IsNG = true, IsSevere = true, NgCount = 1 };
            }

            string camName = Service_Camera.GetCam_Name(camIndex);

            if (product?.ngDefectResults == null || product.ngDefectResults.Count == 0)
            {
                // 空帧没有任何待检项，所有类的连续计数都归零
                ClearDefectFrameCounts(camIndex);
                // 没有缺陷也要在明细里留一行“本帧 OK”，否则 CSV 里看不出这一片检过
                return new DefectFrameResult();
            }

            return JudgeItems(product.ngDefectResults, name => craft.Get_Station1_DefectCrafts(name),
                camIndex, camName, window, true, product.productDataResult.DefectData,
                product.productDataResult, true);
        }

        private DefectFrameResult JudgeSizeDefects(int camIndex, SizeDectectProduct product, ShowHalconWindow window)
        {
            CraftParam_五金模切 craft = Data.Craft;
            if (craft == null)
            {
                Frame_Core.业务类.五金模切告警.Raise(
                    "缺陷工艺缺失_尺寸",
                    "尺寸配方缺失",
                    "尺寸工位未加载工艺配方，本片按 NG 结算。");
                return new DefectFrameResult { IsNG = true, IsSevere = true, NgCount = 1 };
            }

            string camName = Service_Camera.GetCam_Name(camIndex);

            if (product?.ngDefectResults == null || product.ngDefectResults.Count == 0)
            {
                // 空帧没有任何待检项，所有类的连续计数都归零
                ClearDefectFrameCounts(camIndex);
                return new DefectFrameResult();
            }

            // 尺寸工位不允许待检：即使出现兜底类名，也按两态处理
            return JudgeItems(product.ngDefectResults, name => craft.Get_Station3_DefectCrafts(name),
                camIndex, camName, window, false, product.productDataResult.DefectData,
                product.productDataResult, false);
        }

        /// <summary>
        /// 逐项套工艺规则并生成明细行。取工艺的方法按工位传入（外观用 Station1 项，尺寸用 Station3 项），
        /// 判定与落盘逻辑本身与工位无关，只有"是否重度"这一列是外观工位独有的。
        /// </summary>
        private DefectFrameResult JudgeItems(IEnumerable<NgDefectResult> defects,
                                    Func<string, DefectCraft_DieCuttingCut> GetCraft,
                                    int camIndex, string camName,
                                    ShowHalconWindow window,
                                    bool isAppearanceStation,
                                    List<List<RowData>> detailRows,
                                    ProductDataResult productResult,
                                    bool allowReview)
        {
            DefectFrameResult result = new DefectFrameResult();
            ImageSaveParamConfig saveParam = Project.Instance.M_NewImageSaveParamer.ImageSaveConfigItems.GetSaveParam(camIndex);
            // 任何一条缺陷判不出来，这一片就不能算合格。
            // 不能只靠把该条 T_IsNG 置位：汇总还要过工艺的"同类累计 N 片"条件，单条未必判 NG
            bool judgeFailed = false;

            // 汇总判定要按类名重新遍历一遍，所以先物化；调用方传进来的本来就是 List，不会产生额外拷贝
            List<NgDefectResult> items = defects as List<NgDefectResult> ?? defects.ToList();
            // 本帧出现过的类名：帧结束后只清“没出现的类”，待检类不在当帧被打断
            HashSet<string> seenNames = new HashSet<string>();

            foreach (NgDefectResult item in items)
            {
                try
                {
                    if (item == null) continue;

                    // 与激光模切一致：判定与落盘前统一取两位小数，
                    // 否则浮点尾数会把刚好在阈值上的样本判反
                    item.Area = Math.Round(item.Area, 2);
                    item.Width = Math.Round(item.Width, 2);
                    item.Height = Math.Round(item.Height, 2);

                    string name = item.ClassName;
                    seenNames.Add(name ?? "");
                    // 待检只看最终类名：亮缺陷/暗缺陷兜底不再看 DB 与尺寸条件
                    bool isReview = allowReview && IsReviewClassName(name);

                    DefectCraft_DieCuttingCut craft = isReview ? null : GetCraft(name);
                    string noCraftMessage = "";

                    bool isOverspec;
                    bool isNg;
                    bool isSevere = false;

                    item.T_IsReview = isReview;

                    if (isReview)
                    {
                        // 兜底类需要人工复检：既不算 NG，也不依赖工艺项是否存在
                        isOverspec = false;
                        isNg = false;
                        isSevere = false;
                        item.T_Path = Path.Combine(Project.Instance.paramPathSet.ProductMojuanPath, camName, "缺陷小图", "待检", name);
                    }
                    else if (craft == null)
                    {
                        // 没有对应工艺项：按 NG 处理（与激光模切一致），并留一条可排查的日志
                        isOverspec = true;
                        isNg = true;
                        isSevere = true;
                        noCraftMessage = "未找到工艺类型";
                        item.T_Path = Path.Combine(Project.Instance.paramPathSet.ProductMojuanPath, camName, "缺陷小图", "无工艺", name);
                        AppLog.Error($"{noCraftMessage}：{name}，相机：{camName}");
                    }
                    else
                    {
                        isSevere = craft.IsSevere;
                        isOverspec = craft.HaveOrNot ? true : DieCutJudge.DefectResultJudgment(item, craft);
                        isNg = isOverspec && craft.DB;
                        item.T_Path = Path.Combine(Project.Instance.paramPathSet.ProductMojuanPath, camName, "缺陷小图", isNg ? "NG" : "OK", name);
                    }

                    item.T_IsSevere = isSevere;
                    item.T_IsOverspecification = isOverspec;
                    item.T_IsNG = isNg;
                    item.T_nImgIndex = window?.m_nImageFrameID ?? 0;
                    item.T_iProductIndex = window?.m_nProductID ?? 0;
                    item.T_FileName = item.T_nImgIndex.ToString().PadLeft(5, '0') + "_" + item.T_iProductIndex + "_"
                                      + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff")
                                      + $"(A[{item.Area}],W[{item.Width}],H[{item.Height}])";
                    item.T_FileFormat = SaveImageTool.GetImageFormat(saveParam.NGImageSaveParamer.ImageFormat);
                    // 待检图始终落盘，不受“只存 NG/只存 OK”的保存模式限制
                    item.T_IsSave = isReview || saveParam.NGImageSaveParamer.IsSave;

                    if (isNg)
                    {
                        result.NgCount++;
                        if (isSevere) result.IsSevere = true;
                    }

                    // 逐项连续 NG：每项按自己工艺里的"连续数量"计数，满阈值立刻置多次NG信号。
                    // 没有工艺项的缺陷不计数——阈值未知，也就无从判断"连续多少次"。
                    // 待检不增不清：既不算连续 NG，也不打断正在累计的连续计数。
                    if (!isReview && craft != null && RecordDefectConsecutiveNg(camIndex, name, isNg, craft.ContinuousCount))
                    {
                        AppLog.Error(
                            $"五金模切缺陷项[{name}]连续NG达到阈值{craft.ContinuousCount}，相机：{camName}");
                        TriggerNgSignal(isAppearanceStation, camIndex);
                    }

                    HalconColor color = isReview ? HalconColor.yellow : DieCutJudge.GetHalconColor(isOverspec, isNg);

                    if (window != null)
                    {
                        window.m_bIsNG |= isNg;
                        window.m_regionShowInfos.Add(new RegionShowInfo() { m_hRegion = item.Region, m_stuGetColor = color });
                        window.m_messageShowInfos.Add(new MessageShowInfo()
                        {
                            m_strMessage = $"{name}：面积（{item.Area}）宽度（{item.Width}）高度（{item.Height}）{noCraftMessage}",
                            m_stuGetColor = color,
                        });
                    }

                    // 缺陷小图落到对应目录（小图所有权随 ShowHalconWindow 转移）。
                    // 待检图直接进保存队列、不进 NG 轮播：轮播格只放 NG/OK 缺陷，待检由人工到“待检”目录查看。
                    ShowHalconWindow ngWindow = DieCutJudge.NgDefectResultToShowHalconWindow(item, camName);
                    if (ngWindow != null)
                    {
                        ngWindow.m_bIsNG = isNg;
                        ngWindow.m_strCamName = camName;
                        ngWindow.m_messageShowInfos.Add(new MessageShowInfo() { m_strMessage = camName });
                        ngWindow.m_messageShowInfos.Add(new MessageShowInfo()
                        {
                            m_strMessage = isReview
                                ? $"{name}（待检）：面积（{item.Area}）宽度（{item.Width}）高度（{item.Height}）"
                                : $"{name}：面积（{item.Area}）宽度（{item.Width}）高度（{item.Height}）{noCraftMessage}",
                            m_stuGetColor = color,
                        });
                        ngWindow.m_imageSavePathInfo.Path = item.T_Path;
                        ngWindow.m_imageSavePathInfo.FileName = item.T_FileName;
                        ngWindow.m_imageSavePathInfo.FileFormat = saveParam.NGImageSaveParamer.ImageFormat;
                        ngWindow.m_imageSavePathInfo.IsShow = saveParam.NGImageSaveParamer.IsShow;
                        // 待检图始终保存，否则人工复检时找不到图
                        ngWindow.m_imageSavePathInfo.IsSave = isReview || saveParam.NGImageSaveParamer.IsSave;

                        if (camIndex >= 0 && camIndex < Data.Cams.Length)
                        {
                            if (isReview)
                                Data.Cams[camIndex].NgSave.DataQueue.Enqueue(ngWindow);
                            else
                                Data.Cams[camIndex].NgShow.DataQueue.Enqueue(ngWindow);
                        }
                    }

                    if (detailRows != null) detailRows.Add(BuildDefectRow(item, isAppearanceStation, camName, color));
                }
                catch (Exception ex)
                {
                    AppLog.Error($"五金模切缺陷判定异常，相机：{camName}\r\n{ex}");

                    // 判定不了的缺陷不能当 OK：标记成 NG 让它进入 JudgeSummary，
                    // 否则这一条被静默跳过，整片可能因为"没有其它缺陷"而回传 OK
                    try
                    {
                        item.T_IsOverspecification = true;
                        item.T_IsNG = true;
                        item.T_IsSevere = true;
                        item.T_IsReview = false;
                        result.NgCount++;
                        result.IsSevere = true;
                        judgeFailed = true;
                    }
                    catch
                    {
                        // 标记失败也不能把异常抛回流程线程，结论仍由汇总层兜底
                    }

                    // 异常时这一条的 Region 不会交给窗口（正常路径由 RegionShowInfo/ngWindow 接管），
                    // 不释放会随帧累积，最终拖垮 Halcon 内存
                    item.Dispose();
                }
            }

            // 汇总结果随产品一起带走：累计 NG 按片计数时要用到全部 NG 类名，
            // 只留 highestPriority 会把同一片上的其它 NG 项漏掉。
            List<DefectSummary> summaries = JudgeSummary(items, GetCraft, ref result);
            if (productResult != null) productResult.defectSummaries = summaries;

            // 汇总层可能因为"同类累计 N 片"的条件把单条异常判回 OK，这里强制兜底
            if (judgeFailed) result.IsNG = true;

            // 片级三态：NG > 待检 > OK
            result.IsReview = !result.IsNG && items.Any(p => p != null && p.T_IsReview);

            // 本帧没出现的类视为 OK，连续计数归零；出现过的类已按 NG/OK/待检分别处理
            ClearUnseenConsecutiveCounts(camIndex, seenNames);
            return result;
        }

        /// <summary>帧结束清理：只清本帧没出现过的检测项，待检项因此不会打断连续 NG。</summary>
        private void ClearUnseenConsecutiveCounts(int camIndex, HashSet<string> seenNames)
        {
            lock (_consecutiveLock)
            {
                Dictionary<string, int> table = GetDefectCountTable(camIndex);
                foreach (string key in new List<string>(table.Keys))
                {
                    if (seenNames == null || !seenNames.Contains(key))
                        table[key] = 0;
                }
            }
        }

        /// <summary>
        /// 待检类名：亮缺陷/暗缺陷是分类规则的兜底类，无法确定具体缺陷类型，交人工复检。
        /// 只按最终类名精确匹配，不看工艺项、DB 与尺寸条件。
        /// </summary>
        private static bool IsReviewClassName(string name)
        {
            string value = (name ?? "").Trim();
            return value == "亮缺陷" || value == "暗缺陷";
        }

        /// <summary>
        /// 汇总判定：把这一帧的缺陷按类名归并，再套工艺里的膜片数条件（Condition1），
        /// 得出"这一帧算不算 NG、算不算重度"，以及优先级最高的一项。
        ///
        /// 这一层不能省：逐项判定只回答"这一处算不算 NG"，而工艺可以要求
        /// "同类缺陷累计 N 片才算 NG"。与激光模切 Station1_DefectJudgment 的口径一致，
        /// 片级统计也只用这里的 HighestPriority 计一次，不逐缺陷累加。
        /// </summary>
        private static List<DefectSummary> JudgeSummary(List<NgDefectResult> items,
                                      Func<string, DefectCraft_DieCuttingCut> GetCraft,
                                      ref DefectFrameResult result)
        {
            List<DefectSummary> summaries = new List<DefectSummary>();
            foreach (var sameGroup in items.Where(p => p != null).GroupBy(p => p.ClassName))
            {
                // 同类里只要有一处 NG，就用面积最大的那处代表这一类的名字
                NgDefectResult representative = sameGroup.Where(p => p.T_IsNG).OrderByDescending(p => p.Area).FirstOrDefault();
                if (representative == null) continue;   // 这一类全是 OK，不参与片结论

                DefectSummary summary = new DefectSummary
                {
                    T_TotalCount = sameGroup.Count(),
                    T_NgCount = sameGroup.Count(p => p.T_IsNG),
                    T_Name = representative.ClassName,
                };

                DefectCraft_DieCuttingCut craft = GetCraft(summary.T_Name);
                if (craft == null)
                {
                    summary.T_DefectLevel = 10000;
                    summary.T_IsNG = true;
                    summary.T_IsSevere = true;
                }
                else
                {
                    summary.T_DefectLevel = craft.DefectLevel;
                    summary.T_IsSevere = craft.IsSevere;
                    summary.T_IsNG = DieCutJudge.TotalDefectResultJudgment(summary, craft);
                }
                summaries.Add(summary);
            }

            if (summaries.Count == 0) return summaries;

            result.IsNG = summaries.Any(p => p.T_IsNG);
            result.IsSevere = summaries.Any(p => p.T_IsNG && p.T_IsSevere);
            result.HighestPriority = DieCutJudge.GetHighestPriority(summaries.ToArray());
            return summaries;
        }

        /// <summary>
        /// 合片后记一次分布：A/B 两面各有一个"最高优先级 NG 项"，取两者里更优先的那个计 1 次。
        ///
        /// 与激光模切 Station1_StatisticalData 的口径一致：一片只记一项，
        /// 所以各缺陷占比之和不会超过 100%，"占比"读起来才是"这一类缺陷占了多少片"。
        /// </summary>
        private void CountDefectDistribution(ProductDataResult aLine, ProductDataResult bLine)
        {
            List<DefectSummary> candidates = new List<DefectSummary>();
            if (aLine?.highestPriorityNgDefect != null)
            {
                candidates.Add(aLine.highestPriorityNgDefect);
            }
            if (bLine?.highestPriorityNgDefect != null)
            {
                candidates.Add(bLine.highestPriorityNgDefect);
            }

            if (candidates.Count == 0) return;

            DefectSummary top = DieCutJudge.GetHighestPriority(candidates.ToArray());

            if (top == null || string.IsNullOrEmpty(top.T_Name)) return;

            Data.DefectCounts.AddOrUpdate(top.T_Name, 1, (k, v) => v + 1);
        }

        /// <summary>
        /// 一行缺陷明细。外观工位多一列"是否重度"，尺寸工位第二列是相机名——
        /// 列顺序必须与 Model 里两张表的表头一致，否则 CSV 会错列。
        /// </summary>
        private static List<RowData> BuildDefectRow(NgDefectResult item, bool isAppearanceStation, string camName, HalconColor color)
        {
            ResultType resultType = color == HalconColor.red ? ResultType.NG
                                : color == HalconColor.yellow ? ResultType.Warn
                                : ResultType.OK;

            List<RowData> row = new List<RowData>
            {
                new RowData() { dataStr = DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_fff"), resultType = ResultType.OK },
            };

            if (isAppearanceStation)
            {
                row.Add(new RowData() { dataStr = item.T_nImgIndex.ToString(), resultType = resultType });
                row.Add(new RowData() { dataStr = item.T_iProductIndex.ToString(), resultType = resultType });
            }
            else
            {
                // 尺寸工位的表头第二列是相机名，顺序不能错，否则 CSV 整行错位
                row.Add(new RowData() { dataStr = camName, resultType = resultType });
                row.Add(new RowData() { dataStr = item.T_nImgIndex.ToString(), resultType = resultType });
            }

            row.Add(new RowData() { dataStr = item.ClassName, resultType = resultType });
            row.Add(new RowData() { dataStr = item.Area.ToString(), resultType = resultType });
            row.Add(new RowData() { dataStr = item.Width.ToString(), resultType = resultType });
            row.Add(new RowData() { dataStr = item.Height.ToString(), resultType = resultType });
            row.Add(new RowData() { dataStr = item.T_IsOverspecification.ToString(), resultType = resultType });
            if (isAppearanceStation)
            {
                row.Add(new RowData() { dataStr = item.T_IsSevere.ToString(), resultType = resultType });
            }
            row.Add(new RowData() { dataStr = item.T_IsNG.ToString(), resultType = resultType });
            row.Add(new RowData() { dataStr = item.T_IsSave.ToString(), resultType = resultType });
            row.Add(new RowData() { dataStr = Path.Combine(item.T_Path, item.T_FileName + item.T_FileFormat), resultType = resultType });

            return row;
        }
    }
}
