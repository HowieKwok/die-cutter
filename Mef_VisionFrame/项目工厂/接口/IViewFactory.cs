namespace Mef_VisionFrame
{
    public interface IViewFactory
    {
        void Load(IDeviceDescriptor descriptor, VMFactory vMFactory);
    }
}