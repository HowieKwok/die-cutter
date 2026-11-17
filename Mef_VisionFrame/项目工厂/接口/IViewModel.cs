namespace Mef_VisionFrame
{
    public interface IViewModel
    {
        void Load(IDeviceDescriptor descriptor, ModelFactory modelFactory, ServerFactory serverFactory);
    }
}