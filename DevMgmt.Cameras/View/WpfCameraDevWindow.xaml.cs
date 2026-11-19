using DevMgmt.Cameras.Models;
using DevMgmt.Cameras.Tool;
using FrameCommunity;
using FrameCommunity.Data;
using FrameCommunity.MessageNotification;
using FrameCommunity.Thd;
using FrameCommunity.错误代码;
using HalconDotNet;
using IKapBoardClassLibrary;
using LogTool;
using MvFGCtrlC.NET;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ViewROIBase;

namespace DevMgmt.Cameras.View
{
    /// <summary>
    /// WpfCameraDev.xaml 的交互逻辑
    /// </summary>
    public partial class WpfCameraDevWindow :  Window
    {
        public Thd_WorkClass thd_WorkClass = new Thd_WorkClass();      
        private readonly CameraManagerViewModel CameraVM;
        private CameraDev _currentSelectedCamera;
        int camID = 0;

        // 窗口全局缓存资源（构造函数只查一次）
        private readonly Style _cameraItemStyle;
        private readonly ContextMenu _cameraItemMenu;
        public WpfCameraDevWindow(CamParam _CamParam)
        {
            CameraVM = new CameraManagerViewModel(_CamParam);
            // 令牌字典必须挂在窗口自身：本应用没有 WPF Application 对象，缺这一步 DynamicResource 会静默回退。
            Frame_UI.Wpf.ThemeScope.Ensure(this);
            InitializeComponent();
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            WindowInit();
            // 缓存资源，避免循环频繁 FindResource
            _cameraItemStyle = (Style)FindResource("CameraItemStyle");
            _cameraItemMenu = (ContextMenu)FindResource("CameraItemMenu");
            DataContext = CameraVM;
            RefreshCameraList();
        }


        private void AddDeviceBtn_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = new AddCameraWindow(CameraVM);
            addWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            addWindow.ShowDialog();

            RefreshCameraList();
        }

