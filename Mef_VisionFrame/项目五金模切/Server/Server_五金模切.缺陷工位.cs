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
    /// 判定规则直接复用激光的规则引擎（<see cref="Server_DieCuttingCut.DefectResultJudgment"/>），
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
        private struct 缺陷帧结论
        {
            public bool IsNG;
            public bool IsSevere;
            public int NgCount;

            /// <summary>
            /// 这一帧里优先级最高的 NG 项。片级判定与统计都靠它，
            /// 与激光模切的 productDataResult.highestPriorityNgDefect 同义。
            /// </summary>
            public DefectSummary HighestPriority;
        }

        private 缺陷帧结论 外观缺陷判定(int camIndex, DieCutEAProduct product, ShowHalconWindow window)
        {
            CraftParam_五金模切 craft = Data.Craft;
            if (craft == null) return new 缺陷帧结论();

            string camName = Service_Camera.GetCam_Name(camIndex);

            if (product?.ngDefectResults == null || product.ngDefectResults.Count == 0)
            {
                // 没有缺陷也要在明细里留一行“本帧 OK”，否则 CSV 里看不出这一片检过
                return new 缺陷帧结论();
            }

            return 逐项判定(product.ngDefectResults, name => craft.Get_Station1_DefectCrafts(name),
                camIndex, camName, window, true, product.productDataResult.DefectData);
        }

        private 缺陷帧结论 尺寸缺陷判定(int camIndex, SizeDectectProduct product, ShowHalconWindow window)
        {
            CraftParam_五金模切 craft = Data.Craft;
            if (craft == null) return new 缺陷帧结论();

            string camName = Service_Camera.GetCam_Name(camIndex);

            if (product?.ngDefectResults == null || product.ngDefectResults.Count == 0)
            {
                return new 缺陷帧结论();
            }

            return 逐项判定(product.ngDefectResults, name => craft.Get_Station3_DefectCrafts(name),
                camIndex, camName, window, false, product.productDataResult.DefectData);
        }

        /// <summary>
        /// 逐项套工艺规则并生成明细行。取工艺的方法按工位传入（外观用 Station1 项，尺寸用 Station3 项），
        /// 判定与落盘逻辑本身与工位无关，只有"是否重度"这一列是外观工位独有的。
        /// </summary>
        private 缺陷帧结论 逐项判定(IEnumerable<NgDefectResult> defects,
                                    Func<string, DefectCraft_DieCuttingCut> 取工艺,
                                    int camIndex, string camName,
                                    ShowHalconWindow window,
                                    bool 是否外观工位,
                                    List<List<RowData>> 明细行)
        {
            缺陷帧结论 结论 = new 缺陷帧结论();
            ImageSaveParamConfig saveParam = Project.Instance.M_NewImageSaveParamer.ImageSaveConfigItems.GetSaveParam(camIndex);

            // 汇总判定要按类名重新遍历一遍，所以先物化；调用方传进来的本来就是 List，不会产生额外拷贝
            List<NgDefectResult> 项 = defects as List<NgDefectResult> ?? defects.ToList();

            foreach (NgDefectResult item in 项)
            {
                try
                {
                    if (item == null) continue;

                    // 与激光模切一致：判定与落盘前统一取两位小数，
                    // 否则浮点尾数会把刚好在阈值上的样本判反
                    item.Area = Math.Round(item.Area, 2);
                    item.Width = Math.Round(item.Width, 2);
                    item.Height = Math.Round(item.Height, 2);

                    string 名称 = item.ClassName;
                    DefectCraft_DieCuttingCut 工艺 = 取工艺(名称);
                    string 未找到工艺说明 = "";

                    bool 超规;
                    bool 判NG;
                    bool 重度 = false;

                    if (工艺 == null)
                    {
                        // 没有对应工艺项：按 NG 处理（与激光模切一致），并留一条可排查的日志
                        超规 = true;
                        判NG = true;
                        重度 = true;
                        未找到工艺说明 = "未找到工艺类型";
                        item.T_Path = Path.Combine(Project.Instance.paramPathSet.ProductMojuanPath, camName, "缺陷小图", "无工艺", 名称);
                        Log4netRecord.ErrorLog.Error($"{未找到工艺说明}：{名称}，相机：{camName}");
                    }
                    else
                    {
                        重度 = 工艺.IsSevere;
                        超规 = 工艺.HaveOrNot ? true : Server_DieCuttingCut.DefectResultJudgment(item, 工艺);
                        判NG = 超规 && 工艺.DB;
                        item.T_Path = Path.Combine(Project.Instance.paramPathSet.ProductMojuanPath, camName, "缺陷小图", 判NG ? "NG" : "OK", 名称);
                    }

                    item.T_IsSevere = 重度;
                    item.T_IsOverspecification = 超规;
                    item.T_IsNG = 判NG;
                    item.T_nImgIndex = window?.m_nImageFrameID ?? 0;
                    item.T_iProductIndex = window?.m_nProductID ?? 0;
                    item.T_FileName = item.T_nImgIndex.ToString().PadLeft(5, '0') + "_" + item.T_iProductIndex + "_"
                                      + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff")
                                      + $"(A[{item.Area}],W[{item.Width}],H[{item.Height}])";
                    item.T_FileFormat = SaveImageTool.GetImageFormat(saveParam.NGImageSaveParamer.ImageFormat);
                    item.T_IsSave = saveParam.NGImageSaveParamer.IsSave;

                    if (判NG)
                    {
                        结论.NgCount++;
                        if (重度) 结论.IsSevere = true;
                    }

                    HalconColor 颜色 = Server_DieCuttingCut.GetHalconColor(超规, 判NG);

                    if (window != null)
                    {
                        window.m_bIsNG |= 判NG;
                        window.m_regionShowInfos.Add(new RegionShowInfo() { m_hRegion = item.Region, m_stuGetColor = 颜色 });
                        window.m_messageShowInfos.Add(new MessageShowInfo()
                        {
                            m_strMessage = $"{名称}：面积（{item.Area}）宽度（{item.Width}）高度（{item.Height}）{未找到工艺说明}",
                            m_stuGetColor = 颜色,
                        });
                    }

                    // 缺陷小图进该路相机的 NG 轮播（小图所有权随 ShowHalconWindow 转移）
                    ShowHalconWindow ngWindow = Server_DieCuttingCut.NgDefectResultToShowHalconWindow(item, camName);
                    if (ngWindow != null)
                    {
                        ngWindow.m_bIsNG = 判NG;
                        ngWindow.m_strCamName = camName;
                        ngWindow.m_messageShowInfos.Add(new MessageShowInfo() { m_strMessage = camName });
                        ngWindow.m_messageShowInfos.Add(new MessageShowInfo()
                        {
                            m_strMessage = $"{名称}：面积（{item.Area}）宽度（{item.Width}）高度（{item.Height}）{未找到工艺说明}",
                            m_stuGetColor = 颜色,
                        });
                        ngWindow.m_imageSavePathInfo.Path = item.T_Path;
                        ngWindow.m_imageSavePathInfo.FileName = item.T_FileName;
                        ngWindow.m_imageSavePathInfo.FileFormat = saveParam.NGImageSaveParamer.ImageFormat;
                        ngWindow.m_imageSavePathInfo.IsShow = saveParam.NGImageSaveParamer.IsShow;
                        ngWindow.m_imageSavePathInfo.IsSave = saveParam.NGImageSaveParamer.IsSave;

                        if (camIndex >= 0 && camIndex < Data.Cams.Length)
                        {
                            Data.Cams[camIndex].NgShow.DataQueue.Enqueue(ngWindow);
                        }
                    }

                    if (明细行 != null) 明细行.Add(建缺陷明细行(item, 是否外观工位, camName, 颜色));
                }
                catch (Exception ex)
                {
                    Log4netRecord.ErrorLog.Error($"五金模切缺陷判定异常，相机：{camName}\r\n{ex}");
                    // 异常时这一条的 Region 不会交给窗口（正常路径由 RegionShowInfo/ngWindow 接管），
                    // 不释放会随帧累积，最终拖垮 Halcon 内存
                    item.Dispose();
                }
            }

            汇总判定(项, 取工艺, ref 结论);
            return 结论;
        }

        /// <summary>
        /// 汇总判定：把这一帧的缺陷按类名归并，再套工艺里的膜片数条件（Condition1），
        /// 得出"这一帧算不算 NG、算不算重度"，以及优先级最高的一项。
        ///
        /// 这一层不能省：逐项判定只回答"这一处算不算 NG"，而工艺可以要求
        /// "同类缺陷累计 N 片才算 NG"。与激光模切 Station1_DefectJudgment 的口径一致，
        /// 片级统计也只用这里的 HighestPriority 计一次，不逐缺陷累加。
        /// </summary>
        private static void 汇总判定(List<NgDefectResult> 项,
                                      Func<string, DefectCraft_DieCuttingCut> 取工艺,
                                      ref 缺陷帧结论 结论)
        {
            List<DefectSummary> 汇总 = new List<DefectSummary>();
            foreach (var 同类 in 项.Where(p => p != null).GroupBy(p => p.ClassName))
            {
                // 同类里只要有一处 NG，就用面积最大的那处代表这一类的名字
                NgDefectResult 代表 = 同类.Where(p => p.T_IsNG).OrderByDescending(p => p.Area).FirstOrDefault();
                if (代表 == null) continue;   // 这一类全是 OK，不参与片结论

                DefectSummary summary = new DefectSummary
                {
                    T_TotalCount = 同类.Count(),
                    T_NgCount = 同类.Count(p => p.T_IsNG),
                    T_Name = 代表.ClassName,
                };

                DefectCraft_DieCuttingCut 工艺 = 取工艺(summary.T_Name);
                if (工艺 == null)
                {
                    summary.T_DefectLevel = 10000;
                    summary.T_IsNG = true;
                    summary.T_IsSevere = true;
                }
                else
                {
                    summary.T_DefectLevel = 工艺.DefectLevel;
                    summary.T_IsSevere = 工艺.IsSevere;
                    summary.T_IsNG = Server_DieCuttingCut.TotalDefectResultJudgment(summary, 工艺);
                }
                汇总.Add(summary);
            }

            if (汇总.Count == 0) return;

            结论.IsNG = 汇总.Any(p => p.T_IsNG);
            结论.IsSevere = 汇总.Any(p => p.T_IsNG && p.T_IsSevere);
            结论.HighestPriority = Server_DieCuttingCut.GetHighestPriority(汇总.ToArray());
        }

        /// <summary>
        /// 合片后记一次分布：A/B 两面各有一个"最高优先级 NG 项"，取两者里更优先的那个计 1 次。
        ///
        /// 与激光模切 Station1_StatisticalData 的口径一致：一片只记一项，
        /// 所以各缺陷占比之和不会超过 100%，"占比"读起来才是"这一类缺陷占了多少片"。
        /// </summary>
        private void 计缺陷片分布(Station1Product product)
        {
            List<DefectSummary> 候选 = new List<DefectSummary>();
            if (product?.ALine_DieCutEAProduct?.highestPriorityNgDefect != null)
            {
                候选.Add(product.ALine_DieCutEAProduct.highestPriorityNgDefect);
            }
            if (product?.BLine_DieCutEAProduct?.highestPriorityNgDefect != null)
            {
                候选.Add(product.BLine_DieCutEAProduct.highestPriorityNgDefect);
            }

            if (候选.Count == 0) return;

            DefectSummary 最高 = Server_DieCuttingCut.GetHighestPriority(候选.ToArray());
            if (最高 == null || string.IsNullOrEmpty(最高.T_Name)) return;

            Data.DefectCounts.AddOrUpdate(最高.T_Name, 1, (k, v) => v + 1);
        }

        /// <summary>
        /// 一行缺陷明细。外观工位多一列"是否重度"，尺寸工位第二列是相机名——
        /// 列顺序必须与 Model 里两张表的表头一致，否则 CSV 会错列。
        /// </summary>
        private static List<RowData> 建缺陷明细行(NgDefectResult item, bool 是否外观工位, string camName, HalconColor 颜色)
        {
            ResultType 结果类型 = 颜色 == HalconColor.red ? ResultType.NG
                                : 颜色 == HalconColor.yellow ? ResultType.Warn
                                : ResultType.OK;

            List<RowData> 行 = new List<RowData>
            {
                new RowData() { dataStr = DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_fff"), resultType = ResultType.OK },
            };

            if (是否外观工位)
            {
                行.Add(new RowData() { dataStr = item.T_nImgIndex.ToString(), resultType = 结果类型 });
                行.Add(new RowData() { dataStr = item.T_iProductIndex.ToString(), resultType = 结果类型 });
            }
            else
            {
                // 尺寸工位的表头第二列是相机名，顺序不能错，否则 CSV 整行错位
                行.Add(new RowData() { dataStr = camName, resultType = 结果类型 });
                行.Add(new RowData() { dataStr = item.T_nImgIndex.ToString(), resultType = 结果类型 });
            }

            行.Add(new RowData() { dataStr = item.ClassName, resultType = 结果类型 });
            行.Add(new RowData() { dataStr = item.Area.ToString(), resultType = 结果类型 });
            行.Add(new RowData() { dataStr = item.Width.ToString(), resultType = 结果类型 });
            行.Add(new RowData() { dataStr = item.Height.ToString(), resultType = 结果类型 });
            行.Add(new RowData() { dataStr = item.T_IsOverspecification.ToString(), resultType = 结果类型 });
            if (是否外观工位)
            {
                行.Add(new RowData() { dataStr = item.T_IsSevere.ToString(), resultType = 结果类型 });
            }
            行.Add(new RowData() { dataStr = item.T_IsNG.ToString(), resultType = 结果类型 });
            行.Add(new RowData() { dataStr = item.T_IsSave.ToString(), resultType = 结果类型 });
            行.Add(new RowData() { dataStr = Path.Combine(item.T_Path, item.T_FileName + item.T_FileFormat), resultType = 结果类型 });

            return 行;
        }
    }
}