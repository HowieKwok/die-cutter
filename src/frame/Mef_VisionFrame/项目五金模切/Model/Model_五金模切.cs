using Frame_Core;
using FrameCommunity;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 一路相机的全部图像队列：主图为相机原图（将来的裁图流程会用大图替换），
    /// 主从模切相机另外还有 EA 图与 NG 缺陷图。
    ///
    /// 用一个小对象聚合，而不是像激光模切那样按 Cam1_SourceImage_ImageShow 逐字段声明：
    /// 那样 5 路相机要写 60 多个字段、60 多行 Dispose，新增一路就要再抄 12 行。
    /// 这里队列在构造时全部建好（空队列开销可忽略），界面只决定"哪条流接到哪个格子"，
    /// 因此没有 EA 的尺寸相机也不需要特判。
    /// </summary>
    public sealed class CamImageQueues : IDisposable
    {
        /// <summary>原图：只保存不显示（拼图输入，界面不展示）。</summary>
        public readonly ImageQueueData RawSave;
        public readonly ImageQueueData RawDispose;

        public readonly ImageQueueData MainShow;
        public readonly ImageQueueData MainSave;
        public readonly ImageQueueData MainDispose;
        public readonly ImageQueueData MainScreenshotSave;
        public readonly ImageQueueData MainHighScreenshot;

        public readonly ImageQueueData EaShow;
        public readonly ImageQueueData EaSave;
        public readonly ImageQueueData EaDispose;
        public readonly ImageQueueData EaScreenshotSave;
        public readonly ImageQueueData EaHighScreenshot;

        public readonly ImageQueueData NgShow;
        public readonly ImageQueueData NgSave;
        public readonly ImageQueueData NgDispose;

        private readonly ImageQueueData[] all;

        private CamImageQueues(string camName)
        {
            RawSave = new ImageQueueData(camName);
            RawDispose = new ImageQueueData(camName);

            MainShow = new ImageQueueData(camName);
            MainSave = new ImageQueueData(camName);
            MainDispose = new ImageQueueData(camName);
            MainScreenshotSave = new ImageQueueData(camName);
            MainHighScreenshot = new ImageQueueData(camName);

            EaShow = new ImageQueueData(camName);
            EaSave = new ImageQueueData(camName);
            EaDispose = new ImageQueueData(camName);
            EaScreenshotSave = new ImageQueueData(camName);
            EaHighScreenshot = new ImageQueueData(camName);

            NgShow = new ImageQueueData(camName);
            NgSave = new ImageQueueData(camName);
            NgDispose = new ImageQueueData(camName);

            all = new[]
            {
                RawSave, RawDispose,
                MainShow, MainSave, MainDispose, MainScreenshotSave, MainHighScreenshot,
                EaShow, EaSave, EaDispose, EaScreenshotSave, EaHighScreenshot,
                NgShow, NgSave, NgDispose,
            };
        }

        public static CamImageQueues Create(string camName)
        {
            return new CamImageQueues(camName);
        }

        /// <summary>清空并释放所有队列里残留的图（换产品/重载时调用）。</summary>
        public void Dispose()
        {
            foreach (ImageQueueData queue in all)
            {
                queue.Dispose();
            }
        }
    }

    /// <summary>
    /// 五金模切的工位数据：目前只有 5 路相机的图像队列。
    /// 缺陷/尺寸统计、CSV 路径等按工艺补齐后加在这里，不要放回 Server——Server 只做投递。
    /// </summary>
    public class Model_五金模切 : ModelBase
    {
        public CraftParam_五金模切 Craft
        {
            get { return Project.Instance.craftBase as CraftParam_五金模切; }
        }

        /// <summary>按下标（= 物理相机下标）索引的图像队列表。</summary>
        public CamImageQueues[] Cams { get; private set; }

        public Model_五金模切()
        {
            int count = DeviceProfileRegistry.Resolve(MaterialCrafEnum.五金模切).CameraCount;
            Cams = new CamImageQueues[count];
            for (int i = 0; i < count; i++)
            {
                Cams[i] = CamImageQueues.Create(Service_Camera.GetCam_Name(i));
            }
        }

        public override void Init()
        {
            // 必须在清队列之前重建路径：换卷后 ProductMojuanPath 已经变了，
            // 不重建的话所有过程数据 CSV（缺陷明细/尺寸结果/发送日志/统计/检测耗时）都写进上一卷目录。
            InitFilePath();

            for (int i = 0; i < Cams.Length; i++)
            {
                Cams[i].Dispose();
            }

            Reset_DefectStation();
            Reset_StationStatistics();
        }

        #region 右侧数据区

        /// <summary>缺陷工位统计表：检测项 / 数量 / 占比。</summary>
        public BindingList<QualityData> DefectStation_BindingDatas { get; } = new BindingList<QualityData>();

        /// <summary>尺寸工位统计表：检测项 / 数量 / 占比。</summary>
        public BindingList<QualityData> SizeStation_BindingDatas { get; } = new BindingList<QualityData>();

        /// <summary>两个工位的总数 / OK / NG / 良率。</summary>
        public StatisticalData Station1_StatisticalData { get; set; } = new StatisticalData();
        public StatisticalData Station3_StatisticalData { get; set; } = new StatisticalData();

        /// <summary>尺寸曲线：键是配方里的尺寸项 Name。</summary>
        public Dictionary<string, ScotPotUIUser> chartUIUser { get; } = new Dictionary<string, ScotPotUIUser>();

        /// <summary>
        /// 缺陷分布计数（累计）。流程线程写、界面线程读，所以用并发字典。
        /// 键是缺陷名称，值是该缺陷的累计次数。
        /// </summary>
        public ConcurrentDictionary<string, int> DefectCounts { get; } = new ConcurrentDictionary<string, int>();
        public ConcurrentDictionary<string, int> SizeDefectCounts { get; } = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// 统计表最上面三行的固定名称。与激光模切一致：这两张表不是纯缺陷分布，
        /// 前 3 行是"这一卷检了多少片、其中多少 OK/NG"，下面才是各检测项占了多少片。
        /// </summary>
        public const string StatTotal = "总数";
        public const string StatOk = "OK数";
        public const string StatNg = "NG数";

        /// <summary>
        /// 把累计计数刷进两张统计表。必须在界面线程调用（由 UC 的刷新定时器驱动），
        /// 不在这里做跨线程 Invoke——Server 不持有任何界面引用。
        /// </summary>
        public void ApplyCounts()
        {
            ApplyCountsTo(DefectStation_BindingDatas, DefectCounts, Station1_StatisticalData);
            ApplyCountsTo(SizeStation_BindingDatas, SizeDefectCounts, Station3_StatisticalData);
        }

        private static void ApplyCountsTo(BindingList<QualityData> rows, ConcurrentDictionary<string, int> counts, StatisticalData data)
        {
            double total = data?.SumupCount ?? 0;
            double okCount = data?.OKCount ?? 0;
            double ngCount = data?.NGCount ?? 0;

            // 配方里没有、流程却报出来的缺陷名追加到表尾。
            // 不追加的话它在表里"查无此项"，良率掉了却找不到是哪一类。
            foreach (KeyValuePair<string, int> item in counts)
            {
                if (rows.All(p => p.Name != item.Key))
                {
                    rows.Add(new QualityData { Name = item.Key, Count = 0, Ratio = "" });
                }
            }

            foreach (QualityData row in rows)
            {
                double count;
                if (row.Name == StatTotal) count = total;
                else if (row.Name == StatOk) count = okCount;
                else if (row.Name == StatNg) count = ngCount;
                else
                {
                    int value;
                    counts.TryGetValue(row.Name, out value);
                    count = value;
                }

                row.Count = (int)count;
                row.Ratio = row.Name == StatTotal ? "100%"
                    : total > 0 ? (count / total * 100).ToString("0.00") + "%" : "";
            }
        }

        /// <summary>
        /// 生成当前统计快照，供落盘使用。
        /// 与 <see cref="ApplyCountsTo"/> 同一套行序与占比算法（含前 3 行的总数/OK/NG），
        /// 保证 CSV 与界面看到的一致；在判定线程调用，所以只读并发字典、不碰 BindingList。
        /// </summary>
        public static List<QualityData> Snapshot(ConcurrentDictionary<string, int> counts, StatisticalData data)
        {
            List<QualityData> rows = new List<QualityData>();
            double total = data?.SumupCount ?? 0;
            double okCount = data?.OKCount ?? 0;
            double ngCount = data?.NGCount ?? 0;

            rows.Add(new QualityData { Name = StatTotal, Count = (int)total, Ratio = "100%" });
            rows.Add(new QualityData
            {
                Name = StatOk,
                Count = (int)okCount,
                Ratio = total > 0 ? (okCount / total * 100).ToString("0.00") + "%" : "",
            });
            rows.Add(new QualityData
            {
                Name = StatNg,
                Count = (int)ngCount,
                Ratio = total > 0 ? (ngCount / total * 100).ToString("0.00") + "%" : "",
            });

            foreach (KeyValuePair<string, int> item in counts)
            {
                rows.Add(new QualityData
                {
                    Name = item.Key,
                    Count = item.Value,
                    Ratio = total > 0 ? (item.Value / total * 100).ToString("0.00") + "%" : "",
                });
            }
            return rows;
        }

        #region 缺陷工位合片与结果发送

        /// <summary>外观工位 A/B 两面的判定结果队列，合片线程按 EA 号配对。</summary>
        public QueuePackage<ProductDataResult> ALine_DieCutEAProduct { get; } = new QueuePackage<ProductDataResult>();
        public QueuePackage<ProductDataResult> BLine_DieCutEAProduct { get; } = new QueuePackage<ProductDataResult>();

        /// <summary>合片完成的产品队列。只用于统计与界面，PLC 结果在判定时已回传，不等这里。</summary>


        public string Station1_ALineCamPath_Defect = "";
        public string Station1_BLineCamPath_Defect = "";
        public string Station3_Path_Defect = "";

        /// <summary>结果发送日志：每次给 PLC 发结果写一行，用于分析握手耗时与连续 NG。</summary>
        public string Station1_DefectCamSendPath = "";
        public string Station3_SizeCamSendPath = "";

        /// <summary>工位统计的落盘路径，由 DataSave 线程每 500ms 写一次快照。</summary>
        public string Station1_StatisticsPath = "";
        public string Station3_StatisticsPath = "";

        /// <summary>尺寸结果（每片的各项尺寸值）落盘路径。</summary>
        public string Station3_Path_Size = "";

        public List<string> Station1_Title_Statistics { get; } = new List<string> { "检测项", "数量", "占比" };
        public List<string> Station3_Title_Statistics { get; } = new List<string> { "检测项", "数量", "占比" };

        /// <summary>
        /// 尺寸结果表的列：时间 + 帧号 + 配方里的尺寸项名。
        ///
        /// 每次取都新建一份：判定线程（写 CSV、出表）与界面线程（建列、消费队列）会同时取它，
        /// 共用一个 List 实例并原地 Clear/AddRange，会在换配方那一瞬间读到半张表。
        /// 调用方一律 ToArray() 即取即用，不持有引用，所以每次分配没有副作用。
        /// </summary>
        public List<string> Station3_Title_Size
        {
            get
            {
                List<string> titles = new List<string> { "时间", "帧号" };
                if (Craft != null) titles.AddRange(Craft.Get_Station3_SizeNames());
                return titles;
            }
        }

        /// <summary>检测耗时表头与激光模切一致，便于两台机对比。</summary>
        public List<string> DetectionTimeTitle { get; } = new List<string>
        {
            "时间", "帧号", "EA", "图像处理", "数据处理", "总耗时"
        };

        /// <summary>
        /// 检测耗时 CSV 路径，下标是 [相机下标, 流]，流 0 = 大图/原图、1 = EA 图。
        /// 尺寸相机只有流 0，流 1 的路径留着不用。
        /// </summary>
        public string[][] DetectionTimePath { get; private set; } = new string[0][];

        /// <summary>
        /// 统计快照队列：判定线程入队、DataSave 线程落盘。
        /// 界面不吃这个队列（它读 BindingList），所以两条路互不干扰。
        /// </summary>
        public QueuePackage<List<QualityData>> DefectStation_BindingDatasSave { get; } = new QueuePackage<List<QualityData>>();
        public QueuePackage<List<QualityData>> SizeStation_BindingDatasSave { get; } = new QueuePackage<List<QualityData>>();

        /// <summary>
        /// 右侧"实时明细"面板的数据源：一行一条记录，由界面线程 500ms 取出刷面板。
        /// 用队列而不是直接推控件，Server 就不持有任何界面引用。
        /// </summary>
        public QueuePackage<List<RowData>> UpCamDefectShowQueue { get; } = new QueuePackage<List<RowData>>();
        public QueuePackage<List<RowData>> DownCamDefectShowQueue { get; } = new QueuePackage<List<RowData>>();
        public QueuePackage<List<RowData>> Station3_DefectShowQueue { get; } = new QueuePackage<List<RowData>>();
        public QueuePackage<List<RowData>> Station3_SizeShowQueue { get; } = new QueuePackage<List<RowData>>();

        /// <summary>结果发送日志的显示队列（与 CSV 同源），对应激光的"缺陷/尺寸发送数据"面板。</summary>
        public QueuePackage<List<RowData>> Station1_SendShowQueue { get; } = new QueuePackage<List<RowData>>();
        public QueuePackage<List<RowData>> Station3_SendShowQueue { get; } = new QueuePackage<List<RowData>>();

        public List<string> Station1_CamSendTitle { get; } = new List<string>
        {
            "时间", "线别", "帧号", "回传结果"
        };

        public List<string> Station3_CamSendTitle { get; } = new List<string>
        {
            "时间", "帧号", "回传结果"
        };

        /// <summary>缺陷明细 CSV 的表头（与激光模切一致）。</summary>
        public List<string> Station1_DefectTitle { get; } = new List<string>
        {
            "时间", "帧号", "EA", "名称", "面积", "宽度", "高度", "是否超规格", "是否重度", "是否NG", "是否保存", "路径"
        };

        public List<string> Station3_DefectTitle { get; } = new List<string>
        {
            "时间", "相机", "帧号", "名称", "面积", "宽度", "高度", "是否超规格", "是否NG", "是否保存", "路径"
        };

        /// <summary>换卷/复位时清空合片与结果队列，避免把上一卷的结论发出去。</summary>
        public void Reset_DefectStation()
        {
            while (ALine_DieCutEAProduct.Count > 0) ALine_DieCutEAProduct.Dequeue();
            while (BLine_DieCutEAProduct.Count > 0) BLine_DieCutEAProduct.Dequeue();



            UpCamDefectShowQueue.Clear();
            DownCamDefectShowQueue.Clear();
            Station3_DefectShowQueue.Clear();
            Station3_SizeShowQueue.Clear();

            // 两个工位的累计数一起清零：只清缺陷工位会让尺寸良率跨卷累加，
            // 界面上的"良率"于是永远不可能回到 100%。
            Station1_StatisticalData = new StatisticalData();
            Station3_StatisticalData = new StatisticalData();
        }

        #endregion

        /// <summary>
        /// 按配方重建右侧统计表的行。
        /// 判定还没接进来，所以这里只把"有哪些检测项"建好、计数清零；
        /// 判定落地后由 Server 直接改这些行的 Count / Ratio，不要在这里累积。
        /// </summary>
        public void Reset_StationStatistics()
        {
            BuildStatisticsRows(DefectStation_BindingDatas, Craft?.Get_Station1_DefectNames() ?? new List<string>());

            if (Craft != null)
            {
                // 尺寸工位的统计表要同时覆盖"尺寸项"和"缺陷项"：
                // 尺寸超规与外观缺陷都会让这一片 NG，只列缺陷项会让尺寸超规在良率表里查无此项。
                List<string> names = new List<string>();
                names.AddRange(Craft.Get_Station3_SizeNames());
                foreach (string name in Craft.Get_Station3_DefectNames())
                {
                    if (!names.Contains(name)) names.Add(name);
                }
                BuildStatisticsRows(SizeStation_BindingDatas, names);
            }
            else
            {
                BuildStatisticsRows(SizeStation_BindingDatas, new List<string>());
            }

            Station1_StatisticalData = new StatisticalData();
            Station3_StatisticalData = new StatisticalData();

            DefectCounts.Clear();
            SizeDefectCounts.Clear();
        }

        /// <summary>
        /// 按"总数/OK数/NG数 + 检测项"重建一张统计表。
        /// 前 3 行是这一卷的产量与良率，固定在最上面，与激光模切的行序一致——
        /// 两台机的统计 CSV 才能直接对照。
        /// </summary>
        private static void BuildStatisticsRows(BindingList<QualityData> rows, IEnumerable<string> names)
        {
            rows.Clear();
            rows.Add(new QualityData { Name = StatTotal, Count = 0, Ratio = "" });
            rows.Add(new QualityData { Name = StatOk, Count = 0, Ratio = "" });
            rows.Add(new QualityData { Name = StatNg, Count = 0, Ratio = "" });

            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (name == StatTotal || name == StatOk || name == StatNg) continue;
                rows.Add(new QualityData { Name = name, Count = 0, Ratio = "" });
            }
        }

        #endregion

        public override void InitFilePath()
        {
            // 缺陷明细 CSV：路径与激光模切保持一致（生产数据目录下按相机/工位分文件）
            Station1_ALineCamPath_Defect = Project.Instance.paramPathSet.ProductMojuanPath + $"缺陷过程数据_{Service_Camera.GetCam_Name(0)}.csv";
            Station1_BLineCamPath_Defect = Project.Instance.paramPathSet.ProductMojuanPath + $"缺陷过程数据_{Service_Camera.GetCam_Name(1)}.csv";
            Station3_Path_Defect = Project.Instance.paramPathSet.ProductMojuanPath + "缺陷过程数据_极片尺寸工位.csv";

            // 结果发送日志：路径与激光模切同名，便于同一台机器上比对两种机型的握手数据
            Station1_DefectCamSendPath = Project.Instance.paramPathSet.ProductMojuanPath + "过程发送数据_缺陷检测工位.csv";
            Station3_SizeCamSendPath = Project.Instance.paramPathSet.ProductMojuanPath + "过程发送数据_极片尺寸工位.csv";

            // 工位统计（名称与激光模切一致，方便直接对比两台机的良率口径）
            Station1_StatisticsPath = Project.Instance.paramPathSet.ProductMojuanPath + "数据统计_缺陷检测工位.csv";
            Station3_StatisticsPath = Project.Instance.paramPathSet.ProductMojuanPath + "数据统计_极片尺寸工位.csv";

            // 尺寸结果：与激光模切同名，便于对比两台机同一片的测量值
            Station3_Path_Size = Project.Instance.paramPathSet.ProductMojuanPath + "尺寸过程数据_极片尺寸工位.csv";

            // 检测耗时：每路相机一份，模切相机再区分大图与 EA 图
            int camCount = DeviceProfileRegistry.Resolve(MaterialCrafEnum.五金模切).CameraCount;
            DetectionTimePath = new string[camCount][];
            for (int i = 0; i < camCount; i++)
            {
                string camName = Service_Camera.GetCam_Name(i);
                DetectionTimePath[i] = new string[]
                {
                    Project.Instance.paramPathSet.ProductMojuanPath + $"检测耗时_{camName}.csv",
                    Project.Instance.paramPathSet.ProductMojuanPath + $"检测耗时_{camName}EA图.csv",
                };
            }

            DefectStation_BindingDatasSave.Clear();
            SizeStation_BindingDatasSave.Clear();

            UpCamDefectShowQueue.Clear();
            DownCamDefectShowQueue.Clear();
            Station3_DefectShowQueue.Clear();
            Station3_SizeShowQueue.Clear();
            Station1_SendShowQueue.Clear();
            Station3_SendShowQueue.Clear();
        }
    }
}