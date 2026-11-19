using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Frame_Core
{
    /// <summary>
    /// 工具栏几何图标（播放 / 停止 / 上一片 / 下一片）。
    ///
    /// 存在理由：这些按钮以前用字符文本（▶ ■ ◀◀ ▶▶）冒充图标，
    /// 字形取决于系统字体是否收录（真机表现：上一片只剩一条竖杠、下一片和启动键同形），
    /// 也不能跟随状态配色。这里用 GDI+ 直接画，字号/DPI/字体都无关。
    /// </summary>
    public static class ToolGlyph
    {
        /// <summary>实心三角，指向右（启动）。</summary>
        public static Image Play(int size, Color ink)
        {
            return Draw(size, g =>
            {
                float h = size * 0.72f;
                float w = h * 0.86f;
                float left = size * 0.22f;
                float top = (size - h) / 2f;
                g.FillPolygon(new SolidBrush(ink), new[]
                {
                    new PointF(left, top),
                    new PointF(left, top + h),
                    new PointF(left + w, top + h / 2f)
                });
            });
        }

        /// <summary>实心方块（运行中 / 停止）。</summary>
        public static Image Stop(int size, Color ink)
        {
            return Draw(size, g =>
            {
                float side = size * 0.56f;
                float pos = (size - side) / 2f;
                g.FillRectangle(new SolidBrush(ink), pos, pos, side, side);
            });
        }

        /// <summary>竖线 + 左向三角（回到上一片）。</summary>
        public static Image SkipPrev(int size, Color ink)
        {
            return Draw(size, g => DrawSkip(g, size, ink, true));
        }

        /// <summary>右向三角 + 竖线（推进到下一片）。</summary>
        public static Image SkipNext(int size, Color ink)
        {
            return Draw(size, g => DrawSkip(g, size, ink, false));
        }

        /// <summary>循环箭头（相机标题栏的“重置显示/适应画面”）。</summary>
        public static Image RefreshView(int size, Color ink)
        {
            return Draw(size, g =>
            {
                // 以 32 格为设计网格再按 size 缩放：14px 这种小尺寸下，
                // 直接用像素算半径/箭头很容易把笔画顶出画布（真机表现是边上被切一刀）。
                float u = size / 32f;
                float c = 16f * u;
                float r = 10f * u;

                using (Pen pen = new Pen(ink, Math.Max(1.5f, 2.6f * u)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    // 留一个缺口给箭头，不要画成闭合圆环
                    g.DrawArc(pen, c - r, c - r, r * 2f, r * 2f, -60f, 290f);

                    double angle = (-60d + 290d) * Math.PI / 180d;
                    float ex = c + (float)Math.Cos(angle) * r;
                    float ey = c + (float)Math.Sin(angle) * r;
                    double tangent = angle + Math.PI / 2d;
                    float len = 5f * u;

                    for (int i = -1; i <= 1; i += 2)
                    {
                        double barb = tangent + 0.85 * i;
                        g.DrawLine(pen, ex, ey,
                            ex + (float)Math.Cos(barb) * len,
                            ey + (float)Math.Sin(barb) * len);
                    }
                }
            });
        }

        /// <summary>齿轮（自动模式：设备自动运行）。</summary>
        public static Image AutoMode(int size, Color ink)
        {
            return Draw(size, g =>
            {
                float c = size / 2f;
                float r = size * 0.28f;
                float tooth = size * 0.13f;
                using (Pen pen = new Pen(ink, Math.Max(1.4f, size * 0.11f)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawEllipse(pen, c - r, c - r, r * 2f, r * 2f);
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = i * Math.PI / 4d;
                        float cos = (float)Math.Cos(angle);
                        float sin = (float)Math.Sin(angle);
                        g.DrawLine(pen,
                            c + cos * r, c + sin * r,
                            c + cos * (r + tooth), c + sin * (r + tooth));
                    }
                }
                using (SolidBrush brush = new SolidBrush(ink))
                {
                    float hub = r * 0.34f;
                    g.FillEllipse(brush, c - hub, c - hub, hub * 2f, hub * 2f);
                }
            });
        }

        /// <summary>锥形瓶（仿真模式：不接设备的离线跑图）。</summary>
        public static Image SimMode(int size, Color ink)
        {
            return Draw(size, g =>
            {
                float c = size / 2f;
                float neckHalf = size * 0.08f;
                float top = size * 0.16f;
                float shoulder = size * 0.42f;
                float bottom = size * 0.84f;
                float footHalf = size * 0.32f;

                using (Pen pen = new Pen(ink, Math.Max(1.4f, size * 0.09f)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLine(pen, c - neckHalf, top, c - neckHalf, shoulder);
                    g.DrawLine(pen, c + neckHalf, top, c + neckHalf, shoulder);
                    g.DrawLine(pen, c - neckHalf, shoulder, c - footHalf, bottom);
                    g.DrawLine(pen, c + neckHalf, shoulder, c + footHalf, bottom);
                    g.DrawLine(pen, c - footHalf, bottom, c + footHalf, bottom);
                    g.DrawLine(pen, c - footHalf * 0.55f, size * 0.68f, c + footHalf * 0.55f, size * 0.68f);
                }
            });
        }

        /// <summary>垃圾篓（清除数据）。</summary>
        public static Image ClearData(int size, Color ink)
        {
            return Draw(size, g =>
            {
                float c = size / 2f;
                float lidY = size * 0.28f;
                float bodyTop = size * 0.34f;
                float bodyBottom = size * 0.84f;
                float half = size * 0.26f;

                using (Pen pen = new Pen(ink, Math.Max(1.4f, size * 0.09f)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLine(pen, size * 0.16f, lidY, size * 0.84f, lidY);
                    g.DrawLine(pen, c - size * 0.10f, size * 0.18f, c + size * 0.10f, size * 0.18f);
                    g.DrawLine(pen, c - size * 0.10f, size * 0.18f, c - size * 0.10f, lidY);
                    g.DrawLine(pen, c + size * 0.10f, size * 0.18f, c + size * 0.10f, lidY);
                    g.DrawLine(pen, c - half, bodyTop, c - half * 0.8f, bodyBottom);
                    g.DrawLine(pen, c + half, bodyTop, c + half * 0.8f, bodyBottom);
                    g.DrawLine(pen, c - half * 0.8f, bodyBottom, c + half * 0.8f, bodyBottom);
                    g.DrawLine(pen, c - size * 0.08f, size * 0.42f, c - size * 0.05f, size * 0.76f);
                    g.DrawLine(pen, c + size * 0.08f, size * 0.42f, c + size * 0.05f, size * 0.76f);
                }
            });
        }

        /// <summary>卷轴（开始 / 结束膜卷）。</summary>
        public static Image FilmRoll(int size, Color ink)
        {
            return Draw(size, g =>
            {
                float c = size / 2f;
                float outer = size * 0.30f;
                float inner = size * 0.12f;

                using (Pen pen = new Pen(ink, Math.Max(1.4f, size * 0.10f)))
                {
                    g.DrawEllipse(pen, c - outer, c - outer, outer * 2f, outer * 2f);
                    g.DrawEllipse(pen, c - inner, c - inner, inner * 2f, inner * 2f);
                    // 卷尾：从外圆右下角伸出一小段
                    g.DrawLine(pen, c + outer * 0.72f, c + outer * 0.72f, size * 0.92f, size * 0.92f);
                }
            });
        }

        /// <summary>
        /// 按背景亮度选前景墨色：亮底用深墨、暗底用浅墨。
        /// 启动键绿底配深色箭头、运行键红底配白方块，都由这一条规则统一得出。
        /// </summary>
        public static Color ContrastInk(Color background)
        {
            double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255d;
            return luminance > 0.62 ? Color.FromArgb(6, 16, 24) : Color.White;
        }

        private static void DrawSkip(Graphics g, int size, Color ink, bool prev)
        {
            float h = size * 0.56f;
            float w = h * 0.78f;
            float top = (size - h) / 2f;
            float barWidth = Math.Max(1.6f, size * 0.09f);
            float margin = size * 0.18f;

            if (prev)
            {
                float barLeft = margin;
                g.FillRectangle(new SolidBrush(ink), barLeft, top, barWidth, h);
                float tipLeft = barLeft + barWidth;
                g.FillPolygon(new SolidBrush(ink), new[]
                {
                    new PointF(tipLeft + w, top),
                    new PointF(tipLeft + w, top + h),
                    new PointF(tipLeft, top + h / 2f)
                });
            }
            else
            {
                float barLeft = size - margin - barWidth;
                g.FillRectangle(new SolidBrush(ink), barLeft, top, barWidth, h);
                float tipLeft = barLeft - w;
                g.FillPolygon(new SolidBrush(ink), new[]
                {
                    new PointF(tipLeft, top),
                    new PointF(tipLeft, top + h),
                    new PointF(tipLeft + w, top + h / 2f)
                });
            }
        }

        private static Image Draw(int size, Action<Graphics> paint)
        {
            if (size <= 0) size = 16;

            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                paint(g);
            }
            return bmp;
        }
    }
}