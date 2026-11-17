using System;
using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// 五金模切：5 路相机 = 主模切 + 从模切 + 3 路尺寸；
    /// 没有裁切定位工位（原激光模切的裁切纠偏相机及其流程都不存在）。
    /// 工艺点位先沿用激光模切，上机后在这里换成自己的点位表即可。
    /// </summary>
    public class 五金模切Profile : DeviceProfileBase
    {
        public override MaterialCrafEnum CraftType
        {
            get { return MaterialCrafEnum.五金模切; }
        }

        public override string DisplayName
        {
            get { return "五金模切"; }
        }

        public override int ProcessCount
        {
            get { return 7; }
        }

        public override IReadOnlyList<IoPoint> IoPoints
        {
            get { return DieCuttingCutIoPoints.All; }
        }

        public override BaseProDevice CreateProDevice()
        {
            return new 业务类.五金模切ProDevice(this);
        }

        public override CraftBase ReadCraft(CraftBase baseCraft, string craftBasePath, string recipeName, int cameraCount)
        {
            return baseCraft.ReadCraft<CraftParam_五金模切>(craftBasePath, CraftType, recipeName, cameraCount);
        }

        protected override CamSlotDef[] BuildCamSlots()
        {
            return new[]
            {
                // 下标 0/1：模切主、从相机（与激光模切同角色）
                new CamSlotDef(CamRole.模切主相机, CamName(0), name => new 业务参数类.DieCutParam(name)),
                new CamSlotDef(CamRole.模切从相机, CamName(1), name => new 业务参数类.DieCutParam(name)),
                // 下标 2~4：尺寸检测（没有裁切纠偏，所以尺寸从下标 2 开始）
                new CamSlotDef(CamRole.尺寸检测, CamName(2), name => new 业务参数类.SizeDetectParam(name)),
                new CamSlotDef(CamRole.尺寸检测, CamName(3), name => new 业务参数类.SizeDetectParam(name)),
                new CamSlotDef(CamRole.尺寸检测, CamName(4), name => new 业务参数类.SizeDetectParam(name)),
            };
        }

        /// <summary>
        /// 7 条流程：2 条裁图 + 2 条极片EA + 3 条尺寸（暂无裁切纠偏）。
        /// 流程内容（模块装配/运行处理）尚未实现，见 五金模切ProDevice。
        /// </summary>
        public override IReadOnlyList<ProcessSpec> BuildProcessList()
        {
            return new[]
            {
                new ProcessSpec(0, ProcessName(0), ProcessKind.相机裁图),
                new ProcessSpec(1, ProcessName(1), ProcessKind.相机裁图),
                new ProcessSpec(0, ProcessName(0) + "_极片EA", ProcessKind.极片EA),
                new ProcessSpec(1, ProcessName(1) + "_极片EA", ProcessKind.极片EA),
                new ProcessSpec(2, ProcessName(2), ProcessKind.极片尺寸检测),
                new ProcessSpec(3, ProcessName(3), ProcessKind.极片尺寸检测),
                new ProcessSpec(4, ProcessName(4), ProcessKind.极片尺寸检测),
            };
        }

        /// <summary>流程名取配置里的相机名，与激光模切保持同一套约定。</summary>
        private static string ProcessName(int cameraIndex)
        {
            return Service_Camera.GetCam_Name(cameraIndex);
        }

        private static string CamName(int index)
        {
            return ((CameraEnum)index).ToString();
        }
    }
}