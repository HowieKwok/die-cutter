using AntdUI;
using Frame_Core;
using LogTool;
using NPOI.SS.Formula.Functions;
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

namespace Mef_VisionFrame
{
    public partial class Form_CraftFrame : Form
    {
        VM_Craft VM = null;
        FormCraftBase M_FormCraftBase = null;
        CraftBase CurrentCraftBase = new CraftBase();
        List<CraftRow> recipeInfo;

        public Form_CraftFrame(SystemInfo _systemInfo, CraftBase craftBase, string craftBasePath)
        {
            InitializeComponent();
            Frame_Core.FormStylingHook.Style(this);
            M_FormCraftBase = Craft_ViewService.GetView(M_FormCraftBase, _systemInfo.CraftType);
            VM = new VM_Craft(_systemInfo, craftBase, craftBasePath);
            InitTableColumns();

        }

        /// <summary>
        /// 当前运行的机型。启动时由 SystemParam.CraftType 决定，运行期不允许切换：
        /// 机型一旦改变，相机布局、流程结构、配方目录都会换一套，中途切换必然读到半套数据。
        /// </summary>
        private MaterialCrafEnum MachineType
        {
            get { return VM.systemInfo.CraftType; }
        }

        private void Form_CraftFrame_Load(object sender, EventArgs e)
        {
            Update_TableCrafts(MachineType, VM.systemInfo.RecipeName);
            ShowCraftView();
        }
        #region 私有工具
        private void InitTableColumns()
        {
            tableCrafts.Columns.Clear();
            tableCrafts.AutoGenerateColumns = false;

            tableCrafts.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
            tableCrafts.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(31, 35, 41);
            tableCrafts.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            tableCrafts.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 244, 255);
            tableCrafts.DefaultCellStyle.SelectionForeColor = Color.FromArgb(22, 119, 255);
            tableCrafts.DefaultCellStyle.Font = Font;

            tableCrafts.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "CraftName",
                DataPropertyName = "CraftName",
                HeaderText = "配方名",
                Width = 270,
                MinimumWidth = 220,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            tableCrafts.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Status",
                DataPropertyName = "Status",
                HeaderText = "状态",
                Width = 80,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            tableCrafts.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ActionText",
                DataPropertyName = "ActionText",
                HeaderText = "操作",
                Width = 60,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
        }

