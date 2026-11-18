using Frame_Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Frame_Core
{
    /// <summary>
    /// 进行用户管理的窗体程序
    /// </summary>
    public partial class UserManagerForm : Form
    {
        UserManage userManage;
        public UserManagerForm(UserManage _UserManage)
        {
            userManage = _UserManage;
            InitializeComponent();
        }

        private void btnAddAccount_Click(object sender, EventArgs e)
        {
            AddUserForm frm = new AddUserForm(userManage);
            frm.ShowDialog();
            AllUserShow();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                string userType = LanguageProject.GetLangeuageAttribute(StatusStripEnum.当前用户类型, Project.Instance.systemInfo.m_CurrentLanguageType) +
           LanguageProject.GetLangeuageAttribute(Project.Instance.userManage.userData.CurrentUser, Project.Instance.systemInfo.m_CurrentLanguageType);
                string userID = LanguageProject.GetLangeuageAttribute(StatusStripEnum.当前用户ID, Project.Instance.systemInfo.m_CurrentLanguageType) + Project.Instance.userManage.userData.CurrentUserID;
                label1.Text = userType + "\r\n" + userID;

                AllUserShow();

                //==================================================================================================================
                //=====【】本界面入口已限定为“已登录管理员”，这里只保留权限分配的管理员校验
                btnAddAccount.Enabled = false;
                btnDelAccount.Enabled = false;
                btnControlAccount.Enabled = false;
                cb_PwdFile_Enable.Enabled = false;
                txt_PwdFile_Path.Visible = false;
                lbl_path.Visible = false;
                if (userManage.userData.CurrentUser == UserType.Admin)
                {
                    btnAddAccount.Enabled = true;
                    btnDelAccount.Enabled = true;
                    btnControlAccount.Enabled = true;
                }
            }
            catch (Exception ex)
            {

                System.Windows.Forms.MessageBox.Show(ex.ToString());
            }

        }

        /// <summary>
        /// 列出全部账户。密码按当前 isEncrypt 设置解密后显示
        /// </summary>
        private void AllUserShow()
        {
            try
            {
                this.lvwUserList.Items.Clear();
                int num = 1;
                foreach (UserModel user in userManage.userData.AllUsers())
                {
                    ListViewItem userItem = new ListViewItem((num++).ToString());
                    userItem.SubItems.Add(LanguageSwitch.ToUserLanguageg(user.userType, UserLanguageg.中文));//用户类型
                    userItem.SubItems.Add(user.Id);//用户ID
                    userItem.SubItems.Add(DisplayPassword(user.password));//密码
                    this.lvwUserList.Items.Add(userItem);
                }
            }
            catch (Exception ex)
            {
            }
        }

        private string DisplayPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;
            if (!userManage.userData.isEncrypt) return password;
            try
            {
                return SecurityHelper.Decrypt(password);
            }
            catch (Exception)
            {
                return password;
            }
        }

        private void btnDelAccount_Click(object sender, EventArgs e)
        {

            try
            {
                string currentUser = LanguageSwitch.ToUserLanguageg(userManage.userData.CurrentUser, UserLanguageg.中文);
                string currentUserID = userManage.userData.CurrentUserID;
                string selectUserTypeCN = "";
                string selectUserId = "";
                UserType userTypeTemp = UserType.Null;
                foreach (int i in lvwUserList.SelectedIndices)
                {
                    selectUserTypeCN = lvwUserList.Items[i].SubItems[1].Text;
                    selectUserId = lvwUserList.Items[i].SubItems[2].Text;

                    var userTypeEnum = LanguageProject.MatchStringToEnum<UserType>(selectUserTypeCN);
                    if (userTypeEnum != null)
                    {
                        userTypeTemp = userTypeEnum ?? UserType.Operator;
                    }
                    else
                    {
                        System.Windows.Forms.MessageBox.Show("用户类型转换错误！");
                        return;
                    }

                    if (userManage.userData.CurrentUser == userTypeTemp && userManage.userData.CurrentUserID == selectUserId)
                    {
                        System.Windows.Forms.MessageBox.Show("禁止删除当前用户！");
                        return;
                    }
                }

                if (lvwUserList.SelectedIndices.Count > 0)
                {
                    string message = string.Format("删除账户 {0} {1}", selectUserTypeCN, selectUserId);
                    DialogResult result = System.Windows.Forms.MessageBox.Show(message, "询问", MessageBoxButtons.YesNo);
                    if (result.Equals(DialogResult.Yes))
                    {
                        UserCtrlStatus delResult = userManage.DelUser(userTypeTemp, selectUserId);
                        if (delResult == UserCtrlStatus.DelUserSucceed)
                        {
                            // 被删账户如果正是自动登录的对象，清掉标记，避免下次启动空转
                            userManage.ForgetAutoLoginIf(selectUserId);
                            userManage.userData.Save();
                            ParamChangeLog.ParamChangeLog_Inspect("", "", currentUser, currentUserID, "账户管理-用户管理", message, true);
                            System.Windows.Forms.MessageBox.Show("删除成功！");
                        }
                        else if (delResult == UserCtrlStatus.LastAdminProtected)
                        {
                            System.Windows.Forms.MessageBox.Show("管理员账户仅允许一个，不可删除！");
                        }
                        else
                        {
                            System.Windows.Forms.MessageBox.Show("删除失败！");
                            //log
                        }
                        AllUserShow();
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        private void btnControlAccount_Click(object sender, EventArgs e)
        {
            if (Project.Instance.purviewManage == null) return;

            PurviewForm frm = new PurviewForm(Project.Instance.purviewManage.purviewParam);
            frm.ShowDialog();
        }

        private void AccountManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                bool isSuccessfulOperation = false;
                isSuccessfulOperation = userManage.userData.Save();
                if (!isSuccessfulOperation) System.Windows.Forms.MessageBox.Show("权限分配参数存储失敗", "ERROR", MessageBoxButtons.OK);

            }
            catch (Exception ex)
            {

                System.Windows.Forms.MessageBox.Show(ex.ToString());
            }


        }

        private void btn_PwdFile_Refresh_Click(object sender, EventArgs e)
        {

        }

        private void cb_PwdFile_Enable_CheckedChanged(object sender, EventArgs e)
        {

        }
    }
}