using System;
using System.Collections.Generic;
using System.IO;
using DevMgmt;
using DevMgmt.Cameras.Models;
using DevMgmt.Cameras.SDK;
using Frame_Simulation.Capture;
using Frame_Simulation.Config;
using Frame_Simulation.Log;
using Frame_Simulation.Plc;

namespace Frame_Simulation
{
    /// <summary>宿主告知仿真一侧：这台相机叫什么、仿真图片从哪读、什么时候触发。</summary>
    public class SimCameraSpec
    {
        public string Name { get; set; }
        public string ImageDir { get; set; }

        /// <summary>
        /// 相对每个节拍 t0 的触发偏移（毫秒）。0 = 最先触发。
        /// 必须由宿主按"相机在产线上的物理位置"给定，不能按创建顺序猜：
        /// 实测相机创建顺序是 下线/上线/尺寸，按顺序给偏移会让下线比上线先触发。
        /// </summary>
        public int OffsetMs { get; set; }

        /// <summary>
        /// 该相机在产品流里的角色：上线发放令牌，尺寸领取令牌。
        /// None 时不参与令牌流（激光只有一台相机）。
        /// </summary>
        public SimTriggerOrchestrator.SlotRole Role { get; set; }
    }

    /// <summary>
    /// 仿真界面显示的一路工位状态快照。
    ///
    /// 由宿主注入：仿真层不认识"上线/下线/尺寸"这些业务概念，
    /// 也不引用 Frame_Core。它只负责把宿主给的字符串画出来。
    /// </summary>
    public class SimStationState
    {
        /// <summary>列标题，例如"上线扫"。</summary>
        public string Name { get; set; }

        /// <summary>处理中/最近完成的帧号。</summary>
        public int FrameId { get; set; }

        /// <summary>处理阶段文字，例如"处理中 帧12"/"空闲"。</summary>
        public string Stage { get; set; }

        /// <summary>结果队列深度与容量。</summary>
        public int QueueDepth { get; set; }
        public int QueueCapacity { get; set; }

        /// <summary>发布状态文字：空闲 / 发送中 / 已暂停。</summary>
        public string QueueState { get; set; }

        /// <summary>队首帧号与已重试次数，0 表示队空。</summary>
        public int QueueHeadFrameId { get; set; }
        public int QueueHeadAttempts { get; set; }

        /// <summary>最近一次发布成功的帧号。</summary>
        public int LastSentFrameId { get; set; }
    }

    /// <summary>
    /// 仿真运行时。宿主只在启动阶段与它打交道：
    /// 建体检 → 起 PLC 服务端 → 注入相机工厂；之后运行期不再需要感知它存在。
    ///
    /// 之所以做成单例而不是靠 DI：宿主是 WinForms 老工程，注入点只有一个
    /// （CameraDevExtensions.SimCameraFactory），多一层容器只会增加排查成本。
    /// </summary>
    public class SimulationRuntime : IDisposable
    {
        public static SimulationRuntime Current { get; private set; }

        public SimulationConfig Config { get; private set; }
        public SimRecorder Recorder { get; private set; }
        public FramePacer Pacer { get; private set; }
        public SimTriggerOrchestrator Orchestrator { get; private set; }
        public List<SimCamera> Cameras { get; private set; }
        public SimPlcServerBase PlcServer { get; private set; }
        public SimCheckResult CheckResult { get; private set; }

        /// <summary>
        /// 相机是否"被触发"。由宿主注入（通常是"流程是否在运行"），
        /// 统一挂在工厂里，避免每台相机各自判断。
        /// </summary>
        /// <summary>
        /// 需要预注册到符号型服务端的地址。由宿主注入（通常来自当前机型的 PLC 点位表），
        /// 本层因此不需要认识任何具体机型。
        /// </summary>
        public Func<IEnumerable<string>> PlcTagProvider { get; set; }

        public Func<bool> TriggerWhen { get; set; }

        /// <summary>
        /// 工位状态快照来源。宿主注入（五金读帧序号器 + 三条结果队列）；
        /// 不注入时界面显示"未接入"，激光保持原样不受影响。
        /// </summary>
        public Func<IReadOnlyList<SimStationState>> StationStateProvider { get; set; }

        private bool _started;

        private SimulationRuntime(SimulationConfig config)
        {
            Config = config;
            Recorder = new SimRecorder(config.RecorderCapacity);
            Pacer = new FramePacer(config.CapacityPerMinute);
            Orchestrator = new SimTriggerOrchestrator();
            Cameras = new List<SimCamera>();
        }

        /// <summary>
        /// 创建并完成启动体检。返回 null 表示体检有阻断项，调用方不得进入主界面。
        /// </summary>
        public static SimulationRuntime Create(SimulationConfig config, SimCheckInput input, out SimCheckResult checkResult)
        {
            SimulationRuntime runtime = new SimulationRuntime(config ?? new SimulationConfig());
            checkResult = StartupChecker.Check(runtime.Config, input);
            runtime.CheckResult = checkResult;

            foreach (SimCheckEntry entry in checkResult.Entries)
            {
                runtime.Recorder.Plc(SimRecordKind.Info, "[体检]", entry.ToLine());
            }

            Current = runtime;
            return runtime;
        }

