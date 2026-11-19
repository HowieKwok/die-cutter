using Frame_Core;
using FrameCommunity.Data;
using LogTool;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static NPOI.HSSF.Util.HSSFColor;

namespace Mef_VisionFrame
{
    public partial class UC_DieCuttingCut : UCViewBase
    {
        public NewDisplayWindowClass DisplayWindowClass1_EAImage;
        public NewDisplayWindowClass DisplayWindowClass1_BigImage;
        public NewDisplayWindowClass DisplayWindowClass2_EAImage;
        public NewDisplayWindowClass DisplayWindowClass2_BigImage;
        public NewDisplayWindowClass DisplayWindowClass3;
        public NewDisplayWindowClass DisplayWindowClass4;
        public NewDisplayWindowClass DisplayWindowClass5;
        public NewDisplayWindowClass DisplayWindowClass6;

        public NewDisplayWindowClass DisplayWindowClassNGImage1;
        public NewDisplayWindowClass DisplayWindowClassNGImage2;
        public NewDisplayWindowClass DisplayWindowClassNGImage3;
        public NewDisplayWindowClass DisplayWindowClassNGImage4;
        public NewDisplayWindowClass DisplayWindowClassNGImage5;
        public NewDisplayWindowClass DisplayWindowClassNGImage6;
        public NewDisplayWindowClass DisplayWindowClassNGImage7;
        public NewDisplayWindowClass DisplayWindowClassNGImage8;
        public NewDisplayWindowClass DisplayWindowClassNGImage9;
        public NewDisplayWindowClass DisplayWindowClassNGImage10;
        public NewDisplayWindowClass DisplayWindowClassNGImage11;
        public NewDisplayWindowClass DisplayWindowClassNGImage12;

        public CombinationHWindowControl<NewDisplayWindowClass> CombinationHWindowControl_cam1;
        public CombinationHWindowControl<NewDisplayWindowClass> CombinationHWindowControl_cam2;
        public CombinationHWindowControl<NewDisplayWindowClass> CombinationHWindowControl_cam4;
        public CombinationHWindowControl<NewDisplayWindowClass> CombinationHWindowControl_cam5;
        public CombinationHWindowControl<NewDisplayWindowClass> CombinationHWindowControl_cam6;

        public BindingList<QualityData> DefectStation_BindingDatas { get; } = new BindingList<QualityData>();
        public BindingList<QualityData> SizeStation_BindingDatas { get; } = new BindingList<QualityData>();
        VM_DieCuttingCut VM;
        private readonly List<CamDisplayUnit> displayUnits = new List<CamDisplayUnit>();



