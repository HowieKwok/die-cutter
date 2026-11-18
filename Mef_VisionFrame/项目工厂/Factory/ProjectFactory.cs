using System;
using Frame_Core;
using LogTool;

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
            IDeviceDescriptor descriptor = ResolveDescriptor();

            F_Model.Load(descriptor);

            F_Server.Load(descriptor, F_Model);

            F_VM.Load(descriptor, F_Model, F_Server);

            F_View.Load(descriptor, F_VM);
        }

        /// <summary>
        /// 取设备描述符。SystemParam 里的 CraftType 落在注册表之外时（被手改成越界数值、
        /// 或整份配置从别的机型拷贝过来），DeviceRegistry.Resolve 会抛 NotSupportedException。
        /// 本方法在 UI 线程上被调用、没有上层兜底，抛出去就是"未处理异常 + 进度条不关"。
        /// 这里回落到激光模切并留下明显日志，保证程序至少能起来去改配置。
        /// </summary>
        private static IDeviceDescriptor ResolveDescriptor()
        {
            MaterialCrafEnum craftType = Project.Instance.systemInfo.CraftType;

            try
            {
                return DeviceRegistry.Resolve(craftType);
            }
            catch (NotSupportedException)
            {
                MaterialCrafEnum fallback = MaterialCrafEnum.激光模切一体机;
                Log4netRecord.ErrorLog.Error(
                    $"未注册的设备类型：{craftType}，已临时按 {fallback} 启动。请检查 SystemParam 里的 CraftType。");
                return DeviceRegistry.Resolve(fallback);
            }
        }
    }
}