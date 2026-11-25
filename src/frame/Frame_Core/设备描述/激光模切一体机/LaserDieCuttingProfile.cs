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
            int 配置数 = 配置相机数();

            // 6 路是激光模切的标准配置：0/1 模切、2 裁切纠偏、3~5 尺寸
            if (配置数 >= 6)
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

            // 少于 6 路：这台机器没有裁切纠偏相机，2 号起全部按尺寸相机排。
            // 不这样裁齐的话，配方装配会把纠偏的十几项变量装进一个实际是尺寸相机的页签，
            // 界面上名字对、内容错，而且不会报错。
            CamSlotDef[] slots = new CamSlotDef[配置数];
            slots[0] = new CamSlotDef(CamRole.模切主相机, CamName(0), name => new DieCutParam(name));
            slots[1] = new CamSlotDef(CamRole.模切从相机, CamName(1), name => new DieCutParam(name));
            for (int i = 2; i < 配置数; i++)
            {
                slots[i] = new CamSlotDef(CamRole.尺寸检测, CamName(i), name => new SizeDetectParam(name));
            }
            return slots;
        }

        /// <summary>
        /// 当前机器实际配置的相机路数。
        /// 配置还没加载（或异常）时退回标准的 6 路，避免把画像裁成 0 路。
        /// </summary>
        private static int 配置相机数()
        {
            try
            {
                int count = Service_Camera.GetCam_Names().Length;
                if (count >= 2) return count;
            }
            catch (Exception)
            {
                // 配置读不到就按标准 6 路，不在这里吞掉业务异常
            }
            return 6;
        }

        /// <summary>
        /// 8 条流程：2 条裁图 + 2 条极片EA + 1 条纠偏 + 3 条尺寸。
        /// 流程名必须沿用 Service_Camera.GetCam_Name（即配置里的相机名），
        /// 否则 Process 子目录与既有的 Instance.xml 会对不上。
        /// Cam5/Cam6 谁用"带极耳"版本取决于配方全局变量"极耳在左侧"。
        /// </summary>
        public override IReadOnlyList<ProcessSpec> BuildProcessList()
        {
            // 5 条流程（2 裁图 + 2 EA + 1 纠偏）之外的三条尺寸流程按下标 3/4/5 建，
            // 少一路纠偏时整张清单都会错位。这里显式报错，不要让它悄悄起一半流程。
            if (CameraCount < 6)
            {
                throw new InvalidOperationException(string.Format(
                    "激光模切一体机运行需要 6 路相机（含裁切纠偏），当前只配置了 {0} 路。请先补齐 AllCamParam 的相机配置。",
                    CameraCount));
            }

            CraftParam_DieCuttingCut craft = Project.Instance.craftBase as CraftParam_DieCuttingCut;
            if (craft == null)
            {
                throw new InvalidOperationException(
                    "激光模切一体机的配方参数未就绪，无法构建流程清单。");
            }
            bool tabIsLeft = craft.Get_GlobalVariableItem("极耳在左侧").Value.ToInt() == 1;

            // 尺寸三路谁带极耳由 SizeSlotPlan 统一决定，与配方装尺寸项同一份结论。
            SizeSlotPlan sizePlan = SizeSlotPlan.Resolve(CamSlots, tabIsLeft);

            ProcessSpec[] specs = new ProcessSpec[5 + sizePlan.SizeCams.Count];
            specs[0] = new ProcessSpec(0, ProcessName(0), ProcessKind.相机裁图);
            specs[1] = new ProcessSpec(1, ProcessName(1), ProcessKind.相机裁图);
            specs[2] = new ProcessSpec(0, ProcessName(0) + "_极片EA", ProcessKind.极片EA);
            specs[3] = new ProcessSpec(1, ProcessName(1) + "_极片EA", ProcessKind.极片EA);
            specs[4] = new ProcessSpec(2, ProcessName(2), ProcessKind.纠偏检测);

            for (int i = 0; i < sizePlan.SizeCams.Count; i++)
            {
                int camIndex = sizePlan.SizeCams[i];
                ProcessKind kind = camIndex == sizePlan.TabCam
                    ? ProcessKind.极片尺寸带极耳检测
                    : ProcessKind.极片尺寸检测;
                specs[5 + i] = new ProcessSpec(camIndex, ProcessName(camIndex), kind);
            }

            return specs;
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