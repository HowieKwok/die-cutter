using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Frame_Simulation.Config
{
    public enum SimCheckLevel
    {
        /// <summary>阻断：仿真跑不起来，必须修完才能进入主界面。</summary>
        Blocking = 0,
        /// <summary>警告：能跑但结果可能不符合预期。</summary>
        Warning = 1,
        /// <summary>提示：只是告知，例如某项在仿真下被跳过。</summary>
        Info = 2
    }

    public class SimCheckEntry
    {
        public SimCheckLevel Level { get; set; }
        public string Item { get; set; }
        public string Message { get; set; }

        public string ToLine()
        {
            string tag = Level == SimCheckLevel.Blocking ? "阻断"
                       : Level == SimCheckLevel.Warning ? "警告" : "提示";
            return string.Format("[{0}] {1}：{2}", tag, Item, Message);
        }
    }

    /// <summary>某一台相机在仿真下的可见信息。由宿主从相机配置映射进来，检查器不认识框架类型。</summary>
    public class SimCameraCheckItem
    {
        public string Name { get; set; }
        public string ImageDir { get; set; }
        public int ImageCount { get; set; }
    }

    /// <summary>启动体检的输入。全部是纯数据，检查器不引用 Frame_Core / DevMgmt.Cameras。</summary>
    public class SimCheckInput
    {
        public List<SimCameraCheckItem> Cameras { get; set; }
        public int PlcPort { get; set; }
        /// <summary>光源控制器是否已配置（仿真下不模拟，仅检查配置存在）。</summary>
        public bool LightConfigured { get; set; }
        /// <summary>运动控制卡是否已配置（仿真下不模拟，仅检查配置存在）。</summary>
        public bool MotionCardConfigured { get; set; }
        /// <summary>数据根目录是否可写。</summary>
        public string DataRoot { get; set; }

        public SimCheckInput()
        {
            Cameras = new List<SimCameraCheckItem>();
        }
    }

    public class SimCheckResult
    {
        public List<SimCheckEntry> Entries { get; private set; }

        public SimCheckResult()
        {
            Entries = new List<SimCheckEntry>();
        }

        public bool HasBlocking
        {
            get { return Entries.Any(e => e.Level == SimCheckLevel.Blocking); }
        }

        public void Add(SimCheckLevel level, string item, string message)
        {
            Entries.Add(new SimCheckEntry { Level = level, Item = item, Message = message });
        }

        public string Report()
        {
            if (Entries.Count == 0) return "仿真启动体检通过。";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(HasBlocking ? "仿真启动体检未通过：" : "仿真启动体检通过（含告警）：");
            foreach (SimCheckEntry e in Entries.OrderBy(e => (int)e.Level)) sb.AppendLine("  " + e.ToLine());
            return sb.ToString();
        }
    }

    /// <summary>
    /// 启动体检。仿真的价值一半在"能跑"，另一半在"跑错时能立刻说清哪里错"，
    /// 所以这里把问题分成阻断/警告/提示三级，而不是笼统一个 MessageBox。
    /// </summary>
    public static class StartupChecker
    {
        public static SimCheckResult Check(SimulationConfig config, SimCheckInput input)
        {
            SimCheckResult result = new SimCheckResult();
            if (input == null) input = new SimCheckInput();

            CheckConfig(config, result);
            CheckCameras(input, result);
            CheckPlc(input, result);
            CheckPeripherals(input, result);
            CheckDataRoot(input, result);

            return result;
        }

        private static void CheckConfig(SimulationConfig config, SimCheckResult result)
        {
            if (config == null)
            {
                result.Add(SimCheckLevel.Blocking, "仿真配置", "配置为空，无法启动。");
                return;
            }

            bool blocking;
            foreach (string problem in config.Validate(out blocking))
            {
                result.Add(blocking ? SimCheckLevel.Blocking : SimCheckLevel.Warning, "仿真配置", problem);
            }

            if (config.CapacityPerMinute > 0)
            {
                result.Add(SimCheckLevel.Info, "节拍",
                    string.Format("产能 {0} 片/分 → 每 {1} ms 取一张图。", config.CapacityPerMinute, config.FrameIntervalMs));
            }
        }

        private static void CheckCameras(SimCheckInput input, SimCheckResult result)
        {
            const string howTo = "请在『相机设置』里重新选择仿真图片目录，保存后即可开始仿真。";

            if (input.Cameras == null || input.Cameras.Count == 0)
            {
                result.Add(SimCheckLevel.Blocking, "相机", "没有任何相机配置，仿真无法产图。");
                return;
            }

            // 图片目录问题是"用户自己能在软件里修好"的，只提示不阻断：
            // 阻断会让用户连主界面都进不去，而入口恰恰在主界面里。
            foreach (SimCameraCheckItem cam in input.Cameras)
            {
                string name = string.IsNullOrEmpty(cam.Name) ? "(未命名相机)" : cam.Name;

                if (string.IsNullOrWhiteSpace(cam.ImageDir))
                {
                    result.Add(SimCheckLevel.Warning, "相机 " + name, "未配置仿真图片目录。" + howTo);
                    continue;
                }

                if (!Directory.Exists(cam.ImageDir))
                {
                    result.Add(SimCheckLevel.Warning, "相机 " + name,
                        "仿真图片目录不存在：" + cam.ImageDir + "。" + howTo);
                    continue;
                }

                if (cam.ImageCount <= 0)
                {
                    result.Add(SimCheckLevel.Warning, "相机 " + name,
                        "仿真图片目录里没有可用图片：" + cam.ImageDir + "。" + howTo);
                }
            }
        }

        private static void CheckPlc(SimCheckInput input, SimCheckResult result)
        {
            if (input.PlcPort < 1 || input.PlcPort > 65535)
            {
                result.Add(SimCheckLevel.Blocking, "PLC", "监听端口非法：" + input.PlcPort);
                return;
            }

            if (!IsPortFree(input.PlcPort))
            {
                result.Add(SimCheckLevel.Blocking, "PLC",
                    string.Format("端口 {0} 已被占用，PLC 仿真服务端无法启动。", input.PlcPort));
            }
        }

        private static void CheckPeripherals(SimCheckInput input, SimCheckResult result)
        {
            if (!input.LightConfigured)
            {
                result.Add(SimCheckLevel.Warning, "光源", "未检测到光源控制器配置；仿真不模拟光源，流程若依赖光源状态会表现异常。");
            }

            if (!input.MotionCardConfigured)
            {
                result.Add(SimCheckLevel.Warning, "运控IO", "未检测到运动控制卡配置；仿真不模拟 IO，心跳脉冲与工位信号不会真实产生。");
            }
        }

        private static void CheckDataRoot(SimCheckInput input, SimCheckResult result)
        {
            if (string.IsNullOrWhiteSpace(input.DataRoot)) return;
            try
            {
                if (!Directory.Exists(input.DataRoot)) Directory.CreateDirectory(input.DataRoot);
                result.Add(SimCheckLevel.Info, "数据目录", input.DataRoot);
            }
            catch (Exception ex)
            {
                result.Add(SimCheckLevel.Blocking, "数据目录", "无法创建/访问：" + ex.Message);
            }
        }

        /// <summary>探测端口是否可绑定。占用了就是阻断项，不要等到服务端启动才报。</summary>
        private static bool IsPortFree(int port)
        {
            try
            {
                System.Net.Sockets.TcpListener listener =
                    new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}