using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 主界面工厂。项目视图来自设备描述符，描述符未注册时在 DeviceRegistry 就已报错，
    /// 这里不会再出现 ToolView 为 null 后 Controls.Add(null) 的情况。
    /// </summary>
    public class ViewFactory : IViewFactory
    {
        public UC_ToolView ToolView;
        public UCViewBase ProjectView;

        public void Load(IDeviceDescriptor descriptor, VMFactory vMFactory)
        {
            ProjectView = descriptor.CreateProjectView(vMFactory.WorkVMBase);
            ProjectView.Dock = DockStyle.Fill;
            ProjectView.Show();

            ToolView = new UC_ToolView(vMFactory.WorkVMBase) { Dock = DockStyle.Fill };
            ToolView.panel_DataShow.Controls.Clear();
            ToolView.panel_DataShow.Controls.Add(ProjectView);
            ToolView.Show();
        }
    }
}