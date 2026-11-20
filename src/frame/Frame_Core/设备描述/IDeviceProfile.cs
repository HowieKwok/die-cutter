using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// 一种设备类型（工艺）的画像：相机槽位、流程数量、设备业务类的创建方式。
    /// 所有"按机型分支"的设备层代码都应通过 DeviceProfileRegistry 取一个画像，而不是写 if。
    /// </summary>
    public interface IDeviceProfile
    {
        /// <summary>对应的工艺枚举值，注册表的键。</summary>
        MaterialCrafEnum CraftType { get; }

        /// <summary>机型显示名，用于日志与异常提示。</summary>
        string DisplayName { get; }

        /// <summary>相机数量，等于 CamSlots 的数量。</summary>
        int CameraCount { get; }

        /// <summary>该机型构建的流程条数（仅作记录，实际以流程清单为准）。</summary>
        int ProcessCount { get; }

        /// <summary>相机槽位表，下标即物理相机下标，必须连续。</summary>
        IReadOnlyList<CamSlotDef> CamSlots { get; }

        /// <summary>运控 IO 点位表。换机型只换这张表，业务流程代码不动。</summary>
        IReadOnlyList<IoPoint> IoPoints { get; }

        /// <summary>该机型要构建的流程清单，顺序即构建顺序。</summary>
        IReadOnlyList<ProcessSpec> BuildProcessList();

        /// <summary>读取本机型的配方参数对象（按机型决定参数类）。</summary>
        CraftBase ReadCraft(CraftBase baseCraft, string craftBasePath, string recipeName, int cameraCount);

        /// <summary>创建本机型的设备业务对象。</summary>
        BaseProDevice CreateProDevice();

        /// <summary>
        /// 创建本机型的控制卡实现。真机返回 0640 板卡；仿真由宿主把画像换成模拟实现。
        /// 契约：必须返回实例，不得返回 null——"这台机器没有这种卡"要用专门的空实现表达，
        /// 因为 Server 构造期间就要拿它配中断、建 IO 输出，缺卡不能在远处表现为空引用。
        /// </summary>
        ControlBoard.Interface.IControlCard CreateControlCard();

        /// <summary>按角色取相机下标；该机型没有这个角色时抛异常。</summary>
        int IndexOf(CamRole role);

        /// <summary>按角色取相机下标；没有该角色时返回 false。</summary>
        bool TryIndexOf(CamRole role, out int index);
    }
}