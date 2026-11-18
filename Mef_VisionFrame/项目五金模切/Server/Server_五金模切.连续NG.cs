using System.Collections.Generic;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 逐项连续 NG：每个检测项各自计数、各自阈值（工艺里的"连续数量"），
    /// 达到阈值就立刻把对应线别的"多次NG"信号置 1，2 秒后由信号层复位。
    ///
    /// 为什么不是工位级一个计数：连续 NG 的工艺含义是"同一个检测项连续 N 片不合格"。
    /// 工位级计数会把"这一片 A 项 NG、下一片 B 项 NG"也算成连续，PLC 拿到的是假报警。
    ///
    /// 为什么上线/下线各一套计数：PLC 上是两个独立点位，
    /// A 面满阈值不能把 B 面的信号带起来。
    /// </summary>
    public partial class Server_五金模切
    {
        /// <summary>
        /// 外观缺陷：键 = 相机下标 + 类名。
        /// A/B 两面是同一片的两个面，同名缺陷必须各算各的，否则会互相清零永远到不了阈值。
        /// </summary>
        private readonly Dictionary<int, Dictionary<string, int>> _defectConsecutiveCounts =
            new Dictionary<int, Dictionary<string, int>>();

        /// <summary>
        /// 尺寸项：键 = 项名。一个尺寸项只属于一路相机（跨相机项也只有一个名字），
        /// 每片只判一次，不存在两面问题。
        /// </summary>
        private readonly Dictionary<string, int> _sizeConsecutiveCounts = new Dictionary<string, int>();

        private readonly object _consecutiveLock = new object();

        /// <summary>
        /// 缺陷帧开始：这一帧没被检出的类就是 OK，计数归零。
        /// 必须在取缺陷结果之前调用——一帧里没有缺陷时根本走不到逐项判定，
        /// 放在判定里清零会让"连续 NG 之后来一帧全 OK"的计数永远清不掉。
        /// </summary>
        private void ClearDefectFrameCounts(int camIndex)
        {
            lock (_consecutiveLock)
            {
                Dictionary<string, int> table = GetDefectCountTable(camIndex);
                foreach (string key in new List<string>(table.Keys)) table[key] = 0;
            }
        }

        /// <summary>记一次缺陷项的判定结果；返回 true 表示这一片该项满阈值，需要置多次NG信号。</summary>
        private bool RecordDefectConsecutiveNg(int camIndex, string name, bool isNG, int threshold)
        {
            lock (_consecutiveLock)
            {
                Dictionary<string, int> table = GetDefectCountTable(camIndex);

                if (threshold <= 0) { table.Remove(name); return false; }   // 连续数量填 0 = 该项不监视
                if (!isNG) { table[name] = 0; return false; }

                int count;
                table.TryGetValue(name, out count);
                count++;
                table[name] = count;

                if (count < threshold) return false;

                table[name] = 0;   // 满阈值报一次，然后重新计数
                return true;
            }
        }

        /// <summary>记一次尺寸项的判定结果；返回 true 表示这一片该项满阈值。</summary>
        private bool RecordSizeConsecutiveNg(string name, bool isNG, int threshold)
        {
            lock (_consecutiveLock)
            {
                if (threshold <= 0) { _sizeConsecutiveCounts.Remove(name); return false; }
                if (!isNG) { _sizeConsecutiveCounts[name] = 0; return false; }

                int count;
                _sizeConsecutiveCounts.TryGetValue(name, out count);
                count++;
                _sizeConsecutiveCounts[name] = count;

                if (count < threshold) return false;

                _sizeConsecutiveCounts[name] = 0;
                return true;
            }
        }

        private Dictionary<string, int> GetDefectCountTable(int camIndex)
        {
            Dictionary<string, int> table;
            if (!_defectConsecutiveCounts.TryGetValue(camIndex, out table))
            {
                table = new Dictionary<string, int>();
                _defectConsecutiveCounts[camIndex] = table;
            }
            return table;
        }

        /// <summary>换卷 / 复位 / 清数据时清空：连续计数跨卷没有意义。</summary>
        private void ClearConsecutiveCounts()
        {
            lock (_consecutiveLock)
            {
                _defectConsecutiveCounts.Clear();
                _sizeConsecutiveCounts.Clear();
            }
        }
    }
}