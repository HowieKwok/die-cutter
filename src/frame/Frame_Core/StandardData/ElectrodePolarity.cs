using System;

namespace Frame_Core
{
    /// <summary>
    /// 产品极性的两个编号体系之间的唯一换算点。
    ///
    /// 两套编号含义相反，且都已被已发布的数据锁定，都不能改：
    /// - <see cref="ElectrodeEnum"/>：负极=0、正极=1（配方文件 CraftBase.Electrode 用它，改顺序会让存量配方极性翻转）
    /// - <c>ParamsMQDefects.com_Cathode</c>：0=正极、1=负极（缺陷检测插件参数，存量配方里也是这个意思）
    ///
    /// 因此不靠数字巧合，凡是要在两套之间转换的地方都走这里，
    /// 以后新增一处转换就加一个方法，不要再写裸 0/1。
    /// </summary>
    public static class ElectrodePolarity
    {
        /// <summary>配方极性 → 缺陷检测参数值。</summary>
        public static int ToComCathode(ElectrodeEnum electrode)
        {
            return electrode == ElectrodeEnum.正极 ? 0 : 1;
        }

        /// <summary>缺陷检测参数值 → 配方极性。</summary>
        public static ElectrodeEnum ToElectrode(int comCathode)
        {
            return comCathode == 0 ? ElectrodeEnum.正极 : ElectrodeEnum.负极;
        }

        /// <summary>缺陷检测参数值 → AIModel 下的模型目录名（正极 / 负极）。</summary>
        public static string ModelFolderOf(int comCathode)
        {
            return ModelFolderOf(ToElectrode(comCathode));
        }

        /// <summary>配方极性 → AIModel 下的模型目录名。以配方为唯一来源时应走这个重载。</summary>
        public static string ModelFolderOf(ElectrodeEnum electrode)
        {
            return electrode == ElectrodeEnum.正极 ? "正极" : "负极";
        }
    }
}