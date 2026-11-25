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

        /// <summary>
        /// 五金的结果与心跳全部走 PLC（CIP 符号地址），不再使用控制卡 IO 点位。
        /// 这里返回空表，任何按点位表创建脉冲输出的代码都会自然拿不到点位。
        /// </summary>
        public override IReadOnlyList<IoPoint> IoPoints
        {
            get { return EmptyIoPoints; }
        }

        private static readonly IoPoint[] EmptyIoPoints = new IoPoint[0];

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
                // 帧号由工位序号统一分配：上线是源头，下线与上线对齐，二者共用一个序号器
                new CamSlotDef(CamRole.模切主相机, CamName(0), name => new 业务参数类.五金模切LineScanParam(name, 业务参数类.线扫安装位.上线)),
                new CamSlotDef(CamRole.模切从相机, CamName(1), name => new 业务参数类.五金模切LineScanParam(name, 业务参数类.线扫安装位.下线)),
                // 下标 2~4：尺寸检测（没有裁切纠偏，所以尺寸从下标 2 开始）
                // 三路共用一个组窗口取号，保证同组同号
                new CamSlotDef(CamRole.尺寸检测, CamName(2), name => new 业务参数类.五金模切SizeParam(name)),
                new CamSlotDef(CamRole.尺寸检测, CamName(3), name => new 业务参数类.五金模切SizeParam(name)),
                new CamSlotDef(CamRole.尺寸检测, CamName(4), name => new 业务参数类.五金模切SizeParam(name)),
            };
        }

        /// <summary>
        /// 7 条流程：2 条裁图 + 2 条极片EA + 3 条尺寸。
        ///
        /// 没有任何不取图的流程：冲模由 PLC 自己完成，视觉侧不需要交互，
        /// 因此五金只有相机驱动的处理流程——PLC 触发相机出图，帧回调进对应流程。
        ///
        /// 尺寸三路沿用激光的「带极耳」二选一规则：极耳在左侧时中间那路做带极耳检测，
        /// 否则最后一路做。上机确认后可改成按配方逐路指定。
        /// </summary>
        public override IReadOnlyList<ProcessSpec> BuildProcessList()
        {
            CraftParam_DieCutBase craft = Project.Instance.craftBase as CraftParam_DieCutBase;
            if (craft == null)
            {
                throw new InvalidOperationException(
                    "五金模切的配方参数未就绪，无法构建流程清单。");
            }

            GlobalVariableItem tabItem = craft.Get_GlobalVariableItem("极耳在左侧");
            bool tabIsLeft = tabItem != null && tabItem.Value.ToInt() == 1;

            // 谁跑"带极耳"流程由 SizeSlotPlan 统一决定，配方装尺寸项用的是同一份结论。
            // 这里再自己算一遍（以前是 tabIsLeft ? 中间 : 最后）就会出现两处真相，改一处必然分叉。
            SizeSlotPlan sizePlan = SizeSlotPlan.Resolve(CamSlots, tabIsLeft);

            ProcessSpec[] specs = new ProcessSpec[4 + sizePlan.SizeCams.Count];
            specs[0] = new ProcessSpec(0, ProcessName(0), ProcessKind.相机裁图);
            specs[1] = new ProcessSpec(1, ProcessName(1), ProcessKind.相机裁图);
            specs[2] = new ProcessSpec(0, ProcessName(0) + "_极片EA", ProcessKind.极片EA);
            specs[3] = new ProcessSpec(1, ProcessName(1) + "_极片EA", ProcessKind.极片EA);

            for (int i = 0; i < sizePlan.SizeCams.Count; i++)
            {
                int camIndex = sizePlan.SizeCams[i];
                ProcessKind kind = camIndex == sizePlan.TabCam
                    ? ProcessKind.极片尺寸带极耳检测
                    : ProcessKind.极片尺寸检测;
                specs[4 + i] = new ProcessSpec(camIndex, ProcessName(camIndex), kind);
            }

            return specs;
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