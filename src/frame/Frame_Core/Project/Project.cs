using ControlBoard.Model;
using ControlBoard.服务类;
using DevMgmt;
using DevMgmt.Cameras.Models;
using DevMgmt.Light.Models;
using Frame_Core.业务类;
using FrameCommunity.DataOperation;
using FrameCommunity.错误代码;
using LogTool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Frame_Core
{

    public class Project
    {
        private Project() { }
        private static Project project = null;
        /// <summary>
        /// 项目类实例(单例模式)
        /// </summary>
        public static Project Instance
        {
            get
            {
                if (project == null)
                {
                    project = new Project();
                }
                return project;
            }
            set
            {
                project = value;
            }
        }


        /// <summary>
        /// 模块服务管理
        /// </summary>
        public ModelServiceManage modelServiceManage = new ModelServiceManage();

        /// <summary>
        /// 日志参数
        /// </summary>
        public LogParam logParam = new LogParam();


        /// <summary>
        /// 参数路径设置
        /// </summary>
        public ParamPathSet paramPathSet = new ParamPathSet();

        /// <summary>
        /// 可配置的落盘根路径（由“系统设置 → 路径设置”维护）
        /// </summary>
        public PathParameter pathParameter = new PathParameter();



        /// <summary>
        /// 用户管理
        /// </summary>
        public UserManage userManage = new UserManage();

        /// <summary>
        /// 权限管理参数
        /// </summary>
        public PurviewManage purviewManage = new PurviewManage();



        /// <summary>
        /// 3D测厚纠偏管理
        /// </summary>
        public Thick3DManage[] thick3DManages = new Thick3DManage[0];

        /// <summary>
        /// 磁盘参数
        /// </summary>
        public DriveParameter driveParameter = new DriveParameter();


        /// <summary>
        /// 设备PLC管理
        /// </summary>
        public PFTPLCManage cutOverlayPLCManage = new PFTPLCManage();

        /// <summary>
        /// 打标机管理
        /// </summary>
       // public DBJCardManager dbjCardManager = new DBJCardManager();

        /// <summary>
        ///  副机管理
        /// </summary>
        public SlaveMachineManager slaveMachineManager = new SlaveMachineManager();


        //状态
        public bool Is_MojuanStart = false;//膜卷开始标志
        /// <summary>
        /// 是否仿真模式。派生自启动期读取的运行模式配置，运行期不可修改。
        /// 旧的 Is_simulation 字段可写，是"运行中切换模式"那一套的残留。
        /// </summary>
        public bool Is_simulation => systemInfo.RunMode == RunModeEnum.Simulation; //项目仿真运行标志位。

        public bool Is_bShowStationTiming = false;//是否显示工位时间
        public bool LightOnOff = false;//光源状态开关
        /// <summary>
        /// 自动仿真标志
        /// </summary>
        public bool m_SimAutoFlag = false;

        /// <summary>
        /// 仿真运行标志
        /// </summary>
        public bool m_SimRunFlag = false;
        /// <summary>
        /// 启用产品图单步仿真开关
        /// </summary>
        public bool Is_bProductSwitch = false;
        public bool Is_SaveModelImage = false;//保存模板图
        public bool Is_Run = false;//软件运行标志


        /// <summary>
        /// 进入自动标定模式
        /// </summary>
        public bool IsAutoCalibrationModel = false;

        //系统参数
        public SystemInfo systemInfo = new SystemInfo();

        //工艺配方参数
        public CraftBase craftBase = new CraftBase();

        //相机参数
        public CamParam M_CamParam = new CamParam();

        //图像保存参数
        public NewImageSaveParamer M_NewImageSaveParamer = new NewImageSaveParamer();

        //光源参数
        public LightControllerParam M_LightControllerParam = new LightControllerParam();

        //PLC参数（激光模切：Fins + D 寄存器）
        public PLCDevParam M_PLCDevParam = new PLCDevParam();

        //PLC参数（五金模切：CIP + 符号地址，独立文件与独立实例）
        public PLCDevParam_五金 M_PLCDevParam_五金 = new PLCDevParam_五金();

        //PLC服务类（激光模切：D 寄存器地址表）
        public Server_DiecuttingCutPLC server_DiecuttingCutPLC = null;

        //PLC服务类（五金模切：CIP 符号地址表）
        public Server_五金模切PLC server_五金模切PLC = null;

        /// <summary>
        /// 当前机型是否五金模切。两种机型的 PLC 地址表和点位完全不同，
        /// 连接时只能二选一，不能同时连（连不上的那个会持续报"主设备连接失败"）。
        /// </summary>
        public bool Is五金模切
        {
            get { return systemInfo != null && systemInfo.CraftType == MaterialCrafEnum.五金模切; }
        }

        /// <summary>
        /// 当前机型生效的 PLC 通道。由 PlcChannelRegistry 按 CraftType 解析，
        /// 本类因此不出现任何机型分支；新增机型只需增加一个 IPlcChannel 实现。
        /// </summary>
        public IPlcChannel ActivePlcChannel { get; private set; }

        /// <summary>当前机型正在使用的 PLC 通讯服务（尚未创建时为 null）。</summary>
        public PLCCommunicationBase 当前PLC
        {
            get { return ActivePlcChannel == null ? null : ActivePlcChannel.Service; }
        }

        /// <summary>当前 PLC 是否为符号寻址（CIP）。仿真据此选服务端协议，不需要认识机型。</summary>
        public bool PlcUseCip
        {
            get { return ActivePlcChannel != null && ActivePlcChannel.UseCip; }
        }

        //控制卡参数
        public ParamControlCord M_ParamControlCord = new ParamControlCord();

        //点检参数
        public Param_PointInspection M_param_PointInspection = new Param_PointInspection();
        /// <summary>
        /// 相机相关
        /// </summary>
        public Dictionary<int, Tuple<CameraDev, CameraRelevancy>> M_CameraRelevancies = new Dictionary<int, Tuple<CameraDev, CameraRelevancy>>();

        //流程类
        public BaseProDevice baseProDevice = new BaseProDevice();
        /// <summary>
        /// 流程管理模块，集中管理流程列表、流程保存/读取、运行控制和线程释放。
        /// Project 保留旧接口作为兼容门面，避免调试界面和插件旧代码大面积修改。
        /// </summary>
        public ProcessManager Process流程插件管理者 = new ProcessManager();

        /// <summary>
        /// 流程列表旧入口。
        /// 为兼容调试界面、业务类和插件中已有的 Project.Instance.m_ProcessList 调用，保留该属性名；
        /// 实际数据由 Proc.List 统一维护，Project 不再单独保存 instanceList。
        /// </summary>
        public List<ProcessServices> m_ProcessList
        {
            get { return Process流程插件管理者.List; }
        }


        private List<List<ModuleInfo>> instanceList = new List<List<ModuleInfo>>(); //用于保存整个Instance的结构，流程和模块。


        public void Init_Dev()
        {
            // 控制卡是 Server 构造的前置条件（构造期间就要配中断、建 IO 输出），
            // 因此单独成步并排在最前：它失败必须在它自己的名字下报出来，
            // 而不是变成后面某一步的异常、或者远处 Server 里的空引用。
            RunDeviceStep("控制卡", InitControlCard);

            RunDeviceStep("相机", delegate
            {
                M_CamParam.l_CameraDevs.ConnectAllCam();
                M_CamParam.l_CameraDevs.StartGrabImage();
            });

            RunDeviceStep("光源", delegate
            {
                M_LightControllerParam.l_LightControllerDevs.ConnectAllDev();
                M_LightControllerParam.l_LightControllerDevs.OpenAllLight();
                LightOnOff = M_LightControllerParam.l_LightControllerDevs.GetAllLightOpenState();
            });

            // 两种机型的 PLC 地址表不同：五金只连自己的 CIP 符号表，激光只连 D 寄存器表。
            // 同时连会让没接 PLC 的那一路持续触发"主设备连接失败"停机报警。
            // 参数也各用各的实例：协议/地址/端口不能互相污染。
            // 连哪台 PLC、用哪份参数，全部由当前通道决定。
            RunDeviceStep("PLC", delegate { ActivePlcChannel?.Connect(this); });
        }

        /// <summary>
        /// 控制卡实现由机型画像提供：真机是 0640 板卡，仿真时宿主把画像换成模拟卡。
        /// 画像契约保证返回值非空，因此"卡没创建出来"这种状态在类型层面不存在。
        /// </summary>
        private void InitControlCard()
        {
            var card = DeviceProfileRegistry.Resolve(systemInfo.CraftType).CreateControlCard();
            ServiceCardManage.Instance.Init(card, M_ParamControlCord);
        }

        /// <summary>
        /// 单个设备的连接步骤。一步失败不牵连其他设备，但必须带步骤名以 Error 级落日志：
        /// 各 Connect 实现内部本来就会吞异常，这里再静默吞一次就等于"设备没连上，只有一条 Debug 记录"。
        /// </summary>
        private static void RunDeviceStep(string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error("设备初始化失败：" + step + " —— " + ex);
            }
        }
        public void Close_Dev()
        {
            try
            {
                M_CamParam.l_CameraDevs.CloseAllCam();
                M_LightControllerParam.l_LightControllerDevs.CloseAllDev();

                server_DiecuttingCutPLC?.Disconnect();
                server_五金模切PLC?.Disconnect();

                ServiceCardManage.Instance.GetIControlCard()?.Board_Close();
                //if (cutOverlayPLCManage != null)
                //{
                //    cutOverlayPLCManage.Disconnect();
                //}

                //if (dbjCardManager != null)
                //{
                //    dbjCardManager.Close();
                //}

                //if (slaveMachineManager != null)
                //{
                //    slaveMachineManager.Close();
                //}
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(ex);

            }

        }


        /// <summary>
        /// 初始化过程中抛出的异常，null 表示没有硬失败。
        /// 与 <see cref="INIProject"/> 的返回值分开：返回值还会因为某个参数读到默认值而变 false，
        /// 那种情况旧机台一直是带病继续启动的，不能等同于"必须退出"。
        /// </summary>
        public Exception InitError { get; private set; }

        public bool INIProject()
        {
            InitError = null;
            bool initParamResult = true;
            bool initObjectResult = true;
            string paramPath = "";

            int camNum = 2;
            int lightNum = 2;

            try
            {
                ErrorWorks.RegisterErrorInfo();

                paramPathSet.InitRelativeParamPath();

                //加载日志参数
                LogParamManage.GetLogParam(paramPathSet.LogParamPath, ref logParam);
                //加载参数及初始化配置文件和对象
                Log4netRecord.InitLog4netConfigure(logParam);//初始化软件报错日志 

                //加载系统参数
                systemInfo = systemInfo.ReadExtension<SystemInfo>(paramPathSet.SystemPath, $"SystemParam");
                systemInfo.IsHostMachine = true;

                //加载可配置路径并立即应用；文件缺失时 ReadExtension 会生成默认配置并落盘
                pathParameter = pathParameter.ReadExtension<PathParameter>(paramPathSet.SystemPath, "PathParameter");
                paramPathSet.ApplyPathParameter(pathParameter);


                //初始化配方、生产路径
                paramPathSet.UpdateCraftPath(systemInfo.RecipeName, systemInfo.currentMojuanhao, systemInfo.CraftType);

                //加载用户管理参数                       
                paramPath = paramPathSet.UserParamPathName;
                initParamResult &= userManage.InitUserParam(paramPath);

                //加载权限管理参数（纯参数加载，必须早于任何硬件/配方初始化，
                //否则后续任一步骤抛异常都会导致 purviewParam 为 null、菜单权限校验失败）
                foreach (ModelName name in Enum.GetValues(typeof(ModelName)))
                {
                    modelServiceManage.AddModel(name.ToString(), new ModelControl());
                }
                paramPath = paramPathSet.purviewParamPathName;
                initParamResult &= purviewManage.InitpurviewParam(paramPath);
                if (purviewManage.purviewParam != null)
                {
                    purviewManage.UpdateModelPurviewParam(modelServiceManage.m_dicModels, ref purviewManage.purviewParam.l_modelPrivilege);
                    // 旧权限文件一次性对齐角色规范（之后以管理员在界面的修改为准）
                    purviewManage.purviewParam.ApplyCurrentPolicyDefaults();
                    purviewManage.purviewParam.Save();
                }


                //【1】加载相机参数（机型层：相机布局随机型变化）
                M_CamParam = M_CamParam.ReadExtension<CamParam>(paramPathSet.MachinePath, $"AllCamParam");

                //【2】加载光源参数（机型层）
                M_LightControllerParam = M_LightControllerParam.ReadExtension<LightControllerParam>(paramPathSet.MachinePath, $"AllLightControllerParam");

                //【3】加载 配方参数
                craftBase = Craft_ModelService.GetCraft(paramPathSet.CraftPath, systemInfo.CraftType, systemInfo.RecipeName);

                //【4】加载图像保存参数（机型层：按相机数生成）
                M_NewImageSaveParamer = M_NewImageSaveParamer.ReadExtension<NewImageSaveParamer>(paramPathSet.MachinePath, $"ImageSaveParamer", M_CamParam.l_CameraDevs.Count);

                //【5】加载PLC参数：两种机型的协议、地址形式、IP 都可能不同，必须是两份实例 + 两个文件。
                //     共用一份时，在现场改五金 IP/协议会把激光的参数一起改掉。
                //     两种机型各放各的机型目录，只读当前机型那一份：
                //     都读的话，另一份就是上一台机器的残留，却仍会被后续诊断读到。
                //     机型知识全部在各自的 IPlcChannel 里：这里只按 CraftType 取通道，
                //     参数文件、服务类型、协议形态都由通道自己决定，本文件不再出现机型分支。
                ActivePlcChannel = PlcChannelRegistry.Resolve(systemInfo.CraftType);
                if (ActivePlcChannel == null)
                {
                    throw new NotSupportedException(
                        "未注册的 PLC 通道：" + systemInfo.CraftType + "，请在 Frame_Core 增加 IPlcChannel 实现。");
                }
                ActivePlcChannel.LoadParams(this, paramPathSet.MachinePath);

                //【6】加载点检参数（机型层：工位随相机布局变化）
                M_param_PointInspection = M_param_PointInspection.ReadExtension<Param_PointInspection>(paramPathSet.MachinePath, $"PointInspectionParamer");

                //【7】加载控制卡参数
                M_ParamControlCord = M_ParamControlCord.ReadExtension<ParamControlCord>(paramPathSet.SystemPath, $"ControlCordParam");

                //初始化运行内存及CPU读取功能
                MyMetrics.GetPrivateWorkingSetMB();
                MyMetrics.GetCpuPercentLikeTaskManager();

                //【100】连接设备
                // 仿真：宿主注入的初始化（体检 + PLC 服务端 + 相机工厂）；阻断则中止，避免误连真机
                if (DevMgmt.CameraDevExtensions.SimInitialize != null
                    && !DevMgmt.CameraDevExtensions.SimInitialize())
                {
                    return false;
                }

                Init_Dev();

                //相机相关
                M_CameraRelevancies.Clear();
                for (int i = 0; i < M_CamParam.l_CameraDevs.Count; i++)
                {
                    CameraEnum cameraEnum = (CameraEnum)i;
                    CameraDev cameraDev = M_CamParam.l_CameraDevs[i];
                    CameraRelevancy cameraRelevancy = new CameraRelevancy();
                    M_CameraRelevancies.Add(i, new Tuple<CameraDev, CameraRelevancy>(cameraDev, cameraRelevancy));
                    InitCamRelevancies(i);
                }

                // 机型差异统一由设备画像提供，新增机型只需在 DeviceProfileRegistry 注册一条
                baseProDevice = DeviceProfileRegistry.Resolve(systemInfo.CraftType).CreateProDevice();


                //初始化点检功能
                PointInspectionWorks.InitPointInspectionFunction();



                ///加载磁盘参数
                if (driveParameter == null) driveParameter = new DriveParameter();
                driveParameter.filePath = paramPathSet.DiverParamPathName;
                // Read() 在 XML 损坏、或默认文件写不出去时返回 null。这里必须兜底：
                // 状态栏的磁盘检查与"磁盘设置"菜单都会直接解引用 driveParameter。
                driveParameter = driveParameter.Read() as DriveParameter;
                if (driveParameter == null) driveParameter = new DriveParameter();




                //初始化主设备参数
                //      InitModel_cutOverlayPLCManage();

                //加载打标机软件参数
                //dbjCardManager = new DBJCardManager();
                //paramPath = paramPathSet.DBJParamPathName;
                //initParamResult &= dbjCardManager.InitParam(paramPath);
                //initObjectResult &= dbjCardManager.InitPLC();


                //加载副机参数
                //slaveMachineManager = new SlaveMachineManager();
                //paramPath = paramPathSet.SlaveParamPathName;
                //initParamResult &= slaveMachineManager.InitParam(paramPath);
                //initObjectResult &= slaveMachineManager.InitPLC();


                //新建膜卷路径
                initParamResult &= NewMujuanhao(systemInfo.RecipeName, systemInfo.currentMojuanhao, systemInfo.CraftType);

                //插件管理器初始化
                Process流程插件管理者.SetPath(paramPathSet.ProcessPath, paramPathSet.InstanceParamPath);

                // 加载成功才回写：加载失败时保存会把错误的结构写进 Instance.xml，
                // 下次启动再读到它，问题被固化成"现场配置"。
                if (ReadProject())
                {
                    SaveProject(); // 加载配方后，默认添加一个初始编号为0的产品。
                }
                else
                {
                    Log4netRecord.ErrorLog.Fatal("流程加载失败，已跳过自动保存，避免写坏 Instance.xml。");
                }


                baseProDevice.INIPro();
                baseProDevice.INIProduct();

                BindCamImageSource(baseProDevice.m_DicCamOperaParam);

            }
            catch (Exception ex)
            {
                // 异常只记在这里；对操作员的提示统一由 StartupBootstrap 负责，
                // 避免"日志埋一条、弹窗弹一次、上层再猜一次"的多出口写法。
                Log4netRecord.ErrorLog.Error("项目初始化异常", ex);
                InitError = ex;
                initParamResult = false;
            }
            return initParamResult;
        }

        /// <summary>
        /// 初始化相机参数
        /// </summary>
        /// <param name="camNum"></param>
        /// <returns></returns>
        public bool InitCamRelevancies(int index)
        {
            CameraRelevancy cameraRelevancy = Service_Camera.Get_CameraRelevancy(index);
            cameraRelevancy.InitCalibrationPlateParam(paramPathSet.CalibrationPlateParamPath + $"{index}");
            cameraRelevancy.InitCheckerboardParam(paramPathSet.CheckerboardParamPath + $"{index}");
            cameraRelevancy.InitPretargetingCalibParam(paramPathSet.PretargetingCalibParamPath + $"{index}");
            cameraRelevancy.InitImageSaveParamer(paramPathSet.ImageSaveParamPath + $"{index}");
            cameraRelevancy.InitOVHMarkParam(paramPathSet.CutOverlayMarkParamPath + $"{index}");
            cameraRelevancy.InitMsaCircleParam(paramPathSet.MsaCircleParamPath + $"{index}");
            cameraRelevancy.InitMsaLineWidthParam(paramPathSet.MsaLineWidthParamPath + $"{index}");


            cameraRelevancy.InitParam_GrayScaleAdaptationParameter(paramPathSet.GrayScaleAdaptationParamPath + $"{index}");
            cameraRelevancy.m_SizePointInspectionManage.InitParam(paramPathSet.CamSizePointInspectionParamPathName + $"{index}");
            cameraRelevancy.m_DefectPointInspectionManage.InitParam(paramPathSet.CamDefectPointInspectionParamPathName + $"{index}");
            ApplyPointCheckImageDefaults(cameraRelevancy);
            cameraRelevancy.InitParam_PointInspectionStateParameter(paramPathSet.CamPointInspectionStateParamPathName + $"{index}");
            return true;
        }

        /// <summary>
        /// 相机点检图像路径为空或仍是旧 E: 默认值时，按“路径设置”的图像根补齐。
        /// 现场手工改到别处的相机路径保持不动。
        /// </summary>
        private void ApplyPointCheckImageDefaults(CameraRelevancy cameraRelevancy)
        {
            if (cameraRelevancy == null || pathParameter == null) return;
            string root = pathParameter.PointCheckImageRoot;
            if (string.IsNullOrEmpty(root)) return;

            SizePointInspectionParam sizeParam =
                cameraRelevancy.m_SizePointInspectionManage.sizePointInspectionParam;
            if (sizeParam != null && PathConfigService.IsDefaultPointCheckImage(sizeParam.PointCheckImgPath, null))
            {
                sizeParam.PointCheckImgPath = Path.Combine(root, "尺寸点检图像");
                sizeParam.Save();
            }

            DefectPointInspectionParam defectParam =
                cameraRelevancy.m_DefectPointInspectionManage.defectPointInspectionParam;
            if (defectParam != null && PathConfigService.IsDefaultPointCheckImage(defectParam.PointCheckImgPath, null))
            {
                defectParam.PointCheckImgPath = Path.Combine(root, "缺陷点检图像");
                defectParam.Save();
            }
        }



        public bool Init3DThickManage(int thickNum)
        {
            bool result = true;
            try
            {

                thick3DManages = new Thick3DManage[thickNum];
                for (int i = 0; i < thickNum; i++)
                {
                    thick3DManages[i] = new Thick3DManage();
                    thick3DManages[i].InitParam(paramPathSet.CamParamPathName + $"3D{i + 1}");
                    result = true;
                }
            }
            catch (Exception ee)
            {

                result = false; ;

            }
            return result;

        }



        /// <summary>
        /// 初始化模块-切叠设备PLC
        /// </summary>
        public void InitModel_cutOverlayPLCManage()
        {
            ////加载主设备PLC参数  
            if (cutOverlayPLCManage == null) cutOverlayPLCManage = new PFTPLCManage();
            cutOverlayPLCManage.LoadParam(paramPathSet.CutOverlayPLCParamPath);
            cutOverlayPLCManage.Connect(cutOverlayPLCManage.m_DevParam.IsConnect, PLCTypeEnum.Omron, (CommunicationProtocolEnum)cutOverlayPLCManage.m_DevParam.ComType, cutOverlayPLCManage.m_DevParam.PLC_IP, cutOverlayPLCManage.m_DevParam.DevPort, (emDataFormat)cutOverlayPLCManage.m_DevParam.nByteTrans);
        }

        public void BindCamImageSource(Dictionary<int, BaseProParam> m_DicCamOperaParam)
        {
            baseProDevice.m_DicCamOperaParam = m_DicCamOperaParam;

            for (int i = 0; i < M_CamParam.l_CameraDevs.Count; i++)
            {
                // 相机对象未创建（如禁用/未连接的槽位）时跳过绑定
                var cameraDev = M_CamParam.l_CameraDevs[i];
                if (cameraDev?.CameraParameter == null) continue;
                if (cameraDev.Camera == null) continue;
                //注册流程相机图像队列
                if (baseProDevice.m_DicCamOperaParam.Count > i)
                {
                    baseProDevice.m_DicCamOperaParam[i].QueueGrabImage = cameraDev.Camera.QueueGrabImage;

                    cameraDev.Camera.CamCallbackEndEvent = null;
                    cameraDev.Camera.CamCallbackEndEvent = baseProDevice.m_DicCamOperaParam[i].CallBackImgDataDeal;
                }
            }
        }
        public void RemoveBindCamImageSource()
        {
            foreach (var item in M_CameraRelevancies.Keys)
            {
                //注册流程相机图像队列                  
                // baseProDevice.m_DicCamOperaParam[(int)item].QueueGrabImage = M_CameraRelevancies[item].Camera.QueueGrabImage;
                //M_CameraRelevancies[item].Camera.CamCallbackEvent -= baseProDevice.m_DicCamOperaParam[(int)item].CallBackImgDataDeal;
                //M_CameraRelevancies[item].Camera.CamCallbackEvent += baseProDevice.m_DicCamOperaParam[(int)item].CallBackImgDataDeal;
            }
        }

        /// <summary>
        /// 加载工艺 和 流程列表
        /// </summary>
        /// <param name="_CurrentCraft"></param>
        /// <param name="_Currentmaterial"></param>
        /// <returns></returns>
        public bool ChnageCraft(string _CurrentCraft, MaterialCrafEnum _Currentmaterial)
        {
            bool flag1 = true;
            try
            {
                //初始化配方流程路径
                craftBase.filePath = paramPathSet.CraftPath + _Currentmaterial.GetDescription() + "\\" + _CurrentCraft;
                //craftInfo.fileFullPath = paramPathSet.craftParamPath;
                switch (_Currentmaterial)
                {

                    case MaterialCrafEnum.激光模切一体机:
                        craftBase = craftBase.Read<PFTCleanCraft>();
                        if (craftBase == null)
                        {
                            flag1 = false;
                            craftBase = new PFTCleanCraft();
                            craftBase.Init();
                        }

                        break;


                    default:
                        craftBase = new PFTCleanCraft();
                        break;
                }
                if (flag1 == false)
                {
                    Log4netRecord.ErrorLog.Fatal("工艺配方加载失败:" + craftBase.fileFullPath);
                    return false;
                }
                craftBase.fileFullPath = paramPathSet.CraftPath + _Currentmaterial.GetDescription() + "\\" + _CurrentCraft + ".json";
            }
            // craftBase.fileFullPath = paramPathSet.CraftPath + _Currentmaterial.ToString() + "\\" + _CurrentCraft + ".cfts";
            //加载配方的检测流程。
            //flag1 = ReadProject(); // 加载配方后，默认添加一个初始编号为0的产品。
            //SaveProject();

            catch (Exception ex)
            {
                flag1 = false;
                Log4netRecord.ErrorLog.Debug(ex);
            }
            return flag1;
        }
        object NewMujuanhao_lock = new object();
        public bool NewMujuanhao(string _currentProduct, string _currentMojuanhao, MaterialCrafEnum _materialCrafEnum)
        {
            string error;
            return NewMujuanhao(_currentProduct, _currentMojuanhao, _materialCrafEnum, out error);
        }

        /// <summary>带失败原因的新建膜卷：异常不再只写日志，调用方可以展示给操作者。</summary>
        public bool NewMujuanhao(string _currentProduct, string _currentMojuanhao, MaterialCrafEnum _materialCrafEnum, out string error)
        {
            error = null;
            lock (NewMujuanhao_lock)
            {
                try
                {
                    paramPathSet.UpdateParamPath(_currentProduct, _currentMojuanhao, _materialCrafEnum, m_ProcessList, systemInfo.IsHostMachine);

                    server_DiecuttingCutPLC?.SetLogPath(paramPathSet.ProductMojuanPath);
                    server_五金模切PLC?.SetLogPath(paramPathSet.ProductMojuanPath);
                    cutOverlayPLCManage?.SetLogPath(paramPathSet.ProductMojuanPath);

                    string strPath = paramPathSet.ProductMojuanPath;
                    Directory.CreateDirectory(strPath);

                    TJBJInfo.TJBJInfoPath = Path.Combine(strPath, "CCD停机报警信息记录.ini");
                    LogWorks.SetGrayScaleAdaptationLoggPath(Path.Combine(strPath, "GrayLog"));

                    //重开膜卷初始化主副机软件参数和信息路径
                    if (slaveMachineManager != null)
                    {
                        slaveMachineManager.ReadLogPath = Path.Combine(strPath, "主副机接收记录-接收MarkingSoftwareReceiveRecord.ini");
                        slaveMachineManager.SendLogPath = Path.Combine(strPath, "主副机发送记录-发送MarkingSoftwareSendRecord.ini");
                        slaveMachineManager.SendHeartPath = Path.Combine(strPath, "主副机发送记录-心跳MarkingSoftwareHeartbeatRecord.ini");
                        slaveMachineManager.SendDBPath = Path.Combine(strPath, "主副机发送记录-打标MarkingSoftwareMarkingRecord.ini");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Log4netRecord.ErrorLog.Error("新建膜卷失败：" + ex);
                    return false;
                }
            }
        }



        #region Process流程控制
        /// <summary>
        /// 创建流程。
        /// 兼容旧的 Project.CreateProcess() 写法，内部交给 Proc 创建 ProcessServices 并生成不重复 ProcessID。
        /// </summary>
        /// <returns>新流程 ID。</returns>
        public int CreateProcess()
        {
            return Process流程插件管理者.Create();
        }
        /// <summary>
        /// 按指定 ID 和名称创建流程。
        /// 兼容旧代码入口，实际创建逻辑由 Proc 管理。
        /// </summary>
        /// <param name="ID">流程 ID。</param>
        /// <param name="Name">流程名称。</param>
        public void CreateProcess(int ID, string Name)
        {
            Process流程插件管理者.Create(ID, Name);
        }
        /// <summary>
        /// 根据 ID 获取对应的 Process。
        /// 插件中 GetResultParams 等旧代码仍可通过该入口把结果写回流程输出表。
        /// </summary>
        /// <param name="id">流程 ID。</param>
        /// <returns>对应 Process；未找到返回 null。</returns>
        public Process GetProcessById(int id)
        {
            return Process流程插件管理者.GetProcess(id);
        }
        /// <summary>
        /// 根据 ID 获取对应的 ProcessServices。
        /// BuildProcessServiceList 创建流程后会通过该入口取回服务对象并绑定初始化/运行事件。
        /// </summary>
        /// <param name="id">流程 ID。</param>
        /// <returns>对应 ProcessServices；未找到返回 null。</returns>
        public ProcessServices GetProcessServiceById(int id)
        {
            return Process流程插件管理者.GetService(id);
        }
        /// <summary>
        /// 根据流程名称获取对应的 Process。
        /// 调试设置界面按流程名定位当前流程时使用该旧入口。
        /// </summary>
        /// <param name="name">流程名称。</param>
        /// <returns>对应 Process；未找到返回 null。</returns>
        public Process GetProcessByName(string name)
        {
            return Process流程插件管理者.GetProcess(name);
        }
        /// <summary>
        /// 根据流程名称获取对应的 ProcessServices。
        /// </summary>
        /// <param name="name">流程名称。</param>
        /// <returns>对应 ProcessServices；未找到返回 null。</returns>
        public ProcessServices GetProcessServiceByName(string name)
        {
            return Process流程插件管理者.GetService(name);
        }
        /// <summary>
        /// 清空指定 Process 下的模块对象，流程本身保留在流程列表中。
        /// 需要把流程从列表移除请使用 DeleteProcessService。
        /// </summary>
        /// <param name="proc">需要清空模块的流程对象。</param>
        public void ClearProcessModules(Process proc)
        {
            Process流程插件管理者.ClearProcessModules(proc);
        }
        /// <summary>
        /// 根据 Process 反查所属的 ProcessServices。
        /// </summary>
        /// <param name="proc">流程对象。</param>
        /// <returns>找到返回 ProcessServices；未找到返回 null。</returns>
        public ProcessServices GetProcessService(Process proc)
        {
            return Process流程插件管理者.GetService(proc);
        }
        /// <summary>
        /// 删除指定 ProcessServices。
        /// 内部会先请求流程线程安全退出，再释放模块并从 Proc.List 移除。
        /// </summary>
        /// <param name="procS">需要删除的流程服务。</param>
        /// <returns>流程已从列表移除返回 true；线程未退出或发生异常返回 false。</returns>
        public bool DeleteProcessService(ProcessServices procS)
        {
            return Process流程插件管理者.DeleteService(procS);
        }
        /// <summary>
        /// 清空所有 ProcessServices 和 Process。
        /// 主要在重新加载流程、切换流程结构前使用；实际线程关闭和模块释放由 Proc.DeleteAll 完成。
        /// </summary>
        public void DeleteALLProcess()
        {
            Process流程插件管理者.DeleteAll();
        }

        /// <summary>
        /// 获取所有流程运行状态。
        /// </summary>
        /// <returns>任一流程正在运行返回 true。</returns>
        public bool GetStates()
        {
            return Process流程插件管理者.GetStates();
        }

        /// <summary>
        /// 启动指定流程连续运行。
        /// </summary>
        /// <param name="processID">流程 ID。</param>
        public void StartRun(int processID)
        {
            Is_Run = true;
            Process流程插件管理者.Start(processID);
        }

        /// <summary>
        /// 指定流程执行一次。
        /// </summary>
        /// <param name="processID">流程 ID。</param>
        public void ExecuteOnce(int processID)
        {
            Process流程插件管理者.ExecuteOnce(processID);
        }

        /// <summary>
        /// 停止指定流程运行。
        /// </summary>
        /// <param name="processID">流程 ID。</param>
        public void StopRun(int processID)
        {
            Is_Run = false;
            Process流程插件管理者.Stop(processID);
        }

        /// <summary>
        /// 保存当前配方流程。
        /// Project 层只负责同步当前路径并判断 Proc.Save 返回值；具体模块参数和 Instance.xml 保存由 Proc 统一处理。
        /// </summary>
        public void SaveProject()
        {
            Process流程插件管理者.SetPath(paramPathSet.ProcessPath, paramPathSet.InstanceParamPath);
            if (!Process流程插件管理者.Save())
            {
                Log4netRecord.ErrorLog.Fatal("配方的检测流程保存失败:" + Process流程插件管理者.LastError);
            }
        }

        /// <summary>
        /// 读取当前配方流程。
        /// 先初始化业务设备基础数据，再把 baseProDevice.BuildProcessServiceList 作为标准流程构建动作传给 Proc.Load；
        /// Proc 内部完成清理旧流程、构建标准流程、读取 Instance.xml、自检合并、加载插件参数和读后自动保存。
        /// </summary>
        /// <returns>流程加载成功返回 true；失败时写 Fatal 并返回 false。</returns>
        public bool ReadProject()
        {
            try
            {
                baseProDevice.INIPro();

                bool ok = Process流程插件管理者.Load(paramPathSet.ProcessPath, paramPathSet.InstanceParamPath, baseProDevice.BuildProcessServiceList);
                if (!ok)
                {
                    Log4netRecord.ErrorLog.Fatal("配方的检测流程加载失败:" + Process流程插件管理者.LastError);
                }

                return ok;
            }
            catch (Exception ee)
            {
                Log4netRecord.ErrorLog.Fatal("配方的检测流程加载失败:" + ee);
                return false;
            }


            //FileStream fs = new FileStream(@"D:\testCode\mef\Frame_Core\Frame_Core\bin\x64\Debug\ProjectParams\Record1.data", FileMode.Open);
            //BinaryFormatter bf = new BinaryFormatter();
            //m_ProcessInfoList = (List<ProcessInfo>)bf.Deserialize(fs);

        }

        #endregion

    }
}
