using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Frame_Core
{
    /// <summary>
    /// 模切类配方（CraftParam_DieCutBase）的只读查询与格式转换。
    /// 接收基类类型，激光模切与五金模切共用同一套查询方法，机型差异只体现在 Init 的装配上。
    /// </summary>
    public static class CraftParam_DieCutBaseExtension
    {
        public static List<string> Get_Cam_SizeNames(this CraftParam_DieCutBase Crafts, int camIndex)
        {
            if (Crafts == null)
            {
                return new List<string>();
            }
            if (Crafts.Crafts == null || camIndex < 0 || camIndex >= Crafts.Crafts.Count)
            {
                return new List<string>();
            }
            return Crafts.Crafts[camIndex].l_SizeCraft?.Select(p => p.Name).ToList() ?? new List<string>();
        }
        public static List<string> Get_Cam_DefectNames(this CraftParam_DieCutBase Crafts, int camIndex)
        {
            if (Crafts == null)
            {
                return new List<string>();
            }
            if (Crafts.Crafts == null || camIndex < 0 || camIndex >= Crafts.Crafts.Count)
            {
                return new List<string>();
            }
            return Crafts.Crafts[camIndex].l_DefectCraft?.Select(p => p.Name).ToList() ?? new List<string>();
        }

        public static List<string> Get_Station3_SizeNames(this CraftParam_DieCutBase Crafts)
        {
            if (Crafts == null)
            {
                return new List<string>();
            }
            if (Crafts.l_SizeCraftStation_3 == null)
            {
                return new List<string>();
            }
            return Crafts.l_SizeCraftStation_3?.Select(p => p.Name).ToList() ?? new List<string>();
        }
        public static List<string> Get_Station1_DefectNames(this CraftParam_DieCutBase Crafts)
        {
            if (Crafts == null)
            {
                return new List<string>();
            }
            if (Crafts.l_DefectCraftStation_1 == null)
            {
                return new List<string>();
            }
            return Crafts.l_DefectCraftStation_1?.Select(p => p.Name).ToList() ?? new List<string>();
        }

        public static List<string> Get_Station3_DefectNames(this CraftParam_DieCutBase Crafts)
        {
            if (Crafts == null)
            {
                return new List<string>();
            }
            if (Crafts.l_DefectCraftStation_3 == null)
            {
                return new List<string>();
            }
            return Crafts.l_DefectCraftStation_3?.Select(p => p.Name).ToList() ?? new List<string>();
        }

        public static string Get_SizeNames(this List<SizeCraft_DieCuttingCut>  sizeCraft_DieCuttingCuts, int index)
        {

            if (sizeCraft_DieCuttingCuts == null || index < 0 || index >= sizeCraft_DieCuttingCuts.Count)
            {
                return "";
            }

            return sizeCraft_DieCuttingCuts[index].Name;
        }

        public static string Get_SizeNames(this List<SizeCraftStation_DieCuttingCut> sizeCraftStation_DieCuttingCuts, int index)
        {

            if (sizeCraftStation_DieCuttingCuts == null || index < 0 || index >= sizeCraftStation_DieCuttingCuts.Count)
            {
                return "";
            }

            return sizeCraftStation_DieCuttingCuts[index].Name;
        }

        public static string Get_DefectNames(this List<DefectCraft_DieCuttingCut>  defectCraft_DieCuttingCuts, int index)
        {

            if (defectCraft_DieCuttingCuts == null || index < 0 || index >= defectCraft_DieCuttingCuts.Count)
            {
                return "";
            }

            return defectCraft_DieCuttingCuts[index].Name;
        }



        public static List<SizeInfo> Get_Cam_SizeDefaultInfo(this CraftParam_DieCutBase Crafts, int camIndex)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.Crafts == null || camIndex < 0 || camIndex >= Crafts.Crafts.Count)
            {
                return null;
            }
            return Crafts.Crafts[camIndex].l_SizeCraft.Select(item =>
            {
                return item.SizeCraft_DieCuttingCutToSizeInfo();

            }).ToList();
        }
        public static SizeInfo SizeCraft_DieCuttingCutToSizeInfo(this SizeCraft_DieCuttingCut sizeCraft_DieCuttingCut)
        {
            SizeInfo sizeInfo = new SizeInfo();
            sizeInfo.Name = sizeCraft_DieCuttingCut.Name;
            sizeInfo.Level = sizeCraft_DieCuttingCut.Id;
            sizeInfo.StandardValue = sizeCraft_DieCuttingCut.StandardValue;
            sizeInfo.TolUpper = sizeCraft_DieCuttingCut.DBTolUpper;
            sizeInfo.TolLower = sizeCraft_DieCuttingCut.DBTolLower;
            sizeInfo.upperLimitValue = sizeInfo.StandardValue + sizeInfo.TolUpper;
            sizeInfo.lowerLimitValue = sizeInfo.StandardValue - sizeInfo.TolLower;
            return sizeInfo;
        }
        public static SizeInfo SizeCraftStation_DieCuttingCutToSizeInfo(this SizeCraftStation_DieCuttingCut sizeCraft_DieCuttingCut)
        {
            SizeInfo sizeInfo = new SizeInfo();
            sizeInfo.Name = sizeCraft_DieCuttingCut.Name;
            sizeInfo.Level = sizeCraft_DieCuttingCut.Id;
            sizeInfo.StandardValue = sizeCraft_DieCuttingCut.StandardValue;
            sizeInfo.TolUpper = sizeCraft_DieCuttingCut.DBTolUpper;
            sizeInfo.TolLower = sizeCraft_DieCuttingCut.DBTolLower;
            sizeInfo.upperLimitValue = sizeInfo.StandardValue + sizeInfo.TolUpper;
            sizeInfo.lowerLimitValue = sizeInfo.StandardValue - sizeInfo.TolLower;
            return sizeInfo;
        }

        public static List<SizeInfo> Get_Cam_SizeDefaultInfo(this CraftParam_DieCutBase Crafts)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.l_SizeCraftStation_3 == null)
            {
                return null;
            }
            return Crafts.l_SizeCraftStation_3.Select(item =>
            {
                return item.SizeCraftStation_DieCuttingCutToSizeInfo();

            }).ToList();
        }



        public static List<SizeCraft_DieCuttingCut> Get_Cam_SizeCrafts(this CraftParam_DieCutBase Crafts, int camIndex)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.Crafts == null || camIndex < 0 || camIndex >= Crafts.Crafts.Count)
            {
                return null;
            }
            return Crafts.Crafts[camIndex].l_SizeCraft;
        }



        public static List<DefectCraft_DieCuttingCut> Get_Cam_DefectCrafts(this CraftParam_DieCutBase Crafts, int camIndex)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.Crafts == null || camIndex < 0 || camIndex >= Crafts.Crafts.Count)
            {
                return null;
            }
            return Crafts.Crafts[camIndex].l_DefectCraft;
        }

        public static SizeCraft_DieCuttingCut Get_Cam_SizeCraft(this CraftParam_DieCutBase Crafts, int camIndex, string name)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.Crafts == null || camIndex < 0 || camIndex >= Crafts.Crafts.Count)
            {
                return null;
            }
            return Crafts.Crafts[camIndex].l_SizeCraft?.Find(p => p.Name == name);
        }

        public static DefectCraft_DieCuttingCut Get_Cam_DefectCraft(this CraftParam_DieCutBase Crafts, int camIndex, string name)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.Crafts == null || camIndex < 0 || camIndex >= Crafts.Crafts.Count)
            {
                return null;
            }
            return Crafts.Crafts[camIndex].l_DefectCraft?.Find(p => p.Name == name);
        }

        public static DefectCraft_DieCuttingCut Get_Station1_DefectCrafts(this CraftParam_DieCutBase Crafts, string name)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.l_DefectCraftStation_1 == null)
            {
                return null;
            }
            return Crafts.l_DefectCraftStation_1.Find(p => p.Name == name);
        }


        public static List<SizeCraftStation_DieCuttingCut> Get_Station3_SizeCrafts(this CraftParam_DieCutBase Crafts)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.l_SizeCraftStation_3 == null)
            {
                return null;
            }
            return Crafts.l_SizeCraftStation_3;
        }

        public static List<SizeCraftStation_DieCuttingCut> Get_Station3_SizeCrafts(this CraftParam_DieCutBase Crafts, int camIndex)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.l_SizeCraftStation_3 == null)
            {
                return null;
            }
            return Crafts.l_SizeCraftStation_3.FindAll(p => p.CameraId1 == camIndex && p.CameraId2 == camIndex);
        }

        public static List<SizeCraftStation_DieCuttingCut> Get_Station3_SizeCrafts_CamDifferent(this CraftParam_DieCutBase Crafts)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.l_SizeCraftStation_3 == null)
            {
                return null;
            }
            return Crafts.l_SizeCraftStation_3.FindAll(p => p.CameraId1 != p.CameraId2);
        }

        public static SizeCraftStation_DieCuttingCut Get_Station3_SizeCraft(this CraftParam_DieCutBase Crafts, string name)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.l_SizeCraftStation_3 == null)
            {
                return null;
            }
            return Crafts.l_SizeCraftStation_3?.Find(p => p.Name == name);
        }

        public static List<DefectCraft_DieCuttingCut> Get_Station3_DefectCrafts(this CraftParam_DieCutBase Crafts)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.l_DefectCraftStation_3 == null)
            {
                return null;
            }
            return Crafts.l_DefectCraftStation_3;
        }

        public static DefectCraft_DieCuttingCut Get_Station3_DefectCrafts(this CraftParam_DieCutBase Crafts, string name)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.l_DefectCraftStation_3 == null)
            {
                return null;
            }
            return Crafts.l_DefectCraftStation_3.Find(p => p.Name == name);
        }

        #region AT 运行期过滤

        /// <summary>尺寸项数据源中代表 AT 抓边模块的名字片段。</summary>
        private const string AT抓边标识 = "极片AT抓边";

        /// <summary>AT 尺寸项名：数据源被改成别的名字时仍能兜底排除。</summary>
        private const string AT尺寸项名 = "带料高";

        /// <summary>AT 缺陷项名，必须与流程 emit 的 ClassName 逐字一致。</summary>
        private const string AT缺陷项名 = "AT漏金属";

        /// <summary>尺寸项是否依赖 AT 抓边：项名命中，或数据源任一端含 AT 抓边模块。</summary>
        private static bool IsATSizeCraft(SizeCraftStation_DieCuttingCut item)
        {
            if (item == null) return false;
            if (item.Name == AT尺寸项名) return true;
            return IsAT数据源(item.DataSource1) || IsAT数据源(item.DataSource2);
        }

        private static bool IsAT数据源(string dataSource)
        {
            return !string.IsNullOrEmpty(dataSource)
                && dataSource.IndexOf(AT抓边标识, StringComparison.Ordinal) >= 0;
        }

        /// <summary>尺寸项运行期排除：AT 关闭时排除依赖项，或机型自有条件（V角）。</summary>
        private static bool ShouldExcludeSizeCraft(CraftParam_DieCutBase crafts, SizeCraftStation_DieCuttingCut item)
        {
            if (item == null) return true;
            if (!crafts.IsAT && IsATSizeCraft(item)) return true;
            return crafts.ExcludeSizeCraftAtRuntime(item);
        }

        /// <summary>缺陷项运行期排除：AT 关闭时的 AT 漏金属，或机型自有条件（V角）。</summary>
        private static bool ShouldExcludeDefectName(CraftParam_DieCutBase crafts, string name)
        {
            if (!crafts.IsAT && name == AT缺陷项名) return true;
            return crafts.ExcludeDefectNameAtRuntime(name);
        }

        /// <summary>
        /// 运行期尺寸项：在原始列表上叠加 AT 与机型排除条件。
        /// 无排除时不分配，直接返回原列表引用；调用方只读，不得原地修改。
        /// </summary>
        public static List<SizeCraftStation_DieCuttingCut> Get_Station3_SizeCrafts_Runtime(this CraftParam_DieCutBase Crafts)
        {
            if (Crafts == null || Crafts.l_SizeCraftStation_3 == null)
            {
                return new List<SizeCraftStation_DieCuttingCut>();
            }

            List<SizeCraftStation_DieCuttingCut> all = Crafts.l_SizeCraftStation_3;
            List<SizeCraftStation_DieCuttingCut> result = null;
            for (int i = 0; i < all.Count; i++)
            {
                SizeCraftStation_DieCuttingCut item = all[i];
                if (ShouldExcludeSizeCraft(Crafts, item))
                {
                    // 首次命中才复制前缀，无过滤时零分配
                    if (result == null)
                    {
                        result = new List<SizeCraftStation_DieCuttingCut>(all.Count);
                        for (int j = 0; j < i; j++) result.Add(all[j]);
                    }
                    continue;
                }
                result?.Add(item);
            }
            return result ?? all;
        }

        /// <summary>运行期单路尺寸项（CameraId1 == CameraId2 == camIndex）。</summary>
        public static List<SizeCraftStation_DieCuttingCut> Get_Station3_SizeCrafts_Runtime(this CraftParam_DieCutBase Crafts, int camIndex)
        {
            List<SizeCraftStation_DieCuttingCut> all = Get_Station3_SizeCrafts_Runtime(Crafts);
            List<SizeCraftStation_DieCuttingCut> result = new List<SizeCraftStation_DieCuttingCut>();
            for (int i = 0; i < all.Count; i++)
            {
                SizeCraftStation_DieCuttingCut item = all[i];
                if (item.CameraId1 == camIndex && item.CameraId2 == camIndex) result.Add(item);
            }
            return result;
        }

        /// <summary>运行期跨相机尺寸项（CameraId1 != CameraId2）。</summary>
        public static List<SizeCraftStation_DieCuttingCut> Get_Station3_SizeCrafts_CamDifferent_Runtime(this CraftParam_DieCutBase Crafts)
        {
            List<SizeCraftStation_DieCuttingCut> all = Get_Station3_SizeCrafts_Runtime(Crafts);
            List<SizeCraftStation_DieCuttingCut> result = new List<SizeCraftStation_DieCuttingCut>();
            for (int i = 0; i < all.Count; i++)
            {
                SizeCraftStation_DieCuttingCut item = all[i];
                if (item.CameraId1 != item.CameraId2) result.Add(item);
            }
            return result;
        }

        /// <summary>运行期尺寸项名，顺序与 Get_Station3_SizeCrafts_Runtime() 一致。</summary>
        public static List<string> Get_Station3_SizeNames_Runtime(this CraftParam_DieCutBase Crafts)
        {
            List<SizeCraftStation_DieCuttingCut> items = Get_Station3_SizeCrafts_Runtime(Crafts);
            List<string> names = new List<string>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null) names.Add(items[i].Name);
            }
            return names;
        }

        /// <summary>运行期工位3缺陷项名（按 AT/机型条件剔除，供统计与显示）。</summary>
        public static List<string> Get_Station3_DefectNames_Runtime(this CraftParam_DieCutBase Crafts)
        {
            if (Crafts == null || Crafts.l_DefectCraftStation_3 == null)
            {
                return new List<string>();
            }

            List<DefectCraft_DieCuttingCut> all = Crafts.l_DefectCraftStation_3;
            List<string> names = new List<string>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                DefectCraft_DieCuttingCut item = all[i];
                if (item == null || string.IsNullOrEmpty(item.Name)) continue;
                if (ShouldExcludeDefectName(Crafts, item.Name)) continue;
                names.Add(item.Name);
            }
            return names;
        }

        /// <summary>运行期工位1缺陷项名（AT 关闭时剔除 AT 漏金属）。</summary>
        public static List<string> Get_Station1_DefectNames_Runtime(this CraftParam_DieCutBase Crafts)
        {
            if (Crafts == null || Crafts.l_DefectCraftStation_1 == null)
            {
                return new List<string>();
            }

            List<DefectCraft_DieCuttingCut> all = Crafts.l_DefectCraftStation_1;
            List<string> names = new List<string>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                DefectCraft_DieCuttingCut item = all[i];
                if (item == null || string.IsNullOrEmpty(item.Name)) continue;
                if (ShouldExcludeDefectName(Crafts, item.Name)) continue;
                names.Add(item.Name);
            }
            return names;
        }

        #endregion

        public static GlobalVariableItem Get_GlobalVariableItem(this CraftParam_DieCutBase Crafts, int camIndex, string name)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.Crafts == null || camIndex < 0 || camIndex >= Crafts.Crafts.Count)
            {
                return null;
            }
            return Crafts.Crafts[camIndex].M_GlobalVariable?.l_GlobalVariableItems?.Find(p => p.Name == name);
        }

        public static GlobalVariableItem Get_GlobalVariableItem(this CraftParam_DieCutBase Crafts, string name)
        {
            if (Crafts == null)
            {
                return null;
            }
            if (Crafts.M_GlobalVariable == null || Crafts.M_GlobalVariable.l_GlobalVariableItems == null)
            {
                return null;
            }
            return Crafts.M_GlobalVariable.l_GlobalVariableItems?.Find(p => p.Name == name);
        }

        public static double ToTryDouble(this string strType, double defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(strType))
                return defaultValue;

            return double.TryParse(strType, out double result)
              ? result
              : defaultValue;
        }

        public static double ToDouble(this string strType, double defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(strType))
                return defaultValue;

            return double.Parse(strType);
        }

        public static bool ToTryBool(this string strType, bool defaultValue = false)
        {
            if (string.IsNullOrWhiteSpace(strType))
                return defaultValue;

            // 配方布尔值统一用 1/0；同时兼容旧配方的 true/false
            if (strType == "1") return true;
            if (strType == "0") return false;

            return bool.TryParse(strType, out bool result)
              ? result
              : defaultValue;
        }

        public static bool ToBool(this string strType, bool defaultValue = false)
        {
            if (string.IsNullOrWhiteSpace(strType))
                return defaultValue;

            if (strType == "1") return true;
            if (strType == "0") return false;

            return bool.Parse(strType);
        }

        public static int ToTryInt(this string strType, int defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(strType))
                return defaultValue;

            return int.TryParse(strType, out int result)
              ? result
              : defaultValue;
        }

        public static int ToInt(this string strType, int defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(strType))
                return defaultValue;

            return int.Parse(strType);
        }
    }
}
