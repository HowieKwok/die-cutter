using DefectRuleClassifier.Core.Models;
using Frame_Core;
using Frame_Core.流程汇总文件.产品类;
using FrameCommunity;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Linq;


namespace Mef_VisionFrame
{
    /// <summary>
    /// 模切类机型共用的判定与测量算法。
    ///
    /// 这些方法只依赖入参：不持有设备状态、不碰队列、不写 PLC、不读 Project。
    /// 因此激光模切与五金模切都调这一份——算法口径只有一处，
    /// 而"哪路相机、用哪组工艺、结论往哪里写"仍留在各自的 Server 里。
    ///
    /// 机型专用的部分不在这里：
    ///   · 相机下标写死的入口（如激光 3/4/5 的旧尺寸入口）留在 Server_DieCuttingCut；
    ///   · 流程编排、统计落盘、PLC 回传留在各自机型的 Server。
    /// </summary>
    public static class DieCutJudge
    {
        public static ExecuteResult<double> SizeCam_SizeComputationalLogic(int camIndex, SizeCraftStation_DieCuttingCut sizeCraft, SizeDectectProduct sizeDectectProduct)
        {
            ExecuteResult<double> executeResult = new ExecuteResult<double>();
            executeResult.IsSuccess = false;
            executeResult.Value = -999;
            return ExecuteMethodClass.ExecuteMethod(() =>
            {
                double calibValueX = Service_Camera.GetCam_CalibrationValue_X((int)camIndex);
                double calibValueY = Service_Camera.GetCam_CalibrationValue_Y((int)camIndex);
                double CalibrateCoeff = sizeCraft.CalibrateCoeff;
                double MeasureOffset = sizeCraft.MeasureOffset;

                string[] DataSource1 = sizeCraft.DataSource1.Split('.');
                string[] DataSource2 = sizeCraft.DataSource2.Split('.');
                CalcFormulaEnum CalcFormula = (CalcFormulaEnum)sizeCraft.CalcFormula;

                string dataSource1_ModelName = DataSource1?.Count() > 0 ? DataSource1[0] : "";
                string dataSource1_ModeOutName = DataSource1?.Count() > 1 ? DataSource1[1] : "";
                string dataSource2_ModelName = DataSource2?.Count() > 0 ? DataSource2[0] : "";
                string dataSource2_ModeOutName = DataSource2?.Count() > 1 ? DataSource2[1] : "";

                if (CalcFormula == CalcFormulaEnum.点线)
                {
                    PointInfo pointInfo = GetPointInfo(sizeDectectProduct.CrossPointInfos, dataSource1_ModelName, dataSource1_ModeOutName);
                    if (pointInfo == null) pointInfo = GetPointInfo(sizeDectectProduct.PointResults, dataSource1_ModelName);
                    if (pointInfo == null) executeResult.Message += "数据源1未找到；";

                    Frame_Core.LineInfo lineInfo = GetLineInfo(sizeDectectProduct.CrossPointInfos, dataSource2_ModelName, dataSource2_ModeOutName);
                    if (lineInfo == null) lineInfo = GetLineInfo(sizeDectectProduct.LineInfosResult, dataSource2_ModelName);
                    if (lineInfo == null) executeResult.Message += "数据源2未找到；";

                    if ((pointInfo?.bIsSuccess ?? false) && (lineInfo?.IsLineSuccessfull ?? false))
                    {
                        if (SizeComputationalFormula.DistancePl(pointInfo, lineInfo, CalibrateCoeff, MeasureOffset, out double val))
                        {
                            executeResult.IsSuccess = true;
                            if (sizeCraft.EnableDataModel) val = TopLevelMode(val, sizeCraft.StandardValue, sizeCraft.CompensateO, sizeCraft.CompensateF);
                            executeResult.Value = val;
                            return executeResult;
                        }
                    }
                    else return executeResult;
                }
                if (CalcFormula == CalcFormulaEnum.点点)
                {
                    PointInfo pointInfo1 = GetPointInfo(sizeDectectProduct.CrossPointInfos, dataSource1_ModelName, dataSource1_ModeOutName);
                    if (pointInfo1 == null) pointInfo1 = GetPointInfo(sizeDectectProduct.PointResults, dataSource1_ModelName);
                    if (pointInfo1 == null) executeResult.Message += "数据源1未找到；";

                    PointInfo pointInfo2 = GetPointInfo(sizeDectectProduct.CrossPointInfos, dataSource2_ModelName, dataSource2_ModeOutName);
                    if (pointInfo2 == null) pointInfo2 = GetPointInfo(sizeDectectProduct.PointResults, dataSource2_ModelName);
                    if (pointInfo2 == null) executeResult.Message += "数据源2未找到；";

                    if ((pointInfo1?.bIsSuccess ?? false) && (pointInfo2?.bIsSuccess ?? false))
                    {
                        if (SizeComputationalFormula.DistancePp(pointInfo1, pointInfo2, CalibrateCoeff, MeasureOffset, out double val))
                        {
                            executeResult.IsSuccess = true;
                            if (sizeCraft.EnableDataModel) val = TopLevelMode(val, sizeCraft.StandardValue, sizeCraft.CompensateO, sizeCraft.CompensateF);
                            executeResult.Value = val;
                            return executeResult;
                        }
                    }
                    else return executeResult;
                }
                else if (CalcFormula == CalcFormulaEnum.线线X || CalcFormula == CalcFormulaEnum.线线Y)
                {
                    SizeDirectionEnum sizeDirectionEnum = CalcFormula == CalcFormulaEnum.线线X ? SizeDirectionEnum.X : SizeDirectionEnum.Y;
                    Frame_Core.LineInfo lineInfo1 = GetLineInfo(sizeDectectProduct.CrossPointInfos, dataSource1_ModelName, dataSource1_ModeOutName);
                    if (lineInfo1 == null) lineInfo1 = GetLineInfo(sizeDectectProduct.LineInfosResult, dataSource1_ModelName);
                    if (lineInfo1 == null) executeResult.Message += "数据源1未找到；";

                    Frame_Core.LineInfo lineInfo2 = GetLineInfo(sizeDectectProduct.CrossPointInfos, dataSource2_ModelName, dataSource2_ModeOutName);
                    if (lineInfo2 == null) lineInfo2 = GetLineInfo(sizeDectectProduct.LineInfosResult, dataSource2_ModelName);
                    if (lineInfo2 == null) executeResult.Message += "数据源2未找到；";

                    if ((lineInfo1?.IsLineSuccessfull ?? false) && (lineInfo2?.IsLineSuccessfull ?? false))
                    {
                        if (SizeComputationalFormula.DistanceLl(lineInfo1, lineInfo2, sizeDirectionEnum, CalibrateCoeff, MeasureOffset, out double val))
                        {
                            executeResult.IsSuccess = true;
                            if (sizeCraft.EnableDataModel) val = TopLevelMode(val, sizeCraft.StandardValue, sizeCraft.CompensateO, sizeCraft.CompensateF);
                            executeResult.Value = val;
                            return executeResult;
                        }
                    }
                    else return executeResult;
                }
                else if (CalcFormula == CalcFormulaEnum.角度)
                {
                    Frame_Core.LineInfo lineInfo1 = GetLineInfo(sizeDectectProduct.CrossPointInfos, dataSource1_ModelName, dataSource1_ModeOutName);
                    if (lineInfo1 == null) lineInfo1 = GetLineInfo(sizeDectectProduct.LineInfosResult, dataSource1_ModelName);
                    if (lineInfo1 == null) executeResult.Message += "数据源1未找到；";

                    Frame_Core.LineInfo lineInfo2 = GetLineInfo(sizeDectectProduct.CrossPointInfos, dataSource2_ModelName, dataSource2_ModeOutName);
                    if (lineInfo2 == null) lineInfo2 = GetLineInfo(sizeDectectProduct.LineInfosResult, dataSource2_ModelName);
                    if (lineInfo2 == null) executeResult.Message += "数据源2未找到；";


                    if ((lineInfo1?.IsLineSuccessfull ?? false) && (lineInfo2?.IsLineSuccessfull ?? false))
                    {
                        if (SizeComputationalFormula.DistanceAngle(lineInfo1, lineInfo2, out double val))
                        {
                            executeResult.IsSuccess = true;
                            if (sizeCraft.EnableDataModel) val = TopLevelMode(val, sizeCraft.StandardValue, sizeCraft.CompensateO, sizeCraft.CompensateF);
                            executeResult.Value = val;
                            return executeResult;
                        }
                    }
                    else return executeResult;
                }
                executeResult.Message = "未检测到算法类型";
                return executeResult;
            });

        }

