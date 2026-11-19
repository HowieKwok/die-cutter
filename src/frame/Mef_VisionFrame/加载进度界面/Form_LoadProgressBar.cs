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
    public partial class Form_LoadProgressBar : Form
    {
        public bool closingFlag = false;
        private Action<Form_LoadProgressBar> action = null;

        public Form_LoadProgressBar()
        {
            InitializeComponent();

            // 启动页是固定品牌画面（黑底 + 白色 logo/文案），**不参与主题下发**。
            // 之前接 FormStylingHook 后，浅色主题会把黑底刷成近白，
            // logo 与白字全部糊在浅底上——看起来像蒙了一层白雾。
            // 业务界面统一走主题，启动页保持设计稿配色。
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (closingFlag)
            {
                closingFlag = false;
                this.Invoke(new Action(() => { this.Close(); this.Dispose(); }));
            }
        }
    }
}
