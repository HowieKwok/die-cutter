using Mef_VisionFrame;
using ScottPlot;
using ScottPlot.Plottable;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ChartPerf
{
    /// <summary>
    /// 尺寸曲线性能测试。只跑 ScotPotUIUser（生产代码原样链接），不启动视觉框架。
    ///
    /// 四项指标：
    ///   A 建图        14 个参数图（含 FormsPlot + 曲线初始化）的耗时
    ///   B 刷新节拍    当前实现每 500ms 做的事：每图 3 条标线 + 锁一次轴
    ///   C 单点写入    UpdateCurve 真实路径（BeginInvoke → UI 线程写 Ys → Refresh）
    ///   D 重置复现    写点与节拍交替，检查 Ys 是否被 SetSignalPlotLine 冲掉
    /// </summary>
    internal static class Program
    {
        private const int ChartCount = 14;   // 与激光模切参数项数量对齐
        private const double Standard = 10.0;

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();

            Form host = new Form { Text = "曲线性能测试", Width = 1400, Height = 900 };
            TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 0 };
            host.Controls.Add(grid);
            HostGrid = grid;
            host.Show();

            // UpdateCurve 需要 Form_Main.FormMain 的句柄，给一个最小替身
            Form_Main.FormMain = new Form_Main();
            Form_Main.FormMain.Show();
            Application.DoEvents();

            Console.WriteLine("ScottPlot 4.1.74 | 图数=" + ChartCount + " | 窗口点数=100");
            Console.WriteLine(new string('-', 64));

            MeasureBuild();
            MeasureTick();
            MeasurePointWrite();
            MeasureFlush();
            MeasureResetDefect();

            host.Close();
            return 0;
        }

        #region A 建图

        private static void MeasureBuild()
        {
            Stopwatch sw = Stopwatch.StartNew();
            MakeCharts(new TableLayoutPanel { ColumnCount = 2, RowCount = 0 }, ChartCount, initCurve: true);
            sw.Stop();

            Console.WriteLine($"A 建图  : {ChartCount} 图共 {sw.ElapsedMilliseconds} ms"
                              + $"（{sw.Elapsed.TotalMilliseconds / ChartCount:0.0} ms/图）");
        }

        #endregion

        #region B 刷新节拍

        private static void MeasureTick()
        {
            ScotPotUIUser[] charts = MakeCharts(HostGrid, ChartCount, initCurve: true).ToArray();

            // 预热一次，把首次布局/句柄创建排除在统计外
            TickOnce(charts);

            Stopwatch sw = Stopwatch.StartNew();
            const int ticks = 20;
            for (int i = 0; i < ticks; i++) TickOnce(charts);
            sw.Stop();

            double perTick = sw.Elapsed.TotalMilliseconds / ticks;
            Console.WriteLine($"B 节拍  : （旧实现对照）{ChartCount} 图一次刷新 {perTick:0.0} ms"
                              + $"（500ms 预算占用 {perTick / 500 * 100:0.0}%）");
        }

        /// <summary>
        /// 复刻一次界面刷新。reinitialize=true 是修复前的行为（每拍 SetSignalPlotLine），
        /// false 是修复后的行为（建图时初始化，每拍不绘图）。
        /// </summary>
        private static void TickOnce(ScotPotUIUser[] charts, bool reinitialize = true)
        {
            double up = Standard + 0.5, down = Standard - 0.5;
            foreach (ScotPotUIUser chart in charts)
            {
                if (reinitialize) chart.SetSignalPlotLine(Standard, Color.White, "当前值");
                chart.SetHorizontalLine(Standard, Color.LimeGreen, "标准值", LineStyle.Dash);
                chart.SetHorizontalLine(up, Color.Red, "上限", LineStyle.Dash);
                chart.SetHorizontalLine(down, Color.Red, "下限", LineStyle.Dash);
                chart.SetYAxisRange(down, up);
            }
        }

        #endregion

        #region C 单点写入

        private static void MeasurePointWrite()
        {
            ScotPotUIUser chart = MakeCharts(HostGrid, 1, initCurve: true)[0];

            Stopwatch sw = Stopwatch.StartNew();
            const int points = 200;
            for (int i = 0; i < points; i++)
            {
                chart.UpdateCurve("当前值", Standard + i * 0.01);   // 内部 BeginInvoke
                Application.DoEvents();                             // 让 UI 线程真正执行
            }
            sw.Stop();

            Console.WriteLine($"C 写入  : {points} 点共 {sw.ElapsedMilliseconds} ms"
                              + $"（{sw.Elapsed.TotalMilliseconds / points:0.00} ms/点）");
        }

        #endregion

        #region E 节流验证

        /// <summary>写点不应立刻重绘；等一个 33ms 节拍后，脏表应被清空（说明合并刷新生效）。</summary>
        private static void MeasureFlush()
        {
            ScotPotUIUser chart = MakeCharts(HostGrid, 1, initCurve: true)[0];

            for (int i = 0; i < 20; i++) chart.UpdateCurve("当前值", Standard + i * 0.01);
            Application.DoEvents();              // 执行排队的写入，但不让 33ms 定时器到点

            int pending = DirtyCount();
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 120)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }
            int after = DirtyCount();

            Console.WriteLine($"E 节流  : 写完 20 点待刷 {pending} 个 → 等 120ms 后 {after} 个"
                              + (after == 0 ? "（合并刷新生效）" : "（定时器未生效）"));
        }

        private static int DirtyCount()
        {
            object set = typeof(ScotPotUIUser)
                .GetField("dirtyCharts", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            return (int)set.GetType().GetProperty("Count").GetValue(set);
        }

        #endregion

        #region D 重置复现

        private static void MeasureResetDefect()
        {
            Console.WriteLine("D 重置  : 旧实现 —— 每拍都调 SetSignalPlotLine");
            ReportReset(reinitializePerTick: true);
            Console.WriteLine("          新实现 —— 只在建图时初始化，每拍不绘图");
            ReportReset(reinitializePerTick: false);
        }

        private static void ReportReset(bool reinitializePerTick)
        {
            ScotPotUIUser chart = MakeCharts(HostGrid, 1, initCurve: true)[0];
            const int rounds = 5, pointsPerRound = 4;
            double value = Standard;

            for (int r = 0; r < rounds; r++)
            {
                for (int i = 0; i < pointsPerRound; i++)
                {
                    chart.UpdateCurve("当前值", value += 0.1);
                    Application.DoEvents();
                }
                TickOnce(new[] { chart }, reinitializePerTick);   // 500ms 节拍
            }

            double[] ys = SignalOf(chart).Ys;
            int written = rounds * pointsPerRound;
            int distinct = ys.Distinct().Count();
            int saturated = ys.Count(y => Math.Abs(y - Standard) < 1e-9);

            Console.WriteLine($"          : 写入 {written} 点 → Ys 不同值 {distinct} 个，"
                              + $"等于标准值 {saturated}/{ys.Length}，残留有效点 {ys.Length - saturated}");
        }

        #endregion

        #region 工具

        private static TableLayoutPanel HostGrid;

        private static ScotPotUIUser[] MakeCharts(TableLayoutPanel grid, int count, bool initCurve)
        {
            ScotPotUIUser[] charts = new ScotPotUIUser[count];
            for (int i = 0; i < count; i++)
            {
                ScotPotUIUser chart = new ScotPotUIUser { Dock = DockStyle.Fill };
                charts[i] = chart;
                grid.Controls.Add(chart, i % 2, i / 2);
            }
            if (initCurve)
            {
                foreach (ScotPotUIUser chart in charts)
                {
                    chart.SetSignalPlotLine(Standard, Color.White, "当前值");
                }
            }
            grid.Parent?.PerformLayout();
            Application.DoEvents();
            return charts;
        }

        private static SignalPlot SignalOf(ScotPotUIUser chart)
        {
            FormsPlot view = (FormsPlot)typeof(ScotPotUIUser)
                .GetField("_plotView", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(chart);
            return view.Plot.GetPlottables().OfType<SignalPlot>().First();
        }

        #endregion
    }
}