        /// <summary>
        /// 三路尺寸合并算一项。按 (相机下标 → 该路产品) 查数据源，不再把 3/4/5 写死在公式里，
        /// 这样激光（3/4/5）和五金（2/3/4）能共用同一套合并逻辑。
        /// </summary>
        public static ExecuteResult<double> Station3_SizeComputationalLogic(SizeCraftStation_DieCuttingCut sizeCraftStation,
                                                                            IReadOnlyDictionary<int, SizeDectectProduct> sizeProducts)
        {
            SizeDectectProduct product1 = null;
            SizeDectectProduct product2 = null;
            if (sizeProducts != null)
            {
                sizeProducts.TryGetValue(sizeCraftStation.CameraId1, out product1);
                sizeProducts.TryGetValue(sizeCraftStation.CameraId2, out product2);
            }
            return Station3_SizeComputationalLogic(sizeCraftStation, product1, product2);
        }

        /// <summary>核心：两个数据源已按相机下标解析好，这里只看 p1/p2，与机型无关。</summary>
        public static ExecuteResult<double> Station3_SizeComputationalLogic(SizeCraftStation_DieCuttingCut sizeCraftStation,
                                                                            SizeDectectProduct sizeDectectProduct1, SizeDectectProduct sizeDectectProduct2)
        {
            ExecuteResult<double> executeResult = new ExecuteResult<double>();
            executeResult.IsSuccess = false;
            executeResult.Value = -999;

            if (sizeDectectProduct1 == null || sizeDectectProduct2 == null)
            {
                executeResult.Message = "数据源相机未到齐；";
                return executeResult;
            }

            return ExecuteMethodClass.ExecuteMethod(() =>
             {
                 //double calibValueX = Service_Camera.GetCam_CalibrationValue_X((int)camIndex);
                 //double calibValueY = Service_Camera.GetCam_CalibrationValue_Y((int)camIndex);
                 double CalibrateCoeff = sizeCraftStation.CalibrateCoeff;
                 double MeasureOffset = sizeCraftStation.MeasureOffset;

                 string[] DataSource1 = sizeCraftStation.DataSource1.Split('.');
                 string[] DataSource2 = sizeCraftStation.DataSource2.Split('.');
                 CalcFormulaEnum CalcFormula = (CalcFormulaEnum)sizeCraftStation.CalcFormula;

                 string dataSource1_ModelName = DataSource1?.Count() > 0 ? DataSource1[0] : "";
                 string dataSource1_ModeOutName = DataSource1?.Count() > 1 ? DataSource1[1] : "";
                 string dataSource2_ModelName = DataSource2?.Count() > 0 ? DataSource2[0] : "";
                 string dataSource2_ModeOutName = DataSource2?.Count() > 1 ? DataSource2[1] : "";

                 if (CalcFormula == CalcFormulaEnum.点线)
                 {
                     PointInfo pointInfo = GetPointInfo(sizeDectectProduct1.CrossPointInfos, dataSource1_ModelName, dataSource1_ModeOutName);
                     if (pointInfo == null) pointInfo = GetPointInfo(sizeDectectProduct1.PointResults, dataSource1_ModelName);
                     if (pointInfo == null) executeResult.Message += "数据源1未找到；";

                     Frame_Core.LineInfo lineInfo = GetLineInfo(sizeDectectProduct2.CrossPointInfos, dataSource2_ModelName, dataSource2_ModeOutName);
                     if (lineInfo == null) lineInfo = GetLineInfo(sizeDectectProduct2.LineInfosResult, dataSource2_ModelName);
                     if (lineInfo == null) executeResult.Message += "数据源2未找到；";

                     if ((pointInfo?.bIsSuccess ?? false) && (lineInfo?.IsLineSuccessfull ?? false))
                     {
                         if (SizeComputationalFormula.DistancePl(pointInfo, lineInfo, CalibrateCoeff, MeasureOffset, out double val))
                         {
                             executeResult.IsSuccess = true;
                             if (sizeCraftStation.EnableDataModel) val = TopLevelMode(val, sizeCraftStation.StandardValue, sizeCraftStation.CompensateO, sizeCraftStation.CompensateF);
                             executeResult.Value = val;
                             return executeResult;
                         }
                     }
                     else return executeResult;
                 }
                 if (CalcFormula == CalcFormulaEnum.点点)
                 {
                     PointInfo pointInfo1 = GetPointInfo(sizeDectectProduct1.CrossPointInfos, dataSource1_ModelName, dataSource1_ModeOutName);
                     if (pointInfo1 == null) pointInfo1 = GetPointInfo(sizeDectectProduct1.PointResults, dataSource1_ModelName);
                     if (pointInfo1 == null) executeResult.Message += "数据源1未找到；";

                     PointInfo pointInfo2 = GetPointInfo(sizeDectectProduct1.CrossPointInfos, dataSource2_ModelName, dataSource2_ModeOutName);
                     if (pointInfo2 == null) pointInfo2 = GetPointInfo(sizeDectectProduct1.PointResults, dataSource2_ModelName);
                     if (pointInfo2 == null) executeResult.Message += "数据源2未找到；";

                     if ((pointInfo1?.bIsSuccess ?? false) && (pointInfo2?.bIsSuccess ?? false))
                     {
                         if (SizeComputationalFormula.DistancePp(pointInfo1, pointInfo2, CalibrateCoeff, MeasureOffset, out double val))
                         {
                             executeResult.IsSuccess = true;
                             if (sizeCraftStation.EnableDataModel) val = TopLevelMode(val, sizeCraftStation.StandardValue, sizeCraftStation.CompensateO, sizeCraftStation.CompensateF);
                             executeResult.Value = val;
                             return executeResult;
                         }
                     }
                     else return executeResult;
                 }
                 else if (CalcFormula == CalcFormulaEnum.线线X || CalcFormula == CalcFormulaEnum.线线Y)
                 {
                     SizeDirectionEnum sizeDirectionEnum = CalcFormula == CalcFormulaEnum.线线X ? SizeDirectionEnum.X : SizeDirectionEnum.Y;
                     Frame_Core.LineInfo lineInfo1 = GetLineInfo(sizeDectectProduct1.CrossPointInfos, dataSource1_ModelName, dataSource1_ModeOutName);
                     if (lineInfo1 == null) lineInfo1 = GetLineInfo(sizeDectectProduct1.LineInfosResult, dataSource1_ModelName);
                     if (lineInfo1 == null) executeResult.Message += "数据源1未找到；";

                     Frame_Core.LineInfo lineInfo2 = GetLineInfo(sizeDectectProduct2.CrossPointInfos, dataSource2_ModelName, dataSource2_ModeOutName);
                     if (lineInfo2 == null) lineInfo2 = GetLineInfo(sizeDectectProduct2.LineInfosResult, dataSource2_ModelName);
                     if (lineInfo2 == null) executeResult.Message += "数据源2未找到；";

                     if ((lineInfo1?.IsLineSuccessfull ?? false) && (lineInfo2?.IsLineSuccessfull ?? false))
                     {
                         if (SizeComputationalFormula.DistanceLl(lineInfo1, lineInfo2, sizeDirectionEnum, CalibrateCoeff, MeasureOffset, out double val))
                         {
                             executeResult.IsSuccess = true;
                             if (sizeCraftStation.EnableDataModel) val = TopLevelMode(val, sizeCraftStation.StandardValue, sizeCraftStation.CompensateO, sizeCraftStation.CompensateF);
                             executeResult.Value = val;
                             return executeResult;
                         }
                     }
                     else return executeResult;
                 }
                 else if (CalcFormula == CalcFormulaEnum.角度)
                 {
                     Frame_Core.LineInfo lineInfo1 = GetLineInfo(sizeDectectProduct1.CrossPointInfos, dataSource1_ModelName, dataSource1_ModeOutName);
                     if (lineInfo1 == null) lineInfo1 = GetLineInfo(sizeDectectProduct1.LineInfosResult, dataSource1_ModelName);
                     if (lineInfo1 == null) executeResult.Message += "数据源1未找到；";

                     Frame_Core.LineInfo lineInfo2 = GetLineInfo(sizeDectectProduct2.CrossPointInfos, dataSource2_ModelName, dataSource2_ModeOutName);
                     if (lineInfo2 == null) lineInfo2 = GetLineInfo(sizeDectectProduct2.LineInfosResult, dataSource2_ModelName);
                     if (lineInfo2 == null) executeResult.Message += "数据源2未找到；";


                     if ((lineInfo1?.IsLineSuccessfull ?? false) && (lineInfo2?.IsLineSuccessfull ?? false))
                     {
                         if (SizeComputationalFormula.DistanceAngle(lineInfo1, lineInfo2, out double val))
                         {
                             executeResult.IsSuccess = true;
                             if (sizeCraftStation.EnableDataModel) val = TopLevelMode(val, sizeCraftStation.StandardValue, sizeCraftStation.CompensateO, sizeCraftStation.CompensateF);
                             executeResult.Value = val;
                             return executeResult;
                         }
                     }
                     else return executeResult;
                 }

                 executeResult.Message = "未检测到算法类型";
                 return executeResult;
             });

        }

