namespace Mef_VisionFrame
{
    /// <summary>
    /// 工位数据模型工厂。具体类型由设备描述符决定，工厂本身不再判断机型。
    /// </summary>
    public class ModelFactory : IModelFactory
    {
        public ModelBase WorkModelBase;

        public void Load(IDeviceDescriptor descriptor)
        {
            WorkModelBase = descriptor.CreateModel();
        }
    }
}