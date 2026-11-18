using Frame_Core;
using LogTool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Frame_Core
{
    public class UserManage
    {
        /// <summary>
        /// 管理员账户配额：只允许一个
        /// </summary>
        public const int AdminQuota = 1;

        /// <summary>
        /// 工程师账户配额。操作员不限
        /// </summary>
        public const int EngineerQuota = 3;

        /// <summary>
        /// 界面可选的用户类型。技术员/超级管理员已取消，仅为兼容旧文件保留枚举值
        /// </summary>
        public static readonly UserType[] SelectableRoles = { UserType.Operator, UserType.Engineer, UserType.Admin };

        public UserData userData;

     
        public bool InitUserParam(string path, Boolean isCopy = false)
        {
            try
            {
                isCopy = false;
                if (!isCopy)
                {
                    if (userData == null) userData = new UserData();
                    userData.filePath = path;
                    userData.init();

                    // 读失败（文件损坏、或默认文件写不出去）会返回 null。
                    // 不兜底的话 userData 变 null，之后任何菜单权限校验
                    // （userManage.userData.CurrentUser）都会 NRE。
                    userData = userData.Read() as UserData;
                    if (userData == null)
                    {
                        userData = new UserData();
                        userData.init();
                    }

                    userData.filePath = path;
                    userData.EnsureCollections();

                    // 旧文件迁移：技术员降为操作员、超级管理员并入唯一管理员
                    if (userData.Migrate()) userData.Save();

                    ResolveStartupLogin();
                }
                else
                {
                    if (userData == null) userData = new UserData();
                    userData.filePath = path;
                    userData.Save();
                }
            }
            catch (Exception ex)
            {

                 Log4netRecord.ErrorLog.Debug(ex);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 启动时决定登录状态：勾选了自动登录则静默登录，否则退回“未登录”
        /// </summary>
        private void ResolveStartupLogin()
        {
            try
            {
                if (userData.AutoLogin.Enabled)
                {
                    string pwd = SecurityHelper.Decrypt(userData.AutoLogin.PasswordCipher);
                    if (UserLogin(userData.AutoLogin.UserId, pwd) == UserCtrlStatus.PasswordRight) return;
                }
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(ex);
            }

            // 自动登录不可用（账号被删、密码已改、密文损坏）→ 清掉标记，避免每次启动空转
            ClearAutoLogin();
            UserLogin(UserType.Null, "用户未登录", "");
        }

        #region 用户数据操作      
        object loginLock = new object();

        /// <summary>
        /// 用户登录——按账号自动定位角色。角色在新增账户时确定，登录时不再选择
        /// </summary>
        public UserCtrlStatus UserLogin(string userId, string pwd)
        {
            lock (loginLock)
            {
                if (string.IsNullOrEmpty(userId)) return UserCtrlStatus.UserNameWrong;

                UserType userType;
                UserModel user = FindUser(userId, out userType);
                if (user == null) return UserCtrlStatus.UserNameWrong;
                if (user.password != pwd) return UserCtrlStatus.PasswordWrong;

                ApplyLogin(userType, user.Id);
                return UserCtrlStatus.PasswordRight;
            }
        }

        /// <summary>
        /// 指定角色的登录（设备联动下发账号时使用）
        /// </summary>
        public UserCtrlStatus UserLogin(UserType userType, string userId, string pwd)
        {
            lock (loginLock)
            {
                try
                {
                    if (userType != UserType.Null)
                    {
                        List<UserModel> tempUser = ListOf(userType);
                        if (tempUser == null) return UserCtrlStatus.OtherError;

                        UserModel user = tempUser.FirstOrDefault(p => p.Id == userId);
                        if (user == null) return UserCtrlStatus.UserNameWrong;
                        if (user.password != pwd) return UserCtrlStatus.PasswordWrong;
                    }

                    ApplyLogin(userType, userId);
                }
                catch (Exception ex)
                {
                    return UserCtrlStatus.OtherError;

                }

                return UserCtrlStatus.PasswordRight;
            }
           
        }

        /// <summary>
        /// 按账号在全部角色中定位账户。新增账户时已保证 ID 全局唯一
        /// </summary>
        private UserModel FindUser(string userId, out UserType userType)
        {
            UserType[] roles = { UserType.Operator, UserType.Technician, UserType.Engineer, UserType.Admin, UserType.Supervisor };
            foreach (UserType role in roles)
            {
                List<UserModel> list = ListOf(role);
                if (list == null) continue;

                UserModel found = list.FirstOrDefault(p => p.Id == userId);
                if (found != null)
                {
                    userType = found.userType;
                    return found;
                }
            }

            userType = UserType.Null;
            return null;
        }

        private void ApplyLogin(UserType userType, string userId)
        {
            userData.CurrentUser = userType;
            userData.CurrentUserID = userId;
            //登录成功更改界面显示事件
            OnLoginChange(userType);
        }

        /// <summary>
        /// 添加用户
        /// </summary>
        /// <param name="userType">用户类型</param>
        /// <param name="userId">用户ID</param>
        /// <param name="pwd">密码</param>
        /// <returns>返回结果</returns>
        public UserCtrlStatus AddUser(UserType userType, string userId, string pwd, Boolean isencrypt = false)
        {

            try
            {
                if (userData.AllUsers().Any(p => p.Id == userId))
                {
                    return UserCtrlStatus.UserNameExist;
                }

                // 配额约束：管理员 1 个、工程师 3 个、操作员不限
                if (CountOf(userType) >= QuotaOf(userType))
                {
                    return UserCtrlStatus.QuotaExceeded;
                }

                if (isencrypt)
                {
                    //密码需加密
                    userId = SecurityHelper.Encrypt(userId);
                    pwd = SecurityHelper.Encrypt(pwd);
                }

                List<UserModel> target = ListOf(userType);
                if (target == null) return UserCtrlStatus.AddUserError;

                target.Add(new UserModel() { userType = userType, Id = userId, password = pwd });
            }
            catch (Exception)
            {
                return UserCtrlStatus.AddUserError;
            }
            return UserCtrlStatus.AddUserSucceed;
        }
        /// <summary>
        /// 删除用户
        /// </summary>
        /// <param name="userType">用户类型</param>
        /// <param name="userId">用户ID</param>
        /// <returns>返回结果</returns>
        public UserCtrlStatus DelUser(UserType userType, string userId)
        {
            try
            {
                // 管理员配额只有 1，删掉就再也没人能进账户管理
                if (userType == UserType.Admin && CountOf(UserType.Admin) <= 1)
                {
                    return UserCtrlStatus.LastAdminProtected;
                }

                List<UserModel> target = ListOf(userType);
                if (target == null) return UserCtrlStatus.DelUserError;

                int index = target.FindIndex(p => p.Id == userId);
                if (index < 0) return UserCtrlStatus.UserNameWrong;

                target.RemoveAt(index);
            }
            catch (Exception)
            {
                return UserCtrlStatus.DelUserError;
            }
            return UserCtrlStatus.DelUserSucceed;
        }

        /// <summary>
        /// 修改密码——目标恒为当前登录用户。
        /// 不提供“改他人密码”的入口，管理员无权重置他人密码。
        /// </summary>
        public UserCtrlStatus ChangeOwnPwd(string oldPwd, string newPwd)
        {
            if (userData.CurrentUser == UserType.Null) return UserCtrlStatus.OtherError;
            return ChangePwdCore(userData.CurrentUser, userData.CurrentUserID, oldPwd, newPwd);
        }

        private UserCtrlStatus ChangePwdCore(UserType userType, string userId, string oldPwd, string newPwd)
        {
            try
            {
                List<UserModel> tempUser = ListOf(userType);
                if (tempUser == null) return UserCtrlStatus.ModifyPwdError;

                int index = tempUser.FindIndex(p => p.Id == userId);
                if (index < 0) return UserCtrlStatus.UserNameWrong;

                if (tempUser[index].password != oldPwd) return UserCtrlStatus.PasswordWrong;

                tempUser[index].password = newPwd;

                // 勾选了自动登录时同步更新凭据，否则下次启动静默登录会失败
                if (userData.AutoLogin.Enabled && userData.AutoLogin.UserId == userId)
                {
                    userData.AutoLogin.PasswordCipher = SecurityHelper.Encrypt(newPwd);
                }
            }
            catch (Exception)
            {
                return UserCtrlStatus.ModifyPwdError;
            }
            return UserCtrlStatus.ModifyPwdSucceed;
        }

        #endregion

        #region 账户配额与角色
        /// <summary>
        /// 指定类型的账户配额，int.MaxValue = 不限
        /// </summary>
        public static int QuotaOf(UserType userType)
        {
            switch (userType)
            {
                case UserType.Admin: return AdminQuota;
                case UserType.Engineer: return EngineerQuota;
                default: return int.MaxValue;
            }
        }

        /// <summary>
        /// 指定类型的现有账户数量
        /// </summary>
        public int CountOf(UserType userType)
        {
            List<UserModel> list = ListOf(userType);
            return list == null ? 0 : list.Count;
        }

        /// <summary>
        /// 用户类型 → 账户列表
        /// </summary>
        private List<UserModel> ListOf(UserType userType)
        {
            if (userData == null) return null;
            switch (userType)
            {
                case UserType.Operator: return userData.OperatorUser;
                case UserType.Technician: return userData.TechnicianUser;
                case UserType.Engineer: return userData.EngineerUser;
                case UserType.Admin: return userData.AdminUser;
                case UserType.Supervisor: return userData.SupervisorUser;
                default: return null;
            }
        }
        #endregion

        #region 自动登录
        /// <summary>
        /// 记录“下次自动登录”凭据（密码加密保存）
        /// </summary>
        public void SaveAutoLogin(string userId, string pwd)
        {
            if (userData.AutoLogin == null) userData.AutoLogin = new AutoLoginModel();
            userData.AutoLogin.Enabled = true;
            userData.AutoLogin.UserType = userData.CurrentUser;
            userData.AutoLogin.UserId = userId;
            userData.AutoLogin.PasswordCipher = SecurityHelper.Encrypt(pwd);
        }

        /// <summary>
        /// 清除自动登录标记
        /// </summary>
        public void ClearAutoLogin()
        {
            if (userData.AutoLogin == null) userData.AutoLogin = new AutoLoginModel();
            userData.AutoLogin.Enabled = false;
            userData.AutoLogin.UserId = null;
            userData.AutoLogin.PasswordCipher = null;
        }

        /// <summary>
        /// 删除账户后，清理指向该账户的自动登录标记
        /// </summary>
        public void ForgetAutoLoginIf(string userId)
        {
            if (userData.AutoLogin == null || !userData.AutoLogin.Enabled) return;
            if (userData.AutoLogin.UserId == userId)
            {
                ClearAutoLogin();
            }
        }
        #endregion


        #region
        /// <summary>
        ///  获取用户类型
        /// </summary>
        /// <param name="userType"></param>
        /// <param name="Operator"></param>
        /// <returns></returns>
        public  UserType ToUserType(string userType)
        {
            if ((userType == "操作员") || (userType == "Operator"))
            {
                return UserType.Operator;
            }
            if ((userType == "技术员") || (userType == "Technician"))
            {
                return UserType.Technician;
            }
            if ((userType == "工程师") || (userType == "Engineer"))
            {
                return UserType.Engineer;
            }
            if ((userType == "管理员") || (userType == "Admin"))
            {
                return UserType.Admin;
            }
            if ((userType == "超级管理员") || (userType == "Supervisor"))
            {
                return UserType.Supervisor;
            }
            return UserType.Null;
        }

        /// <summary>
        /// 登陆状态发生改变时触发
        /// </summary>
        public delegate void LoginDelegate(UserType userType);
        public event LoginDelegate LoginEvent;
        readonly object loginChange_flag = new object();
        private void OnLoginChange(UserType type)
        {
            lock (loginChange_flag)
            {
                lastLoginTime = DateTime.Now;
                LoginEvent?.Invoke(type);
            }

        }


        DateTime lastLoginTime;
        /// <summary>
        /// 登录过期(长时间未操作)
        /// </summary>
        public void MonitorLogonOutTime()
        {
            try
            {
                // 勾选了自动登录的常驻用户不做超时踢出，否则每次启动的免登录没有意义
                if (userData.AutoLogin != null && userData.AutoLogin.Enabled) return;

                // 0 = 永不超时
                if (userData.LoginOutTime == 0) return;

                System.TimeSpan ts = DateTime.Now - lastLoginTime;
                if (ts.TotalSeconds > userData.LoginOutTime)
                {
                    OnLoginChange(UserType.Operator);//长时间未操作界面刷新为操作员权限                  
                    UserLogin(UserType.Null, "用户未登录", "");
                }
            }
            catch (Exception ex)
            {
                 Log4netRecord.ErrorLog.Debug(ex);
            }
        }

        #endregion

    }
    public enum UserLanguageg
    {
        中文,
        英文

    }
    public enum UserCtrlStatus
    {
        /// <summary>
        /// 用户名不存在
        /// </summary>
        UserNameWrong = 0,
        /// <summary>
        /// 用户名已存在
        /// </summary>
        UserNameExist = 1,
        /// <summary>
        /// 密码错误
        /// </summary>
        PasswordWrong = 2,
        /// <summary>
        /// 密码正确
        /// </summary>
        PasswordRight = 3,
        /// <summary>
        /// 添加用户失败
        /// </summary>
        AddUserError = 4,
        /// <summary>
        /// 添加用户成功
        /// </summary>
        AddUserSucceed = 5,
        /// <summary>
        /// 删除用户失败
        /// </summary>
        DelUserError = 6,
        /// <summary>
        /// 删除用户成功
        /// </summary>
        DelUserSucceed = 7,
        /// <summary>
        /// 修改密码失败
        /// </summary>
        ModifyPwdError = 8,
        /// <summary>
        /// 修改密码成功
        /// </summary>
        ModifyPwdSucceed = 9,
        /// <summary>
        /// 其他错误
        /// </summary>
        OtherError = 10,
        /// <summary>
        /// 超出该类型账户配额
        /// </summary>
        QuotaExceeded = 11,
        /// <summary>
        /// 最后一个管理员不可删除
        /// </summary>
        LastAdminProtected = 12,
    }
}