        public static void DataStatistical(StatisticalData statisticalData, long sumupCount, bool isNG)
        {
            DataStatistical(statisticalData, sumupCount,
                isNG ? Frame_Core.业务参数类.FrameSettlementState.Ng
                     : Frame_Core.业务参数类.FrameSettlementState.Ok);
        }

        /// <summary>
        /// 三态统计：总数包含待检，OK = 总数 - NG - 待检。
        /// 良率分母排除待检，只反映已经判定的部分：OK / (OK + NG)。
        /// </summary>
        public static void DataStatistical(StatisticalData statisticalData, long sumupCount,
                                            Frame_Core.业务参数类.FrameSettlementState verdict)
        {
            if (statisticalData == null) return;

            statisticalData.SumupCount = sumupCount;
            if (verdict == Frame_Core.业务参数类.FrameSettlementState.Ng)
                statisticalData.NGCount++;
            else if (verdict == Frame_Core.业务参数类.FrameSettlementState.Review)
                statisticalData.ReviewCount++;

            statisticalData.OKCount = sumupCount - statisticalData.NGCount - statisticalData.ReviewCount;
            if (statisticalData.OKCount < 0) statisticalData.OKCount = 0;

            double judged = statisticalData.OKCount + statisticalData.NGCount;
            statisticalData.Yield = judged > 0
                ? Math.Round(statisticalData.OKCount / judged * 100, 2)
                : 0;
        }

