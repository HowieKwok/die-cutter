using System;
using System.IO;

namespace Frame_Core
{
    /// <summary>
    /// 可配置的落盘根路径。由“系统设置 → 路径设置”维护，
    /// 保存后立即应用到 ParamPathSet，不需要重启软件。
    ///
    /// 日志目录不在这里：它由既有的 LogParam 维护，避免两套配置各写一半。
    /// </summary>
    [Serializable]
    public class PathParameter : FrameCommunity.DataOperation.JsonSerializationOperation
    {
        /// <summary>生产数据根（膜卷目录的父目录）。</summary>
        public string DataRoot { get; set; } = "";

        /// <summary>标定根目录。</summary>
        public string CalibrationRoot { get; set; } = "";

        /// <summary>点检数据根（点检记录与按日期的点检数据）。</summary>
        public string PointCheckRoot { get; set; } = "";

        /// <summary>点检图像根（尺寸点检图 / 缺陷点检图）。</summary>
        public string PointCheckImageRoot { get; set; } = "";

        /// <summary>MES 文件根。预留：当前版本没有 MES 落盘实现。</summary>
        public string MesRoot { get; set; } = "";

        /// <summary>
        /// 首次生成或字段为空时补默认值：全部从程序目录与生产数据根派生。
        /// ReadExtension 会在 Init 之后落盘，现场能看到完整默认配置。
        /// </summary>
        public override void Init()
        {
            base.Init();
            // 统一走 Normalize：空值与非法值都回退到默认派生路径，
            // 避免坏配置在 Path.Combine 里抛异常把启动打挂。
            Normalize();
        }

        /// <summary>
        /// 规范化：去空格、绝对化、统一补尾部反斜杠。
        /// 坏值（非法字符、无法解析）退回默认值，避免手工改坏配置后启动即崩。
        /// </summary>
        public void Normalize()
        {
            DataRoot = NormalizeRoot(DataRoot, DefaultDataRoot());
            CalibrationRoot = NormalizeRoot(CalibrationRoot, Path.Combine(DataRoot, "标定"));
            PointCheckRoot = NormalizeRoot(PointCheckRoot, Path.Combine(DataRoot, "点检数据"));
            PointCheckImageRoot = NormalizeRoot(PointCheckImageRoot, Path.Combine(DataRoot, "点检图像"));
            MesRoot = NormalizeRoot(MesRoot, Path.Combine(DataRoot, "MES文件"));
        }

        private static string NormalizeRoot(string path, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                try { return Path.GetFullPath(path.Trim()).TrimEnd('\\', '/') + "\\"; }
                catch { }
            }
            return WithSlash(fallback);
        }

        /// <summary>生产数据根的默认值：程序目录下的“生产数据”。</summary>
        public static string DefaultDataRoot()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "生产数据");
        }

        /// <summary>去空格、绝对化并补尾部反斜杠；空值或非法路径返回空串。</summary>
        public static string WithSlash(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path.Trim()).TrimEnd('\\', '/') + "\\"; }
            catch { return ""; }
        }

        /// <summary>日志目录的默认值。日志参数由 LogParam 单独保存。</summary>
        public static string DefaultLogPath()
        {
            return WithSlash(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "日志文件"));
        }
    }
}
