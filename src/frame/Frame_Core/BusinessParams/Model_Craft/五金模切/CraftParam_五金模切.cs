using System;
using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// 五金模切的配方参数。
    ///
    /// 数据形状与激光模切一致（CameraCraft + 工位检测项），差异只在"哪一路相机是什么角色"：
    /// 激光 0/1 模切、2 纠偏、3~5 尺寸；五金 0/1 模切、2~4 尺寸，没有纠偏工位。
    /// 因此装配一律按 <see cref="CamSlotDef.Role"/> 走，不再出现 i == 3 || i == 4 这种下标判断——
    /// 下标一旦错位，配方里存的相机地址会指向别的相机，后续所有算法都跟着错。
    ///
    /// 工位检测项的默认项暂按激光模切的清单建立，上机后按五金工艺调整（见各 region 的 TODO）。
    /// </summary>
    [Serializable]
    public class CraftParam_五金模切 : CraftParam_DieCutBase
    {
        public override void Init(int length)
        {
            IDeviceProfile profile = DeviceProfileRegistry.Resolve(CraftType);
            IReadOnlyList<CamSlotDef> slots = profile.CamSlots;

            // 相机数一律以设备画像为准：相机配置里多出来的那几路没有槽位，也就没有参数。
            // 基类只增不减是为了不丢数据，但这类残留只会出现在"把激光的 6 路配方拷给五金"时，
            // 多出来的第 6 项在参数界面上会变成一个指向不存在的相机的页签，所以这里直接截断。
            EnsureCamCraftCount(slots.Count);
            if (Crafts.Count > slots.Count) Crafts.RemoveRange(slots.Count, Crafts.Count - slots.Count);
            InitCameraCrafts(slots);
            InitStation1Defects();

            // 尺寸项按"角色"装，不按第几路：带极耳那一路可能是中间也可是最后一路，
            // 按位置装会把极耳类项指到只建了 V角凸点 的那一路上（恒 -999，不报错）。
            SizeSlotPlan sizePlan = SizeSlotPlan.Resolve(slots, ReadTabIsLeft());
            InitStation3Sizes(sizePlan, ReadCraftVersion() < SizeCraftVersion);

            InitStation3Defects();
            InitGlobalVariables();
        }

        #region 每路相机

        private void InitCameraCrafts(IReadOnlyList<CamSlotDef> slots)
        {
            int sizeOccurrence = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                CamCraft craft = Crafts[i];
                List<GlobalVariableItem> variables = new List<GlobalVariableItem>();

                switch (slots[i].Role)
                {
                    case CamRole.模切主相机:
                        AddDieCutCamVariables(variables, i);
                        // 模切相机承担缺陷检测，预留缺陷项容器（默认项在工位1里维护）
                        craft.l_DefectCraft = new List<DefectCraft_DieCuttingCut>();
                        break;

                    case CamRole.模切从相机:
                        AddDieCutCamVariables(variables, i);
                        craft.l_DefectCraft = new List<DefectCraft_DieCuttingCut>();
                        break;

                    case CamRole.裁切纠偏:
                        // 五金模切没有纠偏工位；这里保留分支，仅当画像将来补上该角色时才启用
                        AddAlignCamVariables(variables, i);
                        break;

                    case CamRole.尺寸检测:
                        AddSizeCamVariables(variables, i, ++sizeOccurrence);
                        craft.l_SizeCraft = new List<SizeCraft_DieCuttingCut>();
                        craft.l_DefectCraft = new List<DefectCraft_DieCuttingCut>();
                        break;
                }

                Reindex(variables);
                craft.M_GlobalVariable.l_GlobalVariableItems = variables;
            }
        }

        /// <summary>
        /// 模切主/从相机共用同一套变量：两路是镜像关系的同一种工艺，变量名与含义完全一致。
        ///
        /// 名字必须与流程实际读取的名字逐字相同（见 五金模切ProDevice.流程内容.cs 的 EA 流程：
        /// Coat/At/Break 三组区域偏移 + BreakAreaW）。少一项不报错，只是那个偏移按 0 参与计算，
        /// 检测区域会塌成一条线——表现为"这类缺陷永远不报"，日志里看不出原因。
        ///
        /// 注意：激光模切把"涂层下偏移"建成 CoatBotRow2、"AT右偏移"建成 AtRgtCol2，
        /// 而流程读的是 CoatTopRow2 / AtLftCol2，那两个键在激光上从来没被读到过（恒为 0）。
        /// 这里按流程读取的名字建，不再复制这个错位。
        /// </summary>
        private void AddDieCutCamVariables(List<GlobalVariableItem> target, int camIndex)
        {
            List<GlobalVariableItem> loaded = Crafts[camIndex].M_GlobalVariable.l_GlobalVariableItems;
            target.AddVariableByOrder(loaded, "CamABGap", "AB相机间隔(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "DefCutPos", "默认切刀位（px）", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "DefCamEALftBaseCol", "缺陷相机&EA左侧边缘基准线Col(px)", DataTypeEnum.Double, "0");

            target.AddVariableByOrder(loaded, "CoatLftCol1", "涂层区域左偏移Col1(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "CoatRgtCol2", "涂层区域右偏移Col2(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "CoatTopRow1", "涂层区域上偏移Row1(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "CoatTopRow2", "涂层区域下偏移Row2(mm)", DataTypeEnum.Double, "0");

            target.AddVariableByOrder(loaded, "AtLftCol1", "AT区域左偏移Col1(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "AtLftCol2", "AT区域右偏移Col2(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "AtTopRow1", "AT区域上偏移Row1(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "AtBotRow2", "AT区域下偏移Row2(mm)", DataTypeEnum.Double, "0");

            target.AddVariableByOrder(loaded, "BreakLftCol1", "破损区域左偏移Col1(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "BreakRgtCol2", "破损区域右偏移Col2(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "BreakTopRow1", "破损区域上偏移Row1(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "BreakBotRow2", "破损区域下偏移Row2(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "BreakAreaW", "破损检测区域宽度（mm）", DataTypeEnum.Double, "0");
        }

        private void AddAlignCamVariables(List<GlobalVariableItem> target, int camIndex)
        {
            List<GlobalVariableItem> loaded = Crafts[camIndex].M_GlobalVariable.l_GlobalVariableItems;
            target.AddVariableByOrder(loaded, "AlignBaseCol", "纠偏相机基准位置Col(px)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "AlignBaseRow", "纠偏相机基准位置Row(px)", DataTypeEnum.Double, "0");
        }

        private void AddSizeCamVariables(List<GlobalVariableItem> target, int camIndex, int occurrence)
        {
            List<GlobalVariableItem> loaded = Crafts[camIndex].M_GlobalVariable.l_GlobalVariableItems;
            string title = "极片尺寸相机" + occurrence;
            target.AddVariableByOrder(loaded, "CamBaseCol", title + "定位基准点Col(px)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "CamBaseRow", title + "定位基准点Row(px)", DataTypeEnum.Double, "0");
        }

        #endregion

        #region 工位

        /// <summary>TODO：默认缺陷项沿用激光模切，五金件的实际缺陷类型待工艺确认后替换。</summary>
        private void InitStation1Defects()
        {
            if (l_DefectCraftStation_1 == null) l_DefectCraftStation_1 = new List<DefectCraft_DieCuttingCut>();

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
            // 流程里硬编码 emit 的另外两个类名（见 五金模切ProDevice.流程内容.cs：
            // 边缘破损检测模块、AT 抓边），不补这两项它们一律会走"未找到工艺类型 → 重度 NG"。
            l_DefectCraftStation_1.AppVariableByOrder("边缘破损");
            l_DefectCraftStation_1.AppVariableByOrder("AT漏金属");

            Reindex(l_DefectCraftStation_1);
        }

        /// <summary>
        /// 工位3尺寸项。装项依据只有两个：哪一路跑"带极耳"流程（<paramref name="plan"/>），
        /// 以及那一路建了哪些模块——不按"第几路"装。
        ///
        /// 模块名是硬约束（见 五金模切ProDevice.流程内容.cs）：
        /// 极耳左侧交点 / 极片右侧抓边 / 极片AT抓边 / 极耳右侧组合交点 / 左侧与右侧V角凸点
        /// 只在带极耳侧建；不带极耳侧只有 粗定位工具 / V角凸点1 / V角凸点2。
        /// 挂错的唯一表现是该项恒 -999（数据源未找到），界面上看不出来，所以按流程分派。
        ///
        /// 第三路尺寸相机没有默认测量项：它的流程与另一路 plain 完全相同，只做缺陷检测。
        /// 要让它参与测量，得先给它独立的流程模块，再在这里补项。
        /// </summary>
        private void InitStation3Sizes(SizeSlotPlan plan, bool upgrade)
        {
            if (l_SizeCraftStation_3 == null) l_SizeCraftStation_3 = new List<SizeCraftStation_DieCuttingCut>();
            if (plan.SizeCams.Count == 0) return;

            AlignLaserSizeCamIndexes(l_SizeCraftStation_3, plan.SizeCams);

            // 先追加缺失项（不动已存的绑定），再按版本决定是否把已知默认项改到正确相机。
            ApplySizeDefaults(plan, false);
            if (upgrade) ApplySizeDefaults(plan, true);

            Reindex(l_SizeCraftStation_3);
        }

        /// <summary>
        /// 装尺寸默认项。<paramref name="overwrite"/> = false 时只补缺失项；
        /// = true 时连同相机/公式/数据源一起覆盖（配方升级用，只对这张已知默认清单生效）。
        /// </summary>
        private void ApplySizeDefaults(SizeSlotPlan plan, bool overwrite)
        {
            int tab = plan.TabCam;

            // 不带极耳侧的各路相机下标，按槽位顺序（SizeSlotPlan.PlainCams 已剔除 TabCam）：
            //   plain_0 = 第 1 路 plain；plain_1 = 第 2 路 plain。
            // 只有一路 plain 时 plain_1 退回 plain_0；一路尺寸相机都没有时两者都退回 tab，
            // 避免指向不存在的相机（与 SizeSlotPlan.FirstPlainCam 的原行为一致）。
            IReadOnlyList<int> plainCams = plan.PlainCams;
            int plain_0 = plainCams.Count > 0 ? plainCams[0] : plan.TabCam;
            int plain_1 = plainCams.Count > 1 ? plainCams[1] : plain_0;

            // 带极耳侧
            l_SizeCraftStation_3.AppVariableByOrder("下左V宽X", CalcFormulaEnum.点线, tab, "左侧V角凸点2", tab, "粗定位工具.抓边1", overwrite);
            l_SizeCraftStation_3.AppVariableByOrder("下左V深Y", CalcFormulaEnum.点线, tab, "左侧V角凸点1", tab, "粗定位工具.抓边2", overwrite);
            l_SizeCraftStation_3.AppVariableByOrder("下右V宽X", CalcFormulaEnum.点线, tab, "右侧V角凸点2", tab, "极片右侧抓边", overwrite);
            l_SizeCraftStation_3.AppVariableByOrder("下右V深Y", CalcFormulaEnum.点线, tab, "右侧V角凸点1", tab, "粗定位工具.抓边2", overwrite);

            l_SizeCraftStation_3.AppVariableByOrder("极耳宽度", CalcFormulaEnum.点点, tab, "极耳左侧交点.交点", tab, "极耳右侧组合交点", overwrite);
            l_SizeCraftStation_3.AppVariableByOrder("极耳高度", CalcFormulaEnum.线线Y, tab, "粗定位工具.抓边2", tab, "极耳左侧交点.抓边2", overwrite);
            l_SizeCraftStation_3.AppVariableByOrder("极耳左肩宽", CalcFormulaEnum.线线X, tab, "粗定位工具.抓边1", tab, "极耳左侧交点.抓边1", overwrite);
            l_SizeCraftStation_3.AppVariableByOrder("带料高", CalcFormulaEnum.线线Y, tab, "极片AT抓边", tab, "极耳左侧交点.抓边2", overwrite);
            l_SizeCraftStation_3.AppVariableByOrder("单片宽度", CalcFormulaEnum.线线X, plain_1, "粗定位工具.抓边1", tab, "极片右侧抓边", overwrite);

            // 不带极耳侧（相机动哪一路由你按 plain_0 / plain_1 指定，项名保持原样）
            l_SizeCraftStation_3.AppVariableByOrder("上左V宽X", CalcFormulaEnum.点线, plain_0, "V角凸点1", plain_0, "粗定位工具.抓边1", overwrite);
            l_SizeCraftStation_3.AppVariableByOrder("上左V深Y", CalcFormulaEnum.点线, plain_0, "V角凸点2", plain_0, "粗定位工具.抓边2", overwrite);
            l_SizeCraftStation_3.AppVariableByOrder("极片角度", CalcFormulaEnum.角度, plain_0, "粗定位工具.抓边1", plain_0, "粗定位工具.抓边2", overwrite);

            // 跨相机项：粗定位工具两边都有，一端 plain、一端 tab
            l_SizeCraftStation_3.AppVariableByOrder("单片长度", CalcFormulaEnum.线线Y, plain_0, "粗定位工具.抓边2", tab, "粗定位工具.抓边2", overwrite);
        }

        /// <summary>
        /// 把沿用激光模切编号（3/4/5）的尺寸项相机下标平移到五金槽位（2/3/4）。
        /// 判定条件是本机最大尺寸下标之上的恰好一位（激光 5 / 五金 4），
        /// 不能用"有没有越界"——那会把本就正确的 2/3/4 也一起 -1。
        /// 已知局限：只有 +1 偏移能识别；来源配方若只用了 3/4（缺 5）则不会平移。
        /// </summary>
        private static void AlignLaserSizeCamIndexes(List<SizeCraftStation_DieCuttingCut> items, IReadOnlyList<int> sizeCams)
        {
            if (items == null || items.Count == 0) return;

            int min = sizeCams[0];
            int max = sizeCams[sizeCams.Count - 1];

            int maxUsed = min;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].CameraId1 > maxUsed) maxUsed = items[i].CameraId1;
                if (items[i].CameraId2 > maxUsed) maxUsed = items[i].CameraId2;
            }
            if (maxUsed != max + 1) return;

            for (int i = 0; i < items.Count; i++)
            {
                SizeCraftStation_DieCuttingCut item = items[i];
                item.CameraId1 = Math.Max(min, item.CameraId1 - 1);
                item.CameraId2 = Math.Max(min, item.CameraId2 - 1);
            }
        }

        private void InitStation3Defects()
        {
            if (l_DefectCraftStation_3 == null) l_DefectCraftStation_3 = new List<DefectCraft_DieCuttingCut>();

            l_DefectCraftStation_3.AppVariableByOrder("V角检测有无");
            l_DefectCraftStation_3.AppVariableByOrder("极片有无");
            l_DefectCraftStation_3.AppVariableByOrder("极耳翻折检测");
            l_DefectCraftStation_3.AppVariableByOrder("极耳余料检测");
            l_DefectCraftStation_3.AppVariableByOrder("极耳涂布内翻检测");

            Reindex(l_DefectCraftStation_3);
        }

        #endregion

        #region 机台全局变量

        /// <summary>
        /// 机台级变量。这里不建 PID / 纠偏相关项：五金模切没有纠偏工位，
        /// 建了也只会在界面上留下一排永远为 0 的参数，误导调机。
        /// </summary>
        private void InitGlobalVariables()
        {
            if (M_GlobalVariable == null) M_GlobalVariable = new GlobalVariable();
            if (M_GlobalVariable.l_GlobalVariableItems == null)
                M_GlobalVariable.l_GlobalVariableItems = new List<GlobalVariableItem>();

            List<GlobalVariableItem> loaded = M_GlobalVariable.l_GlobalVariableItems;
            List<GlobalVariableItem> target = new List<GlobalVariableItem>();

            // 配方结构版本：尺寸项改为按角色装配后，旧配方里错绑的相机需要一次性纠正。
            // 装项只增不覆盖（见 AppVariableByOrder），所以这件事必须靠版本号驱动，且只跑一次。
            target.AddVariableByOrder(loaded, CraftVersionKey, "配方结构版本（勿手改）", DataTypeEnum.Int, SizeCraftVersion.ToString());

            target.AddVariableByOrder(loaded, "极耳在左侧", "左侧为1右侧为0", DataTypeEnum.Int, "1");
            target.AddVariableByOrder(loaded, "带AT", "带AT（true）", DataTypeEnum.Bool, "false");
            target.AddVariableByOrder(loaded, "防抖时间", "防抖时间(ms)", DataTypeEnum.Int, "10");
            target.AddVariableByOrder(loaded, "PulseWidth", "脉冲宽度(ms)", DataTypeEnum.Int, "10");

            target.AddVariableByOrder(loaded, "SizeStationTimeout", "极片尺寸工位超时时间(ms)", DataTypeEnum.Int, "1500");
            target.AddVariableByOrder(loaded, "FrontSideCount", "正面片数", DataTypeEnum.Int, "0");
            target.AddVariableByOrder(loaded, "BackSideCount", "背面片数", DataTypeEnum.Int, "0");
            target.AddVariableByOrder(loaded, "尺寸相机返回结果片数", "尺寸相机返回结果片数", DataTypeEnum.Int, "0");

            // 上下线两台线扫相机之间隔几片：0 = 看同一片（不隔片），N = 下线比上线晚 N 片。
            // 稳态下 下线帧号 = 上线帧号 - 该值；仿真的下线触发偏移也按它计算。
            target.AddVariableByOrder(loaded, "下线片距", "上下线相机之间隔几片，0=同一片；下线帧号=上线帧号-该值", DataTypeEnum.Int, "0");

            target.AddVariableByOrder(loaded, "PieWidth", "(缺陷相机)极片宽度(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "PieHeight", "(缺陷相机)极片高度(mm)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "CropCenterOffset", "居中裁图偏移距离(px)", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "CropHeightScale", "裁图高度比例系数", DataTypeEnum.Double, "0");
            target.AddVariableByOrder(loaded, "AtWidth", "(缺陷相机)AT宽度(mm)", DataTypeEnum.Double, "0");

            M_GlobalVariable.l_GlobalVariableItems = target;

            // 走到这里说明本版本的装配已经应用完（含上面的旧配方纠正），版本号可以落到当前值。
            GlobalVariableItem version = target.Find(p => p.Name == CraftVersionKey);
            if (version != null) version.Value = SizeCraftVersion.ToString();

            Reindex(M_GlobalVariable.l_GlobalVariableItems);
        }

        #endregion

        #region 工具

        /// <summary>配方结构版本：2 = 尺寸项按"带极耳/plain 角色"装配（此前按第几路装）。</summary>
        private const int SizeCraftVersion = 2;

        private const string CraftVersionKey = "CraftVersion";

        /// <summary>读配方里保存的"极耳在左侧"；新配方（没有该项）按默认左侧处理。</summary>
        private bool ReadTabIsLeft()
        {
            GlobalVariableItem item = this.Get_GlobalVariableItem("极耳在左侧");
            return item == null || item.Value.ToInt() == 1;
        }

        /// <summary>读配方结构版本；老配方没有这个键，返回 0 触发一次纠正。</summary>
        private int ReadCraftVersion()
        {
            GlobalVariableItem item = this.Get_GlobalVariableItem(CraftVersionKey);
            return item == null ? 0 : item.Value.ToInt();
        }

        #endregion
    }
}