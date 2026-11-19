using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using HalconDotNet;

namespace Frame_Core
{
    public partial class UC_HalconWindow : UserControl
    {

        // 定义一个公共属性，用于设置标题
        [Category("Custom Properties")] // 分类显示在属性窗口中
        [Description("设置控件的标题")] // 属性描述
        public string Title
        {
            get { return LB_Process.Text; }
            set { LB_Process.Text = value; }
        }


        // 定义一个公共属性，用于设置灰度
        [Category("Custom Properties")] // 分类显示在属性窗口中
        [Description("灰度信息")] // 属性描述
        public string GrayInfo
        {
            get { return TB_PositionAndGray.Text; }
            set { TB_PositionAndGray.Text = value; }
        }

        // 公开PictureBox的引用
        public PictureBox PB_FitWinOutter => PB_FitWin;

        public HWindowControl hWindow => HWC;

        public TextBox TB_GrayInfo => TB_PositionAndGray;

        public UC_HalconWindow()
        {
            InitializeComponent();

            // 标题栏图标改为现画：资源里的位图是白色填充，
            // 浅色主题下标题栏被刷成 Canvas，白图标压在浅底上等于消失。
            // 墨色按控件实际底色反差推算，主题切换改底色时会重算一次（见 BackColorChanged）。
            PB_FitWin.BackColorChanged += delegate { ApplyFitGlyph(); };
            ApplyFitGlyph();
        }

        /// <summary>按当前底色反差重画“重置显示”图标（14px，粗描边，抗锯齿）。</summary>
        private void ApplyFitGlyph()
        {
            PB_FitWin.Image = ToolGlyph.RefreshView(14, ToolGlyph.ContrastInk(PB_FitWin.BackColor));
        }
    }
}
