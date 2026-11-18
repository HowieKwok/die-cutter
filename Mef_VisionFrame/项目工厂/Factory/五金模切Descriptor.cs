using Frame_Core;
using System;
using System.Collections.Generic;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切：5 路相机（主模切 / 从模切 / 尺寸×3），无裁切纠偏工位。
    ///
    /// 槽位只负责"哪一路相机把图交给 Server 的哪个方法"，算法与显示都在 Server 里。
    /// 当前 5 路都只挂 ProductCallBack（原图）：裁图流程还不存在，也就没有 EA 图产出，
    /// 所以 ProductCallBackCraft 暂时不挂——挂了也永远不会有回调，只会让人以为 EA 链路已经通了。
    /// </summary>
    public class 五金模切Descriptor : IDeviceDescriptor
    {
        public MaterialCrafEnum CraftType
        {
            get { return MaterialCrafEnum.五金模切; }
        }

        public string DisplayName
        {
            get { return "五金模切"; }
        }

        public ModelBase CreateModel()
        {
            return new Model_五金模切();
        }

        public ServerBase CreateServer(ModelBase model)
        {
            return new Server_五金模切((Model_五金模切)model);
        }

        public VMBase CreateViewModel(ModelBase model, ServerBase server)
        {
            return new VM_五金模切((Model_五金模切)model, (Server_五金模切)server);
        }

        public UCViewBase CreateProjectView(VMBase viewModel)
        {
            return new UC_五金模切((VM_五金模切)viewModel);
        }

        public IReadOnlyList<CamSlot> CamSlots
        {
            get { return camSlots; }
        }

        private static readonly CamSlot[] camSlots = new CamSlot[]
        {
            // 模切主：原图/大图走 ProductCallBack，极片 EA 走 ProductCallBackCraft
            slot(0, (server, param) =>
            {
                Bind(param, server.模切主相机_Process);
                BindEa(param, server.模切主相机_EA_Process);
            }),
            // 模切从
            slot(1, (server, param) =>
            {
                Bind(param, server.模切从相机_Process);
                BindEa(param, server.模切从相机_EA_Process);
            }),
            slot(2, (server, param) => Bind(param, server.尺寸相机1_Process)),  // 尺寸 1
            slot(3, (server, param) => Bind(param, server.尺寸相机2_Process)),  // 尺寸 2
            slot(4, (server, param) => Bind(param, server.尺寸相机3_Process)),  // 尺寸 3
        };

        /// <summary>
        /// 先减后加，保证同一套参数被重复绑定时不会回调两次。
        /// 用方法组而不是闭包：闭包每次都会生成新的委托，-= 摘不掉上一次的绑定。
        /// </summary>
        private static void Bind(BaseProParam param, Action<ProductBase> handler)
        {
            param.ProductCallBack -= handler;
            param.ProductCallBack += handler;
        }

        /// <summary>工艺图（裁图流程切出的极片 EA）的回调，与相机原图分开两路。</summary>
        private static void BindEa(BaseProParam param, Action<ProductBase> handler)
        {
            param.ProductCallBackCraft -= handler;
            param.ProductCallBackCraft += handler;
        }

        private static CamSlot slot(int index, Action<Server_五金模切, BaseProParam> bind)
        {
            return new CamSlot(index, ((CameraEnum)index).ToString(),
                (server, param) => bind((Server_五金模切)server, param));
        }
    }
}