using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevMgmt;
using DevMgmt.Cameras.Models;
using Frame_Core;
using Frame_Simulation;
using Frame_Simulation.Config;

namespace Mef_VisionFrame.Simulation
{
    /// <summary>
    /// 宿主侧的仿真启动器。它的唯一职责是"把核心层的相机抽象与仿真实现接起来"：
    /// 核心层只暴露两个静态委托（SimInitialize / SimImageDirResolver），
    /// 具体用哪个仿真实现由这里决定，因此 Frame_Core 不认识 Frame_Simulation。
    /// </summary>
    public static class SimBootstrap
    {
        public static SimulationRuntime Runtime { get; private set; }

        /// <summary>
        /// 仿真启动被阻断。宿主据此决定是否中止主界面构建——
        /// 不要用 INIProject 的返回值判断，它过去一直被忽略。
        /// </summary>
        public static bool Blocked { get; private set; }

        public static bool IsSimulation
        {
            get
            {
                try { return Project.Instance.systemInfo.RunMode == RunModeEnum.Simulation; }
                catch { return false; }
            }
        }

        /// <summary>在 Program.Main 里挂上，INIProject 会在连接相机之前调用。</summary>
        public static void Attach()
        {
            CameraDevExtensions.SimInitialize = Initialize;
            CameraDevExtensions.SimImageDirResolver = ResolveImageDir;
        }

        /// <summary>最近一次阻断原因。供启动阶段的配置窗口显示标题，不再靠弹窗转述。</summary>
        public static string LastBlockReason { get; private set; }

        /// <summary>
        /// 启动时未满足、但不该阻断的仿真条件（典型：图片目录没配/目录里没图）。
        /// 由主界面顶部提示条展示，用户自己进相机设置改完即可，不需要重启。
        /// </summary>
        public static IReadOnlyList<string> StartupWarnings { get; private set; } = new string[0];

        /// <summary>
        /// 相机设置保存后重新指向图片目录：不重建相机、不重启进程，下一次取图就用新目录。
        /// 仿真的"改完立刻能跑"就靠这一句。
        /// </summary>
        public static void ReloadImageDirs()
        {
            if (!IsSimulation || Runtime == null) return;

            Runtime.ReloadImageSets(ResolveSpec, EnumerateDevices());
        }

        /// <summary>
        /// 体检里没有阻断项时，把 Warning 项收集成可展示的文本；有阻断项则由 Block 负责。
        /// </summary>
        private static void CaptureWarnings(SimCheckResult check)
        {
            StartupWarnings = check.Entries
                .Where(e => e.Level == SimCheckLevel.Warning)
                .Select(e => e.Item + "：" + e.Message)
                .ToList();
        }

        /// <summary>
        /// 阻断的唯一出口：只记录原因并置标志，不弹任何框。
        /// 启动链路只允许一个 UI 出口，否则会出现"体检弹一次、启动失败再弹一次"的引用式提示。
        /// </summary>
        public static void Block(string title, string detail)
        {
            LastBlockReason = string.IsNullOrEmpty(detail) ? title : title + "：" + detail;
            Blocked = true;
        }

        /// <summary>
        /// 只重跑体检，不启动任何服务端：用户改完配置后用它复查，避免重复起 PLC 服务端。
        /// </summary>
        public static bool Recheck()
        {
            SimulationConfig cfg = SimulationConfig.Load();
            SimCheckResult check = StartupChecker.Check(cfg, BuildCheckInput(cfg));

            if (check.HasBlocking)
            {
                LastBlockReason = check.Report();
                Blocked = true;
                return false;
            }

            // 通过（或只有警告）：警告进主界面提示条，不阻断启动。
            CaptureWarnings(check);
            Blocked = false;
            LastBlockReason = null;
            return true;
        }

