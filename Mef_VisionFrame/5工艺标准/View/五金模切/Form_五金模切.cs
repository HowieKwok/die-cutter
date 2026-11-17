using Frame_Core;
using System.Drawing;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切的工艺参数界面。参数字段待工艺确定后补充，
    /// 先给一个占位窗体，保证配方管理窗口能正常打开、配方能正常切换。
    /// </summary>
    public class Form_五金模切 : FormCraftBase
    {
        private CraftParam_五金模切 Craft;

        public Form_五金模切()
        {
            Controls.Add(new Label
            {
                Text = "五金模切工艺参数界面待实现",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            });
        }

        public override void Init(CraftBase craftBase)
        {
            Craft = craftBase as CraftParam_五金模切;
            ParamToView();
        }

        public override void ParamToView()
        {
        }

        public override void ViewToParam()
        {
        }
    }
}