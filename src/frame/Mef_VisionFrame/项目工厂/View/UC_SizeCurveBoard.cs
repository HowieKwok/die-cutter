using Frame_Core;
using ScottPlot;
using ScottPlot.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 尺寸曲线面板：所有机型共用的唯一实现（以五金模切那套为基准）。
    ///
    /// 结构：两级页签 —— 一级按参数名分组（极片/极耳/V角下/V角上），二级每个参数一页一张整图，
    /// 参数页标签放在底部。只依赖 SizeCraftStation_DieCuttingCut，不认识任何机型，
    /// 以后新增机型直接 new 一块放进去，不要在别的机型里再抄一份曲线代码。
    ///
    /// 数据：曲线对象由调用方传入的字典持有（各机型 Model 已有 chartUIUser），
    /// 采集线程按参数名往里写，界面只负责建页签和刷标线。
    /// </summary>
    public class UC_SizeCurveBoard : UserControl
    {
        /// <summary>分组顺序，也是分组名的唯一来源。</summary>
        private static readonly string[] 分组顺序 = { "极片数据", "极耳数据", "V角数据(下)", "V角数据(上)" };

        private readonly Dictionary<string, AntdUI.Tabs> groupTabs = new Dictionary<string, AntdUI.Tabs>();
        private readonly IDictionary<string, ScotPotUIUser> charts;

        /// <summary>当前页签对应的配方签名；只有它变了才重建页签。</summary>
        private string signature;

        public UC_SizeCurveBoard(IDictionary<string, ScotPotUIUser> charts)
        {
            this.charts = charts ?? throw new ArgumentNullException(nameof(charts));
            Dock = DockStyle.Fill;
            BuildTabs();
        }

        #region 对外接口

        /// <summary>
        /// 每拍调用一次：配方项变了才重建页签，标线每次都刷（幂等，随便调）。
        /// 调用方不需要关心"什么时候该建图"，也不用额外的就绪事件。
        /// </summary>
        public void Sync(IList<SizeCraftStation_DieCuttingCut> items)
        {
            // 只比签名：曲线数据由采集线程独占写入，界面这边每拍不做任何绘图。
            // 之前每拍都调 SetSignalPlotLine，会把 100 点缓冲重铺成标准值平线并回退写指针，
            // 真实波形永远起不来（ChartPerf 场景 D 可复现）。
            string current = Signature(items);
            if (string.Equals(current, signature, StringComparison.Ordinal)) return;
            Rebuild(items, current);
        }

        /// <summary>强制下一次 Sync 重建。换卷/换配方时用，用来清掉上一轮的波形。</summary>
        public void Reset()
        {
            signature = null;
        }

        /// <summary>
        /// 建图时初始化一次：铺标准值平线 + 标准/上限/下限三条标线 + 锁 Y 轴。
        /// 这是 SetSignalPlotLine 的唯一调用点，之后曲线的唯一写入方是 UpdateCurve。
        /// </summary>
        private static void InitChart(ScotPotUIUser chart, SizeCraftStation_DieCuttingCut item)
        {
            double up = item.StandardValue + item.DBTolUpper;
            double down = item.StandardValue - item.DBTolLower;

            chart.GetAxess(down, up, out double min, out double max);
            chart.SetSignalPlotLine(item.StandardValue, Color.White, "当前值");
            chart.SetHorizontalLine(item.StandardValue, Color.LimeGreen, "标准值", LineStyle.Dash);
            chart.SetHorizontalLine(up, Color.Red, "上限", LineStyle.Dash);
            chart.SetHorizontalLine(down, Color.Red, "下限", LineStyle.Dash);
            chart.SetYAxisRange(min, max);
        }

        /// <summary>参数名 → 分组。只按前缀判断，配方新增项不用改代码。</summary>
        public static string GroupOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return 分组顺序[0];
            if (name.StartsWith("下左") || name.StartsWith("下右")) return "V角数据(下)";
            if (name.StartsWith("上左") || name.StartsWith("上右")) return "V角数据(上)";
            if (name.StartsWith("极耳") || name == "带料高") return "极耳数据";
            return 分组顺序[0];
        }

        #endregion

        #region 页签

        private void BuildTabs()
        {
            AntdUI.Tabs outer = new AntdUI.Tabs { Dock = DockStyle.Fill };
            foreach (string group in 分组顺序)
            {
                // 参数页签放底部：图在上、参数名在下，切换参数时视线不用跳到图外
                AntdUI.Tabs inner = new AntdUI.Tabs { Dock = DockStyle.Fill, Alignment = TabAlignment.Bottom };
                // 参数页懒创建：切过去时 FormsPlot 才第一次拿到非零尺寸，只重绘一次
                // （重绘不动 Ys，绝不能在这里重放标线初始化）
                inner.SelectedIndexChanged += delegate { Repaint(inner); };
                groupTabs[group] = inner;
                outer.Pages.Add(NewPage(group, inner));
            }
            Controls.Add(outer);
        }

        /// <summary>切到某个参数页时只重绘该页的图：ScottPlot 在 0×0 上初始化过，不给一次重绘可能首帧空白。</summary>
        private static void Repaint(AntdUI.Tabs tabs)
        {
            AntdUI.TabPage page = tabs.SelectedTab;
            if (page == null) return;

            foreach (Control content in page.Controls)
            {
                ScotPotUIUser chart = content as ScotPotUIUser;
                if (chart == null) continue;
                chart.RefreshUI();
                return;
            }
        }

        private void Rebuild(IList<SizeCraftStation_DieCuttingCut> items, string current)
        {
            foreach (AntdUI.Tabs tabs in groupTabs.Values) tabs.Pages.Clear();

            charts.Clear();
            signature = current;

            if (items == null || items.Count == 0)
            {
                foreach (AntdUI.Tabs tabs in groupTabs.Values)
                {
                    tabs.Pages.Add(NewPage("—", new Label
                    {
                        Dock = DockStyle.Fill,
                        Text = "配方未加载",
                        TextAlign = ContentAlignment.MiddleCenter,
                    }));
                }
                return;
            }

            foreach (SizeCraftStation_DieCuttingCut item in items)
            {
                if (item == null) continue;

                string title = string.IsNullOrEmpty(item.Describe) ? item.Name : item.Describe;

                FormsPlot plot = new FormsPlot { Dock = DockStyle.Fill, Margin = new Padding(0) };
                // 6/8：上下各留 6/8 个像素的观察余量，曲线不贴边
                ScotPotUIUser chart = new ScotPotUIUser(plot, title, 6, 8) { Dock = DockStyle.Fill };
                // ScotPotUIUser 的动态构造只持有 FormsPlot 引用，不托管它；不挂上去页签里就只剩空壳
                chart.Controls.Add(plot);

                groupTabs[GroupOf(item.Name)].Pages.Add(NewPage(title, chart));
                charts[item.Name] = chart;
                InitChart(chart, item);
            }
        }

        private static AntdUI.TabPage NewPage(string title, Control content)
        {
            AntdUI.TabPage page = new AntdUI.TabPage { Text = title, Dock = DockStyle.Fill };
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
            return page;
        }

        /// <summary>名称 + 标准值 + 公差；任一变化都意味着页签/标线要重建。</summary>
        private static string Signature(IList<SizeCraftStation_DieCuttingCut> items)
        {
            if (items == null || items.Count == 0) return string.Empty;

            return string.Join("|", items
                .Where(item => item != null)
                .Select(item => item.Name + ":" + item.StandardValue + "," + item.DBTolUpper + "," + item.DBTolLower));
        }

        #endregion
    }
}