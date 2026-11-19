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
        private bool pressed;
        private bool dragging;
        private bool dragged;
        private int dragStartX;

        /// <summary>true = 数据区当前是展开的，箭头与文案跟着反向。</summary>
        public bool Expanded { get; set; }

        /// <summary>
        /// 展开态下允许按住拖动来调左右宽度。
        /// 收起时数据区宽度为 0，拖动没有意义，由宿主关掉。
        /// </summary>
        public bool DragEnabled
        {
            get { return dragEnabled; }
            set
            {
                dragEnabled = value;
                Cursor = value ? Cursors.SizeWE : Cursors.Hand;
            }
        }
        private bool dragEnabled;

        /// <summary>点击整条窄按钮都触发，不区分具体位置。</summary>
        public event Action ToggleRequested;

        /// <summary>拖动过程中的累计水平位移（相对按下点），宿主据此画预览虚线。</summary>
        public event Action<int> DragMoved;

        /// <summary>松手时提交的累计位移；此时才真正重排列宽。</summary>
        public event Action<int> DragCommitted;

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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                dragging = dragEnabled;
                dragged = false;
                dragStartX = e.X;
                // 鼠标一旦压住就抓取：窄条只有十几像素，拖动必然跑出边界，
                // 不捕获就收不到 MouseUp，点击也就永远等不到松开。
                Capture = true;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                // 先过阈值再动列宽：手抖 1~2 像素不该把布局推走。
                if (!dragged && Math.Abs(e.X - dragStartX) >= 3) dragged = true;

                if (dragged)
                {
                    // 只上报累计位移，不在这里改布局：
                    // 拖动中每次重排都会让相机窗口跟着缩放重画，画面因此不断闪烁。
                    Action<int> moved = DragMoved;
                    if (moved != null) moved(e.X - dragStartX);
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            // 折叠开关与拖动是两件事：收起态不允许拖动（DragEnabled=false），
            // 但点击必须依然能展开，所以这里判 pressed 而不是 dragging。
            if (pressed)
            {
                pressed = false;
                dragging = false;
                Capture = false;

                if (dragged)
                {
                    Action<int> commit = DragCommitted;
                    if (commit != null) commit(e.X - dragStartX);
                }
                else
                {
                    Action handler = ToggleRequested;
                    if (handler != null) handler();
                }
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 颜色全部取自统一样式令牌（ColorBridge 每次绘制实时读取，主题切换即生效）
            Color textColor = hovered
                ? Frame_UI.WinForms.ColorBridge.TextPrimary
                : Frame_UI.WinForms.ColorBridge.TextSecondary;

            Graphics g = e.Graphics;
            // 底色跟随窗口底而不是 PanelDeep：这条紧贴相机画面，
            // 深一档的底色会在黑色画面上显出一条“压在图上”的竖条。
            g.Clear(hovered
                ? Frame_UI.WinForms.ColorBridge.ButtonHoverBg
                : Frame_UI.WinForms.ColorBridge.Canvas);

            // 左边一条分隔线，把窄条和相机区分开；右边界就是数据区，展开时另有它的边框
            using (Pen edge = new Pen(Frame_UI.WinForms.ColorBridge.LineSubtle))
            {
                g.DrawLine(edge, 0, 0, 0, Height);
            }

            // 窄条（宽度不足 22px）只画箭头：竖排文字的字高就是它占的横向宽度，
            // 再窄会切字；而且这块是贴在相机画面边上的，能省则省。
            bool showLabel = Width >= 22;
            float arrowCenterY = Height / 2F;

            if (showLabel)
            {
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
                    using (SolidBrush brush = new SolidBrush(textColor))
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

                arrowCenterY = top + textSize.Width + gap + arrowHeight / 2F;
            }

            // 箭头画在未旋转的坐标里，方向不会被旋转换算弄反：
            // 展开时数据区往右退走（▶），收起时数据区从右边进来（◀）
            float dir = Expanded ? 1F : -1F;
            using (SolidBrush brush = new SolidBrush(textColor))
            {
                g.FillPolygon(brush, new[]
                {
                    new PointF(Width / 2F + 5F * dir, arrowCenterY),
                    new PointF(Width / 2F - 3F * dir, arrowCenterY - 6F),
                    new PointF(Width / 2F - 3F * dir, arrowCenterY + 6F),
                });
            }
        }
    }
}