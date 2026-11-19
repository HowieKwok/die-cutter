using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Frame_Core;
namespace Frame_Core
{
    /// <summary>
    /// 权限参数
    /// </summary>
    public class PurviewParam : XmlSerializationOperation
    {
        /// <summary>
        /// 数据结构版本。旧文件没有该节点，读出来是 0
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion { get; set; }

        /// <summary>
        /// 模块权限参数
        /// </summary>
        public List<ModelPrivilege> l_modelPrivilege;

        public PurviewParam()
        {
            init();
        }
        public void init()
        {
            l_modelPrivilege = new List<ModelPrivilege>();
            SchemaVersion = CurrentSchemaVersion;
        }

        /// <summary>
        /// 把旧权限文件一次性对齐到当前角色规范：
        /// 系统设置/工艺标准/数据分析 对操作员开放，管理员恒可访问。
        /// 仅执行一次，之后完全以管理员在“权限分配”界面的修改为准。
        /// </summary>
        /// <returns>是否发生了升级（需要回写文件）</returns>
        public bool ApplyCurrentPolicyDefaults()
        {
            if (l_modelPrivilege == null) l_modelPrivilege = new List<ModelPrivilege>();
            if (SchemaVersion >= CurrentSchemaVersion) return false;

            foreach (var item in l_modelPrivilege)
            {
                if (item == null) continue;

                item.AdminPurview = true;
                if (ModelPrivilege.IsOpenToOperator(item.ModelName)) item.OperatorPurview = true;
            }

            SchemaVersion = CurrentSchemaVersion;
            return true;
        }
    }
}