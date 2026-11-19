using System;
using System.Windows.Forms;

using EHolly.ImageTool;

using Frame_Core;

namespace Mef_VisionFrame
{
    public partial class Form_ImageManage : Form
    {
        public Form_ImageManage()
        {
            InitializeComponent();
            imageToolPanel1.Dock = DockStyle.Fill;

            // 这里不写颜色：EHolly.ImageTool.ImageToolPanel 内部全部是标准 WinForms 控件
            // （GroupBox / Label / NumericUpDown / CheckBox / ComboBox / DataGridView / Button），
            // 主题由 FormStylingHook 统一下发即可覆盖，单独赋色反而会被后续下发覆盖成两套观感。
        }

        private void Form_ImageManage_Load(object sender, EventArgs e)
        {
            if (Project.Instance.userManage.userData != null
                && Project.Instance.userManage.userData.CurrentUser == UserType.Admin)
            {
                EHollyImageTool.EnableProSetting = true;
            }

            // 主题必须在 Load 末尾再下发一次：
            // 面板的子控件是它自己在构造期创建的，而“高级设置”开关会改变可见子树，
            // 只在构造期下发会漏掉后出现的控件。
            FormStylingHook.Style(this);
        }
    }
}