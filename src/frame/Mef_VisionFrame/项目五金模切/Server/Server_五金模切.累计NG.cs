using Frame_Core;
using LogTool;
using System.Collections.Generic;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 逐项累计 NG（按片）：每个检测项各自计数、各自阈值（工艺里的"累计NG阈值"）。
    ///
    /// 与连续 NG 的区别：
    ///   连续 NG：中间来一片 OK 就清零，计的是"连着几片"；
    ///   累计 NG：中间来 OK 不清零，计的是"本次清零周期内一共几片"。
    ///
    /// 计数单位是"片"：同一片同一检测项最多 +1，同一片出现多处的同类缺陷也只记一次。
    /// 外观工位按 A/B 合片后的一片算；尺寸工位按三路到齐后的一片算。
    ///
    /// 达到阈值后按工艺勾选走 TJBJInfo 报警通道：
    ///   勾了"累计NG停机"写停机；只勾"累计NG报警"写报警；两个都不勾则该项不监视。
    /// 触发后计数清零，换卷/复位/清数据也清零。
    /// </summary>
    public partial class Server_五金模切
    {
        /// <summary>外观工位缺陷累计计数：键 = 检测项名。</summary>
        private readonly Dictionary<string, int> _station1DefectCumulativeNgCounts =
            new Dictionary<string, int>();

        /// <summary>尺寸工位缺陷累计计数：键 = 检测项名。</summary>
        private readonly Dictionary<string, int> _station3DefectCumulativeNgCounts =
            new Dictionary<string, int>();

        /// <summary>尺寸工位尺寸项累计计数：键 = 尺寸项名。</summary>
        private readonly Dictionary<string, int> _station3SizeCumulativeNgCounts =
            new Dictionary<string, int>();

        private readonly object _cumulativeNgLock = new object();

        /// <summary>
        /// 外观工位按片累计：调用方传入这一片 A/B 两面全部 NG 的缺陷类名。
        /// station1 = true 取工位1的缺陷工艺，false 取工位3的缺陷工艺。
        /// </summary>
        private void CountCumulativeNgForDefect(Dictionary<string, int> counts,
            List<string> ngNames, bool station1, string source)
        {
            CraftParam_五金模切 craft = Data.Craft;
            if (craft == null || counts == null || ngNames == null) return;

            lock (_cumulativeNgLock)
            {
                foreach (string name in DistinctNgNames(ngNames))
                {
                    DefectCraft_DieCuttingCut item = station1
                        ? craft.Get_Station1_DefectCrafts(name)
                        : craft.Get_Station3_DefectCrafts(name);
                    if (item == null) continue;

                    CountOneCumulativeNg(counts, name, item.AccumulateCount,
                        item.BJ_Accumulate, item.TJ_Accumulate, source);
                }
            }
        }

        /// <summary>尺寸工位按片累计：调用方传入这一片全部 NG 的尺寸项名。</summary>
        private void CountCumulativeNgForSize(List<string> ngNames, string source)
        {
            CraftParam_五金模切 craft = Data.Craft;
            if (craft == null || ngNames == null) return;

            List<SizeCraftStation_DieCuttingCut> items = craft.Get_Station3_SizeCrafts_Runtime();

            lock (_cumulativeNgLock)
            {
                foreach (string name in DistinctNgNames(ngNames))
                {
                    SizeCraftStation_DieCuttingCut item = items.Find(p => p.Name == name);
                    if (item == null) continue;

                    CountOneCumulativeNg(_station3SizeCumulativeNgCounts, name, item.AccumulateCount,
                        item.BJ_Accumulate, item.TJ_Accumulate, source);
                }
            }
        }

        /// <summary>
        /// 计一次并判断是否触发。阈值 ≤ 0 或报警/停机都没勾选时，该项不监视。
        /// 触发后计数归零，重新累计。
        /// </summary>
        private void CountOneCumulativeNg(Dictionary<string, int> counts, string name,
            int threshold, bool alarmEnabled, bool stopEnabled, string source)
        {
            if (threshold <= 0 || (!alarmEnabled && !stopEnabled)) return;

            int count;
            counts.TryGetValue(name, out count);
            count++;

            if (count < threshold)
            {
                counts[name] = count;
                return;
            }

            counts[name] = 0;

            string detail = $"{source}-{name}_累计：当前个数{count} > 设定个数{threshold}";
            if (stopEnabled)
                TJBJInfo.AlarmWriteState(AlarmName.检测项报警停机, StopAlarmStateEnum.停机, detail);
            else
                TJBJInfo.AlarmWriteState(AlarmName.检测项报警停机, StopAlarmStateEnum.报警, detail);

            AppLog.Warn($"五金模切累计NG触发：{detail}");
        }

        /// <summary>取一片产品里所有 NG 缺陷的类名；A/B 两面各自调用后放在同一个列表里。</summary>
        private static void CollectNgDefectNames(List<DefectSummary> summaries, List<string> names)
        {
            if (summaries == null || names == null) return;

            foreach (DefectSummary item in summaries)
            {
                if (item == null || !item.T_IsNG || string.IsNullOrEmpty(item.T_Name)) continue;
                names.Add(item.T_Name);
            }
        }

        /// <summary>同一片里同名缺陷只计一次。</summary>
        private static IEnumerable<string> DistinctNgNames(List<string> names)
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (seen.Add(name)) yield return name;
            }
        }

        /// <summary>换卷 / 复位 / 清数据：累计计数跨卷没有意义。</summary>
        private void ClearCumulativeNgCounts()
        {
            lock (_cumulativeNgLock)
            {
                _station1DefectCumulativeNgCounts.Clear();
                _station3DefectCumulativeNgCounts.Clear();
                _station3SizeCumulativeNgCounts.Clear();
            }
        }
    }
}