        public static PointInfo GetPointInfo(List<PointInfo> pointInfos, string modelName)
        {
            if (pointInfos == null || pointInfos.Count == 0 || string.IsNullOrEmpty(modelName))
                return null;

            return pointInfos.FirstOrDefault(item => item.ModuleName == modelName);
        }

        public static PointInfo GetPointInfo(List<OutCrossGroup> outCrossGroups, string modelName, string modelOutName)
        {
            if (outCrossGroups == null || outCrossGroups.Count == 0 || string.IsNullOrEmpty(modelName) || string.IsNullOrEmpty(modelOutName))
                return null;

            OutCrossGroup outCrossGroup = outCrossGroups.FirstOrDefault(item => item.ModuleName == modelName);
            if (outCrossGroup == null) return null;
            if (modelOutName == "交点") return outCrossGroup.point;
            return null;
        }
        public static Frame_Core.LineInfo GetLineInfo(List<OutCrossGroup> outCrossGroups, string modelName, string modelOutName)
        {
            if (outCrossGroups == null || outCrossGroups.Count == 0 || string.IsNullOrEmpty(modelName) || string.IsNullOrEmpty(modelOutName))
                return null;


            OutCrossGroup outCrossGroup = outCrossGroups.FirstOrDefault(item => item.ModuleName == modelName);
            if (outCrossGroup == null) return null;
            if (modelOutName == "抓边1") return outCrossGroup.LineInfo1;
            if (modelOutName == "抓边2") return outCrossGroup.LineInfo2;

            return null;
        }
        public static Frame_Core.LineInfo GetLineInfo(List<Frame_Core.LineInfo> lineInfos, string modelName)
        {
            if (lineInfos == null || lineInfos.Count == 0 || string.IsNullOrEmpty(modelName))
                return null;

            return lineInfos.FirstOrDefault(item => item.strMoudleName == modelName);
        }

