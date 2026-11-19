using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Frame_UI.WinForms
{
    /// <summary>
    /// TabControl 标题条底色接管。
    ///
    /// 存在理由：页签可以自绘（DrawItem），但标题条的“空白段 + 外框”由系统主题绘制，
    /// 属性改不动——深色主题下就出现“页签和页体都是深的，中间夹一条白底”。
    ///
    /// 做法：挂一个 NativeWindow，等系统画完（WM_PAINT 之后）用令牌把
    /// “客户区减去页体矩形”的区域重填一遍，再把页签按同一套配色重画。
    /// 之所以必须在系统绘制之后：TabControl 不是 UserPaint 控件，
    /// 它的标题条早于控件的 Paint 事件、由 comctl32 直接画，先画会被它盖掉。
    /// </summary>
    internal sealed class TabStripPainter : NativeWindow
    {
        private const int WmPaint = 0x000F;

        private static readonly ConditionalWeakTable<TabControl, TabStripPainter> Attached =
            new ConditionalWeakTable<TabControl, TabStripPainter>();

        private readonly TabControl tab;

        private TabStripPainter(TabControl tab)
        {
            this.tab = tab;
        }

        /// <summary>幂等：同一控件只挂一次；句柄重建后重新绑定。</summary>
        public static void Attach(TabControl tab)
        {
            if (tab == null) return;

            TabStripPainter painter;
            if (Attached.TryGetValue(tab, out painter))
            {
                painter.Bind();
                return;
            }

            painter = new TabStripPainter(tab);
            Attached.Add(tab, painter);
            tab.HandleCreated += painter.OnHandleCreated;
            tab.HandleDestroyed += painter.OnHandleDestroyed;
            painter.Bind();
        }

        private void OnHandleCreated(object sender, EventArgs e)
        {
            Bind();
        }

        private void OnHandleDestroyed(object sender, EventArgs e)
        {
            ReleaseHandle();
        }

        private void Bind()
        {
            if (tab.IsDisposed || !tab.IsHandleCreated) return;
            if (Handle != tab.Handle) AssignHandle(tab.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmPaint) PaintStrip();
        }

        private void PaintStrip()
        {
            if (tab == null || tab.IsDisposed || !tab.IsHandleCreated) return;

            Rectangle client = tab.ClientRectangle;
            if (client.Width <= 0 || client.Height <= 0) return;

            Rectangle page = tab.DisplayRectangle;

            using (Graphics g = Graphics.FromHwnd(Handle))
            using (SolidBrush back = new SolidBrush(ColorBridge.Panel))
            {
                // 只填“客户区 - 页体”：页体由 TabPage 自己画，
                // 重填整块会连页内控件一起刷掉，导致内容闪烁。
                Region strip = new Region(client);
                if (page.Width > 0 && page.Height > 0) strip.Exclude(page);
                g.FillRegion(back, strip);
                strip.Dispose();
            }

            // 上面那层填充把页签也盖住了，按同一套配色补画一次。
            using (Graphics g = Graphics.FromHwnd(Handle))
            {
                for (int i = 0; i < tab.TabPages.Count; i++)
                {
                    Rectangle bounds;
                    try
                    {
                        bounds = tab.GetTabRect(i);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        continue;
                    }
                    if (bounds.Width <= 0 || bounds.Height <= 0) continue;

                    bool selected = i == tab.SelectedIndex;
                    using (SolidBrush back = new SolidBrush(selected ? ColorBridge.Panel : ColorBridge.PanelDeep))
                    {
                        g.FillRectangle(back, bounds);
                    }

                    TextRenderer.DrawText(g, tab.TabPages[i].Text, tab.Font, bounds,
                        selected ? ColorBridge.TextPrimary : ColorBridge.TextSecondary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }
    }
}