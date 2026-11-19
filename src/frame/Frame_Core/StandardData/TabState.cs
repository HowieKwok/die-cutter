using System;

namespace Frame_Core
{
    /// <summary>
    /// 极耳状态统一入口。
    /// 当前未接入查找极耳算法，默认“有极耳”；
    /// 后续接入算法后只改此处的默认值或解析逻辑。
    /// </summary>
    public static class TabState
    {
        /// <summary>
        /// 全工程唯一的极耳默认值。未检测（null）时统一回落到这里。
        /// </summary>
        public const bool DefaultHasTab = true;

        /// <summary>
        /// 纠偏值/极耳补偿的中性值。沿用原 Reset_CutStation 的 0.001，
        /// 避免 PLC 把 0 当作“无数据”。全工程唯一出处。
        /// </summary>
        public const float NeutralCorrection = 0.001f;

        /// <summary>
        /// 未检测（null）时回落到默认值。
        /// </summary>
        public static bool Resolve(bool? hasTab)
        {
            return hasTab ?? DefaultHasTab;
        }

        /// <summary>
        /// 未检测（null 结果）时回落到默认值。
        /// </summary>
        public static bool Resolve(TabDetectResult result)
        {
            return result?.HasTab ?? DefaultHasTab;
        }

        /// <summary>
        /// 按 EA 在拼接图中的行范围判断本片是否有极耳。
        /// 未检测时返回默认值；检测到但没有极耳返回 false。
        /// </summary>
        public static bool GetHasTab(TabDetectResult result, double topRow, double bottomRow)
        {
            if (result == null || !result.IsDetected) return DefaultHasTab;
            if (result.Tabs == null || result.Tabs.Count == 0) return result.HasTab;

            for (int i = 0; i < result.Tabs.Count; i++)
            {
                PoleEarsInfo tab = result.Tabs[i];
                if (tab == null) continue;
                if (tab.Row_Mid >= topRow && tab.Row_Mid <= bottomRow) return true;
            }
            return false;
        }

        /// <summary>
        /// PLC 有无极耳：1=有，2=无。只在这里出现 1/2。
        /// </summary>
        public static float ToPlcValue(bool hasTab)
        {
            return hasTab ? 1f : 2f;
        }
    }
}