        /// <summary>
        /// 启动 PLC 服务端。useCip = true 走五金（符号寻址），false 走激光（FINS）。
        /// 必须在 PLC 客户端 Connect 之前调用。
        /// </summary>
        public bool StartPlcServer(bool useCip)
        {
            IEnumerable<string> symbols = useCip && PlcTagProvider != null
                ? (PlcTagProvider() ?? new string[0])
                : new string[0];

            PlcServer = useCip
                ? (SimPlcServerBase)new SimPlcServerCip(Config, Recorder, symbols)
                : new SimPlcServerFins(Config, Recorder);

            bool ok = PlcServer.Start(Config.PlcServerPort);
            if (ok && PlcServer is SimPlcServerFins)
            {
                ((SimPlcServerFins)PlcServer).SeedDefaultValues();
            }
            return ok;
        }

        /// <summary>
        /// 生成要注入给 CameraDevExtensions 的工厂。
        /// resolver 由宿主提供：把一台相机配置翻译成"名字 + 图片目录"，仿真项目因此不需要认识相机配置结构。
        /// </summary>
        public Func<CameraDev, CameraObj> BuildCameraFactory(Func<CameraDev, SimCameraSpec> resolver)
        {
            if (resolver == null) throw new ArgumentNullException("resolver");

            return dev =>
            {
                SimCameraSpec spec = resolver(dev);
                if (spec == null)
                {
                    throw new InvalidOperationException(
                        "仿真模式下有相机没有解析出图片目录，请检查相机配置的仿真路径。");
                }

                SimCamera cam = new SimCamera(spec.Name, spec.ImageDir, Pacer, Recorder);
                cam.TriggerWhen = TriggerWhen;

                // 触发偏移由宿主按物理位置给定（spec.OffsetMs）。
                // 不能按创建顺序推：实测创建顺序是 下线→上线→尺寸，
                // 按顺序给偏移会让下线比上线早 500ms 触发，与产线方向相反。
                int offsetMs = spec.OffsetMs;
                cam.SlotIndex = Orchestrator.Register(offsetMs, spec.Role);
                cam.WaitTurn = stop => Orchestrator.WaitTurn(cam.SlotIndex, stop);

                Cameras.Add(cam);
                Recorder.Cam(SimRecordKind.Info, cam.CameraName,
                    string.Format("已创建，图片目录：{0}，触发偏移 {1}ms", spec.ImageDir, offsetMs));
                return cam;
            };
        }

        /// <summary>产能变更：节拍器立刻生效，正在运行的采集线程下一拍就换节奏。</summary>
        public void SetCapacity(int capacityPerMinute)
        {
            Config.CapacityPerMinute = capacityPerMinute;
            Pacer.CapacityPerMinute = capacityPerMinute;
            Orchestrator.SetInterval(Pacer.IntervalMs);
            Recorder.Cam(SimRecordKind.Info, "[产能]",
                string.Format("调整为 {0} 片/分（每 {1} ms 一张）", capacityPerMinute, Pacer.IntervalMs));
        }

        /// <summary>相机配置里的仿真路径改了之后调用，避免必须重启。</summary>
        public void ReloadImageSets(Func<CameraDev, SimCameraSpec> resolver, IEnumerable<CameraDev> devices)
        {
            if (resolver == null || devices == null) return;

            int i = 0;
            foreach (CameraDev dev in devices)
            {
                SimCameraSpec spec = resolver(dev);
                if (spec == null) continue;
                if (i < Cameras.Count) Cameras[i].ReloadImageSet(spec.ImageDir);
                i++;
            }
        }

        public void Start()
        {
            if (_started) return;
            _started = true;

            // 编排器先起：相机线程一起来就在等令，编排器没跑会让它们空等一个超时周期
            Orchestrator.Start(Pacer.IntervalMs);

            Recorder.Plc(SimRecordKind.Info, "[仿真]", "运行时已启动。");
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;

            foreach (SimCamera cam in Cameras)
            {
                try { cam.StopGrabImage(); } catch { }
            }

            // 相机都停了再停编排器：反过来的话还在等令的线程会先收到停止信号，
            // 与相机停止的顺序交错，退出日志会变得难以阅读
            Orchestrator.Stop();

            if (PlcServer != null) PlcServer.Stop();
            Recorder.Plc(SimRecordKind.Info, "[仿真]", "运行时已停止。");
        }

        public void Dispose()
        {
            Stop();
            Orchestrator.Dispose();
            if (ReferenceEquals(Current, this)) Current = null;
        }

        /// <summary>默认图片目录：&lt;程序目录&gt;/ZImageResource/&lt;相机名&gt;。宿主可覆盖。</summary>
        public string DefaultImageDir(string cameraName)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZImageResource", cameraName ?? "Camera");
        }
    }
}