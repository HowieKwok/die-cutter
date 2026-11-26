using System;

namespace Frame_Core
{
    /// <summary>
    /// 两条近似平行线段的间距求解。
    ///
    /// 不再"各自拟合再相减"，也不再"取两端点中点求轴差"——
    /// 后者的问题是有 Δc = D·n + δ·t 的分解：沿线错位 δ 会以 sinθ 的权重
    /// 混进 |Δcol| / |Δrow|，θ=0.5°、δ=100px 就有 0.87px 假误差。
    ///
    /// 规范（详见 docs/线线间距_计算规范.md）：
    ///   1. 两条线的全部亚像素点合起来算散布矩阵，取共同法向 n（整体最小二乘，闭式解）；
    ///   2. 间距 D = |(c_B - c_A)·n|，只与两质心之差有关，与原点、与倾斜都无关；
    ///   3. 按轴输出时用 D/|n_轴|，保持 线线X/线线Y 原有的"列向/行向尺寸"语义；
    ///   4. 顺手给出 σ_D、夹角、以及全点交叉校验，防止点集混组后静默出错。
    ///
    /// 该解法对线段的走向没有要求：接近水平、接近垂直、45° 一视同仁，
    /// 因为用 (row, col) 的一对坐标做特征分解，不经过斜率参数化。
    /// </summary>
    public static class ParallelLineFit
    {
        public struct FitResult
        {
            public bool IsValid;
            /// <summary>法向间距（像素域）。</summary>
            public double Distance;
            /// <summary>沿共同法向的有符号投影 (c_B - c_A)·n；点检这类需要 line2 - line1 的场合用它。</summary>
            public double SignedDistance;
            /// <summary>共同法向（row 分量、col 分量，单位向量）。</summary>
            public double NormalRow;
            public double NormalCol;
            /// <summary>两线夹角（度）。</summary>
            public double AngleDeg;
            /// <summary>间距的标准误。</summary>
            public double SigmaD;

            /// <summary>按轴间距：alongCol=true 取列向（线线X），否则取行向（线线Y）。</summary>
            public bool TryAxisDistance(bool alongCol, out double value)
            {
                value = double.NaN;
                if (!IsValid) return false;
                return TryScale(Distance, alongCol, out value);
            }

            /// <summary>
            /// 同上但保留符号（line2 - line1 在该轴方向上的位移）。
            /// 点检那套用户自定义项是"两线中点之差的轴分量"，本来就带符号，不能在这里变成绝对值。
            /// </summary>
            public bool TrySignedAxisDistance(bool alongCol, out double value)
            {
                value = double.NaN;
                if (!IsValid) return false;
                return TryScale(SignedDistance, alongCol, out value);
            }

            private bool TryScale(double normalDistance, bool alongCol, out double value)
            {
                value = double.NaN;

                double axisNormal = alongCol ? NormalCol : NormalRow;
                if (Math.Abs(axisNormal) < MinAxisNormal) return false;   // 线走向与所测轴平行，该轴间距无定义

                value = normalDistance / axisNormal;
                return true;
            }
        }

        /// <summary>低于此法向分量的轴距视为退化（约 2.9°）。</summary>
        private const double MinAxisNormal = 0.05;

        /// <summary>交叉校验容差下限（px）。</summary>
        private const double CheckTolerancePx = 0.05;

        /// <summary>工艺层调用方的便捷重载：只做一次适配，几何算法本身只认识 <see cref="LineGeometry"/>。</summary>
        public static FitResult Solve(LineInfo a, LineInfo b)
        {
            return Solve(a.ToGeometry(), b.ToGeometry());
        }

        public static FitResult Solve(LineGeometry a, LineGeometry b)
        {
            FitResult result = new FitResult();

            if (!TryCentroid(a, out double ar, out double ac, out int an)) return result;
            if (!TryCentroid(b, out double br, out double bc, out int bn)) return result;

            // 各自单线法向：只用于剔除外点和输出夹角
            Scatter(a, ar, ac, out double axx, out double ayy, out double axy);
            Scatter(b, br, bc, out double bxx, out double byy, out double bxy);
            PrincipalNormal(axx, ayy, axy, out double anr, out double anc);
            PrincipalNormal(bxx, byy, bxy, out double bnr, out double bnc);

            // 与基准线距离超过 3×中位数的点视为外点（端点伪影），只用一次
            a = RejectOutliers(a, ar, ac, anr, anc);
            b = RejectOutliers(b, br, bc, bnr, bnc);

            if (!TryCentroid(a, out ar, out ac, out an)) return result;
            if (!TryCentroid(b, out br, out bc, out bn)) return result;

            // 联合散布矩阵 → 共同法向
            Scatter(a, ar, ac, out axx, out ayy, out axy);
            Scatter(b, br, bc, out bxx, out byy, out bxy);

            // 完全退化：两条线各点重合（同一条线的两份拷贝、未赋值占位端点），
            // 此时 atan2(0,0) 会给出 n=(1,0) 并静默输出行差，必须拦下。
            if (axx + ayy + bxx + byy < 1e-12) return result;

            PrincipalNormal(axx + bxx, ayy + byy, axy + bxy, out double nr, out double nc);

            if (Math.Abs(nr) + Math.Abs(nc) < 0.5) return result;   // 点集退化（同一像素重复）

            result.SignedDistance = (br - ar) * nr + (bc - ac) * nc;
            result.Distance = Math.Abs(result.SignedDistance);
            result.NormalRow = nr;
            result.NormalCol = nc;
            result.AngleDeg = AngleBetween(anr, anc, bnr, bnc);

            // 不确定度：σ_D² = σ_A²/N_A + σ_B²/N_B
            double sa = PerpStd(a, ar, ac, nr, nc);
            double sb = PerpStd(b, br, bc, nr, nc);
            result.SigmaD = Math.Sqrt(sa * sa / an + sb * sb / bn);

            // 交叉校验：B 的点到 A 所在直线的有符号距离，其中位数应等于 ±D
            double signed = Median(PerpResiduals(b, ar, ac, nr, nc));
            double tol = Math.Max(CheckTolerancePx, 3.0 * result.SigmaD);
            if (Math.Abs(Math.Abs(signed) - result.Distance) > tol) return result;   // 点集混组

            result.IsValid = true;
            return result;
        }

