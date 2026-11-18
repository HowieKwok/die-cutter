using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 数据区折叠条：一条贯穿整高的窄按钮，点一下切换数据区展开/收起。
    ///
    /// 为什么自绘而不是用 Button：WinForms 没有竖排文字，窄条里放横向文字只能一个字一行，
    /// 字号一改就换行。这里把画布转 90° 再按普通水平文字画，宽度由列宽控制、长度自动填满高度。
    /// </summary>
    internal sealed class CollapseRail : Control
    {
        private bool hovered;

        /// <summary>true = 数据区当前是展开的，箭头与文案跟着反向。</summary>
        public bool Expanded { get; set; }

        /// <summary>点击整条窄按钮都触发，不区分具体位置。</summary>
        public event Action ToggleRequested;

        public CollapseRail()
        {
            Dock = DockStyle.Fill;
            Margin = new Padding(0);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ToggleRequested?.Invoke();
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(hovered ? Color.FromArgb(214, 224, 240) : Color.FromArgb(232, 236, 243));

            // 左边一条分隔线，把窄条和相机区分开；右边界就是数据区，展开时另有它的边框
            using (Pen edge = new Pen(Color.FromArgb(190, 200, 216)))
            {
                g.DrawLine(edge, 0, 0, 0, Height);
            }

            string label = Expanded ? "收起数据区" : "展开数据区";
            SizeF textSize = g.MeasureString(label, Font);

            // 竖向排布：文字 + 间距 + 箭头，整块在高度方向上居中
            const float gap = 10F;
            const float arrowHeight = 14F;
            float block = textSize.Width + gap + arrowHeight;
            float top = (Height - block) / 2F;

            GraphicsState state = g.Save();
            try
            {
                // 顺时针转 90°：本地坐标里横向排版的文字，落到屏幕上就是沿高度方向铺开
                g.TranslateTransform(Width / 2F, top + textSize.Width / 2F);
                g.RotateTransform(90);

                using (StringFormat format = new StringFormat())
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(43, 56, 78)))
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    g.DrawString(label, Font, brush,
                        new RectangleF(-textSize.Width / 2F, -Width / 2F, textSize.Width, Width), format);
                }
            }
            finally
            {
                g.Restore(state);
            }

            // 箭头画在未旋转的坐标里，方向不会被旋转换算弄反：
            // 展开时数据区往右退走（▶），收起时数据区从右边进来（◀）
            float arrowY = top + textSize.Width + gap + arrowHeight / 2F;
            float dir = Expanded ? 1F : -1F;
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(43, 56, 78)))
            {
                g.FillPolygon(brush, new[]
                {
                    new PointF(Width / 2F + 6F * dir, arrowY),
                    new PointF(Width / 2F - 4F * dir, arrowY - 7F),
                    new PointF(Width / 2F - 4F * dir, arrowY + 7F),
                });
            }
        }
    }
}