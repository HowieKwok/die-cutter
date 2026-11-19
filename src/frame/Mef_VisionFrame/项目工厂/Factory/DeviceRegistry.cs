using Frame_Core;
using System;
using System.Collections.Generic;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 设备类型 → 描述符 的唯一注册表。
    /// 所有"按设备类型分支"的代码都从这里取一次描述符；取不到立即抛出带机型名的异常，
    /// 避免 WorkServerBase / ToolView 为 null 后在几百行之外才出现空引用。
    /// </summary>
    public static class DeviceRegistry
    {
        private static readonly Dictionary<MaterialCrafEnum, IDeviceDescriptor> descriptors =
            new Dictionary<MaterialCrafEnum, IDeviceDescriptor>
            {
                { MaterialCrafEnum.激光模切一体机, new DieCuttingCutDescriptor() },
                { MaterialCrafEnum.五金模切, new 五金模切Descriptor() },
            };

        /// <summary>
        /// 按工艺类型解析描述符。未注册时抛 NotSupportedException，而不是返回 null。
        /// </summary>
        public static IDeviceDescriptor Resolve(MaterialCrafEnum craftType)
        {
            IDeviceDescriptor descriptor;
            if (descriptors.TryGetValue(craftType, out descriptor)) return descriptor;

            throw new NotSupportedException(
                "未注册的设备类型：" + craftType +
                "。请在 DeviceRegistry.descriptors 中补充该机型的 IDeviceDescriptor。");
        }
    }
}