using Frame_Core;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 业务对象装配入口：解析一次设备描述符，按 Model → Server → VM → View 的顺序构建。
    /// 装配顺序不能调换：Server 需要 Model，VM 需要两者，View 需要 VM。
    /// </summary>
    public static class ProjectFactory
    {
        public static ModelFactory F_Model = new ModelFactory();
        public static ServerFactory F_Server = new ServerFactory();
        public static VMFactory F_VM = new VMFactory();
        public static ViewFactory F_View = new ViewFactory();

        public static void Load()
        {
            IDeviceDescriptor descriptor = DeviceRegistry.Resolve(Project.Instance.systemInfo.CraftType);

            F_Model.Load(descriptor);

            F_Server.Load(descriptor, F_Model);

            F_VM.Load(descriptor, F_Model, F_Server);

            F_View.Load(descriptor, F_VM);
        }
    }
}