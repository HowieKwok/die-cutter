namespace Mef_VisionFrame
{
    /// <summary>
    /// 激光模切一体机的工艺参数界面。
    ///
    /// 界面内容与五金模切完全一致（相机全局变量 / 工位缺陷 / 工位尺寸 / 机台变量），
    /// 差异只有"几路相机、哪一路是什么角色"，而角色由配方和画像决定，界面不感知。
    /// 因此这里不再复制一份窗体，只保留机型入口；真正的界面与逻辑在 Form_DieCutBase。
    /// </summary>
    public class Form_DieCuttingCut : Form_DieCutBase
    {
    }
}