        /// <summary>
        /// 返回最高优先级缺陷信息
        /// </summary>
        /// <param name="defectSummaries"></param>
        /// <returns></returns>
        public static DefectSummary GetHighestPriority(params DefectSummary[] defectSummaries)
        {
            return defectSummaries.Where(item => item.T_IsNG == true).OrderBy(item => item.T_DefectLevel).FirstOrDefault();
        }
        /// <summary>
        /// 返回最高优先级尺寸信息
        /// </summary>
        /// <param name="defectSummaries"></param>
        /// <returns></returns>
        public static SizeInfo GetHighestPriority(params SizeInfo[] sizeInfo)
        {
            return sizeInfo.Where(item => item.IsNG).OrderBy(item => item.Level).FirstOrDefault();
        }

        /// <summary>
        /// 把一个缺陷结果转成用于轮播的显示对象（取缺陷小图，并释放该结果占用的其它图）。
        /// 公开给其他机型复用。
        /// </summary>
        public static ShowHalconWindow NgDefectResultToShowHalconWindow(NgDefectResult ngDefectResult, string camName)
        {
            ShowHalconWindow showHalconWindow = new ShowHalconWindow();
            showHalconWindow.m_strCamName = camName;
            showHalconWindow.m_hImage = ngDefectResult.CropImage;
            ngDefectResult.DisposePortion();
            return showHalconWindow;
        }

