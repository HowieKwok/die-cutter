using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows.Forms;

namespace Frame_Core
{
    [ToolboxItem(true)]
    public class ControlButton : Button
    {
        private string _checkedText = "";
        private string _unCheckedText = "";
        private bool _checked;

        private Color _checkedBackColor = Color.DodgerBlue;
        private Color _unCheckedBackColor = SystemColors.Control;

        private Image _checkedImage;
        private Image _unCheckedImage;

        /// <summary>
        /// 状态色是否由业务语义固定（如启动=绿 / 运行中=红）。
        /// 主题下发器会识别该属性：为 true 时不再改写 Checked/UnChecked 两色，
        /// 否则统一样式会把“启动/停止”刷成普通按钮色，状态一眼看不出。
        /// </summary>
        private bool _keepStateColors;

        public ControlButton()
        {
            UseVisualStyleBackColor = false;
            BackgroundImageLayout = ImageLayout.Stretch;
        }

        [Category("自定义")]
        [Description("选中显示的文本")]
        [Browsable(true)]
        [DefaultValue("")]
        public string CheckedText
        {
            get => _checkedText;
            set
            {
                _checkedText = value;
                UpdateState();
            }
        }

        [Category("自定义")]
        [Description("未选中显示的文本")]
        [Browsable(true)]
        [DefaultValue("")]
        public string UnCheckedText
        {
            get => _unCheckedText;
            set
            {
                _unCheckedText = value;
                UpdateState();
            }
        }

        [Category("自定义")]
        [Description("选中时显示的背景颜色")]
        [Browsable(true)]
        public Color CheckedBackColor
        {
            get => _checkedBackColor;
            set
            {
                _checkedBackColor = value;
                UpdateState();
            }
        }

        [Category("自定义")]
        [Description("未选中时显示的背景颜色")]
        [Browsable(true)]
        public Color UnCheckedBackColor
        {
            get => _unCheckedBackColor;
            set
            {
                _unCheckedBackColor = value;
                UpdateState();
            }
        }

        [Category("自定义")]
        [Description("选中时显示的图像")]
        [Browsable(true)]
        [DefaultValue(null)]
        public Image CheckedImage
        {
            get => _checkedImage;
            set
            {
                _checkedImage = value;
                UpdateState();
            }
        }

        [Category("自定义")]
        [Description("未选中时显示的图像")]
        [Browsable(true)]
        [DefaultValue(null)]
        public Image UnCheckedImage
        {
            get => _unCheckedImage;
            set
            {
                _unCheckedImage = value;
                UpdateState();
            }
        }

        [Category("自定义")]
        [Description("状态色不随主题变化（业务语义色，如启动绿/运行红）")]
        [Browsable(true)]
        [DefaultValue(false)]
        public bool KeepStateColors
        {
            get => _keepStateColors;
            set => _keepStateColors = value;
        }

        [Category("自定义")]
        [Description("选中状态")]
        [Browsable(true)]
        [DefaultValue(false)]
        [RefreshProperties(RefreshProperties.All)]
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value)
                    return;

                _checked = value;
                UpdateState();
            }
        }

        private void UpdateState()
        {
            Text = _checked ? _checkedText : _unCheckedText;
            BackColor = _checked ? _checkedBackColor : _unCheckedBackColor;
            Image = _checked ? _checkedImage : _unCheckedImage;
        }
    }
}