        private DataTable BuildCraftTable(List<CraftRow> rows)
        {
            DataTable table = new DataTable();
            table.Columns.Add("CraftName", typeof(string));
            table.Columns.Add("Status", typeof(string));
            table.Columns.Add("ActionText", typeof(string));

            foreach (CraftRow row in rows)
            {
                table.Rows.Add(row.CraftName, row.Status, row.ActionText);
            }


            return table;
        }
        private CraftRow GetSelectRecipeInfo(int rowIndex)
        {
            if (recipeInfo != null)
                if (rowIndex >= 0 && rowIndex < recipeInfo.Count)
                {
                    return recipeInfo[rowIndex];
                }

            return null;
        }
        private bool ShowNameDialog(string title, string defaultName, out string text)
        {
            text = string.Empty;
            using (Form dialog = new Form())
            using (TextBox textBox = new TextBox())
            using (System.Windows.Forms.Button btnOk = new System.Windows.Forms.Button())
            using (System.Windows.Forms.Button btnCancel = new System.Windows.Forms.Button())
            {
                dialog.Text = title;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(360, 112);
                dialog.Font = Font;

                textBox.Location = new Point(16, 18);
                textBox.Size = new Size(328, 26);
                textBox.Text = defaultName;
                textBox.SelectAll();

                btnOk.Text = "确定";
                btnOk.Location = new Point(176, 66);
                btnOk.Size = new Size(80, 30);
                btnOk.DialogResult = DialogResult.OK;

                btnCancel.Text = "取消";
                btnCancel.Location = new Point(264, 66);
                btnCancel.Size = new Size(80, 30);
                btnCancel.DialogResult = DialogResult.Cancel;

                dialog.Controls.Add(textBox);
                dialog.Controls.Add(btnOk);
                dialog.Controls.Add(btnCancel);
                dialog.AcceptButton = btnOk;
                dialog.CancelButton = btnCancel;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    text = textBox.Text.Trim();
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
        private void ShowCraftContextMenu(CraftRow craftRow)
        {
            bool hasSelected = (craftRow != null);
            bool isCurrent = craftRow?.IsCurrent ?? false;
            string selectRecipeName = craftRow?.CraftName ?? "";
            bool canRemove = hasSelected && !isCurrent;
            ContextMenuStripItem removeItem = new ContextMenuStripItem("移除配方")
            {
                Tag = "remove",
                Enabled = canRemove
            };
            // 禁用项仍染红色时，AntdUI 会按 ControlPaint.DrawStringDisabled 把红色与底色混合，
            // 显示成发虚的暗红。只在可用时才上红色。
            if (canRemove) removeItem.Fore = Color.Red;

            IContextMenuStripItem[] items =
            {
                new ContextMenuStripItem("新建配方") { Tag = "create" },
                new ContextMenuStripItem("切换配方") { Tag = "switch", Enabled = hasSelected && !isCurrent },
                new ContextMenuStripItem("复制配方") { Tag = "copy", Enabled = hasSelected },
                new ContextMenuStripItem("重命名配方") { Tag = "rename", Enabled = hasSelected&& !isCurrent},
                removeItem,
                new ContextMenuStripItemDivider(),
                new ContextMenuStripItem("打开配方文件位置") { Tag = "open" }
            };

            AntdUI.ContextMenuStrip.open(tableCrafts, item =>
            {
                string tag = item.Tag as string;

                try
                {
                    if (tag == "switch")
                    {
                        SwitchToRecipe(craftRow);
                    }
                    else if (tag == "create")
                    {
                        if (!ShowNameDialog("新建配方", "", out string newName)) return;
                        VM.CreateRecipe(MachineType, newName);
                        Update_TableCrafts(MachineType, VM.systemInfo.RecipeName);
                    }
                    else if (tag == "copy")
                    {
                        bool result = VM.CopyRecipe(MachineType, selectRecipeName, out string newName);
                        Update_TableCrafts(MachineType, VM.systemInfo.RecipeName);

                        if (result)
                        {
                            string oldProcessPath = CraftExtension.GetProcessFilePath(Project.Instance.paramPathSet.CraftPath, MachineType, selectRecipeName);
                            string oldInstanceParamPath = CraftExtension.GetProcessInstanceFilePath(Project.Instance.paramPathSet.CraftPath, MachineType, selectRecipeName);
                            string newProcessPath = CraftExtension.GetProcessFilePath(Project.Instance.paramPathSet.CraftPath, MachineType, newName);
                            string newInstanceParamPath = CraftExtension.GetProcessInstanceFilePath(Project.Instance.paramPathSet.CraftPath, MachineType, newName);

                            Project.Instance.Process流程插件管理者.CopyRecipe(oldProcessPath, oldInstanceParamPath, newProcessPath, newInstanceParamPath);
                        }

                    }
                    else if (tag == "rename")
                    {
                        if (isCurrent)
                        {
                            MessageBox.Show("不能重命名正在生产的配方！");
                            return;
                        }

                        if (!ShowNameDialog("重命名配方", selectRecipeName, out string newName)) return;
                        VM.RenameRecipe(MachineType, selectRecipeName, newName);
                        Update_TableCrafts(MachineType, VM.systemInfo.RecipeName);

                        string oldProcessPath = CraftExtension.GetProcessFilePath(Project.Instance.paramPathSet.CraftPath, MachineType, selectRecipeName);
                        string oldInstanceParamPath = CraftExtension.GetProcessInstanceFilePath(Project.Instance.paramPathSet.CraftPath, MachineType, selectRecipeName);
                        string newProcessPath = CraftExtension.GetProcessFilePath(Project.Instance.paramPathSet.CraftPath, MachineType, newName);
                        string newInstanceParamPath = CraftExtension.GetProcessInstanceFilePath(Project.Instance.paramPathSet.CraftPath, MachineType, newName);
                        Project.Instance.Process流程插件管理者.RenameRecipe(oldProcessPath, oldInstanceParamPath, newProcessPath, newInstanceParamPath);
                    }
                    else if (tag == "remove")
                    {
                        if (isCurrent)
                        {
                            MessageBox.Show("不能删除正在生产的配方！");
                            return;
                        }

                        try
                        {
                            // DeleteRecipe 内部会二次确认；用户取消时返回 false，
                            // 这里必须立即退出——否则流程目录照删，还会误报"删除成功"。
                            if (!VM.DeleteRecipe(MachineType, selectRecipeName)) return;

                            Update_TableCrafts(MachineType, VM.systemInfo.RecipeName);

                            string processPath = CraftExtension.GetProcessFileBasePath(Project.Instance.paramPathSet.CraftPath, MachineType, selectRecipeName);
                            if (Directory.Exists(processPath))
                            {
                                Directory.Delete(processPath,true);
                            }

                            MessageBox.Show($"配方【{selectRecipeName}】已成功删除");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"删除配方失败：{ex.Message}");
                        }
                    }
                    else if (tag == "open")
                    {
                        VM.OpenRecipeFolder(MachineType);
                    }
                }
                catch (Exception ex)
                {
                    Log4netRecord.ErrorLog.Error("异常", ex);
                }

            }, items, 0);
        }

        private void Update_TableCrafts(MaterialCrafEnum craftType, string currentRecipeName)
        {
            string keyword = inputSearch.Text == null ? "" : inputSearch.Text.Trim();
            recipeInfo = VM.GetRecipeInfo(craftType, currentRecipeName, keyword);
            if (recipeInfo != null)
            {
                tableCrafts.DataSource = BuildCraftTable(recipeInfo);
            }
        }
        #endregion

        /// <summary>
        /// 按当前机型挂载工艺窗体，并定位到正在使用的配方。
        /// 原来是"下拉改机型 → 重建窗体"，机型改成运行期固定后，这里只负责挂载。
        /// </summary>
        private void ShowCraftView()
        {
            M_FormCraftBase = Craft_ViewService.GetView(M_FormCraftBase, MachineType);
            panel_Main.Controls.Clear();
            panel_Main.Controls.Add(M_FormCraftBase);
            M_FormCraftBase.Show();

            if (tableCrafts.Rows.Count == 0)
            {
                panel_Main.Controls.Clear();
                return;
            }

            int rowIndex = 0;
            for (int i = 0; i < tableCrafts.Rows.Count; i++)
            {
                CraftRow item = GetSelectRecipeInfo(i);
                if (item != null && item.IsCurrent)
                {
                    rowIndex = i;
                    break;
                }
            }

            tableCrafts.CurrentCell = tableCrafts.Rows[rowIndex].Cells[0];
            tableCrafts.Rows[rowIndex].Selected = true;

            CraftRow row = GetSelectRecipeInfo(rowIndex);
            Update_ParamToView(MachineType, row.CraftName);
        }
        private void tableCrafts_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            SwitchToRecipe(GetSelectRecipeInfo(e.RowIndex));
        }

