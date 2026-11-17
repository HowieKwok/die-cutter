namespace Frame_Core
{
    /// <summary>
    /// 相机角色。与 CameraEnum（第几路）不同，角色描述的是"这台相机干什么"，
    /// 因此跨机型可比：激光模切有裁切纠偏，五金模切没有。
    /// 一台设备内同一角色可能有多路（尺寸检测），按槽位出现顺序区分。
    /// </summary>
    public enum CamRole
    {
        模切主相机,
        模切从相机,
        裁切纠偏,
        尺寸检测,
    }
}