        public static bool TotalDefectResultJudgment(DefectSummary defectSummary, DefectCraft_DieCuttingCut defectCraft_DieCuttingCut)
        {
            if (defectCraft_DieCuttingCut.Condition1_En)
            {
                if (defectCraft_DieCuttingCut.Condition1AndCondition2_Logic == AndOrLogicEnum.或)
                {
                    return ConditionalJudgment(defectSummary.T_TotalCount, defectCraft_DieCuttingCut.Condition1_MembraneNum) || (defectSummary.T_NgCount > 0);
                }
                else
                {
                    return ConditionalJudgment(defectSummary.T_TotalCount, defectCraft_DieCuttingCut.Condition1_MembraneNum) && (defectSummary.T_NgCount > 0);
                }
            }
            else
            {
                return defectSummary.T_NgCount > 0;
            }
        }
        /// <summary>
        /// 缺陷结果判断
        /// </summary>
        /// <param name="ngDefectResult"></param>
        /// <param name="defectCraft"></param>
        /// <returns>是否NG</returns>
        /// <summary>缺陷结果判断。公开给其他机型复用：规则本身与机型无关，只和工艺有关。</summary>
        public static bool DefectResultJudgment(NgDefectResult ngDefectResult, DefectCraft_DieCuttingCut defectCraft)
        {
            var conditionGroups = GetConditionFormat(defectCraft);
            bool totalResult = true;//默认缺陷NG
            if (conditionGroups == null || conditionGroups.Count == 0) return false;
            if (!(conditionGroups.Where(p => p.ConditionItems.Count > 0).Count() > 0)) return false;

            for (int i = 0; i < conditionGroups.Count; i++)
            {
                var conditions = conditionGroups[i].ConditionItems;
                if (conditions == null || conditions.Count == 0)
                {
                    continue;
                }

                bool groupResult = EvaluateCondition(conditions[0], ngDefectResult);

                for (int j = 1; j < conditions.Count; j++)
                {
                    var condition = conditions[j];
                    bool currentResult = EvaluateCondition(condition, ngDefectResult);

                    groupResult = condition.Logic == AndOrLogicEnum.或
                        ? groupResult || currentResult
                        : groupResult && currentResult;
                }

                if (i == 0)
                {
                    totalResult = groupResult;
                    continue;
                }

                // 当前组首条件的 Logic 表示它与上一组的关系。
                var groupLogic = conditions[0].Logic;
                totalResult = groupLogic == AndOrLogicEnum.或
                    ? totalResult || groupResult
                    : totalResult && groupResult;
            }

            // 根据实际属性名称回写判定结果。
            return totalResult;
        }

