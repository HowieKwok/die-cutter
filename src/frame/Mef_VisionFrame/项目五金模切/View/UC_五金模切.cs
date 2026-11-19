using EHolly.FormControl;
using Frame_Core;
using FrameCommunity;
using ScottPlot;
using ScottPlot.WinForms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切主界面：5 路相机 = 模切主/从（大图 + EA 图）+ 尺寸×3（只有主图）。
    /// 没有裁切纠偏工位，所以比激光模切少一行"纠偏"画面。
    ///
    /// 布局用代码搭而不是 Designer：机型之间的差异只有"哪几路相机、每路几个画面"，
    /// 一张 5 行的表比几百行控件坐标更好维护；CamDisplayUnit 表则负责"哪条图像流接到哪个格子"。
    /// </summary>
    public class UC_五金模切 : UCViewBase
    {
        private readonly VM_五金模切 VM;
        private readonly List<CamDisplayUnit> displayUnits = new List<CamDisplayUnit>();

        // 控件必须留引用：NewDisplayWindowClass 只持有它们的 HWindow，窗体一旦被回收画面就黑掉。
        private UC_HalconWindow winUpMain;
        private UC_HalconWindow winUpEa;
        private UC_HalconWindow winDownMain;
        private UC_HalconWindow winDownEa;
        private UC_HalconWindow winSize1;
        private UC_HalconWindow winSize2;
        private UC_HalconWindow winSize3;

        // 右侧过程数据面板：EA 缺陷（主/从）、缺陷发送、尺寸结果、尺寸缺陷、尺寸发送
        private DynamicGridView dpUpEaDefect;
        private DynamicGridView dpDownEaDefect;
        private DynamicGridView dpDefectSend;
        private DynamicGridView dpSizeResult;
        private DynamicGridView dpSizeDefect;
        private DynamicGridView dpSizeSend;

        /// <summary>日志页：与激光模切共用 UC_Message（自带上限与颜色，自己消费队列）。</summary>
        private UC_Message logView;

        // ── 数据区折叠 ────────────────────────────────────────────────────────
        // 右侧数据区会挤压相机画面，所以放进可折叠区域：收起后只剩一条窄条，
        // 相机拿到整幅宽度。默认收起，且不持久化——开机就是看画面的状态。
        // 宽度只够画一个箭头：这条紧贴相机画面，越窄越不抢视野（文案放 ToolTip）。
        private const int RailWidth = 16;

        /// <summary>折叠条的悬停说明：窄到没有文字，必须靠提示说清它是干什么的。</summary>
        private readonly ToolTip railTips = new ToolTip
        {
            InitialDelay = 300,
            ReshowDelay = 100,
            AutoPopDelay = 6000,
        };

        /// <summary>展开态下左右两栏各自的最小宽度，拖动时不会把任何一边挤没。</summary>
        private const int MinPaneWidth = 320;

        private bool dataExpanded;
        private TableLayoutPanel root;
        private Control dataHost;
        private CollapseRail rail;
        private AntdUI.Tabs dataTabs;

        /// <summary>拖拽时的虚线预览（挂在 UC 本体上，避免被列宽裁剪）。</summary>
        private Control resizePreview;

        /// <summary>
        /// 左侧相机区占可用宽度的百分比（折叠条宽度之外的部分）。
        /// 拖动改的是这个值，折叠/展开也按它复原——用户调过的比例不会被收起动作重置。
        /// </summary>
        private float dataSplitPercent = 62F;

        /// <summary>
        /// 显示线程只读这个快照：由界面线程在折叠/换页时算好写进来。
        /// 图像线程绝不能去读控件状态（Pages/SelectedIndex 都不是线程安全的）。
        /// </summary>
        private volatile bool ngDisplayEnabled;

        /// <summary>
        /// 刷新闸门按页签分流：只有"展开 + 就是当前页"才允许动 UI。
        /// 折叠时看的是相机，展开时看的也只有一个页，其余页的控件不该白刷。
        /// </summary>
        private enum DataPage { 良率统计, 缺陷过程数据, 极片过程数据, 曲线图, 缺陷图片, 日志 }

        /// <summary>页签 → 下标。只由 AddDataPage 登记，别处不要写死数字。</summary>
        private readonly Dictionary<DataPage, int> pageIndexes = new Dictionary<DataPage, int>();


        public UC_五金模切(VM_五金模切 viewModel)
        {
            VM = viewModel;

            VM.Server.InitView -= InitView;
            VM.Server.InitView += InitView;
            VM.Server.RefreshToolView -= RefreshView;
            VM.Server.RefreshToolView += RefreshView;

            // 显示线程由 Server 启动，这里只挂处理函数；队列的显示/保存/释放顺序在 CamStreamUnit.Handle 里统一
            VM.Server.thd_ImageShowAndSave.PerformWork -= ImageShowAndSave;
            VM.Server.thd_ImageShowAndSave.PerformWork += ImageShowAndSave;

            BuildLayout();
            InitView();
            StartViewTimer();
        }

        /// <summary>
        /// 显示单元必须等控件拿到窗口句柄之后再建。
        ///
        /// NewDisplayWindowClass 在构造时就把 HWindowControl.HalconWindow 交给 HWndCtrl 缓存下来；
        /// 句柄还没创建时拿到的是无效窗口，之后控件真正创建句柄、HWindowControl 重新打开窗口，
        /// HWndCtrl 仍然往旧的无效窗口画 —— 表现就是"图保存了、画面一直黑"。
        /// 激光模切的窗格是 Designer 生成的，句柄就绪得早，所以没暴露这个问题。
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (displayUnits.Count == 0)
            {
                BuildDisplayUnits();
            }

            // NG 格子默认在折叠的数据区里、而且只有切到"缺陷图片"页才可见，
            // 等它自然显形再建来不及，所以这里主动把句柄建出来再挂显示对象
            EnsureNgDisplayWindows();
        }

        /// <summary>
        /// NG 显示对象必须在格子的窗口句柄就绪之后再建——规矩与 BuildDisplayUnits 相同。
        ///
        /// 隐藏控件靠 CreateControl() 建不出句柄（它先检查 Visible 直接返回），
        /// 只有取一次 Handle 才会真正走 HWindowControl.OnHandleCreated，Halcon 窗口也是那时才打开。
        /// 幂等，且本控件句柄就绪前直接跳过（构造期由 OnHandleCreated 接手）。
        /// </summary>
        private void EnsureNgDisplayWindows()
        {
            if (!IsHandleCreated) return;

            for (int cam = 0; cam < 5; cam++)
            {
                int slotCount = NgSlotCount(cam);
                for (int slot = 0; slot < slotCount; slot++)
                {
                    if (ngDisplayWindows[cam, slot] != null) continue;

                    UC_HalconWindow window = ngWindows[cam, slot];
                    if (window == null || !ForceHandle(window)) continue;

                    // HWndCtrl 构造时按控件尺寸算缩放因子：尺寸还没排出来就先不建，
                    // 展开/切页会再调一次这里（SyncDataAreaState），幂等
                    if (window.hWindow.Width <= 0 || window.hWindow.Height <= 0) continue;

                    ngDisplayWindows[cam, slot] = new NewDisplayWindowClass(window);
                }
            }
        }

        /// <summary>取一次 Handle 就会为隐藏控件建出窗口，CreateControl() 对 Visible=false 的控件不做事。</summary>
        private static bool ForceHandle(UC_HalconWindow window)
        {
            return window.hWindow.Handle != IntPtr.Zero;
        }

        #region 布局

        /// <summary>
        /// 左：相机画面（模切主/从 上下两行，尺寸三路并排）；
        /// 中：折叠条；右：数据结果区（工位统计 + 检测项统计 + 尺寸曲线）。
        ///
        /// 三列而不是 SplitContainer：折叠等价于"第三列宽度归零 + 数据区隐藏"，
        /// TableLayoutPanel 换列宽就够，不用处理 Panel2Collapsed 与最小宽度的冲突。
        /// </summary>
        private void BuildLayout()
        {
            Dock = DockStyle.Fill;

            root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(4),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, dataExpanded ? dataSplitPercent : 100F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, RailWidth));
            root.ColumnStyles.Add(dataExpanded
                ? new ColumnStyle(SizeType.Percent, 100F - dataSplitPercent)
                : new ColumnStyle(SizeType.Absolute, 0F));

            dataHost = BuildDataPanel();
            dataHost.Visible = dataExpanded;

            rail = new CollapseRail { Expanded = dataExpanded, DragEnabled = dataExpanded };
            rail.ToggleRequested += ToggleDataPanel;
            rail.DragMoved += PreviewDataAreaResize;
            rail.DragCommitted += CommitDataAreaResize;
            railTips.SetToolTip(rail, "点击展开/收起数据区；展开后可左右拖动调整宽度");

            root.Controls.Add(BuildCameraPanel(), 0, 0);
            root.Controls.Add(rail, 1, 0);
            root.Controls.Add(dataHost, 2, 0);
            Controls.Add(root);

            // 拖动预览线：拖动期间只挪这条虚线，松手才动列宽，
            // 否则每移动一像素都要重排相机窗口，画面会连续闪。
            resizePreview = new Panel
            {
                Visible = false,
                Width = 2,
                Enabled = false,
                BackColor = Frame_UI.WinForms.ColorBridge.Canvas,
            };
            resizePreview.Paint += ResizePreview_Paint;
            Controls.Add(resizePreview);
            resizePreview.BringToFront();
        }

        private void ResizePreview_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Frame_UI.WinForms.ColorBridge.AccentPrimary))
            {
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                e.Graphics.DrawLine(pen, 0, 0, 0, resizePreview.Height);
            }
        }

        /// <summary>
        /// 展开/收起只改列宽与可见性，从不重建数据区：
        /// 重建会换掉 HWindow 句柄（画面变黑）并清空曲线，等于每次折叠都把界面初始化一遍。
        /// </summary>
        private void ToggleDataPanel()
        {
            dataExpanded = !dataExpanded;
            ApplyDataPanelState();
        }

        /// <summary>
        /// 拖动中只移动预览线，不碰列宽。
        ///
        /// 直接改 ColumnStyles 会让相机那几路 HWindow 连续重排+重画，
        /// 视觉上就是整块左侧布局在闪；虚线预览把“重排”压缩到松手的一次。
        /// </summary>
        private void PreviewDataAreaResize(int deltaX)
        {
            if (!dataExpanded || root == null || resizePreview == null) return;

            int splitX = ClampSplitX(rail.Left + deltaX);
            int left = root.Left + splitX - 1;

            // 同一个像素位置不重复移动：每次移动都会让下面的相机窗口重画一条窄缝，
            // 鼠标高频移动时没必要为 1px 以内的抖动付这份代价。
            if (resizePreview.Visible && resizePreview.Left == left) return;

            resizePreview.Bounds = new Rectangle(left, root.Top, 2, root.ClientSize.Height);
            resizePreview.Visible = true;
            resizePreview.BringToFront();
        }

        /// <summary>松手才把分界线落到位。</summary>
        private void CommitDataAreaResize(int deltaX)
        {
            if (resizePreview != null) resizePreview.Visible = false;
            if (!dataExpanded || root == null) return;

            float available = root.ClientSize.Width - RailWidth - root.Padding.Horizontal;
            if (available <= MinPaneWidth * 2F) return;   // 窗口太窄，两侧都保不住，先不响应

            int splitX = ClampSplitX(rail.Left + deltaX);
            dataSplitPercent = (splitX - root.Padding.Left) * 100F / available;

            root.SuspendLayout();
            try
            {
                root.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, dataSplitPercent);
                root.ColumnStyles[2] = new ColumnStyle(SizeType.Percent, 100F - dataSplitPercent);
            }
            finally
            {
                root.ResumeLayout(true);
            }
        }

        /// <summary>把分界线候选位置夹在“两侧都不小于 MinPaneWidth”的区间里。</summary>
        private int ClampSplitX(int x)
        {
            int min = root.Padding.Left + MinPaneWidth;
            int max = root.ClientSize.Width - root.Padding.Right - RailWidth - MinPaneWidth;
            if (max < min) return min;
            return Math.Max(min, Math.Min(max, x));
        }

        private void ApplyDataPanelState()
        {
            if (resizePreview != null) resizePreview.Visible = false;

            root.SuspendLayout();
            try
            {
                // 先定显隐再定列宽：数据区缩到 0 宽之前已经不可见，重排不会闪
                dataHost.Visible = dataExpanded;
                root.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, dataExpanded ? dataSplitPercent : 100F);
                root.ColumnStyles[2] = dataExpanded
                    ? new ColumnStyle(SizeType.Percent, 100F - dataSplitPercent)
                    : new ColumnStyle(SizeType.Absolute, 0F);
            }
            finally
            {
                root.ResumeLayout(true);
            }

            rail.Expanded = dataExpanded;
            // 收起时数据区宽度为 0，拖动没有意义，同时把鼠标指针还原成手形
            rail.DragEnabled = dataExpanded;
            rail.Invalidate();

            SyncDataAreaState();

            // 收起期间一行没刷，展开时立刻补齐一次，否则看到的是折叠前的旧数字
            if (dataExpanded) RefreshView();
        }

        /// <summary>
        /// 折叠状态或页签变化后调用：把"哪些区域现在该动 UI"一次算清。
        /// 显示线程与日志页只读结果，避免各处各写一份判断条件。
        /// </summary>
        private void SyncDataAreaState()
        {
            ngDisplayEnabled = IsDataAreaVisible(DataPage.缺陷图片);

            // 幂等：句柄/尺寸就绪后才真正创建；展开、切到"缺陷图片"都会走到这里补一次
            EnsureNgDisplayWindows();

            if (logView != null) logView.Paused = !IsDataAreaVisible(DataPage.日志);
        }


        /// <summary>
        /// 唯一刷新闸门：数据区展开、且目标页签就是当前页，才允许往界面上写。
        /// 折叠、或看着别的页时一律不写——UI 线程要让给相机画面合成。
        /// </summary>
        private bool IsDataAreaVisible(DataPage page)
        {
            if (!dataExpanded || dataTabs == null) return false;

            int index;
            if (!pageIndexes.TryGetValue(page, out index)) return false;

            // 页签集合刚建好、尚未选中时 SelectedIndex 可能是 -1，按第一页算
            int selected = dataTabs.SelectedIndex < 0 ? 0 : dataTabs.SelectedIndex;
            return selected == index;
        }
        /// <summary>
        /// 相机区：左 2×2 四宫格放四路线扫画面（模切主/从 × 大图/EA），右竖排三路尺寸画面。
        /// 展开数据区只是把整块相机区压窄，这个结构不变。
        /// </summary>
        private Control BuildCameraPanel()
        {
            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                // 右边留 4px 空隙：折叠条与相机画面隔开一条窗口底色，
                // 否则那条窄栏贴在图边上，看起来像压在画面上。
                Margin = new Padding(0, 0, 4, 0),
            };
            // 线扫四宫格是主视野，尺寸三路只占右侧一条
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));

            TableLayoutPanel diePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0),
            };
            diePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            diePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            diePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            diePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            winUpMain = NewWindow(0, "大图");
            winUpEa = NewWindow(0, "EA图");
            winDownMain = NewWindow(1, "大图");
            winDownEa = NewWindow(1, "EA图");

            diePanel.Controls.Add(winUpMain, 0, 0);
            diePanel.Controls.Add(winUpEa, 1, 0);
            diePanel.Controls.Add(winDownMain, 0, 1);
            diePanel.Controls.Add(winDownEa, 1, 1);

            TableLayoutPanel sizePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
            };
            for (int i = 0; i < 3; i++)
            {
                sizePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 3));
            }

            winSize1 = NewWindow(2, null);
            winSize2 = NewWindow(3, null);
            winSize3 = NewWindow(4, null);

            sizePanel.Controls.Add(winSize1, 0, 0);
            sizePanel.Controls.Add(winSize2, 0, 1);
            sizePanel.Controls.Add(winSize3, 0, 2);

            panel.Controls.Add(diePanel, 0, 0);
            panel.Controls.Add(sizePanel, 1, 0);
            return panel;
        }

        /// <summary>标题 = 相机名（+ 画面后缀）。相机名来自配置，换相机不用改界面。</summary>
        private UC_HalconWindow NewWindow(int camIndex, string suffix)
        {
            UC_HalconWindow window = new UC_HalconWindow
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2),
                Title = BuildTitle(camIndex, suffix),
            };

            // 记下"这个格子属于哪路相机的哪个画面"，RefreshView 里按最新相机名重写标题，
            // 否则在现场改了相机名之后，画面标题会一直停在启动时的旧名字。
            titledWindows.Add(new Tuple<UC_HalconWindow, int, string>(window, camIndex, suffix));
            return window;
        }

        private static string BuildTitle(int camIndex, string suffix)
        {
            string title = Service_Camera.GetCam_Name(camIndex);
            if (!string.IsNullOrEmpty(suffix)) title += "-" + suffix;
            return title;
        }

        private void RefreshTitles()
        {
            foreach (Tuple<UC_HalconWindow, int, string> item in titledWindows)
            {
                string title = BuildTitle(item.Item2, item.Item3);
                if (item.Item1.Title != title) item.Item1.Title = title;
            }
        }

        #endregion

        #region 右侧数据区

        /// <summary>两个工位的汇总：总数 / OK / NG / 优率，与激光模切的 4 个标签一一对应。</summary>
        private Label lb_num1, lb_OK1, lb_ng1, lb_yield1;
        private Label lb_num3, lb_OK3, lb_ng3, lb_yield3;

        private DataGridView dg_DefectStatistics;
        private DataGridView dg_SizeStatistics;

        /// <summary>
        /// NG 轮播格子：线扫两路各 3 格（一片上常常同时有几处缺陷），尺寸三路各 2 格。
        /// 与激光模切一致（激光 12 格 = 线扫 3×2 + 尺寸 2×3）。
        /// </summary>
        private UC_HalconWindow[,] ngWindows = new UC_HalconWindow[5, 3];
        private NewDisplayWindowClass[,] ngDisplayWindows = new NewDisplayWindowClass[5, 3];

        /// <summary>
        /// 尺寸曲线：按项的性质分四组页签（与激光模切的极片/极耳/V角/V角一致）。
        /// 项数由配方决定，每组两列铺开，配方变几项都不会错位。
        /// </summary>
        private readonly Dictionary<string, TableLayoutPanel> chartPanels = new Dictionary<string, TableLayoutPanel>();
        private AntdUI.Tabs chartTabs;

        private readonly List<Tuple<UC_HalconWindow, int, string>> titledWindows = new List<Tuple<UC_HalconWindow, int, string>>();

        /// <summary>曲线页签顺序，也是分组名的唯一来源。</summary>
        private static readonly string[] 曲线分组 = { "极片数据", "极耳数据", "V角数据(下)", "V角数据(上)" };

        private Timer viewTimer;

        /// <summary>
        /// 右栏页签与激光模切一一对应（不含裁切过程数据）：
        /// 良率统计 / 缺陷过程数据 / 极片过程数据 / 曲线图 / 缺陷图片 / 日志。
        /// </summary>
        private Control BuildDataPanel()
        {
            return BuildDataTabs();
        }

        private Control BuildDataTabs()
        {
            // 与激光模切同一个容器：AntdUI.Tabs（激光右栏 tabs2 也是它），页签外观才一致
            AntdUI.Tabs tabs = new AntdUI.Tabs { Dock = DockStyle.Fill };
            dataTabs = tabs;
            // AntdUI 页签内容可能懒创建/延迟建句柄：换页时对整棵子树重发一次主题（幂等）。
            tabs.SelectedIndexChanged += delegate { Frame_UI.WinForms.WinFormsThemeApplier.Apply(this); };

            dg_DefectStatistics = NewStatisticsGrid(VM.Data.DefectStation_BindingDatas);
            dg_SizeStatistics = NewStatisticsGrid(VM.Data.SizeStation_BindingDatas);

            // 曲线页：四组子页签，每页一个滚动容器 + 两列排版的表格
            chartTabs = new AntdUI.Tabs { Dock = DockStyle.Fill };
            foreach (string group in 曲线分组)
            {
                TableLayoutPanel panel = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    ColumnCount = 2,
                    RowCount = 0,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Margin = new Padding(0),
                };
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

                Panel scroll = new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    Margin = new Padding(0),
                };
                scroll.Controls.Add(panel);

                chartPanels[group] = panel;
                chartTabs.Pages.Add(NewTabPage(group, scroll));
            }
            Panel chartScroll = new Panel { Dock = DockStyle.Fill };
            chartScroll.Controls.Add(chartTabs);

            dpUpEaDefect = new DynamicGridView { Dock = DockStyle.Fill };
            dpDownEaDefect = new DynamicGridView { Dock = DockStyle.Fill };
            dpDefectSend = new DynamicGridView { Dock = DockStyle.Fill };
            dpSizeResult = new DynamicGridView { Dock = DockStyle.Fill };
            dpSizeDefect = new DynamicGridView { Dock = DockStyle.Fill };
            dpSizeSend = new DynamicGridView { Dock = DockStyle.Fill };

            AddDataPage(tabs, DataPage.良率统计, "良率统计", BuildYieldPage());

            // 顺序与激光模切一致：A 面 → B 面 → 发送（激光 tableLayoutPanel6）
            AddDataPage(tabs, DataPage.缺陷过程数据, "缺陷过程数据", BuildSectionStack(
                new[] { "A线扫缺陷过程数据", "B线扫缺陷过程数据", "缺陷工位通讯数据" },
                new Control[] { dpUpEaDefect, dpDownEaDefect, dpDefectSend }));

            // 顺序与激光模切一致：尺寸结果 → 尺寸缺陷 → 发送（激光 tableLayoutPanel9）
            AddDataPage(tabs, DataPage.极片过程数据, "极片过程数据", BuildSectionStack(
                new[] { "极片尺寸过程数据", "极片缺陷过程数据", "极片尺寸工位通讯数据" },
                new Control[] { dpSizeResult, dpSizeDefect, dpSizeSend }));

            AddDataPage(tabs, DataPage.曲线图, "曲线图", chartScroll);
            AddDataPage(tabs, DataPage.缺陷图片, "缺陷图片", BuildNgPanel());

            // 日志页与激光模切同一套控件（UC_Message 自带定时器，消费 MessageNotify 的报警/调试/错误/通讯队列）
            logView = new UC_Message { Dock = DockStyle.Fill, Margin = new Padding(4) };
            AddDataPage(tabs, DataPage.日志, "日志", logView);

            // 换页 = 换一批要刷的控件：闸门重算一次，然后只补刷新页那一份
            tabs.SelectedIndexChanged += DataTabs_SelectedIndexChanged;
            return tabs;
        }

        /// <summary>
        /// 页签登记：页面顺序只在这里确定，闸门表跟着填。
        /// 用 IndexOf 而不是计数器，页面以后插到中间也不会错位。
        /// </summary>
        private void AddDataPage(AntdUI.Tabs tabs, DataPage kind, string title, Control content)
        {
            AntdUI.TabPage page = NewTabPage(title, content);
            tabs.Pages.Add(page);
            pageIndexes[kind] = tabs.Pages.IndexOf(page);
        }

        private void DataTabs_SelectedIndexChanged(object sender, AntdUI.IntEventArgs e)
        {
            SyncDataAreaState();
            RefreshView();   // 刚切到的那一页立刻补齐，不用等下一个 500ms 节拍
        }



        /// <summary>
        /// 良率统计页：与激光模切一致，两个工位左右并排（激光 tableLayoutPanel2 = 2 列），
        /// 每块从上到下是 标题条 → 优率/总数/OK/NG 四行（左标题右数值）→ 统计表。
        /// </summary>
        private Control BuildYieldPage()
        {
            TableLayoutPanel page = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
            };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            page.Controls.Add(BuildStationYield("缺陷检测工位", dg_DefectStatistics,
                out lb_num1, out lb_OK1, out lb_ng1, out lb_yield1), 0, 0);
            page.Controls.Add(BuildStationYield("极片检测工位（缺陷>尺寸）", dg_SizeStatistics,
                out lb_num3, out lb_OK3, out lb_ng3, out lb_yield3), 1, 0);
            return page;
        }

        private static Control BuildStationYield(string title, DataGridView grid,
            out Label num, out Label ok, out Label ng, out Label yield)
        {
            TableLayoutPanel block = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Margin = new Padding(2),
            };
            block.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            block.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            block.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            for (int i = 0; i < 4; i++)
            {
                block.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            }
            block.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            AntdUI.Divider divider = NewDivider(title);
            block.SetColumnSpan(divider, 2);
            block.Controls.Add(divider, 0, 0);

            yield = NewValueLabel();
            num = NewValueLabel();
            ok = NewValueLabel();
            ng = NewValueLabel();

            block.Controls.Add(NewCaptionLabel("优率："), 0, 1);
            block.Controls.Add(yield, 1, 1);
            block.Controls.Add(NewCaptionLabel("总数："), 0, 2);
            block.Controls.Add(num, 1, 2);
            block.Controls.Add(NewCaptionLabel("OK："), 0, 3);
            block.Controls.Add(ok, 1, 3);
            block.Controls.Add(NewCaptionLabel("NG："), 0, 4);
            block.Controls.Add(ng, 1, 4);

            grid.Dock = DockStyle.Fill;
            block.SetColumnSpan(grid, 2);
            block.Controls.Add(grid, 0, 5);
            return block;
        }

        /// <summary>左侧标题：右对齐，与激光模切 label1..4 一致。</summary>
        private static Label NewCaptionLabel(string caption)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = caption,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0),
            };
        }

        /// <summary>右侧数值：左对齐，内容由 RefreshStatistics 每 500ms 写。</summary>
        private static Label NewValueLabel()
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = "0",
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0),
            };
        }

        /// <summary>过程数据页的分区标题条，用激光同款 AntdUI.Divider。颜色不写死，跟随全局深色方案。</summary>
        private static AntdUI.Divider NewDivider(string title)
        {
            return new AntdUI.Divider
            {
                Dock = DockStyle.Fill,
                Text = title,
                Margin = new Padding(0),
            };
        }

        /// <summary>
        /// 过程数据页：若干块"标题条 + 滚动面板"上下排列，结构与激光模切
        /// tableLayoutPanel6 / tableLayoutPanel9（1 列、标题 30px + 内容自适应）一致。
        /// 数据由界面线程定时从 Model 的队列取出，Server 不引用任何界面对象。
        /// </summary>
        private static Control BuildSectionStack(string[] titles, Control[] contents)
        {
            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = titles.Length * 2,
                Margin = new Padding(0),
            };

            for (int i = 0; i < titles.Length; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / titles.Length));

                grid.Controls.Add(NewDivider(titles[i]), 0, i * 2);

                contents[i].Dock = DockStyle.Fill;
                grid.Controls.Add(contents[i], 0, i * 2 + 1);
            }
            return grid;
        }

        /// <summary>
        /// NG 轮播格：与激光模切同一排列方式（激光 12 格 = 3 列 × 4 行，按行填）。
        /// 五金 = 线扫 2 路 × 3 格 + 尺寸 3 路 × 2 格 = 12 格，正好铺满 3×4。
        /// 这里只建格子控件并留引用——NewDisplayWindowClass 要等句柄就绪后由
        /// EnsureNgDisplayWindows 创建，否则它缓存到的是无效窗口，之后一直是黑屏。
        /// </summary>
        private Control BuildNgPanel()
        {
            const int columns = 3;
            const int rows = 4;

            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columns,
                RowCount = rows,
                AutoScroll = true,
                Margin = new Padding(0),
            };
            for (int i = 0; i < columns; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
            }
            for (int i = 0; i < rows; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows));
            }

            int index = 0;
            for (int cam = 0; cam < 5; cam++)
            {
                int slotCount = NgSlotCount(cam);
                for (int slot = 0; slot < slotCount; slot++)
                {
                    UC_HalconWindow window = NewWindow(cam, "NG" + (slot + 1));
                    ngWindows[cam, slot] = window;

                    grid.Controls.Add(window, index % columns, index / columns);
                    index++;
                }
            }

            return grid;
        }

        private static AntdUI.TabPage NewTabPage(string title, Control content)
        {
            AntdUI.TabPage page = new AntdUI.TabPage { Text = title, Dock = DockStyle.Fill };
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
            return page;
        }

        /// <summary>
        /// 统计表。行由 Model 按配方建好（检测项 / 数量 / 占比）；
        /// 列手动建，避免默认用属性名当表头（显示成 Name/Count/Ratio）。
        /// </summary>
        private static DataGridView NewStatisticsGrid(BindingList<QualityData> source)
        {
            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                DataSource = source,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.None,
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "检测项", DataPropertyName = "Name", FillWeight = 50 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "数量", DataPropertyName = "Count", FillWeight = 25 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "占比", DataPropertyName = "Ratio", FillWeight = 25 });
            return grid;
        }

        /// <summary>
        /// 尺寸曲线：每个配方尺寸项一张图，按项的性质落到四组页签里，组内两列铺开。
        /// 分组只按名字前缀判断，所以配方新增项不用改代码，落到哪个组也一眼能看懂。
        /// </summary>
        private void BuildCharts()
        {
            foreach (TableLayoutPanel panel in chartPanels.Values)
            {
                panel.SuspendLayout();
                panel.Controls.Clear();
                panel.RowStyles.Clear();
                panel.RowCount = 0;
            }

            VM.Data.chartUIUser.Clear();

            if (VM.Data.Craft == null)
            {
                foreach (TableLayoutPanel panel in chartPanels.Values) panel.ResumeLayout();
                return;
            }

            Dictionary<string, int> groupIndex = new Dictionary<string, int>();

            foreach (SizeCraftStation_DieCuttingCut item in VM.Data.Craft.Get_Station3_SizeCrafts())
            {
                string group = GetCurveGroupName(item.Name);
                TableLayoutPanel panel = chartPanels[group];

                int index;
                groupIndex.TryGetValue(group, out index);
                groupIndex[group] = index + 1;

                FormsPlot plot = new FormsPlot { Dock = DockStyle.Fill, Margin = new Padding(0) };
                // 6/8 与激光模切一致：上下各留 6/8 个像素的观察余量，曲线不会贴边
                ScotPotUIUser chart = new ScotPotUIUser(plot, string.IsNullOrEmpty(item.Describe) ? item.Name : item.Describe, 6, 8)
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(2),
                };

                if (index % 2 == 0)
                {
                    panel.RowCount++;
                    panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
                }

                panel.Controls.Add(chart, index % 2, index / 2);
                VM.Data.chartUIUser[item.Name] = chart;
            }

            foreach (TableLayoutPanel panel in chartPanels.Values) panel.ResumeLayout();
        }

        /// <summary>
        /// 曲线项 → 页签。极耳相关（极耳宽度/高度/左肩宽、带料高）归"极耳数据"，
        /// V角按上下两面分两页，其余整片类尺寸（单片长度/宽度/角度）归"极片数据"。
        /// </summary>
        private static string GetCurveGroupName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "极片数据";
            if (name.StartsWith("下左") || name.StartsWith("下右")) return "V角数据(下)";
            if (name.StartsWith("上左") || name.StartsWith("上右")) return "V角数据(上)";
            if (name.StartsWith("极耳") || name == "带料高") return "极耳数据";
            return "极片数据";
        }

        /// <summary>按路取一格 NG 显示窗口，每显示一次推进一格（轮播）。</summary>
        private Func<NewDisplayWindowClass> NewNgPool(int camIndex)
        {
            int slotCount = NgSlotCount(camIndex);
            int slotIndex = 0;
            return () =>
            {
                NewDisplayWindowClass window = ngDisplayWindows[camIndex, slotIndex % slotCount];
                slotIndex++;
                return window;
            };
        }

        /// <summary>线扫两路给 3 格，尺寸三路给 2 格——线扫一片上常有几处缺陷同时出现。</summary>
        private static int NgSlotCount(int camIndex)
        {
            return camIndex < 2 ? 3 : 2;
        }

        /// <summary>
        /// 每 500ms 刷一次：统计数字与曲线标线。
        /// 判定没接入前这里数字一直是 0，但界面结构、配方项都在位，接上判定即出数。
        /// </summary>
        private void StartViewTimer()
        {
            viewTimer = new Timer { Interval = 500 };
            viewTimer.Tick += (s, e) => RefreshView();
            viewTimer.Start();
        }

        private void RefreshStatistics()
        {
            // 缺陷分布由流程线程累加，这里在界面线程刷进表里，避免跨线程更新绑定集合
            VM.Data.ApplyCounts();

            FillStation(lb_num1, lb_OK1, lb_ng1, lb_yield1, VM.Data.Station1_StatisticalData);
            FillStation(lb_num3, lb_OK3, lb_ng3, lb_yield3, VM.Data.Station3_StatisticalData);
        }

        /// <summary>一组数值标签：优率 / 总数 / OK / NG（与激光模切同义，标题在左列）。</summary>
        private static void FillStation(Label num, Label ok, Label ng, Label yield, StatisticalData data)
        {
            yield.Text = (data?.Yield ?? 0).ToString("0.00") + "%";
            num.Text = (data?.SumupCount ?? 0).ToString("0");
            ok.Text = (data?.OKCount ?? 0).ToString("0");
            ng.Text = (data?.NGCount ?? 0).ToString("0");
        }

        /// <summary>按配方刷新曲线标线：标准值 + 上下限。</summary>
        private void RefreshCharts()
        {
            if (VM.Data.Craft == null) return;

            foreach (SizeCraftStation_DieCuttingCut item in VM.Data.Craft.Get_Station3_SizeCrafts())
            {
                ScotPotUIUser chart;
                if (!VM.Data.chartUIUser.TryGetValue(item.Name, out chart) || chart == null) continue;

                double up = item.StandardValue + item.DBTolUpper;
                double down = item.StandardValue - item.DBTolLower;

                chart.GetAxess(down, up, out double min, out double max);
                chart.SetSignalPlotLine(item.StandardValue, Color.White, "当前值");
                chart.SetHorizontalLine(item.StandardValue, Color.LimeGreen, "标准值", LineStyle.Dash);
                chart.SetHorizontalLine(up, Color.Red, "上限", LineStyle.Dash);
                chart.SetHorizontalLine(down, Color.Red, "下限", LineStyle.Dash);
                chart.SetYAxisRange(min, max);
            }
        }

        #endregion

        #region 显示单元

        private void BuildDisplayUnits()
        {
            displayUnits.Clear();

            CamImageQueues[] cams = VM.Data.Cams;
            if (cams == null || cams.Length < 5) return;

            AddUnit(0, "线扫", cams[0], winUpMain, winUpEa);
            AddUnit(1, "线扫", cams[1], winDownMain, winDownEa);
            AddUnit(2, "面阵", cams[2], winSize1, null);
            AddUnit(3, "面阵", cams[3], winSize2, null);
            AddUnit(4, "面阵", cams[4], winSize3, null);
        }

        /// <summary>
        /// 一路相机一行。eaWindow 为 null 表示该路没有 EA 图（尺寸相机）。
        /// NG 缺陷图当前没有产出方（没有缺陷判定算法），先不接，避免界面上出现永远空着的轮播格子。
        /// </summary>
        private void AddUnit(int camIndex, string tabGroup, CamImageQueues queues,
                             UC_HalconWindow mainWindow, UC_HalconWindow eaWindow)
        {
            CamDisplayUnit unit = new CamDisplayUnit
            {
                CamIndex = camIndex,
                TabGroup = tabGroup,
                Titles = { CamTitleSlot.Of(mainWindow) },
                // 线扫原图只存不显：拼图要用它，但界面不展示
                RawSaveOnly = eaWindow != null ? CamStreamUnit.SaveOnly(queues.RawSave, queues.RawDispose) : null,
                Main = CamStreamUnit.ShowAt(
                    new NewDisplayWindowClass(mainWindow),
                    queues.MainShow, queues.MainSave, queues.MainDispose,
                    queues.MainScreenshotSave, queues.MainHighScreenshot),
            };

            if (eaWindow != null)
            {
                unit.Titles.Add(CamTitleSlot.Of(eaWindow));
                unit.Ea = CamStreamUnit.ShowAt(
                    new NewDisplayWindowClass(eaWindow),
                    queues.EaShow, queues.EaSave, queues.EaDispose,
                    queues.EaScreenshotSave, queues.EaHighScreenshot);
            }

            // NG 轮播：每路两格轮流显示缺陷小图，池不参与截图（截图会二次取格导致串位）
            unit.Ng = CamStreamUnit.RotateAt(NewNgPool(camIndex), queues.NgShow, queues.NgSave, queues.NgDispose);

            displayUnits.Add(unit);
        }

        #endregion

        #region 线程回调

        public void ImageShowAndSave()
        {
            // 只读一次快照：折叠/换页由界面线程写，这里不碰控件
            bool drawNg = ngDisplayEnabled;

            foreach (CamDisplayUnit unit in displayUnits)
            {
                CamStreamUnit.Handle(unit.RawSaveOnly);
                CamStreamUnit.Handle(unit.Main);
                CamStreamUnit.Handle(unit.Ea);
                CamStreamUnit.Handle(unit.Ng, drawNg);
            }
        }

        public override void InitView()
        {
            // 统计表的行由配方决定（换了配方要重建），曲线同理
            VM.Data.Reset_StationStatistics();
            BuildCharts();
            InitDetailPanels();
            RefreshStatistics();
            RefreshCharts();
            SyncDataAreaState();
        }


        /// <summary>
        /// 明细面板的表头随配方变化（尺寸项名会变），所以换配方后要重建列。
        /// 重建会清空面板内容，这是预期行为：旧配方的列对不上新数据。
        /// </summary>
        private void InitDetailPanels()
        {
            // 参数与激光模切一致：80ms 数据间隔、最多 15 行
            NDataPanelViewModel.Init(dpUpEaDefect, VM.Data.Station1_DefectTitle.ToArray(), 15);
            NDataPanelViewModel.Init(dpDownEaDefect, VM.Data.Station1_DefectTitle.ToArray(), 15);
            NDataPanelViewModel.Init(dpDefectSend, VM.Data.Station1_CamSendTitle.ToArray(), 15);
            NDataPanelViewModel.Init(dpSizeResult, VM.Data.Station3_Title_Size.ToArray(), 15);
            NDataPanelViewModel.Init(dpSizeDefect, VM.Data.Station3_DefectTitle.ToArray(), 15);
            NDataPanelViewModel.Init(dpSizeSend, VM.Data.Station3_CamSendTitle.ToArray(), 15);

            // 双击缺陷明细行打开缺陷小图。与激光模切一致：只对"缺陷明细"面板生效，
            // 尺寸结果/发送日志面板没有图片路径列。
            BindDefectRowDoubleClick(dpUpEaDefect);
            BindDefectRowDoubleClick(dpDownEaDefect);
            BindDefectRowDoubleClick(dpSizeDefect);
        }

        /// <summary>先减后加，保证换配方（InitView 会重复调用）不会重复订阅。</summary>
        private void BindDefectRowDoubleClick(DynamicGridView panel)
        {
            if (panel == null) return;

            panel.RowDoubleClick -= DefectRowDoubleClick;
            panel.RowDoubleClick += DefectRowDoubleClick;
        }

        /// <summary>
        /// 缺陷明细行双击：倒数第二列是"是否保存"，最后一列是缺陷小图的完整路径。
        /// 只有真的存了盘才打开，否则弹提示——存图未开启时路径是规划路径，文件不存在。
        /// </summary>
        private void DefectRowDoubleClick(object sender, DynamicGridRowEventArgs e)
        {
            string[] values = e.Values;
            if (values == null || values.Length < 2) return;

            string savedFlag = values[values.Length - 2];
            string filePath = values[values.Length - 1];

            if (!string.Equals(savedFlag, "True", StringComparison.OrdinalIgnoreCase)) return;

            Frame_Core.ShellPathLauncher.OpenFile(filePath, "缺陷图片");
        }

        public override void RefreshView()
        {
            RefreshTitles();    // 相机标题跟折叠无关，照刷

            // 每个区域各自过闸门：收起或不在该页时只排空队列，不往界面上写
            if (IsDataAreaVisible(DataPage.良率统计)) RefreshStatistics();
            if (IsDataAreaVisible(DataPage.曲线图)) RefreshCharts();

            ConsumeDetailPanels();
        }

        /// <summary>
        /// 明细面板按页签分流。不显示的那一页同样要把队列取空：
        /// 只是不渲染，否则展开那一刻几百行一起插进 ListView，正好卡在要看的时机。
        /// </summary>
        private void ConsumeDetailPanels()
        {
            bool renderDefect = IsDataAreaVisible(DataPage.缺陷过程数据);
            Consume(dpUpEaDefect, VM.Data.UpCamDefectShowQueue, VM.Data.Station1_DefectTitle, renderDefect);
            Consume(dpDownEaDefect, VM.Data.DownCamDefectShowQueue, VM.Data.Station1_DefectTitle, renderDefect);
            Consume(dpDefectSend, VM.Data.Station1_SendShowQueue, VM.Data.Station1_CamSendTitle, renderDefect);

            bool renderSize = IsDataAreaVisible(DataPage.极片过程数据);
            Consume(dpSizeResult, VM.Data.Station3_SizeShowQueue, VM.Data.Station3_Title_Size, renderSize);
            Consume(dpSizeDefect, VM.Data.Station3_DefectShowQueue, VM.Data.Station3_DefectTitle, renderSize);
            Consume(dpSizeSend, VM.Data.Station3_SendShowQueue, VM.Data.Station3_CamSendTitle, renderSize);
        }

        /// <summary>render=false 时只出队丢弃——数据本身在 Model 的累计字典里，不看这一行不会丢数。</summary>
        private static void Consume(DynamicGridView panel, QueuePackage<List<RowData>> queue,
                                    List<string> titles, bool render)
        {
            if (panel == null || queue == null) return;

            string[] titleArray = titles.ToArray();
            while (queue.Count > 0)
            {
                List<RowData> row = queue.Dequeue();
                if (render) NDataPanelViewModel.Add(panel, titleArray, row.ToArray());
            }
        }


        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing && viewTimer != null)
            {
                viewTimer.Stop();
                viewTimer.Dispose();
                viewTimer = null;
            }
            base.Dispose(disposing);
        }
    }
}