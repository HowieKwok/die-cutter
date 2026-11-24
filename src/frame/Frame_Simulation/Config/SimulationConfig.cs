using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Frame_Simulation.Config
{
    /// <summary>
    /// 仿真参数。与 SystemParam.json 分离：这些是"仿真机"本地的运行参数，
    /// 换机台不该带走，也不该被配方覆盖。
    /// </summary>
    [Serializable]
    public class SimulationConfig
    {
        /// <summary>仿真产能，片/分。帧延时 = 60000 / 产能。</summary>
        public int CapacityPerMinute { get; set; } = 30;

        /// <summary>仿真数据根目录名。空则用 SimTest。</summary>
        public string DataRootName { get; set; } = "SimTest";

        /// <summary>PLC 服务端监听端口。客户端按同一端口连接 127.0.0.1。</summary>
        public int PlcServerPort { get; set; } = 9600;

        /// <summary>单次入队上限，超出按"丢旧留新"处理，避免流程卡死时内存堆积。</summary>
        public int MaxQueueDepth { get; set; } = 3;

        /// <summary>仿真记录保留条数（内存环形）。</summary>
        public int RecorderCapacity { get; set; } = 5000;

        /// <summary>
        /// 相机名 → 触发偏移（毫秒）。语义是<b>固定物理距离</b>：offset = 相机间距 / 线速，常量。
        /// 落后片数 = offset / FrameIntervalMs，随产能变化——这是物理事实，不是错位。
        ///
        /// 为什么按名字而不按相机 ID：实测 ID 是 1/2/8/9/10（持久化 ID，不等于工位顺序），
        /// 按 ID 索引会让上线扫拿到尺寸的偏移、尺寸三路拿到 0，表现就是所有工位同刻出图。
        /// 名字由调试日志核实过：「上线扫 / 下线扫 / 左上 / 左下 / 右下」。
        ///
        /// 未配置的相机按 0 放行并留记录（激光只有一台相机，不需要偏移）；
        /// 静默 0 会让"某台相机漏配"表现成"所有工位同刻出图"，很难反推。
        /// </summary>
        public Dictionary<string, int> TriggerOffsetMs { get; set; } = new Dictionary<string, int>
        {
            { "上线扫", 0 },
            { "下线扫", 500 },
            { "左上", 10000 },
            { "左下", 10000 },
            { "右下", 10000 },
        };

        /// <summary>上线扫（产品进入产线）。每放行一次发一个产品令牌。</summary>
        public List<string> ProductProducerCameras { get; set; } = new List<string> { "上线扫" };

        /// <summary>尺寸工位（产品到达下游）。每个组窗口领一个产品令牌当帧号。</summary>
        public List<string> ProductConsumerCameras { get; set; } = new List<string> { "左上", "左下", "右下" };

        /// <summary>帧延时（毫秒）。产能非法时回退到 200ms，避免 0 延时打满 CPU。</summary>
        [JsonIgnore]
        public int FrameIntervalMs
        {
            get
            {
                if (CapacityPerMinute <= 0) return 200;
                int ms = 60000 / CapacityPerMinute;
                return ms < 10 ? 10 : ms;
            }
        }

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "ZSystemConfigurationFile", "SimulationParam.json");
            }
        }

        public static SimulationConfig Load(string path = null)
        {
            string file = string.IsNullOrEmpty(path) ? DefaultPath : path;
            try
            {
                if (!File.Exists(file)) return new SimulationConfig();
                string json = File.ReadAllText(file);
                SimulationConfig cfg = JsonConvert.DeserializeObject<SimulationConfig>(json);
                return cfg ?? new SimulationConfig();
            }
            catch
            {
                // 配置坏了不能让程序起不来：回退默认值，问题在启动体检里报告。
                return new SimulationConfig();
            }
        }

        public void Save(string path = null)
        {
            string file = string.IsNullOrEmpty(path) ? DefaultPath : path;
            string dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(file, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        /// <summary>启动体检用：把明显不合理的值列出来，交给 StartupChecker 汇总。</summary>
        public IEnumerable<string> Validate(out bool blocking)
        {
            List<string> problems = new List<string>();
            blocking = false;

            if (CapacityPerMinute <= 0)
            {
                problems.Add("仿真产能必须大于 0（当前 " + CapacityPerMinute + "），否则不产图。");
                blocking = true;
            }
            if (PlcServerPort < 1 || PlcServerPort > 65535)
            {
                problems.Add("PLC 仿真端口越界（当前 " + PlcServerPort + "）。");
                blocking = true;
            }
            if (string.IsNullOrWhiteSpace(DataRootName))
            {
                problems.Add("仿真数据目录名为空，将回退 SimTest。");
            }
            return problems;
        }
    }
}