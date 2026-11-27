using ControlBoard.服务类;
using Frame_Core;
using Frame_MetalRun;
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

            // 运行状态机最后建：它引用本对象作为宿主，此时线程/通道已就绪
            RunControl = new MetalRunController(this);
            RunControl.StateChanged += delegate { RefreshToolView?.Invoke(); };
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

        /// <summary>五金唯一的运行状态源。工具栏、状态栏、宿主都只读它。</summary>
        public MetalRunController RunControl { get; private set; }

        /// <summary>低层启动：启动发布器与全部流程线程。仅由运行宿主调用。</summary>
        public override bool StartRun()
        {
            try
            {
                int frontSide = Data.Craft?.Get_GlobalVariableItem("FrontSideCount")?.Value.ToInt() ?? 0;
                int backSide = Data.Craft?.Get_GlobalVariableItem("BackSideCount")?.Value.ToInt() ?? 0;
                if (frontSide == 0 && backSide == 0)
                {
                    MessageNotify.AddErrorLog("五金模切：正面/背面片数均为 0，缺陷工位只能靠 A/B 同号合片，配不上时良率不会增长");
                }

                StartResultPublisher();
                foreach (ProcessServices item in Project.Instance.m_ProcessList)
                {
                    Project.Instance.StartRun(item.process.processInfo.ProcessID);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error("五金启动流程失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 低层硬停：停流程 → 停发布器 → 三路写 0 → INIProduct（唯一一次代际 +1、清帧/EA、序号归零）
        /// → 清结果队列/尺寸组。仅由结束膜卷/回滚调用；暂停绝不能走这里。
        /// </summary>
        public override bool StopRun()
        {
            return StopProductionOnly(out _) && ResetProductionAfterStop(out _);
        }

        /// <summary>阶段一：只负责停住流程线程与发布器；失败说明生产可能仍在运行。</summary>
        private bool StopProductionOnly(out string reason)
        {
            reason = null;
            try
            {
                foreach (ProcessServices item in Project.Instance.m_ProcessList)
                {
                    Project.Instance.StopRun(item.process.processInfo.ProcessID);
                }

                StopResultPublisher();
                return true;
            }
            catch (Exception ex)
            {
                reason = "停止流程/发布器异常：" + ex.Message;
                Log4netRecord.ErrorLog.Error("五金停止流程失败", ex);
                return false;
            }
        }

        /// <summary>阶段二：生产已停后的复位与清场；失败不回退到可继续状态。</summary>
        private bool ResetProductionAfterStop(out string reason)
        {
            reason = null;
            try
            {
                ResetAllResults();

                if (!Project.Instance.baseProDevice.INIProduct())
                {
                    reason = "生产已停止，但 INIProduct 复位失败，请重启软件。";
                    Log4netRecord.ErrorLog.Error("五金停止流程时 INIProduct 失败");
                    return false;
                }

                foreach (PlcResultChannel channel in AllResultChannels) channel.Clear();
                ClearSizeGroups();
                return true;
            }
            catch (Exception ex)
            {
                reason = "停止后清场异常：" + ex.Message;
                Log4netRecord.ErrorLog.Error("五金停止后清场失败", ex);
                return false;
            }
        }

        public override void Reset()
        {
            // 五金总开关是开始/结束膜卷；软复位按钮已隐藏，这里不再改运行状态。
            Log4netRecord.RunLog.Info("五金模切不再支持 CCD 软件复位，请使用【结束膜卷】/【开始膜卷】。");
        }

        #endregion

        #region 膜卷

        /// <summary>兼容旧调用：界面新代码直接走 RunControl.StartFilmRoll。</summary>
        public override bool StartFilmRoll(string filmRollName)
        {
            if (RunControl == null)
            {
                MessageBox.Show("运行控制器未初始化");
                return false;
            }

            string error;
            bool ok = RunControl.StartFilmRoll(filmRollName, out error);
            if (!ok) MessageBox.Show(error ?? "开始膜卷失败");
            return ok;
        }

        /// <summary>兼容旧调用：界面新代码直接走 RunControl.EndFilmRoll。</summary>
        public override bool StopFilmRoll()
        {
            if (RunControl == null) return false;

            MetalRunPending pending;
            string error;
            EndRollResult result = RunControl.EndFilmRoll(false, out pending, out error);

            if (result == EndRollResult.NeedsConfirm)
            {
                string message = "当前还有未处理项：\n\n" + pending.ToDisplayText() +
                                 "\n\n结束膜卷将丢弃这些数据，是否继续？";
                if (MessageBox.Show(message, "结束膜卷", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return false;

                result = RunControl.EndFilmRoll(true, out pending, out error);
            }

            if (result == EndRollResult.Failed)
            {
                MessageBox.Show(error ?? "结束膜卷失败");
                return false;
            }

            return result == EndRollResult.Completed;
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
            SafeProcessEaImage(CamRole.模切主相机, 0, productBase);
        }

        public void DieCutSubCamEaProcess(ProductBase productBase)
        {
            SafeProcessEaImage(CamRole.模切从相机, 0, productBase);
        }

        /// <summary>
        /// 外观判定的统一结算出口。
        ///
        /// 这里不是"记录异常"，而是"异常也必须给出结论"：
        /// 少一条结论 = PLC 少一拍，外部表现就是机构停在等结果，比判错更严重。
        /// 所以异常一律结算为 NG。
        /// </summary>
        private void SafeProcessEaImage(CamRole role, int occurrence, ProductBase productBase)
        {
            DieCutEAProduct ea = productBase as DieCutEAProduct;
            int frameId = (int)(ea?.showHalconGroup?.FirstOrDefault()?.m_nImageFrameID ?? 0);

            try
            {
                ProcessEaImage(role, occurrence, productBase);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error($"五金外观判定异常，按 NG 结算：{ex}");

                bool isUp = role == CamRole.模切主相机;
                PlcResultChannel channel = isUp ? UpResultChannel : DownResultChannel;

                // 已经入过结论就不能再补：重复入队会让 PLC 把两片的结论错位。
                // 判定异常多发生在入队之后的落盘/界面环节，必须判重。
                if (frameId > 0 && !HasConclusion(channel, frameId))
                {
                    EnqueueAppearanceResult(isUp, frameId, true);
                    Frame_Core.业务类.五金模切告警.Raise(
                        Frame_Core.业务类.五金模切告警.帧结算异常,
                        "帧结算异常",
                        $"帧 {frameId} 判定抛出异常，已按 NG 结算并回传。{ex.Message}");
                }
            }
        }

        /// <summary>队列里是否已有该帧的结论（不含待检）。仅异常路径调用，队列深度上限 64。</summary>
        private static bool HasConclusion(PlcResultChannel channel, int frameId)
        {
            foreach (PlcResultItem item in channel.Snapshot())
            {
                if (item.FrameId == frameId && item.Value != PlcResult.Pending) return true;
            }
            return false;
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
            EnqueueAppearanceResult(isUp, (int)window.m_nImageFrameID, result.IsNG);
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

        // 旧仿真取图（SimLoadImage / SimGrabByCam / SimRunOnce）已废弃。
        // 仿真改由 Frame_Simulation 的 SimCamera 从相机队列送图，与真机同一条路径。





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

        /// <summary>返回 true 表示这一轮合了一片，可以继续下一轮。</summary>
        private bool MergeOnce()
        {
            if (Data.Craft == null) return false;
            if (Data.ALine_DieCutEAProduct.Count == 0) return false;
            if (Data.BLine_DieCutEAProduct.Count == 0) return false;

            // 按到达顺序配对，不看帧号。
            // 上下线帧号只保证"下线不超前"（LineScanFrameSequencer.TryNextDown），两者之间的在途
            // 片数 = 相机间距 / 皮带速度，是不定值。按帧号相等配对会把不同片配成一对，且无法察觉。
            ProductDataResult aLine = Data.ALine_DieCutEAProduct.Dequeue();
            ProductDataResult bLine = Data.BLine_DieCutEAProduct.Dequeue();
            bool isNG = aLine.IsNG || bLine.IsNG;

            // 统计键沿用上线帧号，与上线结果点位口径一致
            long frameId = aLine.m_nImageFrameID;
            MessageNotify.AddCommLog(
                $"缺陷工位——合片完成，上线帧号{frameId}，下线帧号{bLine.m_nImageFrameID}");

            // 统计按“一片”计：A/B 任一面 NG 即这一片 NG
            DieCutJudge.DataStatistical(Data.Station1_StatisticalData, frameId, isNG);

            // 分布同样按“一片”计：取 A/B 两面中优先级最高的那一个 NG 项，
            // 不逐缺陷累加——否则一片上有 3 个缺陷就记 3 次，占比会超过 100%。
            CountDefectDistribution(aLine, bLine);

            Data.DefectStation_BindingDatasSave.Enqueue(
                Model_五金模切.Snapshot(Data.DefectCounts, Data.Station1_StatisticalData), 1);

            return true;
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
