using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// 查找极耳算法的结果载体。
    /// IsDetected=false 表示插件未接入或执行失败，调用方应使用 TabState 的默认值。
    /// </summary>
    public sealed class TabDetectResult
    {
        /// <summary>
        /// 是否由算法实际检测过。false 时 HasTab/Tabs 无意义。
        /// </summary>
        public bool IsDetected { get; set; }

        /// <summary>
        /// 整帧级别的极耳状态；具体到某片请使用 TabState.GetHasTab。
        /// </summary>
        public bool HasTab { get; set; }

        /// <summary>
        /// 极耳位置，行号坐标系与传入的 ImageInfo 一致。
        /// </summary>
        public List<PoleEarsInfo> Tabs { get; set; } = new List<PoleEarsInfo>();
    }
}
