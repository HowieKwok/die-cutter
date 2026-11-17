using Frame_Core;
using System;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    public class Craft_ViewService : IBusinessService
    {
        /// <summary>
        /// 获取配方对应工艺参数和对应窗体。
        /// 机型必须有对应的工艺窗体；没有实现的机型直接抛异常，不再回落到激光模切的窗体，
        /// 避免在错误的界面上改出别的机型的配方数据。
        /// </summary>
        public static FormCraftBase GetView(FormCraftBase formCraftBase, MaterialCrafEnum craftType)
        {
            if (formCraftBase != null)
            {
                formCraftBase.Close();
                formCraftBase.Dispose();
            }

            formCraftBase = CreateView(craftType);
            formCraftBase.FormBorderStyle = FormBorderStyle.None;
            formCraftBase.Dock = DockStyle.Fill;
            formCraftBase.TopLevel = false;
            return formCraftBase;
        }

        private static FormCraftBase CreateView(MaterialCrafEnum craftType)
        {
            switch (craftType)
            {
                case MaterialCrafEnum.激光模切一体机:
                    return new Form_DieCuttingCut();
                case MaterialCrafEnum.五金模切:
                    return new Form_五金模切();
                default:
                    throw new NotSupportedException(string.Format(
                        "机型 {0} 还没有实现工艺设置窗体。请在 Craft_ViewService.CreateView 中补充该机型的 Form。",
                        craftType));
            }
        }
    }
}