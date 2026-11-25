using System;
using System.Collections.Generic;

namespace Frame_Core
{
    /// <summary>
    /// 尺寸工位的槽位分工：哪一路跑"带极耳"流程、哪些跑"不带极耳"流程。
    ///
    /// profile（建流程）和 CraftParam（装尺寸项）必须用同一份结论。原来两处各算一遍：
    /// profile 读配方变量"极耳在左侧"，CraftParam 直接按"第几路"装项——流程把
    /// 极耳左侧交点/极片右侧抓边/左侧V角凸点建在 A 路，配方却去 B 路取，
    /// 结果恒 -999 且不报任何错。所以这里抽成纯函数：只吃槽位和一个 bool，不读全局状态。
    ///
    /// 跨工位可见的差别只有流程种类：
    /// - 带极耳侧（<see cref="TabCam"/>）：粗定位工具、极耳左侧交点、极片右侧抓边、极耳右侧抓边、
    ///   极片AT抓边、极耳右侧组合交点、极片右侧组合交点、极耳左/右根部组合交点、
    ///   左侧V角凸点1,2、右侧V角凸点1,2、左/右底部V角检测有无、极片有无、极耳翻折/余料/涂布内翻。
    /// - 不带极耳侧（<see cref="PlainCams"/>）：粗定位工具、V角凸点1、V角凸点2、极片有无、顶部V角检测有无。
    /// </summary>
    public sealed class SizeSlotPlan
    {
        private SizeSlotPlan(List<int> sizeCams, int tabCam)
        {
            SizeCams = sizeCams;
            TabCam = tabCam;
        }

        /// <summary>所有尺寸相机的物理下标，按槽位顺序（激光 3/4/5、五金 2/3/4）。</summary>
        public IReadOnlyList<int> SizeCams { get; private set; }

        /// <summary>跑"极片尺寸带极耳检测"的那一路；没有尺寸相机时为 -1。</summary>
        public int TabCam { get; private set; }

        /// <summary>跑"极片尺寸检测"的那几路（按槽位顺序，已剔除 TabCam）。</summary>
        public IReadOnlyList<int> PlainCams
        {
            get
            {
                List<int> plain = new List<int>();
                for (int i = 0; i < SizeCams.Count; i++)
                {
                    if (SizeCams[i] != TabCam) plain.Add(SizeCams[i]);
                }
                return plain;
            }
        }

        /// <summary>除 TabCam 外的第一路；单相机机型退回 TabCam，避免把项指向不存在的相机。</summary>
        public int FirstPlainCam
        {
            get
            {
                IReadOnlyList<int> plain = PlainCams;
                return plain.Count > 0 ? plain[0] : TabCam;
            }
        }

        /// <summary>
        /// 沿用 profile 里既有的二选一规则：极耳在左侧时中间那一路带极耳，否则最后一路带。
        /// 尺寸相机不足三路时没有"中间"，统一落到最后一路。
        /// </summary>
        public static SizeSlotPlan Resolve(IReadOnlyList<CamSlotDef> slots, bool tabIsLeft)
        {
            List<int> sizeCams = new List<int>();
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Role == CamRole.尺寸检测) sizeCams.Add(i);
                }
            }

            if (sizeCams.Count == 0) return new SizeSlotPlan(sizeCams, -1);

            int tabIndex = tabIsLeft && sizeCams.Count >= 3 ? 1 : sizeCams.Count - 1;
            return new SizeSlotPlan(sizeCams, sizeCams[tabIndex]);
        }
    }
}