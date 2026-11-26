using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Navigation;

namespace Frame_Core
{

    [Serializable]
    /// <summary>
    /// 字符信息显示
    /// </summary>
    public class MessageShowInfo
    {
        public string m_strMessage = "";
        public HalconColor m_stuGetColor = HalconColor.green;
        public double m_fCol = 0;
        public double m_fRow = 0;
        public int m_nFontSize = 10;
        public bool m_bIsWindow = true;
    }

    /// <summary>
    /// 区域信息显示
    /// </summary>
    public class RegionShowInfo
    {
        public HObject m_hRegion;
        public HalconColor m_stuGetColor;
    }

    [Serializable]
    public class LineInfo
    {
        public CameraEnum m_camera;
        /// <summary>
        /// 0:正常 1：EA失败 2:首片NG
        /// </summary>
        public int NgType = 0;
        public int nProcessID = 0;
        public long EAID = 0;
        public string strMoudleName = "DefaultMoudleName";
        public String StrLineName = "默认边";   //直线名称，string

        public bool IsStandLine = false;       //直线是否为基准边,默认为插件抓边。 //OverHang工位为是否为隔膜边，

        public bool IsHorizonLine = false;     //X方向抓边，水平边。用于计算线线距离，水平边直接计算纵向距离，垂直边直接计算横向距离
        /// <summary>
        /// 抓边是否成功
        /// </summary>
        public bool IsLineSuccessfull = false;
        /// <summary>
        /// 代表已加入
        /// </summary>
        public bool IsAdd = false;

        public int RowBeginID = 0;
        public double RowBegin = -999;
        public double ColBegin = -999;
        public int RowEndID = 0;
        public double RowEnd = -999;
        public double ColEnd = -999;

        /// <summary>
        /// 亚像素测量点集（抓边模块 metrology 的实测点，行/列成对、下标一一对应）。
        /// 供 <see cref="ParallelLineFit"/> 做整体最小二乘；为空时自动退回两个端点。
        /// 约定只读：跨帧/跨模块传递一律走 <see cref="Copy"/>，不要就地改数组内容。
        /// </summary>
        public double[] PtsRow;
        public double[] PtsCol;

        /// <summary>
        /// 记录抓边点集。行列长度不一致或为空时清空点集，让下游退回端点法，
        /// 宁可精度退化也不留下半组点。
        /// </summary>
        public void SetPoints(HTuple rows, HTuple cols)
        {
            if (rows == null || cols == null || rows.Length < 2 || rows.Length != cols.Length)
            {
                PtsRow = null;
                PtsCol = null;
                return;
            }

            double[] r = new double[rows.Length];
            double[] c = new double[cols.Length];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = rows[i].D;
                c[i] = cols[i].D;
            }
            PtsRow = r;
            PtsCol = c;
        }




        /// <summary>
        /// 线颜色
        /// </summary>
        public HalconColor Color;
        /// <summary>
        /// 线宽
        /// </summary>
        public int m_nLineWidth = 2;

        public LineInfo copyLine(LineInfo line)
        {
            LineInfo RELine = new LineInfo();
            RELine.m_camera = line.m_camera;
            RELine.StrLineName = line.StrLineName;

            RELine.IsHorizonLine = line.IsHorizonLine;
            RELine.IsLineSuccessfull = line.IsLineSuccessfull;
            RELine.IsAdd = line.IsAdd;

            RELine.RowBeginID = line.RowBeginID;
            RELine.RowBegin = line.RowBegin;
            RELine.ColBegin = line.ColBegin;

            RELine.RowEndID = line.RowEndID;
            RELine.RowEnd = line.RowEnd;
            RELine.ColEnd = line.ColEnd;

            RELine.Color = line.Color;
            RELine.m_nLineWidth = line.m_nLineWidth;

            return (LineInfo)MemberwiseClone();
        }

        public LineInfo Copy()
        {
            LineInfo copy = (LineInfo)MemberwiseClone();
            // 点集必须深拷贝：MemberwiseClone 只复制数组引用，跨帧共享后一旦有人就地改点就会串数据。
            if (PtsRow != null) copy.PtsRow = (double[])PtsRow.Clone();
            if (PtsCol != null) copy.PtsCol = (double[])PtsCol.Clone();
            return copy;
        }
    }

  

}
