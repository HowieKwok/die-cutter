using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 测试替身：ScotPotUIUser.UpdateCurve 只用到 Form_Main.FormMain 的窗口句柄
    /// （IsHandleCreated 判断 + BeginInvoke 切回 UI 线程）。性能测试不需要启动整套
    /// 视觉框架、相机和 PLC，所以这里给一个最小实现。
    /// </summary>
    internal class Form_Main : Form
    {
        public static Form_Main FormMain { get; set; }
    }
}