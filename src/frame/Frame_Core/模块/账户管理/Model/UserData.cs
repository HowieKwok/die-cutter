using Frame_Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Frame_Core
{
    public class UserData : XmlSerializationOperation
    {
        /// <summary>
        /// 当前数据结构版本。旧文件没有该节点，读出来是 0。
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        //当前用户      
        [System.Xml.Serialization.XmlIgnore]
        public UserType CurrentUser { get; set; }

        //当前用户ID
        [System.Xml.Serialization.XmlIgnore]     
        public string CurrentUserID { get; set; }

        /// <summary>
        /// 数据结构版本，用于一次性迁移
        /// </summary>
        public int SchemaVersion { get; set; }

        /// <summary>
        /// 登录过期时间(单位秒)。0 = 永不超时
        /// </summary>
        public ushort LoginOutTime { get; set; }

        //加密启用
        public bool isEncrypt { get; set; }//加密功能切换暂时不添加，默认不加密

        /// <summary>
        /// 下次自动登录
        /// </summary>
        public AutoLoginModel AutoLogin { get; set; }

        public List<UserModel> OperatorUser { get; set; }

        /// <summary>
        /// 【兼容旧文件】技术员角色已取消，该列表仅用于迁移读取，迁移后置空。
        /// 不能删除该属性：删掉后旧文件里的技术员账号会静默消失。
        /// </summary>
        public List<UserModel> TechnicianUser { get; set; }

        public List<UserModel> EngineerUser { get; set; }

        public List<UserModel> AdminUser { get; set; }

        /// <summary>
        /// 【兼容旧文件】超级管理员角色已取消，该列表仅用于迁移读取，迁移后置空。
        /// </summary>
        public List<UserModel> SupervisorUser { get; set; }

        public UserData()
        {
            //init();
        
        }
        public void init()
        {
            CurrentUser = UserType.Null;
            CurrentUserID = "用户未登录";
            LoginOutTime = 300;
            isEncrypt = false;
            SchemaVersion = CurrentSchemaVersion;
            AutoLogin = new AutoLoginModel();

            // 新机只预置唯一管理员，否则无法进入账户管理
            AdminUser = new List<UserModel>() { new UserModel() { userType = UserType.Admin, Id = "admin", password = "admin" } };
            EngineerUser = new List<UserModel>();
            OperatorUser = new List<UserModel>();
            TechnicianUser = new List<UserModel>();
            SupervisorUser = new List<UserModel>();
        }

        /// <summary>
        /// 补全反序列化后可能缺失的集合与迁移标记，避免后续空引用
        /// </summary>
        public void EnsureCollections()
        {
            if (OperatorUser == null) OperatorUser = new List<UserModel>();
            if (EngineerUser == null) EngineerUser = new List<UserModel>();
            if (AdminUser == null) AdminUser = new List<UserModel>();
            if (TechnicianUser == null) TechnicianUser = new List<UserModel>();
            if (SupervisorUser == null) SupervisorUser = new List<UserModel>();
            if (AutoLogin == null) AutoLogin = new AutoLoginModel();
        }

        /// <summary>
        /// 全部账号（含尚未迁移的旧角色列表），供列表展示使用
        /// </summary>
        public IEnumerable<UserModel> AllUsers()
        {
            return Safe(OperatorUser)
                .Concat(Safe(TechnicianUser))
                .Concat(Safe(EngineerUser))
                .Concat(Safe(AdminUser))
                .Concat(Safe(SupervisorUser));
        }

        private static IEnumerable<UserModel> Safe(List<UserModel> list)
        {
            return list ?? Enumerable.Empty<UserModel>();
        }

        /// <summary>
        /// 旧文件迁移：技术员降为操作员，超级管理员并入管理员（仅保留一个）。
        /// 幂等：SchemaVersion 达到当前版本后不再执行。
        /// </summary>
        /// <returns>是否发生了迁移（需要回写文件）</returns>
        public bool Migrate()
        {
            EnsureCollections();
            if (SchemaVersion >= CurrentSchemaVersion) return false;

            //【1】管理员与超级管理员合并，只保留一个管理员
            var mergedAdmins = AdminUser.Concat(SupervisorUser).ToList();
            var keptAdmin = mergedAdmins.FirstOrDefault();
            var overflowAdmins = mergedAdmins.Skip(1).ToList();

            AdminUser = keptAdmin == null
                ? new List<UserModel>()
                : new List<UserModel>() { keptAdmin };
            if (keptAdmin != null) keptAdmin.userType = UserType.Admin;

            //【2】超出配额的管理员先降为工程师（工程师配额内），再降为操作员
            var overflowEngineers = new List<UserModel>();
            var overflowOperators = new List<UserModel>();
            var engineerRoom = Math.Max(0, UserManage.EngineerQuota - EngineerUser.Count);
            foreach (var user in overflowAdmins)
            {
                if (overflowEngineers.Count < engineerRoom)
                {
                    user.userType = UserType.Engineer;
                    overflowEngineers.Add(user);
                }
                else
                {
                    user.userType = UserType.Operator;
                    overflowOperators.Add(user);
                }
            }

            //【3】技术员角色取消，按现场习惯降权为操作员
            var demotedTechnicians = TechnicianUser.ToList();
            foreach (var user in demotedTechnicians) user.userType = UserType.Operator;

            EngineerUser = EngineerUser.Concat(overflowEngineers).ToList();
            OperatorUser = OperatorUser.Concat(overflowOperators).Concat(demotedTechnicians).ToList();

            //【4】旧列表置空但属性保留，下次 Save 写成空节点，天然幂等
            TechnicianUser = new List<UserModel>();
            SupervisorUser = new List<UserModel>();

            //【5】兜底：迁移后仍无管理员则重建默认管理员
            if (AdminUser.Count == 0)
            {
                AdminUser.Add(new UserModel() { userType = UserType.Admin, Id = "admin", password = "admin" });
            }

            SchemaVersion = CurrentSchemaVersion;
            return true;
        }
    }

    /// <summary>
    /// 下次自动登录的凭据。密码以密文保存，供启动时静默登录使用。
    /// </summary>
    public class AutoLoginModel
    {
        public bool Enabled { get; set; }
        public UserType UserType { get; set; }
        public string UserId { get; set; }
        public string PasswordCipher { get; set; }
    }

    public class UserModel
    {
        /// <summary>
        /// 用户类型
        /// </summary>
        public UserType userType { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// 用户密码
        /// </summary>
        public string password { get; set; }

        public UserModel()
        {
            init();
        }
        public void init()
        {
            userType = UserType.Operator;
            Id = "001";
            password = "001";
        }
    }
}