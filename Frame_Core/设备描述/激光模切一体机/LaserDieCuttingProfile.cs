using Frame_Core.业务参数类;
using Frame_Core.业务类;
using System;
using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// 激光模切一体机：6 路相机 = 主模切 + 从模切 + 裁切纠偏 + 3 路尺寸。
    /// 相机名沿用 Cam1..Cam6：BaseProDevice.GetCamID 与既有的存图/流程命名都依赖这个名字。
    /// </summary>
    public class LaserDieCuttingProfile : DeviceProfileBase
    {
        public override MaterialCrafEnum CraftType
        {
            get { return MaterialCrafEnum.激光模切一体机; }
        }

        public override string DisplayName
        {
            get { return "激光模切一体机"; }
        }

        public override int ProcessCount
        {
            get { return 8; }
        }

        public override BaseProDevice CreateProDevice()
        {
            return new DieCutProDevice();
        }

        /// <summary>五金模切上机前沿用同一份 IO 点位；上机后换成自己的一份即可。</summary>
        public override IReadOnlyList<IoPoint> IoPoints
        {
            get { return DieCuttingCutIoPoints.All; }
        }

        protected override CamSlotDef[] BuildCamSlots()
        {
            return new[]
            {
                // 下标 0/1：模切主、从相机（大图 + 极片 EA）
                new CamSlotDef(CamRole.模切主相机, CamName(0), name => new DieCutParam(name)),
                new CamSlotDef(CamRole.模切从相机, CamName(1), name => new DieCutParam(name)),
                // 下标 2：裁切定位/纠偏（五金模切没有这一路）
                new CamSlotDef(CamRole.裁切纠偏, CamName(2), name => new CutCorrectParam(name)),
                // 下标 3~5：尺寸检测
                new CamSlotDef(CamRole.尺寸检测, CamName(3), name => new SizeDetectParam(name)),
                new CamSlotDef(CamRole.尺寸检测, CamName(4), name => new SizeDetectParam(name)),
                new CamSlotDef(CamRole.尺寸检测, CamName(5), name => new SizeDetectParam(name)),
            };
        }

        /// <summary>
        /// 8 条流程：2 条裁图 + 2 条极片EA + 1 条纠偏 + 3 条尺寸。
        /// 流程名必须沿用 Service_Camera.GetCam_Name（即配置里的相机名），
        /// 否则 Process 子目录与既有的 Instance.xml 会对不上。
        /// Cam5/Cam6 谁用"带极耳"版本取决于配方全局变量"极耳在左侧"。
        /// </summary>
        public override IReadOnlyList<ProcessSpec> BuildProcessList()
        {
            CraftParam_DieCuttingCut craft = Project.Instance.craftBase as CraftParam_DieCuttingCut;
            if (craft == null)
            {
                throw new InvalidOperationException(
                    "激光模切一体机的配方参数未就绪，无法构建流程清单。");
            }
            bool tabIsLeft = craft.Get_GlobalVariableItem("极耳在左侧").Value.ToInt() == 1;

            ProcessSpec cam5 = tabIsLeft
                ? new ProcessSpec(4, ProcessName(4), ProcessKind.极片尺寸带极耳检测)
                : new ProcessSpec(4, ProcessName(4), ProcessKind.极片尺寸检测);
            ProcessSpec cam6 = tabIsLeft
                ? new ProcessSpec(5, ProcessName(5), ProcessKind.极片尺寸检测)
                : new ProcessSpec(5, ProcessName(5), ProcessKind.极片尺寸带极耳检测);

            return new[]
            {
                new ProcessSpec(0, ProcessName(0), ProcessKind.相机裁图),
                new ProcessSpec(1, ProcessName(1), ProcessKind.相机裁图),
                new ProcessSpec(0, ProcessName(0) + "_极片EA", ProcessKind.极片EA),
                new ProcessSpec(1, ProcessName(1) + "_极片EA", ProcessKind.极片EA),
                new ProcessSpec(2, ProcessName(2), ProcessKind.纠偏检测),
                new ProcessSpec(3, ProcessName(3), ProcessKind.极片尺寸检测),
                cam5,
                cam6,
            };
        }

        /// <summary>流程名取配置里的相机名，与既有 Process 目录、Instance.xml 保持一致。</summary>
        private static string ProcessName(int cameraIndex)
        {
            return Service_Camera.GetCam_Name(cameraIndex);
        }

        public override CraftBase ReadCraft(CraftBase baseCraft, string craftBasePath, string recipeName, int cameraCount)
        {
            return baseCraft.ReadCraft<CraftParam_DieCuttingCut>(craftBasePath, CraftType, recipeName, cameraCount);
        }

        private static string CamName(int index)
        {
            return ((CameraEnum)index).ToString();
        }
    }
}