using Frame_Core;
using HalconDotNet;
using LogTool;
using System;

namespace Frame_Core.业务类
{
    /// <summary>
    /// 五金模切的设备业务对象：5 路相机、7 条流程。
    ///
    /// 流程内容（模块装配、运行处理）在 五金模切ProDevice.流程内容.cs，是从激光模切复制过来的独立一份：
    /// 两边互不影响，改一边不会动另一边。
    ///
    /// 7 条流程 = 2 裁图 + 2 极片EA + 3 尺寸。没有不取图的流程：
    /// 冲模由 PLC 自己完成，视觉侧不参与，因此每条流程都由相机出图驱动。
    /// </summary>
    partial class 五金模切ProDevice : DieCutFlowAlgorithmBase
    {
        /// <summary>
        /// 配方。流程内容统一按基类取用：相机角色/槽位已经由设备画像决定，
        /// 流程代码不该知道自己跑在激光还是五金上。
        /// </summary>
        private CraftParam_DieCutBase m_craft => Project.Instance.craftBase as CraftParam_DieCutBase;

        public 五金模切ProDevice()
            : this(DeviceProfileRegistry.Resolve(MaterialCrafEnum.五金模切))
        {
        }

        /// <summary>
        /// 相机槽位全部来自设备画像。下标必须连续，与 M_CamParam.l_CameraDevs 的顺序一一对应。
        /// 相机出图回调由 Project.Init 在 INIPro/INIProduct 之后统一绑定（BindCamImageSource），
        /// 这里不再重复绑定：重复绑没有副作用，但会让人误以为"注释掉这行就断开了相机链路"。
        /// </summary>
        public 五金模切ProDevice(IDeviceProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");

            m_nCamCount = profile.CameraCount;
            m_nProcessCount = profile.ProcessCount;
            for (int i = 0; i < m_nCamCount; i++)
            {
                CamSlotDef slot = profile.CamSlots[i];
                m_DicCamOperaParam[i] = slot.NewParam();
                m_DicCamOperaParam_Sim[i] = slot.NewParam();
            }

            // 与激光模切同一个 Halcon 全局设置：关闭 region 自动裁剪，
            // 否则超出图像范围的 region/ROI 会被 Halcon 静默截断，测量结果与激光不一致。
            HOperatorSet.SetSystem("clip_region", "false");

            INIPro();
        }

        /// <summary>
        /// 复位 + 换配方。Server 的 Reset() 会调这里，必须清掉每路相机参数里挂的图像队列/缓存，
        /// 否则上一卷的图（含 EA 缓存）会留在队列里被下一卷取走。
        /// </summary>
        public override bool INIProduct()
        {
            try
            {
                foreach (var item in m_DicCamOperaParam)
                {
                    item.Value?.Dispose();
                }
                InitProduct_Sim();
                return true;
            }
            catch (Exception ee)
            {
                Log4netRecord.ErrorLog.Error("五金模切初始化系统数据报错：\n" + ee);
                return false;
            }
        }

        /// <summary>仿真参数同样要清：仿真取图队列里可能还压着上一轮的图。</summary>
        public override bool InitProduct_Sim()
        {
            try
            {
                foreach (var item in m_DicCamOperaParam_Sim)
                {
                    item.Value?.Dispose();
                }
                return true;
            }
            catch (Exception ee)
            {
                Log4netRecord.ErrorLog.Error("五金模切初始化仿真数据报错：\n" + ee);
                return false;
            }
        }

        public override void BuildProcessServiceList()
        {
            IDeviceProfile profile = DeviceProfileRegistry.Resolve(Project.Instance.systemInfo.CraftType);

            foreach (ProcessSpec spec in profile.BuildProcessList())
            {
                int id = Project.Instance.CreateProcess();
                ProcessServices service = Project.Instance.GetProcessServiceById(id);
                service.process.processInfo.CameraID = spec.CameraIndex;
                service.process.processInfo.ProcessName = spec.ProcessName;

                DelegateINIProcess onInit;
                DelegateRUNProcess onRun;
                ResolveProcessHandlers(spec.Kind, out onInit, out onRun);
                if (onInit != null) service.m_eventINIProcess += onInit;
                if (onRun != null) service.m_eventRUNProcess += onRun;

                service.InitProcess();
                service.OpeningThread();
            }
        }

        /// <summary>
        /// 流程种类 → 本机型的装配与运行实现。
        /// 取图流程靠 process.processInfo.CameraID 定位相机，不按下标猜——同一种流程会出现在多路相机上。
        /// </summary>
        private void ResolveProcessHandlers(ProcessKind kind, out DelegateINIProcess onInit, out DelegateRUNProcess onRun)
        {
            onInit = null;
            onRun = null;

            switch (kind)
            {
                // 线扫主/从：裁拼大图 + 产出极片 EA
                case ProcessKind.相机裁图:
                    onInit = InitCropProcess;
                    onRun = RunCropProcess;
                    return;

                // 极片 EA：吃裁图流程产出的 EA 图
                case ProcessKind.极片EA:
                    onInit = InitEaProcess;
                    onRun = RunEaProcess;
                    return;

                // 尺寸 1~3
                case ProcessKind.极片尺寸检测:
                    onInit = InitSizeProcess;
                    onRun = RunSizeProcess;
                    return;
                case ProcessKind.极片尺寸带极耳检测:
                    onInit = InitSizeTabProcess;
                    onRun = RunSizeTabProcess;
                    return;

                default:
                    throw new NotSupportedException("五金模切未实现该流程种类：" + kind);
            }
        }
    }
}