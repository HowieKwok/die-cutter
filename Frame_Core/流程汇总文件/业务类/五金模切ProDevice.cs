namespace Frame_Core.业务类
{
    /// <summary>
    /// 五金模切的设备业务对象：5 路相机、7 条流程的骨架。
    /// 流程内容（模块装配、运行处理、PLC/IO 交互）尚未实现，构建出的流程是空模块列表，
    /// 目的只是让界面、配方结构、流程目录先跑通，后续在 BuildProcessServiceList 下方补齐。
    /// </summary>
    class 五金模切ProDevice : BaseProDevice
    {
        public 五金模切ProDevice()
            : this(DeviceProfileRegistry.Resolve(MaterialCrafEnum.五金模切))
        {
        }

        /// <summary>
        /// 相机槽位全部来自设备画像。下标必须连续，与 M_CamParam.l_CameraDevs 的顺序一一对应。
        /// </summary>
        public 五金模切ProDevice(IDeviceProfile profile)
        {
            if (profile == null) throw new System.ArgumentNullException("profile");

            m_nCamCount = profile.CameraCount;
            m_nProcessCount = profile.ProcessCount;
            for (int i = 0; i < m_nCamCount; i++)
            {
                CamSlotDef slot = profile.CamSlots[i];
                m_DicCamOperaParam[i] = slot.NewParam();
                m_DicCamOperaParam_Sim[i] = slot.NewParam();
            }

            // 流程为空壳时先不把相机出图回调绑到参数上：
            // 没有流程消费图像，绑了只会让图像在队列里积压。
            // 补完流程后在这里加：Project.Instance.BindCamImageSource(m_DicCamOperaParam);
            INIPro();
        }

        /// <summary>
        /// 按设备画像的流程清单建流程。目前不订阅 m_eventINIProcess / m_eventRUNProcess，
        /// 因此每条流程只有空模块列表；补工艺时在下面加事件订阅即可。
        /// </summary>
        public override void BuildProcessServiceList()
        {
            IDeviceProfile profile = DeviceProfileRegistry.Resolve(Project.Instance.systemInfo.CraftType);

            foreach (ProcessSpec spec in profile.BuildProcessList())
            {
                int id = Project.Instance.CreateProcess();
                ProcessServices service = Project.Instance.GetProcessServiceById(id);
                service.process.processInfo.CameraID = spec.CameraIndex;
                service.process.processInfo.ProcessName = spec.ProcessName;

                service.InitProcess();
                service.OpeningThread();
            }
        }
    }
}