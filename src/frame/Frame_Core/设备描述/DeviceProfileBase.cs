using System;
using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// 设备画像基类：统一实现角色查找与槽位数量，子类只需要声明槽位表。
    /// </summary>
    public abstract class DeviceProfileBase : IDeviceProfile
    {
        private CamSlotDef[] camSlots;

        public abstract MaterialCrafEnum CraftType { get; }

        public abstract string DisplayName { get; }

        public virtual int ProcessCount
        {
            get { return CameraCount; }
        }

        public IReadOnlyList<CamSlotDef> CamSlots
        {
            get
            {
                if (camSlots == null) camSlots = BuildCamSlots();
                return camSlots;
            }
        }

        public int CameraCount
        {
            get { return CamSlots.Count; }
        }

        public abstract BaseProDevice CreateProDevice();

        public abstract IReadOnlyList<IoPoint> IoPoints { get; }

        /// <summary>声明流程清单，顺序即构建顺序。建立流程时会连同模块装配一起执行。</summary>
        public abstract IReadOnlyList<ProcessSpec> BuildProcessList();

        /// <summary>读取本机型的配方参数对象。</summary>
        public abstract CraftBase ReadCraft(CraftBase baseCraft, string craftBasePath, string recipeName, int cameraCount);

        public int IndexOf(CamRole role)
        {
            int index;
            if (TryIndexOf(role, out index)) return index;

            throw new InvalidOperationException(string.Format(
                "机型 {0} 没有 {1} 相机的槽位。", DisplayName, role));
        }

        public bool TryIndexOf(CamRole role, out int index)
        {
            for (int i = 0; i < CamSlots.Count; i++)
            {
                if (CamSlots[i].Role == role)
                {
                    index = i;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        /// <summary>声明槽位表，顺序即物理相机下标。</summary>
        protected abstract CamSlotDef[] BuildCamSlots();
    }
}