using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// 模切类设备的默认运控 IO 点位：裁切/缺陷/尺寸三个工位的心跳与 OK/NG 信号。
    /// 名字与 Server 上的字段名一致，业务代码按名字取点位。
    ///
    /// 激光模切一体机使用本表；五金模切上机器前先沿用同一份，
    /// 上机后在自己的 Profile 里换成新表即可，Server 代码不需要改。
    /// </summary>
    public static class DieCuttingCutIoPoints
    {
        public static readonly IReadOnlyList<IoPoint> All = new[]
        {
            // 心跳只打脉冲，不写日志
            new IoPoint("CutStation_Heartbeat", 1, 10, false),
            new IoPoint("DefectStation_Heartbeat", 2, 10, false),
            new IoPoint("DefectStation_MinorNGSignal", 3, 10, true),
            new IoPoint("DefectStation_SevereNGSignal", 4, 10, true),
            new IoPoint("DefectStation_OKSignal", 5, 10, true),
            new IoPoint("DefectStation_ContinuousNGSignal", 6, 10, true),
            new IoPoint("SizeStation_Heartbeat", 7, 10, false),
            new IoPoint("SizeStation_NGSignal", 8, 10, true),
            new IoPoint("SizeStation_OKSignal", 9, 10, true),
            new IoPoint("SizeStation_ContinuousNGSignal", 10, 10, true),
        };
    }
}