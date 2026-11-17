using System;

namespace Frame_Core
{
    /// <summary>
    /// 五金模切的配方参数。
    /// 目前只继承 CraftBase 的公共字段（机台号/配方名/极性/全局变量），
    /// 具体工艺参数待上机确定后在此补充字段，并同步做 Form_五金模切 的界面。
    /// </summary>
    [Serializable]
    public class CraftParam_五金模切 : CraftBase
    {
    }
}