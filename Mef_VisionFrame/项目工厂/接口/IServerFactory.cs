namespace Mef_VisionFrame
{
    public interface IServerFactory
    {
        void Load(IDeviceDescriptor descriptor, ModelFactory modelFactory);
    }
}