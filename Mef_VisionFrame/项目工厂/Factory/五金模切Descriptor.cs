using Frame_Core;
using System.Collections.Generic;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切：5 路相机（主模切 / 从模切 / 尺寸×3），无裁切定位工位。
    /// 各相机的处理函数尚未实现，因此槽位暂不订阅回调；
    /// 补完 Server_五金模切 的 *_Process 后，在对应槽位里加 param.ProductCallBack += ... 即可。
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
            slot(0), // 模切主相机
            slot(1), // 模切从相机
            slot(2), // 尺寸 1
            slot(3), // 尺寸 2
            slot(4), // 尺寸 3
        };

        private static CamSlot slot(int index)
        {
            return new CamSlot(index, ((CameraEnum)index).ToString(), (server, param) =>
            {
                // 处理函数待实现
            });
        }
    }
}