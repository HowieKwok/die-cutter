using System;
using System.Collections.Generic;
using System.Windows.Forms;

using LogTool;

namespace Frame_Core
{
    /// <summary>
    /// 动态表格的读写适配层：把业务侧的“标题数组 + 行数据”翻译成 DynamicGridView 的列/行。
    ///
    /// 存在理由：表头来自配方（运行期才知道列），行来自工位产出的 RowData。
    /// 视图层只关心 Init/Add 两个动作，不关心表格内部是 ListView 还是 DataGridView——
    /// 历史实现绑死在第三方自绘表格上，换主题时表头/行底色不透传，只能整体替换控件。
    /// </summary>
    public class NDataPanelViewModel
    {
        /// <summary>
        /// 按标题重建表头并清空历史数据。换配方时会重复调用，因此必须可重入。
        /// </summary>
        /// <param name="grid">目标表格</param>
        /// <param name="titles">列标题（同时作为列标识）</param>
        /// <param name="maxDisplayCount">表格保留的最大行数</param>
        public static void Init(DynamicGridView grid, string[] titles, int maxDisplayCount = 200)
        {
            if (grid == null || titles == null) return;

            grid.ReSetHeader();
            grid.MaxDisplayCount = maxDisplayCount;
            grid.AutoSetWidthContent = true;

            foreach (string title in titles)
            {
                // width = -1：交给 AutoSetWidthContent 决定列宽
                grid.AddHeader(title, title, -1, HorizontalAlignment.Center);
            }
        }

        public static void Add(DynamicGridView grid, string[] titles, List<List<RowData>> rows)
        {
            if (grid == null || rows == null) return;

            foreach (List<RowData> row in rows)
            {
                Add(grid, titles, row.ToArray());
            }
        }

        public static void Add(DynamicGridView grid, string[] titles, RowData[] rowData)
        {
            if (grid == null || rowData == null || rowData.Length == 0) return;

            if (titles == null || titles.Length == 0 || titles.Length != rowData.Length)
            {
                MessageNotify.AddErrorLog($"NDataPanelViewModel_标题长度_{titles?.Length}_与数据长度_{rowData.Length}_不一致");
                return;
            }

            DynamicGridRow row = new DynamicGridRow();
            for (int i = 0; i < titles.Length; i++)
            {
                row.SetValue(titles[i], rowData[i].dataStr);
                row.SetLevel(titles[i], ToLevel(rowData[i]));
            }
            grid.AddRow(row);
        }

        /// <summary>业务结果 → 单元格配色级别（Normal 不着色，跟随主题文本色）。</summary>
        private static DynamicCellLevel ToLevel(RowData data)
        {
            if (data.resultType == ResultType.Warn) return DynamicCellLevel.Warn;
            if (data.resultType == ResultType.NG) return DynamicCellLevel.Ng;
            return DynamicCellLevel.Normal;
        }
    }
}