        /// <summary>
        /// 切换到目标配方：写 SystemParam.RecipeName 后退出程序，由下次启动生效。
        ///
        /// 三条约束按顺序：拿不到目标 → 提示；已是当前配方 → 提示；生产中 → 拒绝。
        /// 只有全部通过才读盘加载，避免用户取消后 CurrentCraftBase 与 craftBase 指不同配方。
        /// </summary>
        private void SwitchToRecipe(CraftRow row)
        {
            if (row == null) return;

            if (row.CraftName == VM.systemInfo.RecipeName)
            {
                MessageBox.Show($"配方【{row.CraftName}】已经是当前生产配方。");
                return;
            }

            if (Project.Instance.Is_MojuanStart)
            {
                MessageBox.Show("正在生产中，不能切换配方。请先停机。");
                return;
            }

            CraftBase target = Craft_ModelService.GetCraft(VM.CraftBasePath, MachineType, row.CraftName);
            if (target == null)
            {
                MessageBox.Show($"配方【{row.CraftName}】加载失败，无法切换。");
                return;
            }

            string str = "";
            str += $"是否切换配方?\r\n 切换后软件将自动关闭，请手动重启生效。";
            str += $"\r\n 当前配方：{VM.M_craftBase.RecipeName}，工艺极性：{VM.M_craftBase.Electrode}";
            str += $"\r\n 切换配方：{target.RecipeName}，工艺极性：{target.Electrode}";

            if (MessageBox.Show(str, "切换配方", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            Project.Instance.craftBase = target;
            VM.systemInfo.RecipeName = target.RecipeName;
            VM.systemInfo.Save();
            this.Hide();

            Environment.Exit(0); //关闭整个工程
        }

        private void tableCrafts_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            int index = e.RowIndex;
            CraftRow row = GetSelectRecipeInfo(index);
            if (e.Button == MouseButtons.Right)
            {
                ShowCraftContextMenu(row);
            }
            else if (e.Button == MouseButtons.Left && index >= 0)
            {
                if (CurrentCraftBase.RecipeName == row.CraftName) return;

                MaterialCrafEnum materialCrafEnum = MachineType;
                Update_ParamToView(materialCrafEnum, row.CraftName);
            }
        }

        /// <summary>
        /// 加载工艺参数到窗体
        /// </summary>
        public void Update_ParamToView(MaterialCrafEnum craftType, string recipeName)
        {
            CurrentCraftBase = Craft_ModelService.GetCraft(VM.CraftBasePath, craftType, recipeName);

            M_FormCraftBase.Init(CurrentCraftBase);
            M_FormCraftBase.ParamToView();
        }

        private void btn_SaveCurrentCraft_Click(object sender, EventArgs e)
        {
            try
            {
                M_FormCraftBase.ViewToParam();
                CurrentCraftBase.SaveCraft(VM.CraftBasePath, CurrentCraftBase.CraftType, CurrentCraftBase.RecipeName);
                VM.M_craftBase = Craft_ModelService.GetCraft(VM.CraftBasePath, VM.systemInfo.CraftType, VM.systemInfo.RecipeName);
                Project.Instance.craftBase = VM.M_craftBase;
                MessageBox.Show("保存成功");
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败");
            }
        }

        private void inputSearch_TextChanged(object sender, EventArgs e)
        {
            Update_TableCrafts(MachineType, VM.systemInfo.RecipeName);
        }
    }
}