        private void RefreshCameraList()
        {
            // 1. 先解绑旧按钮事件，避免泄漏
            foreach (var btn in CameraItemPanel.Children.OfType<Button>())
            {
                btn.Click -= CameraItemBtn_Click;
                btn.ContextMenu = null;
            }
            CameraItemPanel.Children.Clear();

            foreach (var cam in CameraVM.Params.l_CameraDevs)
            {
                // 用 Grid 做2行2列布局
                var rootGrid = new Grid
                {
                    Margin = new Thickness(),
                    // 🔑 关键：设置 MaxWidth，防止被长文本撑宽（根据你的界面调整这个值）
                    MaxWidth = 1000
                };
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // 关键：两列宽度均分
                rootGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                    MinWidth = 0
                });
                rootGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                    MinWidth = 0
                });

                // 第一行：相机名称（跨两列，左对齐）
                var nameTextBlock = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    // 🔑 关键：超长文本自动显示省略号，不撑开布局
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                // 绑定相机名称
                Binding nameTextBinding = new Binding(nameof(CameraDev.CameraParameter.CameraName))
                {
                    Source = cam.CameraParameter,
                    Mode = BindingMode.OneWay
                };
                nameTextBlock.SetBinding(TextBlock.TextProperty, nameTextBinding);
                Grid.SetRow(nameTextBlock, 0);
                Grid.SetColumnSpan(nameTextBlock, 2);
                rootGrid.Children.Add(nameTextBlock);

                // 第二行第一列：ID（左对齐）
                var idTextBlock = new TextBlock
                {
                    Text = $"ID:{cam.CameraParameter.ID}",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                Grid.SetRow(idTextBlock, 1);
                Grid.SetColumn(idTextBlock, 0);
                rootGrid.Children.Add(idTextBlock);

                // 第二行第二列：状态文本（右对齐）
                var statusTextBlock = new TextBlock
                {
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                // 绑定状态文本
                Binding textBinding = new Binding(nameof(CameraDev.ConnStatus))
                {
                    Source = cam,
                    Mode = BindingMode.OneWay,
                    Converter = BoolToStatusTextConverter.Instance
                };
                statusTextBlock.SetBinding(TextBlock.TextProperty, textBinding);

                // 绑定状态颜色
                Binding brushBinding = new Binding(nameof(CameraDev.ConnStatus))
                {
                    Source = cam,
                    Mode = BindingMode.OneWay,
                    Converter = BoolToStatusBrushConverter.Instance
                };
                statusTextBlock.SetBinding(TextBlock.ForegroundProperty, brushBinding);

                Grid.SetRow(statusTextBlock, 1);
                Grid.SetColumn(statusTextBlock, 1);
                rootGrid.Children.Add(statusTextBlock);

                // 按钮包裹整个 Grid
                var btn = new Button
                {
                    Content = rootGrid,
                    Style = _cameraItemStyle,
                    Tag = cam.CameraParameter.ID,
                    ContextMenu = _cameraItemMenu,
                    // 🔑 关键：让按钮的内容水平拉伸，填满按钮
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                btn.Click += CameraItemBtn_Click;

                CameraItemPanel.Children.Add(btn);
            }

            if (CameraVM.Params.l_CameraDevs.Any())
            {
                var lastCam = CameraVM.Params.l_CameraDevs.Last();
                ShowCameraParameterPanel(lastCam);
                SetSelectedItem(lastCam.CameraParameter.ID);
            }
        }

        // 命名点击事件
        private void CameraItemBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is int selectId)) return;
            if (_currentSelectedCamera != null && _currentSelectedCamera.CameraParameter.ID == selectId) return;

            _currentSelectedCamera = CameraVM.Params.l_CameraDevs.First(c => c.CameraParameter.ID == selectId);
            ShowCameraParameterPanel(_currentSelectedCamera);
            SetSelectedItem(selectId);
        }

        /// <summary>
        /// 设置选中项高亮
        /// </summary>
        private void SetSelectedItem(int cameraId)
        {
            // 清空所有按钮样式
            foreach (var item in CameraItemPanel.Children.OfType<Button>())
            {
                item.Background = (Brush)FindResource("UnSelectedBrush");
                if ((int)item.Tag == cameraId)
                {
                    item.Background = (Brush)FindResource("SelectedBrush");

                }
            }
        }

        private void ShowCameraParameterPanel(CameraDev cameraDev)
        {
            if (cameraDev == null) return;
            _currentSelectedCamera = cameraDev;
            DevParameterPanel.Children.Clear();

            CameraVM.SetParamShowWindow(cameraDev.CameraParameter);
            var areaUC = new DynamicCameraParamUC();
            areaUC.DataContext = cameraDev.CameraParameter;
            DevParameterPanel.Children.Add(areaUC);

        }
        // 重命名
        private void Menu_Rename_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var ctxMenu = menuItem.Parent as ContextMenu;
            var btn = ctxMenu.PlacementTarget as Button;

            if (btn == null || btn.Tag == null) return;

            int cameraId = (int)btn.Tag;
            var camera = CameraVM.Params.l_CameraDevs.FirstOrDefault(c => c.CameraParameter.ID == cameraId);
            if (camera == null) return;

            // 纯WPF输入框
            var inputWin = new Window
            {
                Title = "重命名",
                Width = 350,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var tb = new TextBox { Text = camera.CameraParameter.CameraName, Margin = new Thickness(10), Height = 30 };
            var okBtn = new Button { Content = "确定", Width = 70, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "输入新名称：", Margin = new Thickness(10, 10, 0, 0) });
            panel.Children.Add(tb);
            panel.Children.Add(okBtn);
            inputWin.Content = panel;

            okBtn.Click += (s, args) => { inputWin.DialogResult = true; inputWin.Close(); };

            if (inputWin.ShowDialog() == true)
            {
                string newName = tb.Text.Trim();
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    CameraVM.RenameCam(cameraId, newName);
                }
            }
        }

        // 删除
        private void Menu_Delete_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var ctxMenu = menuItem.Parent as ContextMenu;
            var btn = ctxMenu.PlacementTarget as Button;

            if (btn == null || btn.Tag == null)
                return;

            // 直接获取右键点击的相机ID
            int cameraId = (int)btn.Tag;

            // 找到这个相机
            var camera = CameraVM.Params.l_CameraDevs.FirstOrDefault(c => c.CameraParameter.ID == cameraId);
            if (camera == null)
                return;

            // 确认删除
            if (MessageBox.Show($"确定要删除相机：\n【{camera.CameraParameter.CameraName}】？",
                "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // 删除
                camera.CloseCam();


                CameraVM.Params.l_CameraDevs.Remove(camera);

                for (int i = 0; i < CameraVM.Params.l_CameraDevs.Count; i++)
                {
                    CameraVM.Params.l_CameraDevs[i].CameraParameter.ID = i;
                }

                // 清空右侧面板
                DevParameterPanel.Children.Clear();
                _currentSelectedCamera = null;

                // 刷新列表
                RefreshCameraList();
            }
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSelectedCamera.Camera == null) return;
                if (_currentSelectedCamera.Camera.ConnStatus)
                {
                    if (!thd_WorkClass.IsRunning)
                    {
                        thd_WorkClass.Start(50, "DevCam");
                        thd_WorkClass.PerformWork -= ImgAcquisition;
                        thd_WorkClass.PerformWork += ImgAcquisition;
                    }
                    _currentSelectedCamera.Camera.StartGrabImage();
                   
                }

                else
                {
                    ErrorWorks.ErrorMessageShow(_currentSelectedCamera.CameraParameter.CameraName, ErrorWorks.GetErrorInfo((uint)ErrorCodeEnum.未连接));
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
            }

        }
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSelectedCamera.Camera == null) return;
                if (_currentSelectedCamera.Camera.ConnStatus)
                    _currentSelectedCamera.Camera.StopGrabImage();
                else
                {
                    ErrorWorks.ErrorMessageShow(_currentSelectedCamera.CameraParameter.CameraName, ErrorWorks.GetErrorInfo((uint)ErrorCodeEnum.未连接));
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
            }
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            try
            {
                RePaint();
                //HOperatorSet.ReadImage(out HObject hObject, "D:\\彭佳庆\\图库\\NG_原图\\00-32-37-944.jpg");
                //ShowRoiResult(hObject);

            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
            }

        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {

            try
            {
                _currentSelectedCamera.ConnectCam();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
            }
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentSelectedCamera.Camera == null) return;
                if (_currentSelectedCamera.Camera.ConnStatus)
                    _currentSelectedCamera.Camera.StopGrabImage();
                _currentSelectedCamera.CloseCam();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
            }
        }


        #region ViewROI
        public void RePaint()
        {
            try
            {
                m_hWndCtrl_Display_Cam.resetAll();
                m_hWndCtrl_Display_Cam.repaint();

            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
            }
        }
        public void CloseFromHost()
        {
            HalconHost.Child = null;
            if (hWindowControl != null)
            {
                hWindowControl.Dispose();
            }
            imgInfo?.Dispose();
        }

        private readonly HWindowControl hWindowControl = new HWindowControl();
        public HWndCtrl m_hWndCtrl_Display_Cam;

        public void WindowInit()
        {
            HalconHost.Child = hWindowControl;
            if (hWindowControl != null)
            {

                m_hWndCtrl_Display_Cam = new HWndCtrl(hWindowControl);

                m_hWndCtrl_Display_Cam.setViewState(HWndCtrl.MODE_VIEW_ZOOM);
                m_hWndCtrl_Display_Cam.setViewState(HWndCtrl.MODE_VIEW_MOVE);
                m_hWndCtrl_Display_Cam.ReDrawEvent += new HWndCtrl.ReDrawDelegate(ShowRoiResult);     //更改测量矩形，重绘测量结果
            }
        }

        public void set_display_font(HTuple hv_WindowHandle, HTuple hv_Size, HTuple hv_Font,
              HTuple hv_Bold, HTuple hv_Slant)
        {



            // Local iconic variables 

            // Local control variables 

            HTuple hv_OS = new HTuple(), hv_Fonts = new HTuple();
            HTuple hv_Style = new HTuple(), hv_Exception = new HTuple();
            HTuple hv_AvailableFonts = new HTuple(), hv_Fdx = new HTuple();
            HTuple hv_Indices = new HTuple();
            HTuple hv_Font_COPY_INP_TMP = new HTuple(hv_Font);
            HTuple hv_Size_COPY_INP_TMP = new HTuple(hv_Size);

            // Initialize local and output iconic variables 
            try
            {
                //This procedure sets the text font of the current window with
                //the specified attributes.
                //
                //Input parameters:
                //WindowHandle: The graphics window for which the font will be set
                //Size: The font size. If Size=-1, the default of 16 is used.
                //Bold: If set to 'true', a bold font is used
                //Slant: If set to 'true', a slanted font is used
                //
                hv_OS.Dispose();
                HOperatorSet.GetSystem("operating_system", out hv_OS);
                if ((int)((new HTuple(hv_Size_COPY_INP_TMP.TupleEqual(new HTuple()))).TupleOr(
                    new HTuple(hv_Size_COPY_INP_TMP.TupleEqual(-1)))) != 0)
                {
                    hv_Size_COPY_INP_TMP.Dispose();
                    hv_Size_COPY_INP_TMP = 16;
                }
                if ((int)(new HTuple(((hv_OS.TupleSubstr(0, 2))).TupleEqual("Win"))) != 0)
                {
                    //Restore previous behaviour
                    using (HDevDisposeHelper dh = new HDevDisposeHelper())
                    {
                        {
                            HTuple
                              ExpTmpLocalVar_Size = ((1.13677 * hv_Size_COPY_INP_TMP)).TupleInt()
                                ;
                            hv_Size_COPY_INP_TMP.Dispose();
                            hv_Size_COPY_INP_TMP = ExpTmpLocalVar_Size;
                        }
                    }
                }
                else
                {
                    using (HDevDisposeHelper dh = new HDevDisposeHelper())
                    {
                        {
                            HTuple
                              ExpTmpLocalVar_Size = hv_Size_COPY_INP_TMP.TupleInt()
                                ;
                            hv_Size_COPY_INP_TMP.Dispose();
                            hv_Size_COPY_INP_TMP = ExpTmpLocalVar_Size;
                        }
                    }
                }
                if ((int)(new HTuple(hv_Font_COPY_INP_TMP.TupleEqual("Courier"))) != 0)
                {
                    hv_Fonts.Dispose();
                    hv_Fonts = new HTuple();
                    hv_Fonts[0] = "Courier";
                    hv_Fonts[1] = "Courier 10 Pitch";
                    hv_Fonts[2] = "Courier New";
                    hv_Fonts[3] = "CourierNew";
                    hv_Fonts[4] = "Liberation Mono";
                }
                else if ((int)(new HTuple(hv_Font_COPY_INP_TMP.TupleEqual("mono"))) != 0)
                {
                    hv_Fonts.Dispose();
                    hv_Fonts = new HTuple();
                    hv_Fonts[0] = "Consolas";
                    hv_Fonts[1] = "Menlo";
                    hv_Fonts[2] = "Courier";
                    hv_Fonts[3] = "Courier 10 Pitch";
                    hv_Fonts[4] = "FreeMono";
                    hv_Fonts[5] = "Liberation Mono";
                }
                else if ((int)(new HTuple(hv_Font_COPY_INP_TMP.TupleEqual("sans"))) != 0)
                {
                    hv_Fonts.Dispose();
                    hv_Fonts = new HTuple();
                    hv_Fonts[0] = "Luxi Sans";
                    hv_Fonts[1] = "DejaVu Sans";
                    hv_Fonts[2] = "FreeSans";
                    hv_Fonts[3] = "Arial";
                    hv_Fonts[4] = "Liberation Sans";
                }
                else if ((int)(new HTuple(hv_Font_COPY_INP_TMP.TupleEqual("serif"))) != 0)
                {
                    hv_Fonts.Dispose();
                    hv_Fonts = new HTuple();
                    hv_Fonts[0] = "Times New Roman";
                    hv_Fonts[1] = "Luxi Serif";
                    hv_Fonts[2] = "DejaVu Serif";
                    hv_Fonts[3] = "FreeSerif";
                    hv_Fonts[4] = "Utopia";
                    hv_Fonts[5] = "Liberation Serif";
                }
                else
                {
                    hv_Fonts.Dispose();
                    hv_Fonts = new HTuple(hv_Font_COPY_INP_TMP);
                }
                hv_Style.Dispose();
                hv_Style = "";
                if ((int)(new HTuple(hv_Bold.TupleEqual("true"))) != 0)
                {
                    using (HDevDisposeHelper dh = new HDevDisposeHelper())
                    {
                        {
                            HTuple
                              ExpTmpLocalVar_Style = hv_Style + "Bold";
                            hv_Style.Dispose();
                            hv_Style = ExpTmpLocalVar_Style;
                        }
                    }
                }
                else if ((int)(new HTuple(hv_Bold.TupleNotEqual("false"))) != 0)
                {
                    hv_Exception.Dispose();
                    hv_Exception = "Wrong value of control parameter Bold";
                    throw new HalconException(hv_Exception);
                }
                if ((int)(new HTuple(hv_Slant.TupleEqual("true"))) != 0)
                {
                    using (HDevDisposeHelper dh = new HDevDisposeHelper())
                    {
                        {
                            HTuple
                              ExpTmpLocalVar_Style = hv_Style + "Italic";
                            hv_Style.Dispose();
                            hv_Style = ExpTmpLocalVar_Style;
                        }
                    }
                }
                else if ((int)(new HTuple(hv_Slant.TupleNotEqual("false"))) != 0)
                {
                    hv_Exception.Dispose();
                    hv_Exception = "Wrong value of control parameter Slant";
                    throw new HalconException(hv_Exception);
                }
                if ((int)(new HTuple(hv_Style.TupleEqual(""))) != 0)
                {
                    hv_Style.Dispose();
                    hv_Style = "Normal";
                }
                hv_AvailableFonts.Dispose();
                HOperatorSet.QueryFont(hv_WindowHandle, out hv_AvailableFonts);
                hv_Font_COPY_INP_TMP.Dispose();
                hv_Font_COPY_INP_TMP = "";
                for (hv_Fdx = 0; (int)hv_Fdx <= (int)((new HTuple(hv_Fonts.TupleLength())) - 1); hv_Fdx = (int)hv_Fdx + 1)
                {
                    hv_Indices.Dispose();
                    using (HDevDisposeHelper dh = new HDevDisposeHelper())
                    {
                        hv_Indices = hv_AvailableFonts.TupleFind(
                            hv_Fonts.TupleSelect(hv_Fdx));
                    }
                    if ((int)(new HTuple((new HTuple(hv_Indices.TupleLength())).TupleGreater(
                        0))) != 0)
                    {
                        if ((int)(new HTuple(((hv_Indices.TupleSelect(0))).TupleGreaterEqual(0))) != 0)
                        {
                            hv_Font_COPY_INP_TMP.Dispose();
                            using (HDevDisposeHelper dh = new HDevDisposeHelper())
                            {
                                hv_Font_COPY_INP_TMP = hv_Fonts.TupleSelect(
                                    hv_Fdx);
                            }
                            break;
                        }
                    }
                }
                if ((int)(new HTuple(hv_Font_COPY_INP_TMP.TupleEqual(""))) != 0)
                {
                    throw new HalconException("Wrong value of control parameter Font");
                }
                using (HDevDisposeHelper dh = new HDevDisposeHelper())
                {
                    {
                        HTuple
                          ExpTmpLocalVar_Font = (((hv_Font_COPY_INP_TMP + "-") + hv_Style) + "-") + hv_Size_COPY_INP_TMP;
                        hv_Font_COPY_INP_TMP.Dispose();
                        hv_Font_COPY_INP_TMP = ExpTmpLocalVar_Font;
                    }
                }
                HOperatorSet.SetFont(hv_WindowHandle, hv_Font_COPY_INP_TMP);

                hv_Font_COPY_INP_TMP.Dispose();
                hv_Size_COPY_INP_TMP.Dispose();
                hv_OS.Dispose();
                hv_Fonts.Dispose();
                hv_Style.Dispose();
                hv_Exception.Dispose();
                hv_AvailableFonts.Dispose();
                hv_Fdx.Dispose();
                hv_Indices.Dispose();

                return;
            }
            catch (HalconException HDevExpDefaultException)
            {

                hv_Font_COPY_INP_TMP.Dispose();
                hv_Size_COPY_INP_TMP.Dispose();
                hv_OS.Dispose();
                hv_Fonts.Dispose();
                hv_Style.Dispose();
                hv_Exception.Dispose();
                hv_AvailableFonts.Dispose();
                hv_Fdx.Dispose();
                hv_Indices.Dispose();

                throw HDevExpDefaultException;
            }
        }
        private void ShowRoiResult()
        {
            try
            {



                HWindowControl HWC = hWindowControl;
                HOperatorSet.SetColor(HWC.HalconWindow, "green");
                set_display_font(HWC.HalconWindow, 10, "mono", "true", "false");
                HOperatorSet.DispText(HWC.HalconWindow, $"帧号：{imgInfo.image_ID}", "window", 10, 10, "green", "box", "false");
                HOperatorSet.DispText(HWC.HalconWindow, $"帧开始位置：{imgInfo.nFirstLineEncoderCount}", "window", 30, 10, "green", "box", "false");
                HOperatorSet.DispText(HWC.HalconWindow, $"帧结束位置：{imgInfo.nLastLineEncoderCount}", "window", 50, 10, "green", "box", "false");

                if (_currentSelectedCamera?.Camera?.TryGetEncoderValue(out long encoderValue) ?? false)
                {
                    HOperatorSet.DispText(HWC.HalconWindow, $"当前编码器位置：{encoderValue}", "window", 70, 10, "green", "box", "false");
                }
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error(ex);
            }
        }
        private void ShowRoiResult(HObject hObject)
        {
            try
            {
                HWindowControl HWC = hWindowControl;
                HWC.Invoke(new Action(() =>
                {
                    try
                    {


                        int FontLine = 0;

                        object Locker = new object();
                        lock (Locker)
                        {
                            if (hObject != null)
                            {
                                m_hWndCtrl_Display_Cam.addIconicVar(hObject);
                            }

                            //显示图片区域文本信息
                            RePaint();

                        }
                    }
                    catch (Exception ex)
                    {
                        Log4netRecord.ErrorLog.Error(ex);
                    }

                }));
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error(ex);
            }



        }
        ImageInfo imgInfo = new ImageInfo();
        public void ImgAcquisition()
        {
            try
            {
                foreach (var item in CameraVM.Params.l_CameraDevs)
                {
                    if (item.Camera != null)
                    {
                        if (item.CameraParameter.ID == _currentSelectedCamera.CameraParameter.ID)
                        {
                            if (_currentSelectedCamera.Camera.QueueGrabImage.Count > 0)
                            {
                                imgInfo.ho_image?.Dispose();
                                imgInfo = _currentSelectedCamera.Camera.QueueGrabImage.Dequeue();
                                ShowRoiResult(imgInfo.ho_image);
                            }
                        }
                        else
                        {
                            if (item.Camera.QueueGrabImage.Count > 0)
                            {
                                item.Camera.QueueGrabImage.Dequeue()?.Dispose();
                            }
                        }
                    }
                  
                }

            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error(ex);
            }

        }

        #endregion

        #region IDisposable 实现 配合外部 using
        protected override void OnClosing(CancelEventArgs e)
        {
            // 1. 取消所有任务
            
            thd_WorkClass.Stop();

            base.OnClosing(e);
            // 1. 解除数据上下文（切断绑定链，关键）
            this.DataContext = null;
            CloseFromHost();
            // 2. 清空主容器子元素（释放内部所有控件）
            if (this.Content is Panel mainPanel)
            {
                mainPanel.Children.Clear();
            }

            // 3. 取消所有事件订阅、定时器、后台任务（根据你实际业务补充）
            // 例：如果你有 DispatcherTimer、BackgroundWorker、自定义事件，在这里 Stop/Unsubscribe


            // 4. 强制 GC 建议（可选，加快回收）
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }


        #endregion

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            DetachWindowsFormsHosts(this);
            BindingOperations.ClearAllBindings(this);  // 清除所有子元素的绑定
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            //this.DataContext = null;
        }

        private static void DetachWindowsFormsHosts(DependencyObject current)
        {
            if (current == null) return;

            System.Windows.Forms.Integration.WindowsFormsHost host =
                current as System.Windows.Forms.Integration.WindowsFormsHost;
            if (host != null)
            {
                host.Child = null;
                host.Dispose();
                return;
            }

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                DetachWindowsFormsHosts(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
            }
        }

        public void HideSomeControls()
        {
            OpenCam.Visibility=Visibility.Hidden;
            CloseCam.Visibility = Visibility.Hidden;
            StartCollecting.Visibility = Visibility.Hidden;
            StopCollecting.Visibility = Visibility.Hidden;
            ResetWindow.Visibility = Visibility.Hidden;
        }

        public void VisibleSomeControls()
        {
            OpenCam.Visibility = Visibility.Visible;
            CloseCam.Visibility = Visibility.Visible;
            StartCollecting.Visibility = Visibility.Visible;
            StopCollecting.Visibility = Visibility.Visible;
            ResetWindow.Visibility = Visibility.Visible;
        }
    }
}
