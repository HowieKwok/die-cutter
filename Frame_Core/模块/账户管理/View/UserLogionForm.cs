using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace Frame_Core
{
    public partial class UserLogionForm : Form
    {
        UserManage userManage;
        public UserLogionForm(UserManage _UserManage)
        {
            userManage = _UserManage;
            InitializeComponent();
        }

        private void UserSetting_Load(object sender, EventArgs e)
        {
            try
            {
                // 恢复上次勾选的自动登录信息，方便用户确认/取消
                var autoLogin = userManage.userData.AutoLogin;
                if (autoLogin != null && autoLogin.Enabled)
                {
                    chkAutoLogin.Checked = true;
                    txtUserID.Text = autoLogin.UserId;
                }

                RefreshButtons();
                userManage.LoginEvent += UserManage_LoginEvent;
            }
            catch (Exception)
            {

            }
        }

        private void UserManage_LoginEvent(UserType userType)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)RefreshButtons);
                return;
            }
            RefreshButtons();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            userManage.LoginEvent -= UserManage_LoginEvent;
            base.OnFormClosed(e);
        }

        /// <summary>
        /// 按钮可用性：账户管理仅已登录管理员，修改密码/退出登录仅已登录
        /// </summary>
        private void RefreshButtons()
        {
            bool logged = userManage.userData.CurrentUser != UserType.Null;

            btnModifyPwd.Enabled = logged;
            btnLogout.Enabled = logged;
            btnAccountManage.Enabled = logged && userManage.userData.CurrentUser == UserType.Admin;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btn_login_Click(object sender, EventArgs e)
        {
            Login();
        }

        private void btn_cancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void ckbDispPwd_CheckedChanged(object sender, EventArgs e)
        {
            if (ckbDispPwd.Checked)
            {
                txtPwd.PasswordChar = '\0';
            }
            else
            {
                txtPwd.PasswordChar = '*';
            }
        }

        private void txtPwd_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)13)
            {
                Login();
            }
        }

        private void txtPwd_TextChanged(object sender, EventArgs e)
        {

        }

        private void Login()
        {
            try
            {
                string userId = txtUserID.Text;
                string pwd = txtPwd.Text;

                if (string.IsNullOrEmpty(userId))
                {
                    System.Windows.Forms.MessageBox.Show("请输入用户ID！");
                    return;
                }

                // 角色在新增账户时已确定，登录只需账号与密码
                UserCtrlStatus userCtrlStatus = userManage.UserLogin(userId, pwd);

                if (userCtrlStatus == UserCtrlStatus.PasswordRight)
                {
                    // 记录/清除“下次自动登录”凭据
                    if (chkAutoLogin.Checked) userManage.SaveAutoLogin(userId, pwd);
                    else userManage.ClearAutoLogin();
                    userManage.userData.Save();

                    this.Close();
                    return;
                }

                ShowStatusMessage(userCtrlStatus);
            }
            catch (Exception)
            {

            }
           
        }

        private void ShowStatusMessage(UserCtrlStatus status)
        {
            switch (status)
            {
                case UserCtrlStatus.UserNameWrong:
                    System.Windows.Forms.MessageBox.Show("用户不存在，请确认！");
                    break;
                case UserCtrlStatus.PasswordWrong:
                    System.Windows.Forms.MessageBox.Show("密码错误，请确认！");
                    break;
                case UserCtrlStatus.OtherError:
                    System.Windows.Forms.MessageBox.Show("登录失败，请确认！");
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// 修改密码：只能改当前登录用户自己的密码
        /// </summary>
        private void btnModifyPwd_Click(object sender, EventArgs e)
        {
            if (userManage.userData.CurrentUser == UserType.Null) return;

            using (ModifyPwdForm frm = new ModifyPwdForm(userManage))
            {
                frm.StartPosition = FormStartPosition.CenterParent;
                frm.ShowDialog(this);
            }
        }

        /// <summary>
        /// 账户管理：仅已登录管理员可用
        /// </summary>
        private void btnAccountManage_Click(object sender, EventArgs e)
        {
            if (userManage.userData.CurrentUser != UserType.Admin) return;

            using (UserManagerForm frm = new UserManagerForm(userManage))
            {
                frm.StartPosition = FormStartPosition.CenterParent;
                frm.ShowDialog(this);
            }
            RefreshButtons();
        }

        private void Btn_Logout_Click(object sender, EventArgs e)
        {
            // 退出登录同时取消自动登录，否则重启后又会自动进入
            userManage.ClearAutoLogin();
            userManage.UserLogin(UserType.Null, "用户未登录", "");
            userManage.userData.Save();
            RefreshButtons();
        }
    }
}