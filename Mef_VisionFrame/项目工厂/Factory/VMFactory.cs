namespace Mef_VisionFrame
{
    /// <summary>
    /// 视图模型工厂。ViewModel 只是 Model + Server 的聚合，具体类型由设备描述符决定。
    /// </summary>
    public class VMFactory : IViewModel
    {
        public VMBase WorkVMBase;

        public void Load(IDeviceDescriptor descriptor, ModelFactory modelFactory, ServerFactory serverFactory)
        {
            WorkVMBase = descriptor.CreateViewModel(modelFactory.WorkModelBase, serverFactory.WorkServerBase);
        }
    }
}