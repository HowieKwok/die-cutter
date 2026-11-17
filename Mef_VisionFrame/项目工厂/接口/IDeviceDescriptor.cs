using Frame_Core;
using System;
using System.Collections.Generic;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 一种设备类型（工艺）的完整描述：该机型用哪套 Model/Server/VM/View，
    /// 以及每个相机槽位要挂到 Server 上的回调。
    /// 新增机型 = 新增一个描述符 + 在 DeviceRegistry 中注册一条，不再逐个修改四个工厂。
    /// </summary>
    public interface IDeviceDescriptor
    {
        /// <summary>对应的工艺枚举值，注册表的键。</summary>
        MaterialCrafEnum CraftType { get; }

        /// <summary>机型显示名，只用于日志和错误提示。</summary>
        string DisplayName { get; }

        ModelBase CreateModel();

        ServerBase CreateServer(ModelBase model);

        VMBase CreateViewModel(ModelBase model, ServerBase server);

        /// <summary>项目主视图（该机型的工位/调试界面）。</summary>
        UCViewBase CreateProjectView(VMBase viewModel);

        /// <summary>相机槽位表：下标语义、相机名、回调绑定。</summary>
        IReadOnlyList<CamSlot> CamSlots { get; }
    }
}