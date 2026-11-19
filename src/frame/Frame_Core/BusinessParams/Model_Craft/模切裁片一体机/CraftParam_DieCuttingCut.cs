using Frame_Core;
using FrameCommunity.DataOperation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Frame_Core
{
    [Serializable]
    public class CraftParam_DieCuttingCut : CraftParam_DieCutBase
    {
        // 相机工艺、各工位检测项集合都由 CraftParam_DieCutBase 提供（Crafts / l_DefectCraftStation_1 / l_SizeCraftStation_3 / l_DefectCraftStation_3）。
        // 本机型只负责按"画像里的相机角色"装配：标准 6 路是 0/1 模切、2 纠偏、3~5 尺寸；
        // 少一路纠偏的机器由 LaserDieCuttingProfile 自动裁成 0/1 模切 + 2~4 尺寸。

        /// <summary>
        /// 读取此参数会执行。
        ///
        /// 装配按"画像里的相机角色"而不是"下标"：激光 6 路是 0/1 模切、2 纠偏、3~5 尺寸，
        /// 但同一份代码也可能跑在少一路纠偏的机器上。按下标 switch 会把纠偏的 17 项变量
        /// 装到实际是尺寸相机的那个页签里——界面上名字对、内容错，而且不会报错。
        /// </summary>
        public override void Init(int length)
        {
            IDeviceProfile profile = DeviceProfileRegistry.Resolve(CraftType);
            IReadOnlyList<CamSlotDef> slots = profile.CamSlots;

            // 相机数取"配置相机数"与"画像槽位数"的较小值：
            // 槽位比配置多时，多出来的槽位没有物理相机，装了只会在参数界面上多一个空页签。
            int camCount = Math.Min(length, slots.Count);
            if (camCount <= 0) camCount = slots.Count;

            EnsureCamCraftCount(camCount);
            if (Crafts.Count > camCount) Crafts.RemoveRange(camCount, Crafts.Count - camCount);

            //【】工艺参数初始化
            #region  
            int sizeOccurrence = 0;
            for (int i = 0; i < camCount; i++)
            {
                List<GlobalVariableItem> targetGlobalVariableList = new List<GlobalVariableItem>();

                //============================================================================
                //相机全局变量：角色决定用哪一组变量
                switch (slots[i].Role)
                {
                    case CamRole.模切主相机:
                        Cam1VariableSet(targetGlobalVariableList, i);
                        break;

                    case CamRole.模切从相机:
                        Cam2VariableSet(targetGlobalVariableList, i);
                        break;

                    case CamRole.裁切纠偏:
                        Cam3VariableSet(targetGlobalVariableList, i);
                        break;

                    case CamRole.尺寸检测:
                        // 第 1 路尺寸相机是"带极耳"位置的那一路，2/3 路是左右两侧，
                        // 与原来的 Cam4/Cam5/Cam6 一一对应；多出来的路按序号通用命名。
                        switch (++sizeOccurrence)
                        {
                            case 1: Cam4VariableSet(targetGlobalVariableList, i); break;
                            case 2: Cam5VariableSet(targetGlobalVariableList, i); break;
                            default: Cam6VariableSet(targetGlobalVariableList, i); break;
                        }
                        break;
                }

                Crafts[i].M_GlobalVariable.l_GlobalVariableItems = targetGlobalVariableList;
                for (int j = 0; j < Crafts[i].M_GlobalVariable.l_GlobalVariableItems.Count; j++)
                {
                    Crafts[i].M_GlobalVariable.l_GlobalVariableItems[j].ID = j + 1;
                }

                //============================================================================
                //相机工艺初始化：只有承担该工位的相机才重建容器，纠偏相机保持原样
                if (slots[i].Role == CamRole.模切主相机 || slots[i].Role == CamRole.模切从相机)
                {
                    Crafts[i].l_DefectCraft = new List<DefectCraft_DieCuttingCut>();
                }

                if (slots[i].Role == CamRole.尺寸检测)
                {
                    Crafts[i].l_SizeCraft = new List<SizeCraft_DieCuttingCut>();
                    Crafts[i].l_DefectCraft = new List<DefectCraft_DieCuttingCut>();
                }
            }
            #endregion

            //【】缺陷检测工位1
            #region       
            l_DefectCraftStation_1.AppVariableByOrder("未辊压");
            l_DefectCraftStation_1.AppVariableByOrder("空箔");
            l_DefectCraftStation_1.AppVariableByOrder("胶带");
            l_DefectCraftStation_1.AppVariableByOrder("漏箔");
            l_DefectCraftStation_1.AppVariableByOrder("暗斑");
            l_DefectCraftStation_1.AppVariableByOrder("亮斑");
            l_DefectCraftStation_1.AppVariableByOrder("亮痕");
            l_DefectCraftStation_1.AppVariableByOrder("暗痕");
            l_DefectCraftStation_1.AppVariableByOrder("边缘漏箔");
            l_DefectCraftStation_1.AppVariableByOrder("亮缺陷");
            l_DefectCraftStation_1.AppVariableByOrder("暗缺陷");
            l_DefectCraftStation_1.AppVariableByOrder("涂布不良");
            // 流程里硬编码 emit 的另外两个类名（边缘破损检测模块、AT 抓边），
            // 不补这两项它们一律会走"未找到工艺类型 → 重度 NG"。
            l_DefectCraftStation_1.AppVariableByOrder("边缘破损");
            l_DefectCraftStation_1.AppVariableByOrder("AT漏金属");

            for (int j = 0; j < l_DefectCraftStation_1.Count; j++)
            {
                l_DefectCraftStation_1[j].Id = j + 1;
            }

            #endregion
            //【】极片尺寸工位3
            #region  
            // 尺寸项引用的相机下标必须按画像取：激光 6 路是 3/4/5，少一路纠偏时是 2/3/4。
            // 取不到的（本机尺寸相机不足两路）退回第一路，避免把项指向不存在的相机。
            List<int> sizeCamIndexes = SizeCamIndexes(DeviceProfileRegistry.Resolve(CraftType).CamSlots);
            int camIndex4 = sizeCamIndexes.Count > 0 ? sizeCamIndexes[0] : 0;
            int camIndex5 = sizeCamIndexes.Count > 1 ? sizeCamIndexes[1] : camIndex4;

            l_SizeCraftStation_3.AppVariableByOrder("下左V宽X", CalcFormulaEnum.点线, camIndex4, "左侧V角凸点2", camIndex4, "粗定位工具.抓边1");
            l_SizeCraftStation_3.AppVariableByOrder("下左V深Y", CalcFormulaEnum.点线, camIndex4, "左侧V角凸点1", camIndex4, "粗定位工具.抓边2");

            l_SizeCraftStation_3.AppVariableByOrder("下右V宽X", CalcFormulaEnum.点线, camIndex4, "右侧V角凸点2", camIndex4, "极片右侧抓边");       
            l_SizeCraftStation_3.AppVariableByOrder("下右V深Y", CalcFormulaEnum.点线, camIndex4, "右侧V角凸点1", camIndex4, "粗定位工具.抓边2");

            l_SizeCraftStation_3.AppVariableByOrder("上左V宽X", CalcFormulaEnum.点线, camIndex5, "V角凸点1", camIndex5, "粗定位工具.抓边1");
            l_SizeCraftStation_3.AppVariableByOrder("上左V深Y", CalcFormulaEnum.点线, camIndex5, "V角凸点2", camIndex5, "粗定位工具.抓边2");

            //l_SizeCraftStation_3.AppVariableByOrder("上右V宽X", CalcFormulaEnum.点线, camIndex6, "V角凸点1", camIndex6, "粗定位工具.抓边1");
            //l_SizeCraftStation_3.AppVariableByOrder("上右V深Y", CalcFormulaEnum.点线, camIndex6, "V角凸点2", camIndex6, "粗定位工具.抓边2");

            l_SizeCraftStation_3.AppVariableByOrder("极耳宽度", CalcFormulaEnum.点点, camIndex4, "极耳左侧交点.交点", camIndex4, "极耳右侧组合交点");
            l_SizeCraftStation_3.AppVariableByOrder("极耳高度", CalcFormulaEnum.线线Y, camIndex4, "粗定位工具.抓边2", camIndex4, "极耳左侧交点.抓边2");
            l_SizeCraftStation_3.AppVariableByOrder("极耳左肩宽", CalcFormulaEnum.线线X, camIndex4, "粗定位工具.抓边1", camIndex4, "极耳左侧交点.抓边1");
            l_SizeCraftStation_3.AppVariableByOrder("带料高", CalcFormulaEnum.线线Y, camIndex4, "极片AT抓边", camIndex4, "极耳左侧交点.抓边2");
            l_SizeCraftStation_3.AppVariableByOrder("极片角度", CalcFormulaEnum.角度, camIndex4, "粗定位工具.抓边1", camIndex4, "粗定位工具.抓边2");

            l_SizeCraftStation_3.AppVariableByOrder("单片长度", CalcFormulaEnum.线线Y, camIndex4, "粗定位工具.抓边2", camIndex5, "粗定位工具.抓边2");
            l_SizeCraftStation_3.AppVariableByOrder("单片宽度", CalcFormulaEnum.线线X, camIndex4, "粗定位工具.抓边1", camIndex4, "极片右侧抓边");


            for (int j = 0; j < l_SizeCraftStation_3.Count; j++)
            {
                l_SizeCraftStation_3[j].Id = j + 1;
            }



            l_DefectCraftStation_3.AppVariableByOrder("V角检测有无");
            l_DefectCraftStation_3.AppVariableByOrder("极片有无");
            l_DefectCraftStation_3.AppVariableByOrder("极耳翻折检测");
            l_DefectCraftStation_3.AppVariableByOrder("极耳余料检测");
            l_DefectCraftStation_3.AppVariableByOrder("极耳涂布内翻检测");

            for (int j = 0; j < l_DefectCraftStation_3.Count; j++)
            {
                l_DefectCraftStation_3[j].Id = j + 1;
            }
            #endregion

            //【】全局变量
            #region  
            if (M_GlobalVariable == null) M_GlobalVariable = new GlobalVariable();
            List<GlobalVariableItem> targetList = new List<GlobalVariableItem>();
            // 第一次添加，不存在直接新增
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "极耳在左侧", "左侧为1右侧为0", DataTypeEnum.Int, "1");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "带AT", "带AT（true）", DataTypeEnum.Bool, "false");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "HeartbeatPollingTime", "心跳轮询时间(ms)", DataTypeEnum.Int, "500");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "HeartbeatPulseWidth", "心跳脉冲宽度(ms)", DataTypeEnum.Int, "100");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "防抖时间", "防抖时间(ms)", DataTypeEnum.Int, "10");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "PulseWidth", "脉冲宽度(ms)", DataTypeEnum.Int, "10");

            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "SizeStationTimeout", "极片尺寸工位超时时间(ms)", DataTypeEnum.Int, "1500");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "FrontSideCount", "正面片数", DataTypeEnum.Int, "0");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "BackSideCount", "背面片数", DataTypeEnum.Int, "0");
      
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "尺寸相机返回结果片数", "尺寸相机返回结果片数", DataTypeEnum.Int, "0");

            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "PieWidth", "(缺陷相机)极片宽度(mm)", DataTypeEnum.Double, "0");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "PieHeight", "(缺陷相机)极片高度(mm)", DataTypeEnum.Double, "0");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "CropCenterOffset", "居中裁图偏移距离(px)", DataTypeEnum.Double, "0");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "CropHeightScale", "裁图高度比例系数", DataTypeEnum.Double, "0");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "AtWidth", "(缺陷相机)AT宽度(mm)", DataTypeEnum.Double, "0");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "DefectStation_ContinuousCount", "外观工位连续数量", DataTypeEnum.Int, "0");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "SizeStation_ContinuousCount", "尺寸工位连续数量", DataTypeEnum.Int, "0");

            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "PID启用", "true：开启", DataTypeEnum.Bool, "false");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "PID输出比例", "输出补偿值的比例，方向(+-)", DataTypeEnum.Double, "-0.4");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "PID样本个数", "每次取样的个数", DataTypeEnum.Int, "10");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "PID死区范围", "平均尺寸偏差在该范围内时，输出 0", DataTypeEnum.Double, "0.1");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "PID超限范围", "单个尺寸偏差超过该值时直接丢弃。", DataTypeEnum.Double, "1");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "PID异常值剔除倍数", "IQR异常值剔除倍数(1.5：常用，剔除比较严格 2.0：现场波动稍大 3.0：只剔特别离谱的点)", DataTypeEnum.Double, "1.5");
            targetList.AddVariableByOrder(M_GlobalVariable.l_GlobalVariableItems, "PID输出最大值", "次输出的闭环纠偏最大绝对值", DataTypeEnum.Double, "0.1");

            M_GlobalVariable.l_GlobalVariableItems = targetList;
            for (int i = 0; i < M_GlobalVariable.l_GlobalVariableItems.Count; i++)
            {
                M_GlobalVariable.l_GlobalVariableItems[i].ID = i + 1;
            }
            #endregion
        }

        /// <summary>
        /// 相机1全局变量
        /// </summary>
        /// <param name="targetGlobalVariableList"></param>
        /// <param name="i"></param>
        public void Cam1VariableSet(List<GlobalVariableItem> targetGlobalVariableList, int i)
        {
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CamABGap", "AB相机间隔(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "DefCutPos", "默认切刀位（px）", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "DefCamEALftBaseCol", "缺陷相机&EA左侧边缘基准线Col(px)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CoatLftCol1", "涂层区域左偏移Col1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CoatRgtCol2", "涂层区域右偏移Col2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CoatTopRow1", "涂层区域上偏移Row1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CoatTopRow2", "涂层区域下偏移Row2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "AtLftCol1", "AT区域左偏移Col1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "AtLftCol2", "AT区域右偏移Col2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "AtTopRow1", "AT区域上偏移Row1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "AtBotRow2", "AT区域下偏移Row2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "BreakLftCol1", "破损区域左偏移Col1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "BreakRgtCol2", "破损区域右偏移Col2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "BreakTopRow1", "破损区域上偏移Row1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "BreakBotRow2", "破损区域下偏移Row2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "BreakAreaW", "破损检测区域宽度（mm）", DataTypeEnum.Double, "0");
        }
        /// <summary>
        /// 相机2全局变量
        /// </summary>
        /// <param name="targetGlobalVariableList"></param>
        /// <param name="i"></param>
        public void Cam2VariableSet(List<GlobalVariableItem> targetGlobalVariableList, int i)
        {

            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "DefCutPos", "默认切刀位（px）", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "DefCamEALftBaseCol", "缺陷相机EA左侧边缘基准线Col(px)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CoatLftCol1", "涂层区域左偏移Col1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CoatRgtCol2", "涂层区域右偏移Col2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CoatTopRow1", "涂层区域上偏移Row1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CoatTopRow2", "涂层区域下偏移Row2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "AtLftCol1", "AT区域左偏移Col1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "AtLftCol2", "AT区域右偏移Col2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "AtTopRow1", "AT区域上偏移Row1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "AtBotRow2", "AT区域下偏移Row2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "BreakLftCol1", "破损区域左偏移Col1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "BreakRgtCol2", "破损区域右偏移Col2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "BreakTopRow1", "破损区域上偏移Row1(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "BreakBotRow2", "破损区域下偏移Row2(mm)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "BreakAreaW", "破损检测区域宽度（mm）", DataTypeEnum.Double, "0");
        }
        /// <summary>
        /// 相机3全局变量
        /// </summary>
        /// <param name="targetGlobalVariableList"></param>
        /// <param name="i"></param>
        public void Cam3VariableSet(List<GlobalVariableItem> targetGlobalVariableList, int i)
        {
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "AlignCamBaseCol", "纠偏相机定位基准点X Col (px)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "AlignCamBaseRow", "纠偏相机定位基准点Y Row (px)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "MaxKnifeCorrection", "最大切刀纠偏量", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CorrectionToleranceX", "纠偏允许偏差X", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CutterCompensation", "裁切刀补偿值", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "ShoulderWidthCompensation", "肩宽补偿值", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CameraToCutterDistanceNum", "相机到切刀片数", DataTypeEnum.Int, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CameraToVCutterDistanceNum", "相机到V切刀片数", DataTypeEnum.Int, "0");  
            
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "极耳夹角公差", "超过公差判定无极耳", DataTypeEnum.Int, "10");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "FindTabSum", "寻极耳个数", DataTypeEnum.Int, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "裁切纠偏取反", "裁切纠偏取反", DataTypeEnum.Bool, "false");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "V角纠偏取反", "V角纠偏取反", DataTypeEnum.Bool, "false");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "VCornerCorrectionEnabled", "V角纠偏启用", DataTypeEnum.Bool, "false");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "VCornerCompensation", "V角补偿值", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "VCornerDepthCompensation", "V角深补偿值", DataTypeEnum.Double, "0");

            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CamBaseCol", "流程定基准 Col (px)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CamBaseRow", "流程定基准 Row (px)", DataTypeEnum.Double, "0");
        }
        /// <summary>
        /// 相机4全局变量
        /// </summary>
        /// <param name="targetGlobalVariableList"></param>
        /// <param name="i"></param>
        public void Cam4VariableSet(List<GlobalVariableItem> targetGlobalVariableList, int i)
        {

            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CamBaseCol", "极片尺寸带极耳相机定位基准点Col(px)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CamBaseRow", "极片尺寸带极耳相机定位基准点Row(px)", DataTypeEnum.Double, "0");
        }
        /// <summary>
        /// 相机5全局变量
        /// </summary>
        /// <param name="targetGlobalVariableList"></param>
        /// <param name="i"></param>
        public void Cam5VariableSet(List<GlobalVariableItem> targetGlobalVariableList, int i)
        {

            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CamBaseCol", "极片尺寸左侧相机定位基准点Col(px)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CamBaseRow", "极片尺寸左侧相机定位基准点Row(px)", DataTypeEnum.Double, "0");
        }
        /// <summary>
        /// 相机6全局变量
        /// </summary>
        /// <param name="targetGlobalVariableList"></param>
        /// <param name="i"></param>
        public void Cam6VariableSet(List<GlobalVariableItem> targetGlobalVariableList, int i)
        {
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CamBaseCol", "极片尺寸右侧相机定位基准点Col(px)", DataTypeEnum.Double, "0");
            targetGlobalVariableList.AddVariableByOrder(Crafts[i].M_GlobalVariable.l_GlobalVariableItems, "CamBaseRow", "极片尺寸右侧相机定位基准点Row(px)", DataTypeEnum.Double, "0");
        }
    }

    public class CamCraft : JsonSerializationOperation
    {
        private List<SizeCraft_DieCuttingCut> _l_SizeCraft = new List<SizeCraft_DieCuttingCut>();
        public List<SizeCraft_DieCuttingCut> l_SizeCraft
        {
            get => _l_SizeCraft;
            set { _l_SizeCraft = value; }
        }

        private List<DefectCraft_DieCuttingCut> _l_DefectCraft = new List<DefectCraft_DieCuttingCut>();
        public List<DefectCraft_DieCuttingCut> l_DefectCraft
        {
            get => _l_DefectCraft;
            set { _l_DefectCraft = value; }
        }

        private GlobalVariable _M_GlobalVariable = new GlobalVariable();
        public GlobalVariable M_GlobalVariable
        {
            get => _M_GlobalVariable;
            set { _M_GlobalVariable = value; }
        }
    }
}
