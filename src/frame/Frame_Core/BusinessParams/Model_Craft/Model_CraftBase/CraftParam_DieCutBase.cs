using System;
using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// 模切类机型的配方数据骨架：每路相机一份 <see cref="CamCraft"/>，外加各工位的检测项集合。
    ///
    /// 这里只放"数据形状"，不放相机语义。相机下标代表什么角色由机型画像的 CamSlots 决定，
    /// 因此激光模切（0/1 模切、2 纠偏、3~5 尺寸）和五金模切（0/1 模切、2~4 尺寸）可以共用同一套配方结构。
    /// 各机型在自己的 Init 里按角色装配，不再用 i == 3 || i == 4 这类下标判断。
    /// </summary>
    [Serializable]
    public class CraftParam_DieCutBase : CraftBase
    {
        /// <summary>每路相机的工艺（下标即物理相机下标）。</summary>
        private List<CamCraft> _Crafts = new List<CamCraft>();
        public List<CamCraft> Crafts
        {
            get { return _Crafts; }
            set { _Crafts = value; }
        }

        /// <summary>工位1缺陷检测。</summary>
        private List<DefectCraft_DieCuttingCut> _l_DefectCraftStation_1 = new List<DefectCraft_DieCuttingCut>();
        public List<DefectCraft_DieCuttingCut> l_DefectCraftStation_1
        {
            get { return _l_DefectCraftStation_1; }
            set { _l_DefectCraftStation_1 = value; }
        }

        /// <summary>工位3尺寸检测。</summary>
        private List<SizeCraftStation_DieCuttingCut> _l_SizeCraftStation_3 = new List<SizeCraftStation_DieCuttingCut>();
        public List<SizeCraftStation_DieCuttingCut> l_SizeCraftStation_3
        {
            get { return _l_SizeCraftStation_3; }
            set { _l_SizeCraftStation_3 = value; }
        }

        /// <summary>工位3缺陷检测。</summary>
        private List<DefectCraft_DieCuttingCut> _l_DefectCraftStation_3 = new List<DefectCraft_DieCuttingCut>();
        public List<DefectCraft_DieCuttingCut> l_DefectCraftStation_3
        {
            get { return _l_DefectCraftStation_3; }
            set { _l_DefectCraftStation_3 = value; }
        }

        /// <summary>
        /// 配方是否带 AT（全局变量 `带AT`，缺项按不启用）。
        /// 运行期尺寸项/缺陷项与配方参数界面都按它决定是否展示 AT 相关内容。
        /// </summary>
        public bool IsAT
        {
            get { return GetGlobalVariableValue("带AT").ToTryBool(false); }
        }

        /// <summary>
        /// 把 Bool 全局变量的值统一为 "1"/"0"（1=启用，0=禁用），兼容旧配方的 true/false。
        /// 配方读取后调用一次，只改内存；保存配方时落盘。
        /// </summary>
        public void NormalizeBoolGlobalVariables()
        {
            NormalizeBoolVariables(M_GlobalVariable?.l_GlobalVariableItems);
            if (Crafts == null) return;
            for (int i = 0; i < Crafts.Count; i++)
            {
                NormalizeBoolVariables(Crafts[i]?.M_GlobalVariable?.l_GlobalVariableItems);
            }
        }

        private static void NormalizeBoolVariables(List<GlobalVariableItem> items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                GlobalVariableItem item = items[i];
                if (item == null || item.DataType != DataTypeEnum.Bool) continue;
                item.Value = item.Value.ToTryBool(false) ? "1" : "0";
                NormalizeBoolDescribe(item);
            }
        }

        /// <summary>
        /// 旧配方的布尔描述可能写着 true/false，统一改成 1=启用、0=禁用。
        /// 已知变量给完整描述；其余只要描述里还有旧布尔字样，也一并改写。
        /// </summary>
        private static void NormalizeBoolDescribe(GlobalVariableItem item)
        {
            switch (item.Name)
            {
                case "带AT":
                    item.Describe = "带AT（1=启用，0=禁用）";
                    return;
                case "PID启用":
                    item.Describe = "1=启用，0=禁用";
                    return;
            }

            if (string.IsNullOrEmpty(item.Describe)) return;
            if (item.Describe.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0
                || item.Describe.IndexOf("false", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                item.Describe = "1=启用，0=禁用";
            }
        }

        /// <summary>
        /// 机型自有的尺寸项运行期排除条件（如五金 V角 关闭）。默认不排除。
        /// AT 开关的排除由共享扩展统一处理，不要在这里重复。
        /// </summary>
        public virtual bool ExcludeSizeCraftAtRuntime(SizeCraftStation_DieCuttingCut item)
        {
            return false;
        }

        /// <summary>
        /// 机型自有的缺陷项运行期排除条件（如五金 V角 关闭）。默认不排除。
        /// </summary>
        public virtual bool ExcludeDefectNameAtRuntime(string name)
        {
            return false;
        }

        /// <summary>
        /// 保证 Crafts 至少有 length 项。
        /// 只增不减：配方里可能已经存了比当前相机数更多的相机遇工艺，删掉等于丢数据。
        /// </summary>
        protected void EnsureCamCraftCount(int length)
        {
            if (Crafts == null) Crafts = new List<CamCraft>();
            while (Crafts.Count < length)
            {
                Crafts.Add(new CamCraft());
            }
        }

        /// <summary>
        /// 按画像顺序取所有尺寸相机的物理下标（激光 3/4/5、五金 2/3/4）。
        ///
        /// 工位3的尺寸项必须用它来装配：写死 3/4/5 的配方，在相机数不同的机器上
        /// 会把尺寸项指向别的相机，而且界面上看不出来。
        /// </summary>
        protected static List<int> SizeCamIndexes(IReadOnlyList<CamSlotDef> slots)
        {
            List<int> indexes = new List<int>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Role == CamRole.尺寸检测) indexes.Add(i);
            }
            return indexes;
        }

        /// <summary>按顺序重排各项 Id（1 开始），界面表格的行号依赖它。</summary>
        protected static void Reindex(List<DefectCraft_DieCuttingCut> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) list[i].Id = i + 1;
        }

        protected static void Reindex(List<SizeCraft_DieCuttingCut> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) list[i].Id = i + 1;
        }

        protected static void Reindex(List<SizeCraftStation_DieCuttingCut> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) list[i].Id = i + 1;
        }

        protected static void Reindex(List<GlobalVariableItem> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) list[i].ID = i + 1;
        }
    }
}