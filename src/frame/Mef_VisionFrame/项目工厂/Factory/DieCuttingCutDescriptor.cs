using Frame_Core;
using System;
using System.Collections.Generic;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 激光模切一体机：6 路相机 = 2 路模切（主/从）+ 1 路裁切纠偏 + 3 路尺寸。
    /// 相机下标与参数类型的对应关系由 Frame_Core 的 DieCutProDevice 创建参数时决定，
    /// 本描述符只声明"哪个槽位接 Server 的哪个处理函数"，并在订阅前由 ServerFactory 校验槽位是否存在。
    /// </summary>
    public class DieCuttingCutDescriptor : IDeviceDescriptor
    {
        public MaterialCrafEnum CraftType
        {
            get { return MaterialCrafEnum.激光模切一体机; }
        }

        public string DisplayName
        {
            get { return "激光模切一体机"; }
        }

        public ModelBase CreateModel()
        {
            return new Model_DieCuttingCut();
        }

        public ServerBase CreateServer(ModelBase model)
        {
            return new Server_DieCuttingCut((Model_DieCuttingCut)model);
        }

        public VMBase CreateViewModel(ModelBase model, ServerBase server)
        {
            return new VM_DieCuttingCut((Model_DieCuttingCut)model, (Server_DieCuttingCut)server);
        }

        public UCViewBase CreateProjectView(VMBase viewModel)
        {
            return new UC_DieCuttingCut((VM_DieCuttingCut)viewModel);
        }

        public IReadOnlyList<CamSlot> CamSlots
        {
            get { return camSlots; }
        }

        /// <summary>
        /// 相机槽位表。同一张表会分别应用到实机参数和仿真参数两组字典上。
        /// </summary>
        private static readonly CamSlot[] camSlots = new CamSlot[]
        {
            // 0：模切主相机。原图 → 裁图流程，极片 EA → EA 流程
            slot(0, (server, param) =>
            {
                param.ProductCallBack -= server.ALineCam_SourceImage_Process;
                param.ProductCallBack += server.ALineCam_SourceImage_Process;
                param.ProductCallBackCraft -= server.ALineCam_EAImage_Process;
                param.ProductCallBackCraft += server.ALineCam_EAImage_Process;
            }),

            // 1：模切从相机
            slot(1, (server, param) =>
            {
                param.ProductCallBack -= server.BLineCam_SourceImage_Process;
                param.ProductCallBack += server.BLineCam_SourceImage_Process;
                param.ProductCallBackCraft -= server.BLineCam_EAImage_Process;
                param.ProductCallBackCraft += server.BLineCam_EAImage_Process;
            }),

            // 2：裁切纠偏相机
            slot(2, (server, param) =>
            {
                param.ProductCallBack -= server.纠偏检测数据处理_Process;
                param.ProductCallBack += server.纠偏检测数据处理_Process;
            }),

            // 3/4/5：尺寸相机
            slot(3, (server, param) =>
            {
                param.ProductCallBack -= server.SizeCam4_Process;
                param.ProductCallBack += server.SizeCam4_Process;
            }),
            slot(4, (server, param) =>
            {
                param.ProductCallBack -= server.SizeCam5_Process;
                param.ProductCallBack += server.SizeCam5_Process;
            }),
            slot(5, (server, param) =>
            {
                param.ProductCallBack -= server.SizeCam6_Process;
                param.ProductCallBack += server.SizeCam6_Process;
            }),
        };

        /// <summary>
        /// 声明槽位的简易写法：相机名沿用 CameraEnum 的名称，处理函数直接写 Server 上的方法。
        /// </summary>
        private static CamSlot slot(int index, Action<Server_DieCuttingCut, BaseProParam> bind)
        {
            return new CamSlot(index, ((CameraEnum)index).ToString(),
                (server, param) => bind((Server_DieCuttingCut)server, param));
        }
    }
}