        /// <summary>
        /// 返回 false 表示体检有阻断项，INIProject 必须中止——否则后面会去连真机或直接崩。
        /// </summary>
        public static bool Initialize()
        {
            if (!IsSimulation) return true;

            try
            {
                SimulationConfig cfg = SimulationConfig.Load();
                SimCheckInput input = BuildCheckInput(cfg);

                SimCheckResult check;
                Runtime = SimulationRuntime.Create(cfg, input, out check);

                if (check.HasBlocking)
                {
                    LastBlockReason = check.Report();
                    Blocked = true;
                    return false;
                }

                // 图片目录缺失之类只提示不阻断：启动照常，用户在主界面里改完即生效。
                CaptureWarnings(check);

                // 符号表来自当前机型自己的点位表：仿真层不认识机型，
                // 新增机型只要实现 IPLCAddressTable 并让它的 PLC 服务转出即可。
                Runtime.PlcTagProvider = () =>
                {
                    PLCCommunicationBase plc = Project.Instance.当前PLC;
                    return plc == null ? new string[0] : plc.GetRegisterSymbols();
                };

                // PLC 服务端必须在客户端 Connect 之前启动，这里已经是最后的安全时机。
                // 协议形态由当前 PLC 通道声明，宿主不再判断机型。
                bool useCip = Project.Instance.PlcUseCip;
                if (!Runtime.StartPlcServer(useCip))
                {
                    Block("PLC 仿真服务端启动失败",
                        "无法监听端口 " + cfg.PlcServerPort + "，请关闭占用该端口的程序。");
                    return false;
                }

                // 符号型机型必须有符号表：空表意味着每次写入都会失败，累计 3 次后客户端
                // 自行断链并报警。新机型忘记实现 IPLCAddressTable 时在这里就阻断。
                if (useCip)
                {
                    Frame_Simulation.Plc.SimPlcServerCip cip =
                        Runtime.PlcServer as Frame_Simulation.Plc.SimPlcServerCip;
                    if (cip == null || cip.RegisteredTags.Count == 0)
                    {
                        Block("缺少 PLC 符号表",
                            "当前机型的点位表未实现 IPLCAddressTable 或符号表为空，PLC 写入会全部失败。");
                        return false;
                    }
                }

                // 把 PLC 客户端指向本地服务端：Init_Dev 稍后才会 Connect，
                // 这里覆写地址与端口是最省事且不影响真机配置的办法。
                PointPlcClientToLocal(cfg.PlcServerPort);

                // 仿真触发源 = 流程是否在运行；未点运行不产图，与真机"无触发不开采"一致。
                Runtime.TriggerWhen = () => Project.Instance.Is_Run;
                CameraDevExtensions.SimCameraFactory = Runtime.BuildCameraFactory(ResolveSpec);

                // 控制卡：仿真提供自己的实现，替掉画像里的 0640 板卡。
                // 时机必须在 Init_Dev 之前——控制卡是 Server 构造的前置条件，
                // 换完画像后"卡是否可用"就不再取决于这台机器有没有插板卡。
                IDeviceProfile realProfile = DeviceProfileRegistry.Resolve(Project.Instance.systemInfo.CraftType);
                DeviceProfileRegistry.Override(new SimDeviceProfile(realProfile, new SimControlCard(Runtime.Recorder)));

                Runtime.Start();
                return true;
            }
            catch (Exception ex)
            {
                Block("仿真初始化异常", ex.Message);
                return false;
            }
        }

        /// <summary>仿真下 PLC 客户端连本地回环，端口与刚刚启动的服务端保持一致。</summary>
        private static void PointPlcClientToLocal(int port)
        {
            if (Project.Instance.systemInfo.CraftType == MaterialCrafEnum.五金模切)
            {
                if (Project.Instance.M_PLCDevParam_五金 == null) return;
                Project.Instance.M_PLCDevParam_五金.IsConnect = true;
                Project.Instance.M_PLCDevParam_五金.PLC_IP = "127.0.0.1";
                Project.Instance.M_PLCDevParam_五金.DevPort = port;
                return;
            }

            if (Project.Instance.M_PLCDevParam == null) return;
            Project.Instance.M_PLCDevParam.IsConnect = true;
            Project.Instance.M_PLCDevParam.PLC_IP = "127.0.0.1";
            Project.Instance.M_PLCDevParam.DevPort = port;
        }

        private static SimCheckInput BuildCheckInput(SimulationConfig cfg)
        {
            SimCheckInput input = new SimCheckInput
            {
                PlcPort = cfg.PlcServerPort,
                DataRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cfg.DataRootName),
                LightConfigured = true,      // 光源不参与仿真，配置存在与否只影响提示
                MotionCardConfigured = true  // 由 SimDeviceProfile + SimControlCard 兑现，不是假定
            };

            foreach (CameraDev dev in EnumerateDevices())
            {
                string dir = ResolveImageDir(dev.CameraParameter);
                input.Cameras.Add(new SimCameraCheckItem
                {
                    Name = dev.CameraParameter == null ? null : dev.CameraParameter.CameraName,
                    ImageDir = dir,
                    ImageCount = CountImages(dir)
                });
            }

            return input;
        }

        private static IEnumerable<CameraDev> EnumerateDevices()
        {
            try
            {
                if (Project.Instance.M_CamParam == null || Project.Instance.M_CamParam.l_CameraDevs == null)
                    return Enumerable.Empty<CameraDev>();

                return Project.Instance.M_CamParam.l_CameraDevs.Cast<CameraDev>().ToList();
            }
            catch
            {
                return Enumerable.Empty<CameraDev>();
            }
        }

        private static SimCameraSpec ResolveSpec(CameraDev dev)
        {
            if (dev == null || dev.CameraParameter == null) return null;

            return new SimCameraSpec
            {
                Name = dev.CameraParameter.CameraName,
                ImageDir = ResolveImageDir(dev.CameraParameter)
            };
        }

        /// <summary>
        /// 图片目录三级回退：相机配置的 EmulationPath → 程序目录/ZImageResource/相机名 → 程序目录/ZImageResource。
        /// 前两级都存在时优先配置值，避免"配置了却没生效"的困惑。
        /// </summary>
        public static string ResolveImageDir(CameraParameterBase param)
        {
            if (param == null) return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZImageResource");

            if (!string.IsNullOrWhiteSpace(param.EmulationPath) && Directory.Exists(param.EmulationPath))
                return param.EmulationPath;

            string byName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZImageResource", param.CameraName ?? "");
            if (Directory.Exists(byName)) return byName;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZImageResource");
        }

        private static int CountImages(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return 0;

            return Directory.GetFiles(dir).Count(p =>
                p.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase));
        }
    }
}