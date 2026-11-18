using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Frame_Core
{
    /// <summary>
    /// 界面权限
    /// </summary>
    public class ModelPrivilege
    {
        public string ModelName;//模块名称

        public bool OperatorPurview = false;//操作员
        public bool EngineerPurview = true;//工程师
        public bool AdminPurview = true;//管理员

        /// <summary>
        /// 【兼容旧文件】技术员/超级管理员角色已取消，这两个字段仅为读取旧权限文件保留
        /// </summary>
        public bool TechnicianPurview = false;
        public bool SupervisorPurview = true;

        /// <summary>
        /// 权限表首次生成时使用的默认值。之后完全由“权限分配”界面接管，这里不再生效。
        /// 分级依据：系统设置/工艺标准/数据分析 三等级全开放；调试设置/外联设置 仅工程师与管理员
        /// </summary>
        public static ModelPrivilege DefaultFor(string modelName)
        {
            ModelPrivilege privilege = new ModelPrivilege();
            privilege.ModelName = modelName;

            privilege.OperatorPurview = IsOpenToOperator(modelName);
            privilege.EngineerPurview = true;
            privilege.AdminPurview = true;
            return privilege;
        }

        /// <summary>
        /// 该模块是否对操作员开放（权限规范唯一真源）
        /// </summary>
        public static bool IsOpenToOperator(string modelName)
        {
            switch (modelName)
            {
                //系统设置
                case "保存设置":
                case "磁盘设置":
                case "基础设置":
                case "图片管理":
                //工艺标准
                case "工艺设置":
                //数据分析
                case "数据分析":
                    return true;
                //调试设置、外联设置（相机/光源/主设备通讯/控制卡）对操作员关闭
                default:
                    return false;
            }
        }
    }

}