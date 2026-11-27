using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevMgmt;
using DevMgmt.Cameras.Models;
using Frame_Core;
using Frame_MetalRun;
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
                // 五金暂停时 Orchestrator 已冻结；TriggerPaused 再挡一次残留信号。
                // 激光保持原来的“运行即产图”，不增加门控。
                bool metal = Project.Instance.systemInfo.CraftType == MaterialCrafEnum.五金模切;
                Runtime.TriggerWhen = metal
                    ? (Func<bool>)(() => Project.Instance.Is_Run && !Runtime.TriggerPaused)
                    : (() => Project.Instance.Is_Run);

                // 五金：把三路工位的帧序号与发送队列做成只读快照给界面。
                // 激光不注入，界面显示"未接入"，与改造前行为一致。
                if (Project.Instance.systemInfo.CraftType == MaterialCrafEnum.五金模切)
                {
                    Runtime.StationStateProvider = Build五金StationStates;
                }

                CameraDevExtensions.SimCameraFactory = Runtime.BuildCameraFactory(ResolveSpec);

                // 控制卡：仿真提供自己的实现，替掉画像里的 0640 板卡。
                // 时机必须在 Init_Dev 之前——控制卡是 Server 构造的前置条件，
                // 换完画像后"卡是否可用"就不再取决于这台机器有没有插板卡。
                IDeviceProfile realProfile = DeviceProfileRegistry.Resolve(Project.Instance.systemInfo.CraftType);
                DeviceProfileRegistry.Override(new SimDeviceProfile(realProfile, new SimControlCard(Runtime.Recorder)));

                // 五金：尺寸帧号改从"产品令牌"取（上线放行时发出）。
                // 于是"尺寸落后于线扫"由输送延时自然产生，落后几片 = 队列深度，不计算片数。
                // 激光不注入，Size.NextFrameIdProvider 保持 null，行为不变。
                if (Project.Instance.systemInfo.CraftType == MaterialCrafEnum.五金模切)
                {
                    Frame_Core.业务参数类.五金模切帧序号.Size.NextFrameIdProvider =
                        () => Runtime.Orchestrator.TakeSizeFrameId();

                    // 令牌逐级传递：上线 → 下线 → 尺寸。
                    // 尺寸只能领"已过下线"的产品号，所以界面必然满足 size ≤ down ≤ up，
                    // 不会出现"产品刚到上线就直接到了尺寸"。
                    Frame_Core.业务参数类.LineScanFrameSequencer.DownFrameAssigned =
                        id => Runtime.Orchestrator.EnqueueDownProduct(id);

                    // 换卷/复位时丢掉在途产品，避免换卷前的那几片被当成新片送进尺寸。
                    Frame_Core.业务参数类.五金模切帧序号.OnProductsCleared =
                        () => Runtime.Orchestrator.ClearProducts();
                }

                Runtime.Start();
                return true;
            }
            catch (Exception ex)
            {
                Block("仿真初始化异常", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 五金仿真界面的三列状态：帧号来自序号器，队列状态来自三条结果通道。
        /// 只读快照，不做任何处置——界面不能成为第二个能改运行时状态的地方。
        /// </summary>
        private static IReadOnlyList<Frame_Simulation.SimStationState> Build五金StationStates()
        {
            var lineScan = Frame_Core.业务参数类.五金模切帧序号.LineScan;
            var size = Frame_Core.业务参数类.五金模切帧序号.Size;

            string upCameras, downCameras, sizeCameras;
            ResolveStationCameras(out upCameras, out downCameras, out sizeCameras);

            int perGroup = Frame_Core.业务参数类.SizeFrameSequencer.CamerasPerGroup;

            // 状态文字写成谓语完整的短句：界面只负责加"相机/帧号/状态"标签，不再让人猜字段含义。
            return new[]
            {
                MakeState("上线扫", upCameras, lineScan.UpFrameId,
                    "发号源，每帧 +1", Server_五金模切.UpResultChannel),

                MakeState("下线扫", downCameras, lineScan.DownFrameId,
                    "跟随上线，落后 " + (lineScan.UpFrameId - lineScan.DownFrameId) + " 帧",
                    Server_五金模切.DownResultChannel),

                MakeState("尺寸工位", sizeCameras, size.CurrentFrameId,
                    "上一组 " + (perGroup - size.LastWindowMissing) + "/" + perGroup + " 路到齐",
                    Server_五金模切.SizeResultChannel),
            };
        }

        /// <summary>
        /// 三列各配哪些相机：按相机在仿真配置里的角色划分，
        /// 不按相机创建顺序取索引（顺序是 下线→上线→尺寸），也不按名字猜。
        /// 上线 = 发令相机，尺寸 = 领令相机，下线 = 其余相机（既不发放也不领取产品令牌）。
        /// </summary>
        private static void ResolveStationCameras(out string up, out string down, out string size)
        {
            SimulationConfig cfg = Runtime == null ? null : Runtime.Config;
            List<string> all = CameraNames();

            List<string> producers = InBoth(all, cfg == null ? null : cfg.ProductProducerCameras);
            List<string> consumers = InBoth(all, cfg == null ? null : cfg.ProductConsumerCameras);

            up = string.Join(" / ", producers);
            size = string.Join(" / ", consumers);
            down = string.Join(" / ", all.Where(n => !producers.Contains(n) && !consumers.Contains(n)));
        }

        /// <summary>实际相机与配置名单的交集，顺序以实际相机为准。</summary>
        private static List<string> InBoth(List<string> all, List<string> wanted)
        {
            if (wanted == null || wanted.Count == 0) return new List<string>();
            return all.Where(n => wanted.Contains(n)).ToList();
        }

        private static List<string> CameraNames()
        {
            return EnumerateDevices()
                .Select(d => d.CameraParameter == null ? null : d.CameraParameter.CameraName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }

        private static Frame_Simulation.SimStationState MakeState(
            string name, string cameras, int frameId, string stage, PlcResultChannel channel)
        {
            PlcResultItem head;
            bool hasHead = channel.TryPeek(out head);

            return new Frame_Simulation.SimStationState
            {
                Name = name,
                Cameras = cameras,
                FrameId = frameId,
                Stage = stage,
                QueueDepth = channel.Count,
                QueueCapacity = channel.Capacity,
                QueueState = channel.IsPaused ? "发布暂停" : (hasHead ? "发送中" : "空闲"),
                QueueHeadFrameId = hasHead ? head.FrameId : 0,
                QueueHeadAttempts = hasHead ? head.Attempts : 0,
                LastSentFrameId = channel.LastCompletedFrameId,
            };
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

            string name = dev.CameraParameter.CameraName;
            int offsetMs = ResolveTriggerOffset(name);

            // 偏移必须留在记录里：帧号会因校验推迟，光看帧号无法判断"图像到底晚没晚"。
            var recorder = Runtime == null ? null : Runtime.Recorder;
            if (recorder != null)
                recorder.Cam(Frame_Simulation.Log.SimRecordKind.Info, "编排",
                    string.Format("触发偏移 {0}(#{1}) → {2}ms", name, dev.CameraParameter.ID, offsetMs));

            return new SimCameraSpec
            {
                Name = name,
                ImageDir = ResolveImageDir(dev.CameraParameter),
                OffsetMs = offsetMs,
                Role = ResolveRole(name)
            };
        }

        /// <summary>
        /// 相机在产品流里的角色。由配置名单决定，不再从名字里猜偏移之外的含义：
        /// 上线发放产品令牌，尺寸领取令牌，其余不参与。
        /// </summary>
        private static Frame_Simulation.Capture.SimTriggerOrchestrator.SlotRole ResolveRole(string cameraName)
        {
            SimulationConfig cfg = Runtime == null ? null : Runtime.Config;
            if (cfg == null || string.IsNullOrEmpty(cameraName)) return Frame_Simulation.Capture.SimTriggerOrchestrator.SlotRole.None;

            if (cfg.ProductProducerCameras != null && cfg.ProductProducerCameras.Contains(cameraName))
                return Frame_Simulation.Capture.SimTriggerOrchestrator.SlotRole.ProduceProduct;

            if (cfg.ProductConsumerCameras != null && cfg.ProductConsumerCameras.Contains(cameraName))
                return Frame_Simulation.Capture.SimTriggerOrchestrator.SlotRole.ConsumeProduct;

            return Frame_Simulation.Capture.SimTriggerOrchestrator.SlotRole.None;
        }

        /// <summary>
        /// 触发偏移按相机名从仿真配置取（SimulationConfig.TriggerOffsetMs）。
        ///
        /// 语义是固定物理距离：offset = 相机间距 / 线速，常量毫秒。
        /// 落后片数 = offset / 节拍，随产能变化——产线加速，同一段距离被更少的产品时间覆盖。
        ///
        /// 三条曾经的错法，一并记录，别再走回去：
        ///   1. 按相机名 contains("上线"/"下线"/"尺寸") 猜：现场名字确实叫「上线扫/左上」，
        ///      但猜法对改名和新增工位都没防御，改为精确查配置表；
        ///   2. 按相机 ID 索引表：实测 ID 是 1/2/8/9/10，顺序不等于工位，
        ///      结果上线扫拿到 5000、尺寸三路拿到 0，所有工位同刻出图；
        ///   3. 用「下线片距 × 节拍」当延时：那是逻辑编号，产能一变偏移就漂。
        /// </summary>
        public static int ResolveTriggerOffset(string cameraName)
        {
            Dictionary<string, int> table = Runtime == null ? null : Runtime.Config.TriggerOffsetMs;

            int offset;
            if (table != null && !string.IsNullOrEmpty(cameraName) && table.TryGetValue(cameraName, out offset))
                return Math.Max(0, offset);

            var recorder = Runtime == null ? null : Runtime.Recorder;
            if (recorder != null)
                recorder.Cam(Frame_Simulation.Log.SimRecordKind.Warning, "编排",
                    string.Format("相机「{0}」未配置触发偏移，按 0 处理", cameraName));

            return 0;
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