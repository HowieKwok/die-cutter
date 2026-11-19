using NLog.Config;
using NLog.Time;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntdUI;
using Frame_Core;

namespace Mef_VisionFrame
{
    public partial class FormCraftBase : AntdUI.Window
    {
        public FormCraftBase()
        {
            InitializeComponent();

            // AntdUI 窗口级 Mode 会覆盖全局 Config.Mode，不能写死在 Designer：
            // Auto = 跟随全局 Config.Mode（启动与主题切换时由 AntdUiThemeAdapter 维护），
            // 深/浅切换时本窗体随全局自动换肤。
            Mode = TAMode.Auto;

            // 工艺视图链的根节点：AntdUI.Window 派生的窗体在这里统一下发样式。
            // 子类的 InitializeComponent 之后还会追加自己的控件，因此子类也各自接线一次。
            Frame_Core.FormStylingHook.Style(this);
        }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="craftBase"></param>
        public virtual void Init(CraftBase craftBase)
        {

        }

        /// <summary>
        /// 把参数显示到工艺界面
        /// </summary>
        public virtual void ParamToView()
        {

        }

        /// <summary>
        /// 界面内容下发到参数
        /// </summary>
        public virtual void ViewToParam()
        {

        }
    }
}
