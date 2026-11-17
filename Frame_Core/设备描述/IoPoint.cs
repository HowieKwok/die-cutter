using System;
using System.Collections.Generic;
using System.Linq;

namespace Frame_Core
{
    /// <summary>
    /// 一路运控 IO 的点位定义：位号 + 脉宽 + 是否记日志。
    /// 用途由名字表达（与业务对象上的字段名一致），换机型时只换点位值，不动业务代码。
    /// </summary>
    public sealed class IoPoint
    {
        public IoPoint(string name, int bitNo, int pulseWidthMs, bool saveLog)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("IO 名称不能为空", "name");
            if (bitNo <= 0) throw new ArgumentOutOfRangeException("bitNo", "IO 位号从 1 开始");

            Name = name;
            BitNo = bitNo;
            PulseWidthMs = pulseWidthMs > 0 ? pulseWidthMs : 10;
            SaveLog = saveLog;
        }

        public string Name { get; private set; }

        public int BitNo { get; private set; }

        public int PulseWidthMs { get; private set; }

        public bool SaveLog { get; private set; }
    }

    /// <summary>按名字查 IO 点位；缺项时抛出带可用名称的异常。</summary>
    public sealed class IoPointTable
    {
        private readonly Dictionary<string, IoPoint> points;

        public IoPointTable(IEnumerable<IoPoint> points)
        {
            if (points == null) throw new ArgumentNullException("points");
            this.points = points.ToDictionary(p => p.Name);
        }

        public IoPoint Get(string name)
        {
            IoPoint point;
            if (points.TryGetValue(name, out point)) return point;

            throw new KeyNotFoundException(string.Format(
                "IO 点位表中没有 \"{0}\"。可用点位：{1}",
                name, string.Join("、", points.Keys.ToArray())));
        }
    }
}