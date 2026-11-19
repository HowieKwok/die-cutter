using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Frame_UI.WinForms
{
    /// <summary>
    /// 窗体外壳形态。构造期唯一决定，运行期不可变：
    /// 形态是能力声明，不是可变状态，否则“已装好的外壳”和“新形态”会互相打架。
    /// </summary>
    public enum ShellMode
    {
        /// <summary>独立窗口：无原生边框，由 <see cref="WindowResizeBehavior"/> 补齐缩放与最大化约束。</summary>
        Standalone,

        /// <summary>内嵌到宿主容器：不绘制标题栏，不提供缩放。</summary>
        Embedded
    }

    /// <summary>
    /// 无边框窗口的形态补偿：边缘拖拽缩放 + 最大化不覆盖任务栏。
    ///
    /// 为什么单独成类：去掉原生边框会把“窗口形态”一整块责任丢掉，
    /// 若把 WndProc 的补偿逻辑塞回外壳装配器，视觉与形态又混在一起，
    /// 下一个改动的人仍然要在一个类里同时推理颜色和命中测试。
    ///
    /// 通过 NativeWindow 挂 WndProc，不改变 Form 的继承链，
    /// 因而插件窗体依旧只继承插件基类。
    /// </summary>
    internal sealed class WindowResizeBehavior : NativeWindow
    {
        private const int WmNcHitTest = 0x0084;
        private const int WmGetMinMaxInfo = 0x0024;
        private const int ResizeBorder = 6;

        private const int HtClient = 1;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;

        private readonly Form form;

        private WindowResizeBehavior(Form form)
        {
            this.form = form;
        }

        /// <summary>挂到已创建的窗口句柄上。句柄未创建时挂载会失败，因此必须延迟到 HandleCreated。</summary>
        public static WindowResizeBehavior Attach(Form form)
        {
            var behavior = new WindowResizeBehavior(form);
            behavior.AssignHandle(form.Handle);
            return behavior;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmNcHitTest && form.WindowState == FormWindowState.Normal)
            {
                m.Result = (IntPtr)HitTest(Cursor.Position);
                return;
            }

            if (m.Msg == WmGetMinMaxInfo)
            {
                ConstrainToWorkArea(m.LParam);
                return;
            }

            base.WndProc(ref m);
        }

        private int HitTest(System.Drawing.Point screenPoint)
        {
            System.Drawing.Point client = form.PointToClient(screenPoint);
            int width = form.ClientSize.Width;
            int height = form.ClientSize.Height;

            bool left = client.X <= ResizeBorder;
            bool right = client.X >= width - ResizeBorder;
            bool top = client.Y <= ResizeBorder;
            bool bottom = client.Y >= height - ResizeBorder;

            if (top && left) return HtTopLeft;
            if (top && right) return HtTopRight;
            if (bottom && left) return HtBottomLeft;
            if (bottom && right) return HtBottomRight;
            if (left) return HtLeft;
            if (right) return HtRight;
            if (top) return HtTop;
            if (bottom) return HtBottom;
            return HtClient;
        }

        private void ConstrainToWorkArea(IntPtr lParam)
        {
            IntPtr monitor = MonitorFromWindow(form.Handle, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return;

            var info = new MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (!GetMonitorInfo(monitor, ref info)) return;

            var minMax = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo));
            minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
            minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
            minMax.MaxSize.X = info.Work.Right - info.Work.Left;
            minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;
            Marshal.StructureToPtr(minMax, lParam, false);
        }

        private const int MonitorDefaultToNearest = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public int Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point Reserved;
            public Point MaxSize;
            public Point MaxPosition;
            public Point MinTrackSize;
            public Point MaxTrackSize;
        }
    }
}