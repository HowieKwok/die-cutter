using System;
using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// 动态表格的一行数据。
    ///
    /// 存在理由：动态列意味着行不能有静态属性，只能用“列名 → 值”的字典承载。
    /// 颜色不传 Color 而传语义级别：表格自己决定 Normal 跟随主题文本色，
    /// 否则写死 Color.White 在浅色主题下就是白底白字。
    /// </summary>
    public sealed class DynamicGridRow
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        private readonly Dictionary<string, DynamicCellLevel> _levels = new Dictionary<string, DynamicCellLevel>();

        /// <summary>该行所有列的赋值，键为列名。</summary>
        public IReadOnlyDictionary<string, string> Values { get { return _values; } }

        public DynamicGridRow SetValue(string columnName, string value)
        {
            if (string.IsNullOrEmpty(columnName)) return this;
            _values[columnName] = value ?? string.Empty;
            return this;
        }

        public DynamicGridRow SetLevel(string columnName, DynamicCellLevel level)
        {
            if (string.IsNullOrEmpty(columnName)) return this;
            _levels[columnName] = level;
            return this;
        }

        public DynamicCellLevel GetLevel(string columnName)
        {
            DynamicCellLevel level;
            return columnName != null && _levels.TryGetValue(columnName, out level)
                ? level
                : DynamicCellLevel.Normal;
        }
    }

    /// <summary>单元格的业务级别：决定前景色，不决定背景（背景跟随表格主题）。</summary>
    public enum DynamicCellLevel
    {
        Normal = 0,
        Warn = 1,
        Ng = 2
    }
}