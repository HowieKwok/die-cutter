namespace Frame_Core
{
    public class Craft_ModelService : IBusinessService
    {
        /// <summary>
        /// 读取配方参数对象。参数类由机型画像决定：同一份配方目录下不同机型各读各的类型。
        /// 机型未注册时抛异常，不再静默回落到激光模切的参数类。
        /// </summary>
        public static CraftBase GetCraft(string CraftBasePath, MaterialCrafEnum craftType, string recipeName)
        {
            CraftBase mc = new CraftBase();
            int length = Project.Instance.M_CamParam?.l_CameraDevs?.Count ?? 10;
            return DeviceProfileRegistry.Resolve(craftType).ReadCraft(mc, CraftBasePath, recipeName, length);
        }
    }
}