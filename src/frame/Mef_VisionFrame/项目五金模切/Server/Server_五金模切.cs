using ControlBoard.服务类;
using Frame_Core;
using Frame_Core.流程汇总文件.产品类;
using FrameCommunity;
using FrameCommunity.Data;
using HalconDotNet;
using LogTool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切的业务服务：线程管理、运行控制、报警与状态栏。
    ///
    /// 工艺算法（裁图 / 极片 EA / 尺寸测量）还没有，因此 DataSave / DataProcess / Communication
    /// 这些线程先不启动——启动一条什么都不做的循环线程只会白烧 CPU。等对应工艺补齐时，
    /// 在 InitThread 里加一行 Start 即可，方法体已经留在下面。
    /// </summary>
    public partial class Server_五金模切 : ServerBase
    {
        public Model_五金模切 Data;

        public override event Action RefreshToolView;
        public override event Action InitView;
        public override event Action RefreshView;

        public Server_五金模切(Model_五金模切 modelBase)
            : base(modelBase)
        {
            Data = modelBase;

            // 信号通道先建好再起通讯线程：通讯线程一启动就会推进心跳与多次NG
            InitSignalChannels();
            InitPlcResultCallbacks();
            InitThread();
        }

        /// <summary>
        /// 目前只有"图像显示"这一条线程需要跑：界面每 5ms 取一次显示/保存队列。
        /// 报警与光源线程沿用基类，保证工具条上的光源开关可用。
        /// </summary>
        public override void InitThread()
        {
            base.InitThread();

            // 必须由 Server 启动：基类只启动了报警与光源，UC 里订阅的 PerformWork 不会自己跑
            thd_ImageShowAndSave.Start(5, "thd_ImageShowAndSave");

            // 心跳与结果脉冲：5ms 推进一次状态机，与激光模切同节拍
            thd_Communication.Start(5, "ThdCommunication");
            thd_Communication.PerformWork -= Communication;
            thd_Communication.PerformWork += Communication;

            // 合片：70ms 一次，A/B 两面按 EA 号配对后进结果队列
            thd_DataProcess.Start(70, "thd_DataProcess");
            thd_DataProcess.PerformWork -= DataProcess;
            thd_DataProcess.PerformWork += DataProcess;

            // 统计落盘：500ms 一次，与激光模切同节拍（数据量小，不必跟判定同频）
            thd_DataSave.Start(500, "thd_DataSave");
            thd_DataSave.PerformWork -= DataSave;
            thd_DataSave.PerformWork += DataSave;
        }

        #region 运行控制

        public override bool StartRun()
        {
            return ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                if (Project.Instance.Is_simulation)
                {
                    MessageBox.Show(" “仿真运行”状态禁止自动运行。");
                    return false;
                }

                foreach (ProcessServices item in Project.Instance.m_ProcessList)
                {
                    Project.Instance.StartRun(item.process.processInfo.ProcessID);
                }

                RefreshToolView?.Invoke();
                return true;
            });
        }

        public override bool StopRun()
        {
            return ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                Data.M_SimRunCommand = SimRunCommand.Stop;

                foreach (ProcessServices item in Project.Instance.m_ProcessList)
                {
                    Project.Instance.StopRun(item.process.processInfo.ProcessID);
                }

                // 停机立刻把三路结果写 0，PLC 靠这个值判断这一片还在不在检。
                // 放在 StopRun 之后，确保所有流程已停，不会再有帧开始写"待检"。
                // 心跳不在这里停：它只跟 PLC 通讯状态有关，停机仍继续翻转。
                ResetAllResults();

                RefreshToolView?.Invoke();
                return true;
            });
        }

        public override void Reset()
        {
            ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                Project.Instance.baseProDevice.INIProduct();
                ClearConsecutiveCounts();
                ResetAllResults();
                ClearSizeGroups();
                Data.Reset_DefectStation();
            });
        }

        public override void RunModelSwitch()
        {
            ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                if (Project.Instance.Is_simulation)
                {
                    // 切自动：清掉仿真数据，按当前膜卷重建生产数据路径
                    Project.Instance.Is_simulation = false;
                    Project.Instance.baseProDevice.InitProduct_Sim();

                    if (Project.Instance.Is_MojuanStart)
                    {
                        Project.Instance.NewMujuanhao(Project.Instance.systemInfo.RecipeName,
                            Project.Instance.systemInfo.currentMojuanhao, Project.Instance.systemInfo.CraftType);
                    }
                }
                else
                {
                    // 切仿真：数据目录固定 SimTest，不能继续往实机膜卷目录里写
                    Project.Instance.Is_simulation = true;
                    Project.Instance.NewMujuanhao(Project.Instance.systemInfo.RecipeName, "SimTest",
                        Project.Instance.systemInfo.CraftType);
                    Data.InitFilePath();
                }

                RefreshToolView?.Invoke();
            });
        }

        #endregion

        #region 膜卷

        public override bool StartFilmRoll(string filmRollName)
        {
            return ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                // 与激光模切同一套换卷流程：校验膜卷号 → 重建生产数据路径 → 清业务数据 → 复位相机回调
                if (!System.Text.RegularExpressions.Regex.IsMatch(filmRollName, "^[a-zA-Z0-9-]+$"))
                {
                    MessageBox.Show("膜卷号不合法，只能包含字母、数字、-，不能有空格");
                    return false;
                }

                string strPath = Project.Instance.paramPathSet.ProductRootPath + $"{filmRollName}";
                if (Directory.Exists(strPath))
                {
                    MessageBox.Show("膜卷号重复");
                    return false;
                }

                Project.Instance.Is_MojuanStart = true;
                Project.Instance.systemInfo.currentMojuanhao = filmRollName;
                Project.Instance.systemInfo.Save();

                // 重建 ParamPathSet：生产数据 CSV 与 Log4net 的 RunLog/CamLog/CommLog 都跟着膜卷走。
                // 少了这一步，五金的生产目录下不会生成日志目录，CSV 也还写在上一卷的路径里。
                Project.Instance.NewMujuanhao(Project.Instance.systemInfo.RecipeName, filmRollName, Project.Instance.systemInfo.CraftType);

                // 光源参数随膜卷/配方刷新，与激光模切一致
                UIDelegate.LightParamChange();

                // 清业务数据（图像队列、统计、合片队列、面板队列）
                Data.Init();
                // 连续 NG 的逐项计数跨卷没有意义，否则上一卷末尾的计数会算进新卷第一片
                ClearConsecutiveCounts();
                // 换卷把 PLC 侧的三路结果与心跳清回停止态，新卷从 0 开始
                ResetAllResults();
                // 上一卷没合算完的尺寸组跟着作废，否则新卷第一片会把它凑齐
                ClearSizeGroups();
                Project.Instance.baseProDevice.INIPro();
                Project.Instance.baseProDevice.INIProduct();

                // 相机回调计数清零 + 仿真标志复位 + 点检复位
                foreach (var item in Project.Instance.M_CameraRelevancies)
                {
                    item.Value.Item1.Camera.GrabCount = 0;
                    item.Value.Item2.m_ImageSimManage.InitSimFlag();
                    item.Value.Item2.m_SizePointInspectionManage.sizePointInspectionParam.sizePointInspectionWorkParam.ResetStartDJ();
                }

                RefreshToolView?.Invoke();
                InitView?.Invoke();
                return true;
            });
        }

        public override bool StopFilmRoll()
        {
            return ExecuteMethodClass.ExecuteMethodContainErrShow(() =>
            {
                if (Project.Instance.Is_Run)
                {
                    MessageBox.Show("CCD正在运行中，请先暂停");
                    return false;
                }

                Project.Instance.Is_MojuanStart = false;
                RefreshToolView?.Invoke();
                return true;
            });
        }

        #endregion

        #region 相机图像处理

        // 5 路相机各一个回调入口。方法名固定，是为了让 Descriptor 用方法组 += / -=，
        // 闭包 lambda 每次都会生成新的委托实例，-= 摘不掉上一次绑定的那个。

        /// <summary>模切主相机：裁图流程产出的原图 + 大图，大图进"大图"窗口。</summary>
        public void DieCutMainCamProcess(ProductBase productBase)
        {
            ProcessDieCutImage(CamRole.模切主相机, 0, productBase);
        }

        public void DieCutSubCamProcess(ProductBase productBase)
        {
            ProcessDieCutImage(CamRole.模切从相机, 0, productBase);
        }

        /// <summary>极片 EA：裁图流程切出的 EA 图，进"EA图"窗口。</summary>
        public void DieCutMainCamEaProcess(ProductBase productBase)
        {
            ProcessEaImage(CamRole.模切主相机, 0, productBase);
        }

        public void DieCutSubCamEaProcess(ProductBase productBase)
        {
            ProcessEaImage(CamRole.模切从相机, 0, productBase);
        }

        // 尺寸三路的回调在 Server_五金模切.尺寸工位.cs：它们要等齐三路再判，
        // 不属于"收到一张显示一张"的投递逻辑。

        /// <summary>
        /// 裁图流程产出两张图：showHalconGroup[0] 原图、showHalconGroup[1] 大图。
        /// 界面只显示大图；原图当前没有存档通道（五金不需要激光那套原图留档），直接释放。
        /// </summary>
        private void ProcessDieCutImage(CamRole role, int occurrence, ProductBase productBase)
        {
            DieCutImageProduct product = productBase as DieCutImageProduct;
            if (product?.showHalconGroup == null || product.showHalconGroup.Count < 2) return;

            int camIndex = ResolveCamIndex(role, occurrence);
            if (camIndex < 0) return;

            HOperatorSet.CountSeconds(out product.detectionTime.Data_StartTime);

            // 原图：拼图输入，界面不显示但必须留档。入队后由显示线程保存并释放，
            // 不能在这里同步 Dispose——裁剪流程的 imageInfos 用的正是这张图。
            DeliverImage(camIndex, product.showHalconGroup[0], "原图", Data.Cams[camIndex].RawSave);

            HOperatorSet.CountSeconds(out product.detectionTime.Data_EndTime);
            WriteDetectionTime(camIndex, 0, product.showHalconGroup[1], product.detectionTime);

            DeliverImage(camIndex, product.showHalconGroup[1], "大图", Data.Cams[camIndex].MainShow, 图像流.大图);
        }

        /// <summary>EA 图：一张。判定的 NG 标记由调用方先写在 window 上，这里不再覆盖。</summary>
        private void ProcessEaImage(CamRole role, int occurrence, ProductBase productBase)
        {
            DieCutEAProduct product = productBase as DieCutEAProduct;
            ShowHalconWindow window = product?.showHalconGroup?.FirstOrDefault();
            if (window == null) return;

            int camIndex = ResolveCamIndex(role, occurrence);
            if (camIndex < 0) return;

            HOperatorSet.CountSeconds(out product.detectionTime.Data_StartTime);

            DefectFrameResult result = JudgeAppearanceDefects(camIndex, product, window);
            window.m_bIsNG = result.IsNG;

            // 本片外观结论直接回传 PLC：上线/下线各写各的，不需要等合片、也不需要 PLC 请求。
            // 宁可先给结论再落盘，机构在等这个值。
            bool isUp = role == CamRole.模切主相机;
            WriteAppearanceResult(isUp, result.IsNG);
            WriteAppearanceSendLog(isUp, result.IsNG, window.m_nImageFrameID);

            // 明细落盘：这一帧检出的每个缺陷一行（没有缺陷时为空表，与激光一致）
            string detailPath = role == CamRole.模切主相机 ? Data.Station1_ALineCamPath_Defect : Data.Station1_BLineCamPath_Defect;
            FileOperationCSV.WriteData_CSV(detailPath, Data.Station1_DefectTitle.ToArray(), product.productDataResult.DefectData, Service_Camera.GetCam_Name(camIndex));

            // 右侧"EA 缺陷明细"面板：与 CSV 同一批行，界面线程取走显示
            QueuePackage<List<RowData>> detailQueue = role == CamRole.模切主相机 ? Data.UpCamDefectShowQueue : Data.DownCamDefectShowQueue;
            foreach (List<RowData> row in product.productDataResult.DefectData)
            {
                detailQueue.Enqueue(row, 200);
            }

            // 结果入 A/B 队列，由合片线程按 EA 号配对；统计在合片后按“一片”计，不在这里算
            product.productDataResult.m_nImageFrameID = window.m_nImageFrameID;
            product.productDataResult.m_nProductID = window.m_nProductID;
            product.productDataResult.IsNG = result.IsNG;
            product.productDataResult.IsSevereDefect = result.IsSevere;
            // 片级统计只取这一项，见 计缺陷片分布
            product.productDataResult.highestPriorityNgDefect = result.HighestPriority;

            if (role == CamRole.模切主相机)
            {
                Data.ALine_DieCutEAProduct.Enqueue(product.productDataResult, 100);
            }
            else
            {
                Data.BLine_DieCutEAProduct.Enqueue(product.productDataResult, 100);
            }

            HOperatorSet.CountSeconds(out product.detectionTime.Data_EndTime);
            WriteDetectionTime(camIndex, 1, window, product.detectionTime);

            DeliverImage(camIndex, window, "EA图", Data.Cams[camIndex].EaShow, 图像流.EA图);
        }

        /// <summary>相机下标按角色解析，不写死 0..4——画像换顺序时这里不用改。</summary>
        private static int ResolveCamIndex(CamRole role, int occurrence)
        {
            int camIndex;
            if (!Project.Instance.baseProDevice.TryGetCamIndex(role, occurrence, out camIndex)) return -1;
            return camIndex;
        }

        /// <summary>
        /// 检测耗时落盘。列与激光模切完全一致（时间/帧号/EA/图像处理/数据处理/总耗时），
        /// 其中图像时间由流程自己记，数据处理时间由调用方在入口/出口各打一次点。
        /// 流：0 = 大图/原图，1 = EA 图。
        /// </summary>
        private void WriteDetectionTime(int camIndex, int flow, ShowHalconWindow window, DetectionTime detectionTime)
        {
            if (Data.DetectionTimePath == null || camIndex < 0 || camIndex >= Data.DetectionTimePath.Length) return;
            if (window == null) return;

            string path = Data.DetectionTimePath[camIndex][flow];
            if (string.IsNullOrEmpty(path)) return;

            double imageTime = ((detectionTime.Image_EndTime - detectionTime.Image_StartTime) * 1000).D;
            double dataTime = ((detectionTime.Data_EndTime - detectionTime.Data_StartTime) * 1000).D;
            double sumTime = ((detectionTime.Data_EndTime - detectionTime.Image_StartTime) * 1000).D;

            List<RowData> row = new List<RowData>
            {
                new RowData { dataStr = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") },
                new RowData { dataStr = window.m_nImageFrameID.ToString() },
                new RowData { dataStr = window.m_nProductID.ToString() },
                new RowData { dataStr = imageTime.ToString("0.00") },
                new RowData { dataStr = dataTime.ToString("0.00") },
                new RowData { dataStr = sumTime.ToString("0.00") },
            };

            FileOperationCSV.WriteData_CSV(path, Data.DetectionTimeTitle.ToArray(), row.ToArray(), Service_Camera.GetCam_Name(camIndex));
        }

        /// <summary>图像流：决定取哪一套保存/显示参数。</summary>
        private enum 图像流
        {
            原图,
            大图,
            EA图,
        }

        /// <summary>
        /// 给一张图补上相机名、帧号、存图路径，然后塞进显示队列。
        /// 显示线程取走后按 ShowQueue → SaveQueue → 截图 的顺序处理（见 CamStreamUnit.Handle）。
        ///
        /// 保存/显示参数必须按"哪个流"取，不能一律用原图的：
        /// 线扫相机在配置里关掉了"显示原图"（IsShow=false），但大图/EA 图是开的。
        /// 统一取原图参数会让大图和 EA 图被判定为"只存不显"，界面永远是黑的。
        /// </summary>
        private void DeliverImage(int camIndex, ShowHalconWindow window, string subDir, ImageQueueData showQueue, 图像流 flow = 图像流.原图)
        {
            if (window == null) return;
            if (camIndex < 0 || camIndex >= Data.Cams.Length) return;

            // 图像必须还是有效对象：被别的队列释放掉的图入队只会得到一块黑屏，
            // 而且显示失败是被 RepaintCam 内层 catch 吞掉的，界面上看不到任何线索。
            if (window.m_hImage == null || !window.m_hImage.IsInitialized())
            {
                MessageNotify.AddErrorLog($"【{Service_Camera.GetCam_Name(camIndex)}】-【{subDir}】图像已失效，跳过显示，帧号{window.m_nImageFrameID}");
                return;
            }

            string camName = Service_Camera.GetCam_Name(camIndex);
            // 保存配置缺失时用默认值兜底：这里直接解引用，配置少一路就会抛在回调里，图既不显示也不保存
            ImageSaveParamConfig saveParam = Project.Instance.M_NewImageSaveParamer.ImageSaveConfigItems.GetSaveParam(camIndex)
                                             ?? new ImageSaveParamConfig();

            ImageSaveFormat imageFormat;
            bool isShow;
            ImageSaveSelect saveMode;
            switch (flow)
            {
                case 图像流.大图:
                    imageFormat = saveParam.BigImageSaveParamer.ImageFormat;
                    isShow = saveParam.BigImageSaveParamer.IsShow;
                    saveMode = saveParam.BigImageSaveParamer.SaveMode;
                    break;
                case 图像流.EA图:
                    imageFormat = saveParam.EAImageSaveParamer.ImageFormat;
                    isShow = saveParam.EAImageSaveParamer.IsShow;
                    saveMode = saveParam.EAImageSaveParamer.SaveMode;
                    break;
                default:
                    imageFormat = saveParam.SourceImageSaveParamer.ImageFormat;
                    isShow = saveParam.SourceImageSaveParamer.IsShow;
                    saveMode = saveParam.SourceImageSaveParamer.SaveMode;
                    break;
            }

            window.m_strCamName = camName;
            window.WindowName = camName;
            window.m_messageShowInfos.Add(new MessageShowInfo() { m_strMessage = $"帧号：{window.m_nImageFrameID}" });

            string fileName = window.m_nImageFrameID.ToString().PadLeft(5, '0') + "_" + window.m_nProductID + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            window.m_imageSavePathInfo.Path = Path.Combine(Project.Instance.paramPathSet.ProductMojuanPath, camName, subDir);
            window.m_imageSavePathInfo.FileName = fileName;
            window.m_imageSavePathInfo.FileFormat = imageFormat;
            window.m_imageSavePathInfo.IsShow = isShow;
            window.m_imageSavePathInfo.IsSave = SaveImageTool.GetIsSave(saveMode, window.m_bIsNG);

            window.m_imageSavePathInfo.ScreenshotPath = Path.Combine(Project.Instance.paramPathSet.ProductMojuanPath, camName, subDir + "截图");
            window.m_imageSavePathInfo.ScreenshotFileName = fileName;
            window.m_imageSavePathInfo.ScreenshotFileFormat = saveParam.ScreenshotSaveParamer.ImageFormat;
            window.m_imageSavePathInfo.IsHighDefinitionCapture = saveParam.ScreenshotSaveParamer.IsHighDefinitionCapture;
            window.m_imageSavePathInfo.IsScreenshot = SaveImageTool.GetIsSave(saveParam.ScreenshotSaveParamer.SaveMode, window.m_bIsNG);

            showQueue.DataQueue.Enqueue(window);
        }

        #endregion

        #region 仿真

        /// <summary>
        /// 仿真取图。由 Form_Main 的 500ms 定时器驱动（不是线程），三个命令与激光模切一致：
        ///   SimPrev / SimNext —— 取一张，执行一轮全部流程，然后复位停止，用于单步验证；
        ///   SimRun          —— 每个节拍取一张，流程线程自己在跑。
        ///
        /// 连续仿真额外加了一道闸门：尺寸工位还有没合算完的片就先不喂图，否则新的三路会
        /// 覆盖掉上一片的组内容（仿真是开环的，没有 PLC 那一层节拍保护）。
        /// </summary>
        public override void SimLoadImage()
        {
            if (!Project.Instance.Is_simulation) return;

            ExecuteMethodClass.ExecuteMethod(() =>
            {
                switch (Data.M_SimRunCommand)
                {
                    case SimRunCommand.SimPrev:
                        SimGrabByCam(SimControl.LoadPrevImage_Sim);
                        Data.M_SimRunCommand = SimRunCommand.Stop;
                        SimRunOnce();
                        break;

                    case SimRunCommand.SimNext:
                        SimGrabByCam(SimControl.LoadNextImage_Sim);
                        Data.M_SimRunCommand = SimRunCommand.Stop;
                        SimRunOnce();
                        break;

                    case SimRunCommand.SimRun:
                        if (!IsSizeStationIdle) return;
                        SimGrabByCam(SimControl.LoadNextImage_Sim);
                        break;
                }
            });
        }

        /// <summary>仿真取图函数的签名（带 ref，所以不能用 Func 委托）。</summary>
        private delegate bool SimGrabDelegate(ImageSimManage manage, ref QueuePackage<ImageInfo> queue, string source);

        /// <summary>五路并行取图：取到就交给该路的仿真参数，进 alreadyDealGrabImage。</summary>
        private void SimGrabByCam(SimGrabDelegate grab)
        {
            Parallel.ForEach(Project.Instance.baseProDevice.m_DicCamOperaParam_Sim, item =>
            {
                int camIndex = item.Key;
                string camName = ((CameraEnum)camIndex).ToString();

                try
                {
                    bool got = grab(Service_Camera.Get_CameraRelevancy(camIndex).m_ImageSimManage,
                                     ref item.Value.QueueGrabImage, camName + "仿真加载图片");
                    if (!got) return;   // 该路没有可取的图：不唤醒流程，避免空转

                    item.Value.CallBackImgDataDeal();
                }
                catch (Exception ex)
                {
                    Log4netRecord.ErrorLog.Error($"{camName} 仿真取图异常：{ex}");
                }
            });
        }

        /// <summary>单步：让每条流程各跑一次。</summary>
        private void SimRunOnce()
        {
            foreach (ProcessServices item in Project.Instance.m_ProcessList)
            {
                Project.Instance.ExecuteOnce(item.process.processInfo.ProcessID);
            }
        }

        #endregion

        #region 待补齐的工艺部分

        /// <summary>
        /// 工位统计落盘。判定线程已经把快照入队，这里只负责出队写 CSV，
        /// 所以写盘再慢也不会阻塞判定。
        /// </summary>
        public override void DataSave()
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                if (!Project.Instance.Is_MojuanStart) return;

                if (Data.DefectStation_BindingDatasSave.Count > 0)
                {
                    List<string[]> rows = Data.DefectStation_BindingDatasSave.Dequeue().ToDataList();
                    rows.Insert(0, Data.Station1_Title_Statistics.ToArray());
                    FileOperationCSV.WriteData_CSV(Data.Station1_StatisticsPath, rows, "缺陷检测工位数据统计");
                }

                if (Data.SizeStation_BindingDatasSave.Count > 0)
                {
                    List<string[]> rows = Data.SizeStation_BindingDatasSave.Dequeue().ToDataList();
                    rows.Insert(0, Data.Station3_Title_Statistics.ToArray());
                    FileOperationCSV.WriteData_CSV(Data.Station3_StatisticsPath, rows, "极片尺寸工位数据统计");
                }
            });
        }

        /// <summary>
        /// 合片：外观工位 A/B 两面按 EA 号配成一片。
        /// 片差 = 背面片数 - 正面片数（配方），配对成功后进结果队列等 PLC 取走。
        /// </summary>
        public override void DataProcess()
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                // 一个节拍里最多合 5 片，与激光模切一致；剩下的留到下个节拍
                for (int i = 0; i < 5; i++)
                {
                    if (!MergeOnce()) break;
                }
            });
        }

        /// <summary>返回 true 表示这一轮有进展（合片成功或丢了一片），可以继续下一轮。</summary>
        private bool MergeOnce()
        {
            if (Data.Craft == null) return false;

            int FrontSideCount = Data.Craft.Get_GlobalVariableItem("FrontSideCount")?.Value.ToInt() ?? 0;
            int BackSideCount = Data.Craft.Get_GlobalVariableItem("BackSideCount")?.Value.ToInt() ?? 0;
            int slice = BackSideCount - FrontSideCount;

            long AProductID = 0;
            long BProductID = 0;

            if (Data.ALine_DieCutEAProduct.Count > 0) AProductID = Data.ALine_DieCutEAProduct.Peek().m_nProductID;
            if (Data.BLine_DieCutEAProduct.Count > 0) BProductID = Data.BLine_DieCutEAProduct.Peek().m_nProductID;

            if (AProductID == 0 || BProductID == 0) return false;

            if (AProductID == BProductID - slice)
            {
                Station1Product product = new Station1Product();
                product.ProductID = AProductID;
                product.ALine_DieCutEAProduct = Data.ALine_DieCutEAProduct.Dequeue();
                product.BLine_DieCutEAProduct = Data.BLine_DieCutEAProduct.Dequeue();
                product.IsNG = product.ALine_DieCutEAProduct.IsNG || product.BLine_DieCutEAProduct.IsNG;

                Data.Station1_Product.Enqueue(product, 100);
                MessageNotify.AddCommLog($"缺陷工位——合片完成，产品ID{AProductID}");

                // 统计按“一片”计：A/B 任一面 NG 即这一片 NG
                DieCutJudge.DataStatistical(Data.Station1_StatisticalData, AProductID, product.IsNG);

                // 分布同样按“一片”计：取 A/B 两面中优先级最高的那一个 NG 项，
                // 不逐缺陷累加——否则一片上有 3 个缺陷就记 3 次，占比会超过 100%。
                CountDefectDistribution(product);

                Data.DefectStation_BindingDatasSave.Enqueue(
                    Model_五金模切.Snapshot(Data.DefectCounts, Data.Station1_StatisticalData), 1);

                return true;
            }

            if (AProductID > BProductID - slice)
            {
                var dropped = Data.BLine_DieCutEAProduct.Dequeue();
                MessageNotify.AddErrorLog($"缺陷工位——A线扫产品ID大于B线扫，移除B线扫产品{dropped?.m_nProductID}");
                return true;
            }

            MessageNotify.AddErrorLog($"缺陷工位——A线扫产品ID小于B线扫产品ID（A{AProductID}, B{BProductID}, 片差{slice}）");
            return false;
        }

        public override void ClearCutData(bool isSendPLC)
        {
            // 五金没有裁切工位；这里清的是缺陷/尺寸两个工位的结果与队列
            Data.Reset_DefectStation();
            ClearConsecutiveCounts();
            ResetAllResults();
            ClearSizeGroups();
        }

        public override void AlarmReset()
        {
        }

        public override void SendStopAlarm(int index)
        {
        }

        public override void SendStopAlarm(string msg)
        {
        }

        public override void SendAlarm(int index)
        {
        }

        public override void SendAlarm(string msg)
        {
        }

        /// <summary>
        /// 底部状态栏。内容与激光模切一致（用户、相机、光源、控制卡、内存、CPU、磁盘），
        /// 区别是 PLC 显示的是五金自己的 CIP 实例，不是激光那台 D 寄存器 PLC。
        /// </summary>
        public override List<StatusLabelItem> GetStatusBarInfo()
        {
            return ExecuteMethodClass.ExecuteMethod(() =>
            {
                List<StatusLabelItem> statusLabelItems = new List<StatusLabelItem>();

                Project.Instance.userManage.MonitorLogonOutTime();

                // 用户类型/用户ID 不再占状态栏位置：登录态由用户徽标（Form_Main.RefreshUserBadge）呈现，
                // 状态栏只保留设备连接与资源占用，横向空间不够时也不会挤掉设备状态。
                foreach (var item in Project.Instance.M_CamParam.l_CameraDevs)
                {
                    statusLabelItems.Add(new StatusLabelItem() { Text = item.CameraParameter.CameraName, Status = item.ConnStatus ? StatusLevel.Normal : StatusLevel.Alarm });
                }

                foreach (var item in Project.Instance.M_LightControllerParam.l_LightControllerDevs)
                {
                    statusLabelItems.Add(new StatusLabelItem() { Text = item.LightControllerParameter.LightControllerName, Status = item.ConnStatus ? StatusLevel.Normal : StatusLevel.Alarm });
                }

                statusLabelItems.Add(new StatusLabelItem()
                {
                    Text = "控制卡",
                    Status = ServiceCardManage.Instance.GetIControlCard()?.IsConnnected() != 0 ? StatusLevel.Normal : StatusLevel.Alarm
                });

                // 五金自己的 PLC。取错实例（取到激光那台）会导致状态永远是红的或永远绿，现场极难发现。
                statusLabelItems.Add(new StatusLabelItem()
                {
                    Text = "PLC",
                    Status = Project.Instance.server_五金模切PLC?.InitState == true ? StatusLevel.Normal : StatusLevel.Alarm
                });

                MyMetrics.GetPrivateWorkingSetMB();
                MyMetrics.GetCpuPercentLikeTaskManager();
                statusLabelItems.Add(new StatusLabelItem() { Text = $"当前运行内存：{MyMetrics.fWorkingSetMBValue.ToString("00000.00")} MB", Status = StatusLevel.Normal });
                statusLabelItems.Add(new StatusLabelItem() { Text = $"当前CPU：{MyMetrics.fCpuValue.ToString("0000.00")}%", Status = StatusLevel.Normal });

                StateManage.CheckDriveFreeSpace();
                statusLabelItems.Add(new StatusLabelItem() { Text = $"磁盘剩余：{StateManage.DiskSpace}", Status = StateManage.DiskInterval });

                int index = 0;
                foreach (var item in statusLabelItems)
                {
                    item.Key = index++.ToString();
                }
                return statusLabelItems;
            }) ?? new List<StatusLabelItem>();
        }

        #endregion
    }
}