        public static List<ListConditionItem> GetConditionFormat(DefectCraft_DieCuttingCut defectCraft_DieCuttingCut)
        {
            List<ListConditionItem> ConditionItemss = new List<ListConditionItem>();

            if (defectCraft_DieCuttingCut.Condition2_En)
            {
                ListConditionItem listConditionItem2 = new ListConditionItem();
                listConditionItem2.ConditionItems.Add(new ConditionItem()
                {
                    Logic = defectCraft_DieCuttingCut.Condition1AndCondition2_Logic,
                    DataSource = defectCraft_DieCuttingCut.Condition2_DataSource1,
                    Data = defectCraft_DieCuttingCut.Condition2_Data1
                });

                listConditionItem2.ConditionItems.Add(new ConditionItem()
                {
                    Logic = defectCraft_DieCuttingCut.Condition2_Logic1,
                    DataSource = defectCraft_DieCuttingCut.Condition2_DataSource2,
                    Data = defectCraft_DieCuttingCut.Condition2_Data2
                });

                listConditionItem2.ConditionItems.Add(new ConditionItem()
                {
                    Logic = defectCraft_DieCuttingCut.Condition2_Logic2,
                    DataSource = defectCraft_DieCuttingCut.Condition2_DataSource3,
                    Data = defectCraft_DieCuttingCut.Condition2_Data3
                });
                ConditionItemss.Add(listConditionItem2);
            }

            if (defectCraft_DieCuttingCut.Condition3_En)
            {
                ListConditionItem listConditionItem3 = new ListConditionItem();
                listConditionItem3.ConditionItems.Add(new ConditionItem()
                {
                    Logic = defectCraft_DieCuttingCut.Condition2AndCondition3_Logic,
                    DataSource = defectCraft_DieCuttingCut.Condition3_DataSource1,
                    Data = defectCraft_DieCuttingCut.Condition3_Data1
                });

                listConditionItem3.ConditionItems.Add(new ConditionItem()
                {
                    Logic = defectCraft_DieCuttingCut.Condition3_Logic1,
                    DataSource = defectCraft_DieCuttingCut.Condition3_DataSource2,
                    Data = defectCraft_DieCuttingCut.Condition3_Data2
                });
                ConditionItemss.Add(listConditionItem3);
            }

            if (defectCraft_DieCuttingCut.Condition4_En)
            {
                ListConditionItem listConditionItem4 = new ListConditionItem();
                listConditionItem4.ConditionItems.Add(new ConditionItem()
                {
                    Logic = defectCraft_DieCuttingCut.Condition3AndCondition4_Logic,
                    DataSource = defectCraft_DieCuttingCut.Condition4_DataSource1,
                    Data = defectCraft_DieCuttingCut.Condition4_Data1
                });

                listConditionItem4.ConditionItems.Add(new ConditionItem()
                {
                    Logic = defectCraft_DieCuttingCut.Condition4_Logic1,
                    DataSource = defectCraft_DieCuttingCut.Condition4_DataSource2,
                    Data = defectCraft_DieCuttingCut.Condition4_Data2
                });

                ConditionItemss.Add(listConditionItem4);
            }

            return ConditionItemss;
        }

        public static bool EvaluateCondition(ConditionItem condition, NgDefectResult ngDefectResult)
        {
            double detectionValue = GetConditionalData(condition.DataSource, ngDefectResult);
            return ConditionalJudgment(detectionValue, condition.Data);
        }

        public static double GetConditionalData(DefectDataSourceEnum defectDataSourceEnum, NgDefectResult ngDefectResult)
        {
            if (defectDataSourceEnum == DefectDataSourceEnum.面积)
            {
                return ngDefectResult.Area;
            }
            else if (defectDataSourceEnum == DefectDataSourceEnum.高度)
            {
                return ngDefectResult.Height;
            }
            else if (defectDataSourceEnum == DefectDataSourceEnum.宽度)
            {
                return ngDefectResult.Width;
            }
            return 0;
        }

        public static bool ConditionalJudgment(double detectionValue, double cafectValue)
        {
            return detectionValue > cafectValue;
        }

        public static bool ConditionalJudgment(double value, double cafectStandardValue, double tolUpper, double tolLower)
        {
            return ((value <= tolUpper) && (value >= tolLower));
        }

        /// <summary>超规格/NG 对应的显示颜色。公开给其他机型复用。</summary>
        public static HalconColor GetHalconColor(bool IsOverspecification, bool IsNG)
        {
            if (IsNG)
            {
                return HalconColor.red;
            }
            else if (IsOverspecification && !IsNG)
            {
                return HalconColor.yellow;
            }
            return HalconColor.green;
        }

        /// <summary>
        /// 数据顶级模式
        /// </summary>
        /// <param name="OriginData">原始数据</param>
        /// <param name="StandData">标准值</param>
        /// <param name="offset">偏差顶级范围：与标准值在一定范围内才使用顶级模式</param>
        /// <param name="factor">顶级系数：大于等于1</param>
        /// <returns></returns>
        public static double TopLevelMode(double OriginData, double StandData, double offset, double factor)
        {
            double Topdata = OriginData;
            try
            {
                double SubAbs = Math.Abs(OriginData - StandData);
                if (factor >= 1 && SubAbs < Math.Abs(offset)) //顶级系数大于等于1，且偏差值小于顶级范围。
                {
                    Topdata = (StandData * (factor - 1) + OriginData) / factor;
                }
            }
            catch (Exception ex)
            {
                //Log4netRecord.ErrorLog.Error(ex);
            }
            return Topdata;
        }
    }
}
