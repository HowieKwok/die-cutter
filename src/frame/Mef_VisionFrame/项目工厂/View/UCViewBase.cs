using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    public partial class UCViewBase : UserControl
    {
        public UCViewBase()
        {
            InitializeComponent();

            // 主题在构造期就下发：视图是“先建好、后挂到主窗体”，
            // 等到 OnLoad 再上色的话，首帧会先按 Designer 原始配色画一遍、
            // 再被整体刷成主题色——主界面刚出现时那一闪就是这个。
            // Apply 幂等，OnLoad 里再下一次只是兜底。
            Frame_UI.WinForms.WinFormsThemeApplier.Apply(this);

            // 视图里图层多（相机格子、统计表），双缓冲让首帧和后续重排都不露白。
            DoubleBuffered = true;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // 主视图 UC 链在运行期才被创建并挂进主窗体，错过了窗体构造期的统一样式下发，
            // 这里在 Load 时对本 UC 整棵树补一次。方法幂等，重复调用无副作用。
            Frame_UI.WinForms.WinFormsThemeApplier.Apply(this);
        }

        public virtual void RefreshView()
        {

        }

        public virtual void InitView()
        {

        }
    }
}
