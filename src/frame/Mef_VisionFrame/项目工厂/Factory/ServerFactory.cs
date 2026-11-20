using Frame_Core;
using System;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 业务服务工厂。创建流程业务对象后，把描述符里声明的相机回调挂到设备参数上。
    /// </summary>
    public class ServerFactory : IServerFactory
    {
        public ServerBase WorkServerBase;

        public void Load(IDeviceDescriptor descriptor, ModelFactory modelFactory)
        {
            ServerBase server = descriptor.CreateServer(modelFactory.WorkModelBase);
            BindCameraCallbacks(descriptor, server);
            WorkServerBase = server;
        }

        /// <summary>
        /// 把每路相机的处理函数挂到对应的 BaseProParam 上，实机与仿真各挂一套。
        /// 槽位缺失时立即报出机型名和相机下标，不再用魔法下标硬取字典。
        /// </summary>
        private static void BindCameraCallbacks(IDeviceDescriptor descriptor, ServerBase server)
        {
            BaseProDevice device = Project.Instance.baseProDevice;

            foreach (CamSlot slot in descriptor.CamSlots)
            {
                BaseProParam workParam;
                if (!device.m_DicCamOperaParam.TryGetValue(slot.Index, out workParam))
                {
                    throw new InvalidOperationException(string.Format(
                        "设备 {0} 缺少相机槽位 {1}（{2}），请检查 AllCamParam 中配置的相机数量。",
                        descriptor.DisplayName, slot.Index, slot.Name));
                }
                slot.BindCallbacks(server, workParam);
            }
        }
    }
}