        public UC_DieCuttingCut(VM_DieCuttingCut _VM)
        {


            InitializeComponent();
            VM = _VM;
            VM.Server.InitView -= InitView;
            VM.Server.InitView += InitView;

        
            VM.Server.thd_ImageShowAndSave.PerformWork -= ImageShowAndSave;
            VM.Server.thd_ImageShowAndSave.PerformWork += ImageShowAndSave;

            DisplayWindowClass1_EAImage = new NewDisplayWindowClass(FrontEAHalconView1);
            DisplayWindowClass1_BigImage = new NewDisplayWindowClass(FrontInitHalconView1);
            DisplayWindowClass2_EAImage = new NewDisplayWindowClass(FrontEAHalconView2);
            DisplayWindowClass2_BigImage = new NewDisplayWindowClass(FrontInitHalconView2);
            DisplayWindowClass3 = new NewDisplayWindowClass(FrontSizeHalconView);
            DisplayWindowClass4 = new NewDisplayWindowClass(FrontSizeHalconView1);
            DisplayWindowClass5 = new NewDisplayWindowClass(FrontSizeHalconView2);
            DisplayWindowClass6 = new NewDisplayWindowClass(FrontSizeHalconView3);


            DisplayWindowClassNGImage1 = new NewDisplayWindowClass(hWindowControl1);
            DisplayWindowClassNGImage2 = new NewDisplayWindowClass(hWindowControl2);
            DisplayWindowClassNGImage3 = new NewDisplayWindowClass(hWindowControl3);
            DisplayWindowClassNGImage4 = new NewDisplayWindowClass(hWindowControl4);
            DisplayWindowClassNGImage5 = new NewDisplayWindowClass(hWindowControl5);
            DisplayWindowClassNGImage6 = new NewDisplayWindowClass(hWindowControl6);
            DisplayWindowClassNGImage7 = new NewDisplayWindowClass(hWindowControl7);
            DisplayWindowClassNGImage8 = new NewDisplayWindowClass(hWindowControl8);
            DisplayWindowClassNGImage9 = new NewDisplayWindowClass(hWindowControl9);
            DisplayWindowClassNGImage10 = new NewDisplayWindowClass(hWindowControl10);
            DisplayWindowClassNGImage11 = new NewDisplayWindowClass(hWindowControl11);
            DisplayWindowClassNGImage12 = new NewDisplayWindowClass(hWindowControl12);

            CombinationHWindowControl_cam1 = new CombinationHWindowControl<NewDisplayWindowClass>("Cam1", new List<NewDisplayWindowClass>() { DisplayWindowClassNGImage1, DisplayWindowClassNGImage2, DisplayWindowClassNGImage3 });
            CombinationHWindowControl_cam2 = new CombinationHWindowControl<NewDisplayWindowClass>("Cam2", new List<NewDisplayWindowClass>() { DisplayWindowClassNGImage4, DisplayWindowClassNGImage5, DisplayWindowClassNGImage6 });
            CombinationHWindowControl_cam4 = new CombinationHWindowControl<NewDisplayWindowClass>("Cam4", new List<NewDisplayWindowClass>() { DisplayWindowClassNGImage7, DisplayWindowClassNGImage10 });
            CombinationHWindowControl_cam5 = new CombinationHWindowControl<NewDisplayWindowClass>("Cam5", new List<NewDisplayWindowClass>() { DisplayWindowClassNGImage8, DisplayWindowClassNGImage11 });
            CombinationHWindowControl_cam6 = new CombinationHWindowControl<NewDisplayWindowClass>("Cam6", new List<NewDisplayWindowClass>() { DisplayWindowClassNGImage9, DisplayWindowClassNGImage12 });

            BuildDisplayUnits();

            var sizeName = VM.Data.Craft.Get_Station3_SizeCrafts();
            foreach (var item in sizeName)
            {
                var chart = GetChart(item.Name, item.Describe);
                if (chart != null)
                {                           
                    VM.Data.chartUIUser[item.Name] = chart;
                }
            }

        }
        private void UC_DieCuttingCut_Load(object sender, EventArgs e)
        {
            InitView();

            // 双击缺陷明细行打开缺陷小图。原生表格用 RowDoubleClick，
            // 事件参数按列顺序给出文本，视图层不再依赖 ListView 的 SubItems 结构。
            foreach (var panel in new[] { Dp_ALineCamEADetect, Dp_BLineCamEADetect, Dp_PoleDeleft })
            {
                panel.RowDoubleClick -= DefectRowDoubleClick;
                panel.RowDoubleClick += DefectRowDoubleClick;
            }
            tableLayoutPanel1.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(this.tableLayoutPanel1, true);
            tableLayoutPanel11.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(this.tableLayoutPanel11, true);


            Dg_DefectStationStatistics.SetDoubleBuffered(true).BindingSource(DefectStation_BindingDatas);
            Dg_SizeStationStatistics.SetDoubleBuffered(true).BindingSource(SizeStation_BindingDatas);

            tabs2.SelectedIndex = 0;

            // AntdUI 页签内容可能懒创建/延迟建句柄：换页时对整棵子树重发一次主题（幂等）。
            tabs1.SelectedIndexChanged += delegate { Frame_UI.WinForms.WinFormsThemeApplier.Apply(this); };
            tabs2.SelectedIndexChanged += delegate { Frame_UI.WinForms.WinFormsThemeApplier.Apply(this); };
        }
        public override void RefreshView()
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                this.Invoke((EventHandler)delegate
                {
                    foreach (var unit in displayUnits)
                    {
                        foreach (var slot in unit.Titles)
                        {
                            slot.Control.Title = Service_Camera.GetCam_Name(unit.CamIndex) + (slot.Suffix ?? string.Empty);
                        }
                    }

                    var sizeName = VM.Data.Craft.Get_Station3_SizeCrafts();
                    foreach (var item in sizeName)
                    {
                        if (VM.Data.chartUIUser.ContainsKey(item.Name))
                        {
                            var chart = VM.Data.chartUIUser[item.Name];
                            if (chart != null)
                            {
                                double up = item.StandardValue + item.DBTolUpper;
                                double down = item.StandardValue - item.DBTolLower;
                                chart.GetAxess(down, up, out double Min_Value, out double Max_Value);
                                chart.SetSignalPlotLine(item.StandardValue, Color.White, "当前值");
                                chart.SetHorizontalLine(item.StandardValue, Color.LimeGreen, "标准值", LineStyle.Dash);
                                chart.SetHorizontalLine(up, Color.Red, "上限", LineStyle.Dash);
                                chart.SetHorizontalLine(down, Color.Red, "下限", LineStyle.Dash);
                                chart.SetYAxisRange(Min_Value, Max_Value);
                          
                            }
                        }

                    }
                });
            });
        }
        public override void InitView()
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                this.Invoke((EventHandler)delegate
                {
                    NDataPanelViewModel.Init(Dp_ALineCamEADetect, VM.Data.Station1_ALineCamTitle_Defect.ToArray(), 15);
                    NDataPanelViewModel.Init(Dp_BLineCamEADetect, VM.Data.Station1_BLineCamTitle_Defect.ToArray(), 15);
                    NDataPanelViewModel.Init(DP_DefectSendData, VM.Data.Station1_CamSendTitle_Defect.ToArray(), 15);

                    NDataPanelViewModel.Init(Dp_Cut, VM.Data.Station2_CutCamTitle.ToArray(), 25);
                    NDataPanelViewModel.Init(Dp_CutSend, VM.Data.Station2_CutCamSendTitle.ToArray(), 25);

                    NDataPanelViewModel.Init(Dp_PoleSize, VM.Data.Station3_Title_Size.ToArray(), 15);
                    NDataPanelViewModel.Init(Dp_PoleDeleft, VM.Data.Station3_Title_Defect.ToArray(), 15);
                    NDataPanelViewModel.Init(DP_SizeSendData, VM.Data.Station3_CamSendTitle_Size.ToArray(), 15);

                    Dg_DefectStationStatistics.SetColumnHeaders();
                    Dg_SizeStationStatistics.SetColumnHeaders();
                    DefectStation_BindingDatas.Clear();
                    SizeStation_BindingDatas.Clear();


                });
                RefreshView();
            });
        }

        public void ImageShowAndSave()
        {
            foreach (var unit in displayUnits)
            {
                CamStreamUnit.Handle(unit.RawSaveOnly);
                CamStreamUnit.Handle(unit.Main);
                CamStreamUnit.Handle(unit.Ea);
                CamStreamUnit.Handle(unit.Ng);
            }
        }

        /// <summary>
        /// 界面显示单元表：一路相机一行，全部差异集中在行内，循环体里不再有分支。
        /// 控件仍是 Designer 画好的固定格子，此表只声明"这一路用哪几个"。
        /// </summary>
        private void BuildDisplayUnits()
        {
            var d = VM.Data;

            displayUnits.Clear();

            // 线扫 / 模切主（0）：原图只存不显，大图 + EA 图 + NG 轮播
            displayUnits.Add(new CamDisplayUnit
            {
                CamIndex = 0, TabGroup = "线扫",
                Titles = { CamTitleSlot.Of(FrontInitHalconView1, "大图"), CamTitleSlot.Of(FrontEAHalconView1, "EA图") },
                RawSaveOnly = CamStreamUnit.SaveOnly(d.Cam1_SourceImage_ImageSave, d.Cam1_SourceImage_ImageDispose),
                Main = CamStreamUnit.ShowAt(DisplayWindowClass1_BigImage, d.Cam1_BigImage_ImageShow, d.Cam1_BigImage_ImageSave, d.Cam1_BigImage_ImageDispose),
                Ea = CamStreamUnit.ShowAt(DisplayWindowClass1_EAImage, d.Cam1_EAImage_ImageShow, d.Cam1_EAImage_ImageSave, d.Cam1_EAImage_ImageDispose, d.Cam1_EAImage_ScreenshotSave, d.Cam1_EAImage_HighScreenshotQueue),
                Ng = CamStreamUnit.RotateAt(CombinationHWindowControl_cam1.GetDisplayWindowClass, d.Cam1_NGDefectImage_ImageShow, d.Cam1_NGDefectImage_ImageSave, d.Cam1_NGDefectImage_ImageDispose)
            });

            // 线扫 / 模切从（1）：同上
            displayUnits.Add(new CamDisplayUnit
            {
                CamIndex = 1, TabGroup = "线扫",
                Titles = { CamTitleSlot.Of(FrontInitHalconView2, "大图"), CamTitleSlot.Of(FrontEAHalconView2, "EA图") },
                RawSaveOnly = CamStreamUnit.SaveOnly(d.Cam2_SourceImage_ImageSave, d.Cam2_SourceImage_ImageDispose),
                Main = CamStreamUnit.ShowAt(DisplayWindowClass2_BigImage, d.Cam2_BigImage_ImageShow, d.Cam2_BigImage_ImageSave, d.Cam2_BigImage_ImageDispose),
                Ea = CamStreamUnit.ShowAt(DisplayWindowClass2_EAImage, d.Cam2_EAImage_ImageShow, d.Cam2_EAImage_ImageSave, d.Cam2_EAImage_ImageDispose, d.Cam2_EAImage_ScreenshotSave, d.Cam2_EAImage_HighScreenshotQueue),
                Ng = CamStreamUnit.RotateAt(CombinationHWindowControl_cam2.GetDisplayWindowClass, d.Cam2_NGDefectImage_ImageShow, d.Cam2_NGDefectImage_ImageSave, d.Cam2_NGDefectImage_ImageDispose)
            });

            // 面阵 / 裁切纠偏（2）：无 EA、无 NG
            displayUnits.Add(new CamDisplayUnit
            {
                CamIndex = 2, TabGroup = "面阵",
                Titles = { CamTitleSlot.Of(FrontSizeHalconView) },
                Main = CamStreamUnit.ShowAt(DisplayWindowClass3, d.Cam3_SourceImage_ImageShow, d.Cam3_SourceImage_ImageSave, d.Cam3_SourceImage_ImageDispose, d.Cam3_SourceImage_ScreenshotSave, d.Cam3_SourceImage_HighScreenshotQueue)
            });

            // 面阵 / 尺寸 1（3）
            displayUnits.Add(new CamDisplayUnit
            {
                CamIndex = 3, TabGroup = "面阵",
                Titles = { CamTitleSlot.Of(FrontSizeHalconView1) },
                Main = CamStreamUnit.ShowAt(DisplayWindowClass4, d.Cam4_SourceImage_ImageShow, d.Cam4_SourceImage_ImageSave, d.Cam4_SourceImage_ImageDispose, d.Cam4_SourceImage_ScreenshotSave, d.Cam4_SourceImage_HighScreenshotQueue),
                Ng = CamStreamUnit.RotateAt(CombinationHWindowControl_cam4.GetDisplayWindowClass, d.Cam4_NGDefectImage_ImageShow, d.Cam4_NGDefectImage_ImageSave, d.Cam4_NGDefectImage_ImageDispose)
            });

            // 面阵 / 尺寸 2（4）
            displayUnits.Add(new CamDisplayUnit
            {
                CamIndex = 4, TabGroup = "面阵",
                Titles = { CamTitleSlot.Of(FrontSizeHalconView2) },
                Main = CamStreamUnit.ShowAt(DisplayWindowClass5, d.Cam5_SourceImage_ImageShow, d.Cam5_SourceImage_ImageSave, d.Cam5_SourceImage_ImageDispose, d.Cam5_SourceImage_ScreenshotSave, d.Cam5_SourceImage_HighScreenshotQueue),
                Ng = CamStreamUnit.RotateAt(CombinationHWindowControl_cam5.GetDisplayWindowClass, d.Cam5_NGDefectImage_ImageShow, d.Cam5_NGDefectImage_ImageSave, d.Cam5_NGDefectImage_ImageDispose)
            });

            // 面阵 / 尺寸 3（5）
            displayUnits.Add(new CamDisplayUnit
            {
                CamIndex = 5, TabGroup = "面阵",
                Titles = { CamTitleSlot.Of(FrontSizeHalconView3) },
                Main = CamStreamUnit.ShowAt(DisplayWindowClass6, d.Cam6_SourceImage_ImageShow, d.Cam6_SourceImage_ImageSave, d.Cam6_SourceImage_ImageDispose, d.Cam6_SourceImage_ScreenshotSave, d.Cam6_SourceImage_HighScreenshotQueue),
                Ng = CamStreamUnit.RotateAt(CombinationHWindowControl_cam6.GetDisplayWindowClass, d.Cam6_NGDefectImage_ImageShow, d.Cam6_NGDefectImage_ImageSave, d.Cam6_NGDefectImage_ImageDispose)
            });
        }

        private void timer_Viewrefresh_Tick(object sender, EventArgs e)
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                lb_num1.Text = VM.Data.Station1_StatisticalData.SumupCount.ToString();
                lb_OK1.Text = VM.Data.Station1_StatisticalData.OKCount.ToString();
                lb_ng1.Text = VM.Data.Station1_StatisticalData.NGCount.ToString();
                lb_yield1.Text = VM.Data.Station1_StatisticalData.Yield.ToString() + "%";



                lb_num3.Text = VM.Data.Station3_StatisticalData.SumupCount.ToString();
                lb_OK3.Text = VM.Data.Station3_StatisticalData.OKCount.ToString();
                lb_ng3.Text = VM.Data.Station3_StatisticalData.NGCount.ToString();
                lb_yield3.Text = VM.Data.Station3_StatisticalData.Yield.ToString() + "%";


                if (VM.Data.DefectStation_BindingDatasShow.Count > 0)
                {
                    DefectStation_BindingDatas.UdpateData(VM.Data.DefectStation_BindingDatasShow.Dequeue());
                }

                if (VM.Data.SizeStation_BindingDatasShow.Count > 0)
                {
                    SizeStation_BindingDatas.UdpateData(VM.Data.SizeStation_BindingDatasShow.Dequeue());
                }


                Station3DefectData_ShowAndSave();
            });
        }

        public void Station3DefectData_ShowAndSave()
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                //【】数据保存与显示
                var showData = VM.Data.Station3_Defect_DataShowQueue.DequeueBatch(1000);

                foreach (var item in showData)
                {
                    NDataPanelViewModel.Add(Dp_PoleDeleft, VM.Data.Station3_Title_Defect.ToArray(), item);
                }

            });
        }

        private MonitorForm _monitorForm;
        private MonitorForm V_monitorForm;
        private void divider4_DoubleClick(object sender, EventArgs e)
        {
            // 监控窗体全程只保留一个实例，重复点击只置前显示。
            if (_monitorForm == null || _monitorForm.IsDisposed)
            {
                _monitorForm = new MonitorForm(VM.Data.Cut_CorrectPosDeal, "裁切工位");
                _monitorForm.Show(this);
            }
            else
            {
                if (_monitorForm.WindowState == FormWindowState.Minimized)
                    _monitorForm.WindowState = FormWindowState.Normal;
                _monitorForm.Activate();
            }

            // 监控窗体全程只保留一个实例，重复点击只置前显示。
            if (V_monitorForm == null || V_monitorForm.IsDisposed)
            {
                V_monitorForm = new MonitorForm(VM.Data.V_CorrectPosDeal, "V裁切工位");
                V_monitorForm.Show(this);
            }
            else
            {
                if (V_monitorForm.WindowState == FormWindowState.Minimized)
                    V_monitorForm.WindowState = FormWindowState.Normal;
                V_monitorForm.Activate();
            }
        }

        /// <summary>
        /// 缺陷明细行双击：倒数第二列是“是否保存”，最后一列是缺陷小图完整路径。
        /// 只有真的存了盘才打开——存图未开启时路径是规划路径，文件不存在。
        /// </summary>
        private void DefectRowDoubleClick(object sender, DynamicGridRowEventArgs e)
        {
            string[] values = e.Values;
            if (values == null || values.Length < 2) return;

            string saveStatus = values[values.Length - 2];
            string imagePath = values[values.Length - 1];

            if (!string.Equals(saveStatus, "True", StringComparison.OrdinalIgnoreCase)) return;

            Frame_Core.ShellPathLauncher.OpenFile(imagePath, "缺陷图片");
        }


        private ScotPotUIUser GetChart(string name, string describe)
        {
            switch (name)
            {
                case "单片长度":
                    return new ScotPotUIUser(formsPlot1, describe, 6, 8);
                    break;
                case "带料高":
                    return new ScotPotUIUser(formsPlot2, describe, 6, 8);
                    break;
                case "单片宽度":
                    return new ScotPotUIUser(formsPlot3, describe, 6, 8);
                    break;
                case "极耳宽度":
                    return new ScotPotUIUser(formsPlot4, describe, 6, 8);
                    break;
                case "极耳左肩宽":
                    return new ScotPotUIUser(formsPlot5, describe, 6, 8);
                    break;
                case "极耳高度":
                    return new ScotPotUIUser(formsPlot6, describe, 6, 8);
                    break;
                case "下左V宽X":
                    return new ScotPotUIUser(formsPlot7, describe, 6, 8);
                    break;
                case "下右V宽X":
                    return new ScotPotUIUser(formsPlot8, describe, 6, 8);
                    break;
                case "下左V深Y":
                    return new ScotPotUIUser(formsPlot9, describe, 6, 8);
                    break;
                case "下右V深Y":
                    return new ScotPotUIUser(formsPlot10, describe, 6, 8);
                    break;
                case "上左V宽X":
                    return new ScotPotUIUser(formsPlot11, describe, 6, 8);
                    break;
                case "上左V深Y":
                    return new ScotPotUIUser(formsPlot12, describe, 6, 8);
                    break;
                case "上右V宽X":
                    return new ScotPotUIUser(formsPlot13, describe, 6, 8);
                    break;
                case "上右V深Y":
                    return new ScotPotUIUser(formsPlot14, describe, 6, 8);
                    break;

            }
            return null;
        }
    }
}
