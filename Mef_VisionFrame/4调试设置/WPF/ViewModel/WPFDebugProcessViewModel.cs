using CommunityToolkit.Mvvm.ComponentModel;

namespace Mef_VisionFrame._4调试设置.WPF.ViewModel
{
    /// <summary>
    /// 调试流程窗口的视图模型。
    /// 只保留业务相关的可绑定状态：窗口标题。
    /// 最小化/最大化/关闭命令已上移到 Frame_UI.Wpf.ThemedWindow 统一实现，
    /// 避免每个窗口各自持有一份窗口控制逻辑（那是第二套外壳）。
    /// </summary>
    public sealed class WPFDebugProcessViewModel : ObservableObject
    {
        private string _windowTitle = "调试流程界面";

        public string WindowTitle
        {
            get { return _windowTitle; }
            set { SetProperty(ref _windowTitle, value); }
        }
    }
}