using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Frame_Core
{
    /// <summary>
    /// 动态列的数据表格（原生 DataGridView）。
    ///
    /// 存在理由：过程数据/通讯数据的列由配方决定，不能在 Designer 里画死；
    /// 早期用的是第三方自绘表格（EHolly DynamicDataPanel → ListView），
    /// 表头与行底色是控件内部硬编码，主题切换时和良率统计（原生 DataGridView）形成两种观感。
    /// 这里用原生 DataGridView 承载同样的“动态列 + 环形缓存 + 单元格配色”，
    /// 外观交给 WinFormsThemeApplier 统一下发，与良率统计保持一致。
    /// </summary>
    public class DynamicGridView : DataGridView
    {
        private readonly Dictionary<string, DataGridViewColumn> _columns =
            new Dictionary<string, DataGridViewColumn>();

        public DynamicGridView()
        {
            DoubleBuffered = true;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AllowUserToOrderColumns = false;
            RowHeadersVisible = false;
            ReadOnly = true;
            MultiSelect = false;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            BorderStyle = BorderStyle.None;
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            EnableHeadersVisualStyles = false;
        }

        /// <summary>保留的最大行数，超出后从顶部丢弃（过程数据只看最近若干行）。</summary>
        public int MaxDisplayCount { get; set; }

        /// <summary>单元格内容内边距（沿用原控件的配置项，避免 Designer 反序列化失败）。</summary>
        public int HeaderPadding { get; set; }

        /// <summary>
        /// true：列宽按内容自适应；false：按 AddHeader 指定的宽度，未指定则均分。
        /// 该属性可能在列创建之后才由 Init 赋值，因此 setter 要回头重算所有列。
        /// </summary>
        public bool AutoSetWidthContent
        {
            get { return _autoSetWidthContent; }
            set
            {
                _autoSetWidthContent = value;
                foreach (DataGridViewColumn column in Columns) ApplyAutoSize(column);
            }
        }

        private bool _autoSetWidthContent = true;

        /// <summary>双击某行，参数为该行按列顺序展开的单元格文本。</summary>
        public event EventHandler<DynamicGridRowEventArgs> RowDoubleClick;

        /// <summary>清空表头与数据（换配方时调用，可在任意线程）。</summary>
        public void ReSetHeader()
        {
            RunOnUi(() =>
            {
                _columns.Clear();
                Rows.Clear();
                Columns.Clear();
            });
        }

        /// <summary>
        /// 追加一列。width &lt;= 0 表示“跟随 AutoSetWidthContent”；
        /// name 是列标识（行数据按它取值），text 是显示表头。
        /// </summary>
        public void AddHeader(string name, string text, int width = -1, HorizontalAlignment align = HorizontalAlignment.Center)
        {
            if (string.IsNullOrEmpty(name)) return;

            RunOnUi(() =>
            {
                if (_columns.ContainsKey(name)) return;

                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn
                {
                    Name = name,
                    HeaderText = text ?? name,
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    ReadOnly = true,
                    Width = width > 0 ? width : 100
                };
                column.DefaultCellStyle.Alignment = ToContentAlignment(align);
                column.DefaultCellStyle.Padding = new Padding(HeaderPadding);
                // 记住调用方申请的宽度：AutoSetWidthContent 变化时要据此重算
                column.Tag = width;

                Columns.Add(column);
                _columns[name] = column;

                ApplyAutoSize(column);
            });
        }

        /// <summary>追加一行，可在非 UI 线程调用。</summary>
        public void AddRow(DynamicGridRow row)
        {
            if (row == null) return;

            RunOnUi(() =>
            {
                List<object> values = new List<object>(_columns.Count);
                List<string> names = new List<string>(_columns.Count);

                foreach (DataGridViewColumn column in Columns)
                {
                    names.Add(column.Name);
                    string text;
                    values.Add(row.Values.TryGetValue(column.Name, out text) ? text : string.Empty);
                }

                int index = Rows.Add(values.ToArray());

                for (int i = 0; i < names.Count; i++)
                {
                    DataGridViewCell cell = Rows[index].Cells[i];
                    cell.Style.ForeColor = GetLevelColor(row.GetLevel(names[i]));
                }

                TrimToLimit();
                ScrollToNewest();
            });
        }

        /// <summary>把前 N 列之外的历史行丢掉，避免长时间运行无限增长。</summary>
        private void TrimToLimit()
        {
            int limit = MaxDisplayCount > 0 ? MaxDisplayCount : 200;
            while (Rows.Count > limit) Rows.RemoveAt(0);
        }

        private void ScrollToNewest()
        {
            if (Rows.Count == 0) return;
            try { FirstDisplayedScrollingRowIndex = Rows.Count - 1; }
            catch (InvalidOperationException) { /* 行尚未完成布局，下次刷新会补上 */ }
        }

        /// <summary>业务侧习惯用 HorizontalAlignment，DataGridView 用的是另一种枚举，这里做映射。</summary>
        private static DataGridViewContentAlignment ToContentAlignment(HorizontalAlignment align)
        {
            switch (align)
            {
                case HorizontalAlignment.Left: return DataGridViewContentAlignment.MiddleLeft;
                case HorizontalAlignment.Right: return DataGridViewContentAlignment.MiddleRight;
                default: return DataGridViewContentAlignment.MiddleCenter;
            }
        }

        /// <summary>按调用方申请的宽度与 AutoSetWidthContent 重新决定列宽模式。</summary>
        private void ApplyAutoSize(DataGridViewColumn column)
        {
            int width = column.Tag is int ? (int)column.Tag : -1;

            if (width > 0)
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Width = width;
            }
            else if (AutoSetWidthContent)
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            }
            else
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }
        }

        /// <summary>
        /// 级别 → 前景色。Normal 返回空色，让单元格继承表格主题的文本色；
        /// 只有 Warn/Ng 显式着色，两者在深/浅底色上都有足够对比度。
        /// </summary>
        private static Color GetLevelColor(DynamicCellLevel level)
        {
            switch (level)
            {
                case DynamicCellLevel.Warn: return Color.FromArgb(255, 170, 0);
                case DynamicCellLevel.Ng: return Color.FromArgb(220, 45, 45);
                default: return Color.Empty;
            }
        }

        protected override void OnCellDoubleClick(DataGridViewCellEventArgs e)
        {
            base.OnCellDoubleClick(e);

            EventHandler<DynamicGridRowEventArgs> handler = RowDoubleClick;
            if (handler == null || e.RowIndex < 0 || e.RowIndex >= Rows.Count) return;

            DataGridViewRow row = Rows[e.RowIndex];
            string[] values = new string[row.Cells.Count];
            for (int i = 0; i < row.Cells.Count; i++) values[i] = row.Cells[i].Value as string;

            handler(this, new DynamicGridRowEventArgs(values));
        }

        /// <summary>
        /// 统一线程门：句柄就绪且处于其它线程时切回 UI 线程；否则直接执行。
        /// </summary>
        private void RunOnUi(Action action)
        {
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }
            action();
        }
    }

    /// <summary>动态表格行事件参数：Values 为按列顺序展开的单元格文本。</summary>
    public sealed class DynamicGridRowEventArgs : EventArgs
    {
        public DynamicGridRowEventArgs(string[] values)
        {
            Values = values ?? new string[0];
        }

        public string[] Values { get; private set; }
    }
}