        #region 点集

        private static int Count(LineGeometry line)
        {
            return line.HasPoints ? line.PtsRow.Length : 2;
        }

        private static double PointRow(LineGeometry line, int i)
        {
            return line.HasPoints ? line.PtsRow[i] : (i == 0 ? line.RowBegin : line.RowEnd);
        }

        private static double PointCol(LineGeometry line, int i)
        {
            return line.HasPoints ? line.PtsCol[i] : (i == 0 ? line.ColBegin : line.ColEnd);
        }

        private static bool TryCentroid(LineGeometry line, out double cr, out double cc, out int n)
        {
            cr = cc = 0;
            n = 0;
            if (!line.IsAvailable) return false;

            n = Count(line);
            if (n < 2) return false;

            double sr = 0, sc = 0;
            for (int i = 0; i < n; i++)
            {
                sr += PointRow(line, i);
                sc += PointCol(line, i);
            }

            cr = sr / n;
            cc = sc / n;
            return !double.IsNaN(cr) && !double.IsNaN(cc);
        }

        private static void Scatter(LineGeometry line, double cr, double cc, out double sxx, out double syy, out double sxy)
        {
            sxx = syy = sxy = 0;
            int n = Count(line);
            for (int i = 0; i < n; i++)
            {
                double dr = PointRow(line, i) - cr;
                double dc = PointCol(line, i) - cc;
                sxx += dc * dc;
                syy += dr * dr;
                sxy += dc * dr;
            }
        }

        private static double PerpStd(LineGeometry line, double cr, double cc, double nr, double nc)
        {
            int n = Count(line);
            if (n < 3) return 0;

            double q = 0;
            for (int i = 0; i < n; i++)
            {
                double p = (PointRow(line, i) - cr) * nr + (PointCol(line, i) - cc) * nc;
                q += p * p;
            }
            return Math.Sqrt(q / (n - 1));
        }

        private static double[] PerpResiduals(LineGeometry line, double cr, double cc, double nr, double nc)
        {
            int n = Count(line);
            double[] res = new double[n];
            for (int i = 0; i < n; i++)
                res[i] = (PointRow(line, i) - cr) * nr + (PointCol(line, i) - cc) * nc;
            return res;
        }

        /// <summary>剔除沿线伪影点；判据是到本线参考直线的距离超过 3×中位数。没有点集时原样返回。</summary>
        private static LineGeometry RejectOutliers(LineGeometry line, double cr, double cc, double nr, double nc)
        {
            if (!line.HasPoints) return line;

            double[] res = PerpResiduals(line, cr, cc, nr, nc);
            double[] abs = new double[res.Length];
            for (int i = 0; i < res.Length; i++) abs[i] = Math.Abs(res[i]);

            double median = Median(abs);
            double thr = Math.Max(3.0 * median, CheckTolerancePx);

            int keep = 0;
            for (int i = 0; i < res.Length; i++) if (Math.Abs(res[i]) <= thr) keep++;
            if (keep < 2 || keep == res.Length) return line;

            // 值语义：改点集只影响这一份副本，不会污染调用方持有的 LineInfo/LineInfo1。
            LineGeometry cleaned = line;
            cleaned.PtsRow = new double[keep];
            cleaned.PtsCol = new double[keep];
            int k = 0;
            for (int i = 0; i < res.Length; i++)
            {
                if (Math.Abs(res[i]) > thr) continue;
                cleaned.PtsRow[k] = line.PtsRow[i];
                cleaned.PtsCol[k] = line.PtsCol[i];
                k++;
            }
            return cleaned;
        }

        #endregion

        #region 几何

        /// <summary>整体最小二乘的闭式解：散布矩阵主轴 → 线走向 → 单位法向。</summary>
        private static void PrincipalNormal(double sxx, double syy, double sxy, out double nr, out double nc)
        {
            double phi = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);   // 走向（相对列轴）
            nr = Math.Cos(phi);
            nc = -Math.Sin(phi);
        }

        private static double AngleBetween(double nr1, double nc1, double nr2, double nc2)
        {
            double cos = Math.Abs(nr1 * nr2 + nc1 * nc2);
            if (cos > 1.0) cos = 1.0;
            return Math.Acos(cos) * 180.0 / Math.PI;
        }

        private static double Median(double[] values)
        {
            if (values == null || values.Length == 0) return 0;

            double[] sorted = new double[values.Length];
            Array.Copy(values, sorted, values.Length);
            Array.Sort(sorted);
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : 0.5 * (sorted[mid - 1] + sorted[mid]);
        }

        #endregion
    }
}