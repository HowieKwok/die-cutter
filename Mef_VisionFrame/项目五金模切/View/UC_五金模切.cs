using System.Drawing;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切的主界面。界面布局（相机区 / 数据区）待实现，
    /// 先放一个占位提示，保证按机型装配能跑通；
    /// 实现时参考 UC_DieCuttingCut 的做法：用 CamDisplayUnit 声明每一路相机的显示单元表。
    /// </summary>
    public class UC_五金模切 : UCViewBase
    {
        private readonly VM_五金模切 VM;

        public UC_五金模切(VM_五金模切 viewModel)
        {
            VM = viewModel;

            VM.Server.InitView -= InitView;
            VM.Server.InitView += InitView;
            VM.Server.RefreshToolView -= RefreshView;
            VM.Server.RefreshToolView += RefreshView;

            Controls.Add(new Label
            {
                Text = "五金模切主界面待实现",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            });
        }

        public override void RefreshView()
        {
        }
    }
}