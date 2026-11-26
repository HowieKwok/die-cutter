using System;

namespace Frame_Core
{
    /// <summary>
    /// 一条线的几何值：四端点 + 可选亚像素点集。
    ///
    /// 存在的理由：仓库里对"线"有两套载体——工艺层的 <see cref="LineInfo"/>（带相机、颜色、
    /// NG 类型、显示线宽）和算法层的 <see cref="LineInfo1"/>（带 ROI 测量参数）。两者 80% 的
    /// 字段是同一件东西（名字、是否成功、四端点、点集），于是每加一处几何能力都要写两遍、
    /// 每次跨层传递都要写一个适配器。
    ///
    /// 这里把"几何"抽成一份：几何算法（<see cref="ParallelLineFit"/>）只认识它，
    /// 两个数据类各自提供一个 <c>ToGeometry()</c> 适配，不再互认。
    /// 注意它只有值语义，不带任何显示 / ROI / 工艺含义——那些留在各自的类里。
    /// </summary>
    public struct LineGeometry
    {
        /// <summary>这条线是否可以参与几何计算（抓边成功且有端点）。</summary>
        public bool IsAvailable;

        public double RowBegin;
        public double ColBegin;
        public double RowEnd;
        public double ColEnd;

        /// <summary>亚像素测量点集（行/列成对）；为空时退化为只用两个端点。</summary>
        public double[] PtsRow;
        public double[] PtsCol;

        public bool HasPoints
        {
            get
            {
                return PtsRow != null && PtsCol != null
                    && PtsRow.Length >= 2 && PtsRow.Length == PtsCol.Length;
            }
        }

        /// <summary>工艺层的线（带相机/颜色/NG 等），只取几何部分。</summary>
        public static LineGeometry From(LineInfo line)
        {
            if (line == null) return new LineGeometry();

            return new LineGeometry
            {
                IsAvailable = line.IsLineSuccessfull,
                RowBegin = line.RowBegin,
                ColBegin = line.ColBegin,
                RowEnd = line.RowEnd,
                ColEnd = line.ColEnd,
                PtsRow = line.PtsRow,
                PtsCol = line.PtsCol,
            };
        }

        /// <summary>算法层/点检的线（带 ROI 测量参数），只取几何部分。点集按引用共享，只读。</summary>
        public static LineGeometry From(LineInfo1 line)
        {
            if (line == null) return new LineGeometry();

            return new LineGeometry
            {
                IsAvailable = line.IsLineSuccessfull,
                RowBegin = line.RowBegin,
                ColBegin = line.ColBegin,
                RowEnd = line.RowEnd,
                ColEnd = line.ColEnd,
                PtsRow = line.PtsRow,
                PtsCol = line.PtsCol,
            };
        }
    }

    public static class LineGeometryExtensions
    {
        public static LineGeometry ToGeometry(this LineInfo line)
        {
            return LineGeometry.From(line);
        }

        public static LineGeometry ToGeometry(this LineInfo1 line)
        {
            return LineGeometry.From(line);
        }
    }
}