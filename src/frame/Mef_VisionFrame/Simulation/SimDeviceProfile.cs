using System.Collections.Generic;
using ControlBoard.Interface;
using Frame_Core;

namespace Mef_VisionFrame.Simulation
{
    /// <summary>
    /// 仿真用的设备画像：除控制卡外全部委托给真机画像。
    ///
    /// 用"替换画像"而不是"在 Init_Dev 里判断模式"，是为了让设备组成只有注册一个入口：
    /// 上层拿到的 IDeviceProfile 永远完整可用，运行期没有 if (仿真) 分支，
    /// 新增外设也只需要在画像里加一个成员，不会再多一个散落的注入点。
    /// </summary>
    internal sealed class SimDeviceProfile : IDeviceProfile
    {
        private readonly IDeviceProfile _real;
        private readonly IControlCard _controlCard;

        public SimDeviceProfile(IDeviceProfile real, IControlCard controlCard)
        {
            _real = real;
            _controlCard = controlCard;
        }

        public MaterialCrafEnum CraftType { get { return _real.CraftType; } }

        public string DisplayName { get { return _real.DisplayName; } }

        public int CameraCount { get { return _real.CameraCount; } }

        public int ProcessCount { get { return _real.ProcessCount; } }

        public IReadOnlyList<CamSlotDef> CamSlots { get { return _real.CamSlots; } }

        public IReadOnlyList<IoPoint> IoPoints { get { return _real.IoPoints; } }

        public IReadOnlyList<ProcessSpec> BuildProcessList() { return _real.BuildProcessList(); }

        public CraftBase ReadCraft(CraftBase baseCraft, string craftBasePath, string recipeName, int cameraCount)
        {
            return _real.ReadCraft(baseCraft, craftBasePath, recipeName, cameraCount);
        }

        public BaseProDevice CreateProDevice() { return _real.CreateProDevice(); }

        /// <summary>唯一的差异点：真机是 0640 板卡，这里是进程内的模拟卡。</summary>
        public IControlCard CreateControlCard() { return _controlCard; }

        public int IndexOf(CamRole role) { return _real.IndexOf(role); }

        public bool TryIndexOf(CamRole role, out int index) { return _real.TryIndexOf(role, out index); }
    }
}