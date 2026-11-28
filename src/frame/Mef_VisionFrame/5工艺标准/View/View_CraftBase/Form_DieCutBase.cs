using AntdUI;
using Frame_Core;
using Frame_Core.pluginParams;
using LogTool;
using NPOI.OpenXmlFormats.Dml.Diagram;
using NPOI.SS.Formula.Functions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Forms;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 模切类机型的工艺参数窗体：每路相机的全局变量表 + 工位1缺陷表 + 工位3尺寸/缺陷表 + 机台全局变量。
    ///
    /// 界面结构与相机角色无关，只与相机数量有关（激光 6 路、五金 5 路），
    /// 所以工位页签的下标由相机数推导，不写死 6/7/8。机型只需派生一个空壳窗体并在
    /// Craft_ViewService 里登记，不要在子类里复制这套代码。
    /// </summary>
    public partial class Form_DieCutBase : FormCraftBase
    {
        private readonly VM_Craft_DieCut VM = new VM_Craft_DieCut();
        CamCraft currentCamCraft = null;
        List<DefectCraft_DieCuttingCut> currentDefectCraft = null;
        List<string> l_CraftItems;
        public Form_DieCutBase()
        {
            InitializeComponent();
            Frame_Core.FormStylingHook.Style(this);
            InitTableColumns__DefectCraft(dataGridView_Defect, this.Font);
            InitTableColumns_SizeCraftStation3();
            InitTableColumns_SizeCraft();
            InitTableColumns_GlobalVariableitem();
        }

        #region 虚方法重写
        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="craftBase"></param>
        public override void Init(CraftBase craftBase)
        {
            VM.M_CraftParam = craftBase as CraftParam_DieCutBase;
            if (VM.M_CraftParam == null)
            {
                System.Windows.Forms.MessageBox.Show("参数类型加载错误");
                return;
            }

            VM.Init(VM.M_CraftParam);

            // 装表前先挂容错：任何一格的值与下拉选项不匹配时只记日志，不弹模态框
            绑定表格容错();

            // 机型在出厂时确定，界面不提供切换；极性属于产品配方属性，可在此修改
            ElectrodeText.Items.Clear();
            ElectrodeText.Items.AddRange(Enum.GetNames(typeof(ElectrodeEnum)));

            // 相机页签的数量取配方里的相机项数（= 设备画像的槽位数），
            // 不取相机配置文件的数量：配置里多出来的那几路没有配方项，页签会错位。
            l_CraftItems = new List<string>();
            for (int i = 0; i < VM.M_CraftParam.Crafts.Count; i++)
            {
                l_CraftItems.Add(Service_Camera.GetCam_Name(i));
            }
            l_CraftItems.Add("缺陷检测工位");
            l_CraftItems.Add("极片检测工位");
            l_CraftItems.Add("全局变量");
            CraftItem.Items.Clear();
            for (int i = 0; i < l_CraftItems.Count; i++)
            {
                CraftItem.Items.Add(new AntdUI.SegmentedItem() { Text = l_CraftItems[i] });
            }
        }

        /// <summary>
        /// 把参数显示到工艺界面
        /// </summary>
        public override void ParamToView()
        {
            if (VM.M_CraftParam == null) return;
            CraftType.Text = VM.M_CraftParam.CraftType.ToString();
            RecipeName.Text = VM.M_CraftParam.RecipeName;
            // 极性属于产品配方属性：此处显示并可修改，保存后写入配方文件
            ElectrodeText.SelectedIndex = (int)VM.M_CraftParam.Electrode;
            MachineNumber.Text = VM.M_CraftParam.MachineNumber;

            CraftItem.SelectIndex = 0;

        }

        /// <summary>
        /// 界面内容下发到参数
        /// </summary>
        public override void ViewToParam()
        {
            if (VM.M_CraftParam == null) return;

            VM.M_CraftParam.RecipeName = RecipeName.Text;
            VM.M_CraftParam.Electrode = (ElectrodeEnum)ElectrodeText.SelectedIndex;
            VM.M_CraftParam.MachineNumber = MachineNumber.Text;

            // 极性是配方的属性，缺陷检测模块的 com_Cathode 由它派生，
            // 保存工艺参数时立刻同步，避免要等下次启动才生效。
            Project.Instance.Process流程插件管理者.SyncDefectPolarity();

            ParamAndView_Select(false);
        }
        #endregion
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

                // 输入校验示例：禁止空白确认
                dialog.FormClosing += (s, e) =>
                {
                    if (dialog.DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(textBox.Text))
                    {
                        MessageBox.Show("输入内容不能为空！");
                        e.Cancel = true; // 阻止弹窗关闭
                    }
                };

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

        #region 缺陷工艺
        private static void InitTableColumns__DefectCraft(DataGridView dataGridView, Font font)
        {
            dataGridView.Columns.Clear();
            dataGridView.AutoGenerateColumns = false;

            dataGridView.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
            dataGridView.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(31, 35, 41);
            dataGridView.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridView.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 244, 255);
            dataGridView.DefaultCellStyle.SelectionForeColor = Color.FromArgb(22, 119, 255);
            dataGridView.DefaultCellStyle.Font = font;

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ID",
                DataPropertyName = "ID",
                HeaderText = "ID",
                Width = 40,
                MinimumWidth = 40,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,

            });

            //dataGridView.Columns.Add(new DataGridViewCheckBoxColumn
            //{
            //    Name = "Enable",
            //    DataPropertyName = "Enable",
            //    HeaderText = "启用",
            //    Width = 50,
            //    SortMode = DataGridViewColumnSortMode.NotSortable,
            //    DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            //});

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                DataPropertyName = "Name",
                HeaderText = "检测项",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ReadOnly = true,
            });

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Describe",
                DataPropertyName = "Describe",
                HeaderText = "描述",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Condition",
                DataPropertyName = "Condition",
                HeaderText = "计算公式（面积单位：mm²，其他单位：mm）",
                Width = 500,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ReadOnly = true,
            });

            dataGridView.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "DB",
                DataPropertyName = "DB",
                HeaderText = "剔片",
                Width = 50,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "IsSevere",
                DataPropertyName = "IsSevere",
                HeaderText = "重度",
                Width = 50,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ContinuousCount",
                DataPropertyName = "ContinuousCount",
                HeaderText = "连续NG阈值",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });


            dataGridView.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "BJ_Continuous",
                DataPropertyName = "BJ_Continuous",
                HeaderText = "连续NG报警",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "TJ_Continuous",
                DataPropertyName = "TJ_Continuous",
                HeaderText = "连续NG停机",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "AccumulateCount",
                DataPropertyName = "AccumulateCount",
                HeaderText = "累计NG阈值",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });


            dataGridView.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "BJ_Accumulate",
                DataPropertyName = "BJ_Accumulate",
                HeaderText = "累计NG报警",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "TJ_Accumulate",
                DataPropertyName = "TJ_Accumulate",
                HeaderText = "累计NG停机",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
        }
        private static DataTable BuildTable_DefectCraft(List<DefectCraft_DieCuttingCut> defectCrafts)
        {

            DataTable table = new DataTable();
            table.Columns.Add("ID");
            //table.Columns.Add("Enable");
            table.Columns.Add("Name");
            table.Columns.Add("Describe");
            table.Columns.Add("Condition");
            table.Columns.Add("DB");
            table.Columns.Add("IsSevere");
            table.Columns.Add("ContinuousCount");
            table.Columns.Add("BJ_Continuous");
            table.Columns.Add("TJ_Continuous");
            table.Columns.Add("AccumulateCount");
            table.Columns.Add("BJ_Accumulate");
            table.Columns.Add("TJ_Accumulate");

            foreach (var row in defectCrafts)
            {
                table.Rows.Add(
                    row.Id,
                    //row.Enable,
                    row.Name,
                    row.Describe,
                    row.GetConditionalFormat(),
                    row.DB,
                    row.IsSevere,
                    row.ContinuousCount,
                    row.BJ_Continuous,
                    row.TJ_Continuous,
                    row.AccumulateCount,
                    row.BJ_Accumulate,
                    row.TJ_Accumulate);
            }

            return table;
        }
        private static void GetBuildTable_DefectCraft(DataGridView dataGridView, List<DefectCraft_DieCuttingCut> defectCrafts)
        {
            int colIndex = 0;
            string rowID = "";
            string colName = "";
            try
            {
                for (int i = 0; i < dataGridView.Rows.Count; i++)
                {
                    rowID = defectCrafts[i].Id.ToString();
                    colIndex = 0;
                    defectCrafts[i].Id = Convert.ToInt32(dataGridView.Rows[i].Cells[colIndex++].Value);
                    //defectCrafts[i].Enable = Convert.ToBoolean(dataGridView.Rows[i].Cells[colIndex++].Value);
                    defectCrafts[i].Name = Convert.ToString(dataGridView.Rows[i].Cells[colIndex++].Value);
                    defectCrafts[i].Describe = Convert.ToString(dataGridView.Rows[i].Cells[colIndex++].Value);
                    colIndex++;//这列是条件格式
                    defectCrafts[i].DB = Convert.ToBoolean(dataGridView.Rows[i].Cells[colIndex++].Value);
                    defectCrafts[i].IsSevere = Convert.ToBoolean(dataGridView.Rows[i].Cells[colIndex++].Value);
                    defectCrafts[i].ContinuousCount = Convert.ToInt32(dataGridView.Rows[i].Cells[colIndex++].Value);
                    defectCrafts[i].BJ_Continuous = Convert.ToBoolean(dataGridView.Rows[i].Cells[colIndex++].Value);
                    defectCrafts[i].TJ_Continuous = Convert.ToBoolean(dataGridView.Rows[i].Cells[colIndex++].Value);
                    defectCrafts[i].AccumulateCount = Convert.ToInt32(dataGridView.Rows[i].Cells[colIndex++].Value);
                    defectCrafts[i].BJ_Accumulate = Convert.ToBoolean(dataGridView.Rows[i].Cells[colIndex++].Value);
                    defectCrafts[i].TJ_Accumulate = Convert.ToBoolean(dataGridView.Rows[i].Cells[colIndex++].Value);
                }
            }
            catch (Exception ex)
            {
                colIndex = colIndex - 1;
                if (colIndex < 0) return;
                if (dataGridView?.Columns?.Count > colIndex) colName = dataGridView?.Columns?[colIndex].HeaderText;
                string showMessage = $"行【{rowID}】列【{colName}】\r\n数据类型格式输入错误";
                MessageBox.Show(showMessage, "取消保存");
            }
        }
        private void UpdateTable_DefectCrafts(DataGridView dataGridView, List<DefectCraft_DieCuttingCut> defectCrafts)
        {
            if (defectCrafts != null)
            {
                dataGridView.DataSource = BuildTable_DefectCraft(defectCrafts);
            }
            currentDefectCraft = defectCrafts;
        }
        private void ShowCraftContextMenu_DefectCrafts(DataGridView dataGridView, List<DefectCraft_DieCuttingCut> defectCrafts, bool hasSelected, int index)
        {
            IContextMenuStripItem[] items =
            {
                new ContextMenuStripItem("编辑") { Tag = "edit",Enabled=hasSelected },
                //new ContextMenuStripItem("导出") { Tag = "export",Enabled=hasSelected },
                //new ContextMenuStripItem("加载") { Tag = "load",Enabled=hasSelected },
                new ContextMenuStripItem("置顶") { Tag = "top" ,Enabled=hasSelected},
                new ContextMenuStripItem("上移") { Tag = "moveUp" ,Enabled=hasSelected},
                new ContextMenuStripItem("下移") { Tag = "moveDown" ,Enabled=hasSelected},
                new ContextMenuStripItem("添加类型") { Tag = "create" },
                new ContextMenuStripItem("删除类型") { Tag = "remove" ,Enabled=hasSelected},
            };
            if (defectCrafts == null) return;

            AntdUI.ContextMenuStrip.open(dataGridView, item =>
            {
                string tag = item.Tag as string;
                if (tag == "edit")
                {
                    using (Form_DefectRules form = new Form_DefectRules(defectCrafts, index))
                    {
                        form.StartPosition = FormStartPosition.CenterParent;
                        form.Text = "编辑";
                        form.ShowDialog();
                        UpdateTable_DefectCrafts(dataGridView, defectCrafts);
                    }
                }
                if (tag == "top")
                {
                    VM.Top(defectCrafts, index);
                    UpdateTable_DefectCrafts(dataGridView, defectCrafts);
                }
                if (tag == "moveUp")
                {
                    VM.MoveUp(defectCrafts, index);
                    UpdateTable_DefectCrafts(dataGridView, defectCrafts);
                }
                if (tag == "moveDown")
                {
                    VM.MoveDown(defectCrafts, index);
                    UpdateTable_DefectCrafts(dataGridView, defectCrafts);
                }
                else if (tag == "create")
                {
                    DefectCraft_DieCuttingCut defectCraft = new DefectCraft_DieCuttingCut();

                    using (Form_DefectRules form = new Form_DefectRules(defectCraft))
                    {
                        form.Text = "添加类型";
                        DialogResult d = form.ShowDialog();
                        if (d == DialogResult.OK)
                        {
                            defectCraft.Describe = defectCraft.Name;
                            VM.AddDefect(defectCrafts, defectCraft);
                            UpdateTable_DefectCrafts(dataGridView, defectCrafts);
                        }
                    }
                }
                else if (tag == "remove")
                {
                    string name = defectCrafts?.Get_DefectNames(index);
                    DialogResult result = System.Windows.Forms.MessageBox.Show($"是否删除当前检测项:{name}？", "询问", MessageBoxButtons.YesNo);
                    if (result == DialogResult.No)
                    {
                        return;
                    }

                    VM.DeleteDefect(defectCrafts, index);
                    UpdateTable_DefectCrafts(dataGridView, defectCrafts);
                }
            }, items, 0);
        }
        private void dataGridView_Defect_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            int index = e.RowIndex;
            bool hasSelet = index >= 0;
            if (e.Button == MouseButtons.Right)
            {
                ShowCraftContextMenu_DefectCrafts(dataGridView_Defect, currentDefectCraft, hasSelet, index);
            }
        }
        #endregion


        #region 尺寸工艺
        private void InitTableColumns_SizeCraft()
        {
            dataGridView_Size.Columns.Clear();
            dataGridView_Size.AutoGenerateColumns = false;

            dataGridView_Size.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
            dataGridView_Size.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(31, 35, 41);
            dataGridView_Size.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridView_Size.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 244, 255);
            dataGridView_Size.DefaultCellStyle.SelectionForeColor = Color.FromArgb(22, 119, 255);
            dataGridView_Size.DefaultCellStyle.Font = Font;

            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ID",
                DataPropertyName = "ID",
                HeaderText = "ID",
                Width = 40,
                MinimumWidth = 40,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,

            });

            //dataGridView_Size.Columns.Add(new DataGridViewCheckBoxColumn
            //{
            //    Name = "Enable",
            //    DataPropertyName = "Enable",
            //    HeaderText = "启用",
            //    Width = 50,
            //    SortMode = DataGridViewColumnSortMode.NotSortable,
            //    DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            //});

            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                DataPropertyName = "Name",
                HeaderText = "检测项",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ReadOnly = true,
            });

            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Describe",
                DataPropertyName = "Describe",
                HeaderText = "描述",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "CalibrateCoeff",
                DataPropertyName = "CalibrateCoeff",
                HeaderText = "校准系数",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ReadOnly = true,
            });

            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "MeasureOffset",
                DataPropertyName = "MeasureOffset",
                HeaderText = "测量偏移",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ReadOnly = true,
            });

            dataGridView_Size.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "CalcFormula",
                DataPropertyName = "CalcFormula",
                HeaderText = "计算公式",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                DataSource = Enum.GetValues(typeof(CalcFormulaEnum)).Cast<CalcFormulaEnum>()
                                                                    .Select(e => new
                                                                    {
                                                                        Text = e.ToString(),
                                                                        Value = (int)e
                                                                    }).ToList(),

                DisplayMember = "Text",  // 界面显示枚举文字
                ValueMember = "Value"    // 绑定后台int数值，和DataTable匹配

            });


            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DataSource1",
                DataPropertyName = "DataSource1",
                HeaderText = "数据源1",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },

            });


            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DataSource2",
                DataPropertyName = "DataSource2",
                HeaderText = "数据源2",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "StandardValue",
                DataPropertyName = "StandardValue",
                HeaderText = "标准值",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DBTolLower",
                DataPropertyName = "DBTolLower",
                HeaderText = "下公差",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DBTolUpper",
                DataPropertyName = "DBTolUpper",
                HeaderText = "上公差",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DBContinuous",
                DataPropertyName = "DBContinuous",
                HeaderText = "连续",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });


            dataGridView_Size.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "DB",
                DataPropertyName = "DB",
                HeaderText = "剔片",
                Width = 50,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });



            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ContinuousCount",
                DataPropertyName = "ContinuousCount",
                HeaderText = "连续NG阈值",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_Size.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "BJ_Continuous",
                DataPropertyName = "BJ_Continuous",
                HeaderText = "连续NG报警",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView_Size.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "TJ_Continuous",
                DataPropertyName = "TJ_Continuous",
                HeaderText = "连续NG停机",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });


            dataGridView_Size.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "AccumulateCount",
                DataPropertyName = "AccumulateCount",
                HeaderText = "累计NG阈值",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });


            dataGridView_Size.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "BJ_Accumulate",
                DataPropertyName = "BJ_Accumulate",
                HeaderText = "累计NG报警",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView_Size.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "TJ_Accumulate",
                DataPropertyName = "TJ_Accumulate",
                HeaderText = "累计NG停机",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
        }
        private DataTable BuildTable_SizeCraft(List<SizeCraft_DieCuttingCut> l_Craft)
        {

            DataTable table = new DataTable();
            table.Columns.Add("ID");
            //table.Columns.Add("Enable");
            table.Columns.Add("Name");
            table.Columns.Add("Describe");
            table.Columns.Add("CalibrateCoeff");
            table.Columns.Add("MeasureOffset");
            table.Columns.Add("CalcFormula", typeof(int));
            table.Columns.Add("DataSource1");
            table.Columns.Add("DataSource2");
            table.Columns.Add("StandardValue");

            table.Columns.Add("DBTolLower");
            table.Columns.Add("DBTolUpper");
            table.Columns.Add("DBContinuous");
            table.Columns.Add("DB");

            table.Columns.Add("ContinuousCount");
            table.Columns.Add("BJ_Continuous");
            table.Columns.Add("TJ_Continuous");

            table.Columns.Add("AccumulateCount");
            table.Columns.Add("BJ_Accumulate");
            table.Columns.Add("TJ_Accumulate");

            foreach (var row in l_Craft)
            {
                table.Rows.Add(
                    row.Id,
                    //row.Enable,
                    row.Name,
                    row.Describe,
                    row.CalibrateCoeff,
                    row.MeasureOffset,
                    row.CalcFormula,
                    row.DataSource1,
                    row.DataSource2,
                    row.StandardValue,

                    row.DBTolLower,
                    row.DBTolUpper,
                    row.DBContinuous,
                    row.DBEnable,

                    row.ContinuousCount,
                    row.BJ_Continuous,
                    row.TJ_Continuous,

                    row.AccumulateCount,
                    row.BJ_Accumulate,
                    row.TJ_Accumulate);
            }

            return table;
        }
        private void GetBuildTable_SizeCraft(List<SizeCraft_DieCuttingCut> l_Craft)
        {
            int colIndex = 0;
            string rowID = "";
            string colName = "";
            try
            {
                for (int i = 0; i < dataGridView_Size.Rows.Count; i++)
                {
                    rowID = l_Craft[i].Id.ToString();
                    colIndex = 0;
                    l_Craft[i].Id = Convert.ToInt32(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    //l_Craft[i].Enable = Convert.ToBoolean(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].Name = Convert.ToString(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].Describe = Convert.ToString(dataGridView_Size.Rows[i].Cells[colIndex++].Value);

                    l_Craft[i].CalibrateCoeff = Convert.ToDouble(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].MeasureOffset = Convert.ToDouble(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].CalcFormula = Convert.ToInt32(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DataSource1 = Convert.ToString(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DataSource2 = Convert.ToString(dataGridView_Size.Rows[i].Cells[colIndex++].Value);

                    l_Craft[i].StandardValue = Convert.ToDouble(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DBTolLower = Convert.ToDouble(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DBTolUpper = Convert.ToDouble(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DBContinuous = Convert.ToInt32(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DBEnable = Convert.ToBoolean(dataGridView_Size.Rows[i].Cells[colIndex++].Value);

                    l_Craft[i].ContinuousCount = Convert.ToInt32(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].BJ_Continuous = Convert.ToBoolean(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].TJ_Continuous = Convert.ToBoolean(dataGridView_Size.Rows[i].Cells[colIndex++].Value);

                    l_Craft[i].AccumulateCount = Convert.ToInt32(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].BJ_Accumulate = Convert.ToBoolean(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].TJ_Accumulate = Convert.ToBoolean(dataGridView_Size.Rows[i].Cells[colIndex++].Value);
                }
            }
            catch (Exception ex)
            {
                colIndex = colIndex - 1;
                if (colIndex < 0) return;
                if (dataGridView_Size?.Columns?.Count > colIndex) colName = dataGridView_Size?.Columns?[colIndex].HeaderText;
                string showMessage = $"行【{rowID}】列【{colName}】\r\n数据类型格式输入错误";
                MessageBox.Show(showMessage, "取消保存");
            }
        }
        private void UpdateTable_SizeCraft(List<SizeCraft_DieCuttingCut> l_Craft)
        {
            if (l_Craft != null)
            {
                dataGridView_Size.DataSource = BuildTable_SizeCraft(l_Craft);
            }
        }
        private void ShowCraftContextMenu_SizeCraft(bool hasSelected, int index)
        {
            IContextMenuStripItem[] items =
            {
                new ContextMenuStripItem("编辑") { Tag = "edit",Enabled=hasSelected },
                //new ContextMenuStripItem("导出") { Tag = "export",Enabled=hasSelected },
                //new ContextMenuStripItem("加载") { Tag = "load",Enabled=hasSelected },
                new ContextMenuStripItem("置顶") { Tag = "top" ,Enabled=hasSelected},
                new ContextMenuStripItem("上移") { Tag = "moveUp" ,Enabled=hasSelected},
                new ContextMenuStripItem("下移") { Tag = "moveDown" ,Enabled=hasSelected},
                new ContextMenuStripItem("添加") { Tag = "create" },
                new ContextMenuStripItem("删除") { Tag = "remove" ,Enabled=hasSelected},
            };

            AntdUI.ContextMenuStrip.open(dataGridView_Size, item =>
            {
                string tag = item.Tag as string;
                if (tag == "edit")
                {
                    using (Form_SizeEdit form = new Form_SizeEdit(currentCamCraft.l_SizeCraft, index))
                    {
                        form.StartPosition = FormStartPosition.CenterParent;
                        form.Text = "编辑";
                        form.ShowDialog();
                        UpdateTable_SizeCraft(currentCamCraft.l_SizeCraft);
                    }
                }
                if (tag == "top")
                {
                    VM.Top(currentCamCraft.l_SizeCraft, index);
                    UpdateTable_SizeCraft(currentCamCraft.l_SizeCraft);
                }
                if (tag == "moveUp")
                {
                    VM.MoveUp(currentCamCraft.l_SizeCraft, index);
                    UpdateTable_SizeCraft(currentCamCraft.l_SizeCraft);
                }
                if (tag == "moveDown")
                {
                    VM.MoveDown(currentCamCraft.l_SizeCraft, index);
                    UpdateTable_SizeCraft(currentCamCraft.l_SizeCraft);
                }
                else if (tag == "create")
                {
                    SizeCraft_DieCuttingCut craftTemp = new SizeCraft_DieCuttingCut();

                    if (!ShowNameDialog("添加类型", "", out string newName)) return;
                    craftTemp.Name = newName;
                    craftTemp.Describe = newName;
                    VM.AddSizeCraft(currentCamCraft.l_SizeCraft, craftTemp);
                    UpdateTable_SizeCraft(currentCamCraft.l_SizeCraft);
                }
                else if (tag == "remove")
                {
                    string name = currentCamCraft?.l_SizeCraft?.Get_SizeNames(index);
                    DialogResult result = System.Windows.Forms.MessageBox.Show($"是否删除当前检测项:{name}？", "询问", MessageBoxButtons.YesNo);
                    if (result == DialogResult.No)
                    {
                        return;
                    }

                    VM.DeleteSizeCraft(currentCamCraft.l_SizeCraft, index);
                    UpdateTable_SizeCraft(currentCamCraft.l_SizeCraft);
                }
            }, items, 0);
        }
        private void dataGridView_Size_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            int index = e.RowIndex;
            bool hasSelet = index >= 0;
            if (e.Button == MouseButtons.Right)
            {
                ShowCraftContextMenu_SizeCraft(hasSelet, index);
            }
        }
        #endregion

        #region 工位三尺寸工艺
        private void InitTableColumns_SizeCraftStation3()
        {
            dataGridView_SizeStation3.Columns.Clear();
            dataGridView_SizeStation3.AutoGenerateColumns = false;

            dataGridView_SizeStation3.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
            dataGridView_SizeStation3.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(31, 35, 41);
            dataGridView_SizeStation3.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridView_SizeStation3.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 244, 255);
            dataGridView_SizeStation3.DefaultCellStyle.SelectionForeColor = Color.FromArgb(22, 119, 255);
            dataGridView_SizeStation3.DefaultCellStyle.Font = Font;

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ID",
                DataPropertyName = "ID",
                HeaderText = "ID",
                Width = 40,
                MinimumWidth = 40,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,
            });

            //dataGridView_SizeStation3.Columns.Add(new DataGridViewCheckBoxColumn
            //{
            //    Name = "Enable",
            //    DataPropertyName = "Enable",
            //    HeaderText = "启用",
            //    Width = 50,
            //    SortMode = DataGridViewColumnSortMode.NotSortable,
            //    DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            //});

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                DataPropertyName = "Name",
                HeaderText = "检测项",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ReadOnly = true,
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Describe",
                DataPropertyName = "Describe",
                HeaderText = "描述",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "CalibrateCoeff",
                DataPropertyName = "CalibrateCoeff",
                HeaderText = "校准系数",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ReadOnly = true,
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "MeasureOffset",
                DataPropertyName = "MeasureOffset",
                HeaderText = "测量偏移",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ReadOnly = true,
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "CalcFormula",
                DataPropertyName = "CalcFormula",
                HeaderText = "计算公式",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                DataSource = Enum.GetValues(typeof(CalcFormulaEnum)).Cast<CalcFormulaEnum>()
                                                                    .Select(e => new
                                                                    {
                                                                        Text = e.ToString(),
                                                                        Value = (int)e
                                                                    }).ToList(),

                DisplayMember = "Text",  // 界面显示枚举文字
                ValueMember = "Value"    // 绑定后台int数值，和DataTable匹配

            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "CameraId1",
                DataPropertyName = "CameraId1",
                HeaderText = "所属相机",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                // 具体选项在每次装表时按配方里的最大相机号刷新，见 刷新相机下拉
                DisplayMember = "Text",
                ValueMember = "Value"
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DataSource1",
                DataPropertyName = "DataSource1",
                HeaderText = "数据源1",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "CameraId2",
                DataPropertyName = "CameraId2",
                HeaderText = "所属相机",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                // 具体选项在每次装表时按配方里的最大相机号刷新，见 刷新相机下拉
                DisplayMember = "Text",
                ValueMember = "Value"
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DataSource2",
                DataPropertyName = "DataSource2",
                HeaderText = "数据源2",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "StandardValue",
                DataPropertyName = "StandardValue",
                HeaderText = "标准值",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DBTolLower",
                DataPropertyName = "DBTolLower",
                HeaderText = "下公差",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DBTolUpper",
                DataPropertyName = "DBTolUpper",
                HeaderText = "上公差",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "DBContinuous",
                DataPropertyName = "DBContinuous",
                HeaderText = "连续",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });


            dataGridView_SizeStation3.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "DB",
                DataPropertyName = "DB",
                HeaderText = "剔片",
                Width = 50,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });



            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ContinuousCount",
                DataPropertyName = "ContinuousCount",
                HeaderText = "连续NG阈值",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "BJ_Continuous",
                DataPropertyName = "BJ_Continuous",
                HeaderText = "连续NG报警",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "TJ_Continuous",
                DataPropertyName = "TJ_Continuous",
                HeaderText = "连续NG停机",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });


            dataGridView_SizeStation3.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "AccumulateCount",
                DataPropertyName = "AccumulateCount",
                HeaderText = "累计NG阈值",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });


            dataGridView_SizeStation3.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "BJ_Accumulate",
                DataPropertyName = "BJ_Accumulate",
                HeaderText = "累计NG报警",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dataGridView_SizeStation3.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "TJ_Accumulate",
                DataPropertyName = "TJ_Accumulate",
                HeaderText = "累计NG停机",
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
        }
        private DataTable BuildTable_SizeCraftStation3(List<SizeCraftStation_DieCuttingCut> l_Craft)
        {

            DataTable table = new DataTable();
            table.Columns.Add("ID");
            //table.Columns.Add("Enable");
            table.Columns.Add("Name");
            table.Columns.Add("Describe");
            table.Columns.Add("CalibrateCoeff");
            table.Columns.Add("MeasureOffset");
            table.Columns.Add("CalcFormula", typeof(int));
            table.Columns.Add("CameraId1", typeof(int));
            table.Columns.Add("DataSource1");
            table.Columns.Add("CameraId2", typeof(int));
            table.Columns.Add("DataSource2");
            table.Columns.Add("StandardValue");

            table.Columns.Add("DBTolLower");
            table.Columns.Add("DBTolUpper");
            table.Columns.Add("DBContinuous");
            table.Columns.Add("DB");

            table.Columns.Add("ContinuousCount");
            table.Columns.Add("BJ_Continuous");
            table.Columns.Add("TJ_Continuous");

            table.Columns.Add("AccumulateCount");
            table.Columns.Add("BJ_Accumulate");
            table.Columns.Add("TJ_Accumulate");

            foreach (var row in l_Craft)
            {
                table.Rows.Add(
                    row.Id,
                    //row.Enable,
                    row.Name,
                    row.Describe,
                    row.CalibrateCoeff,
                    row.MeasureOffset,
                    row.CalcFormula,
                    row.CameraId1,
                    row.DataSource1,
                    row.CameraId2,
                    row.DataSource2,
                    row.StandardValue,

                    row.DBTolLower,
                    row.DBTolUpper,
                    row.DBContinuous,
                    row.DBEnable,

                    row.ContinuousCount,
                    row.BJ_Continuous,
                    row.TJ_Continuous,

                    row.AccumulateCount,
                    row.BJ_Accumulate,
                    row.TJ_Accumulate);
            }

            return table;
        }
        private void GetBuildTable_SizeCraftStation3(List<SizeCraftStation_DieCuttingCut> l_Craft)
        {
            int colIndex = 0;
            string rowID = "";
            string colName = "";
            try
            {
                for (int i = 0; i < dataGridView_SizeStation3.Rows.Count; i++)
                {
                    rowID = l_Craft[i].Id.ToString();
                    colIndex = 0;
                    l_Craft[i].Id = Convert.ToInt32(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    //l_Craft[i].Enable = Convert.ToBoolean(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].Name = Convert.ToString(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].Describe = Convert.ToString(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);

                    l_Craft[i].CalibrateCoeff = Convert.ToDouble(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].MeasureOffset = Convert.ToDouble(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].CalcFormula = Convert.ToInt32(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].CameraId1 = Convert.ToInt32(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DataSource1 = Convert.ToString(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].CameraId2 = Convert.ToInt32(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DataSource2 = Convert.ToString(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);

                    l_Craft[i].StandardValue = Convert.ToDouble(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DBTolLower = Convert.ToDouble(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DBTolUpper = Convert.ToDouble(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DBContinuous = Convert.ToInt32(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].DBEnable = Convert.ToBoolean(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);


                    l_Craft[i].ContinuousCount = Convert.ToInt32(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].BJ_Continuous = Convert.ToBoolean(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].TJ_Continuous = Convert.ToBoolean(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);


                    l_Craft[i].AccumulateCount = Convert.ToInt32(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].BJ_Accumulate = Convert.ToBoolean(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                    l_Craft[i].TJ_Accumulate = Convert.ToBoolean(dataGridView_SizeStation3.Rows[i].Cells[colIndex++].Value);
                }
            }
            catch (Exception ex)
            {
                colIndex = colIndex - 1;
                if (colIndex < 0) return;
                if (dataGridView_SizeStation3?.Columns?.Count > colIndex) colName = dataGridView_SizeStation3?.Columns?[colIndex].HeaderText;
                string showMessage = $"行【{rowID}】列【{colName}】\r\n数据类型格式输入错误";
                MessageBox.Show(showMessage, "取消保存");
            }
        }
        private void UpdateTable_SizeCraftStation3(List<SizeCraftStation_DieCuttingCut> l_Craft)
        {
            if (l_Craft != null)
            {
                // 选项要覆盖配方里出现过的最大相机号，否则旧配方引用已删除的相机会让
                // 下拉框抛"值无效"（配方窗口允许切到别的机型，相机配置却是全局唯一一份）
                int 最大相机号 = 0;
                foreach (SizeCraftStation_DieCuttingCut item in l_Craft)
                {
                    最大相机号 = Math.Max(最大相机号, Math.Max(item.CameraId1, item.CameraId2));
                }
                刷新相机下拉(dataGridView_SizeStation3, 最大相机号);

                dataGridView_SizeStation3.DataSource = BuildTable_SizeCraftStation3(l_Craft);
            }
        }
        private void ShowCraftContextMenu_SizeCraftStation3(bool hasSelected, int index)
        {
            IContextMenuStripItem[] items =
            {
                new ContextMenuStripItem("编辑") { Tag = "edit",Enabled=hasSelected },
                //new ContextMenuStripItem("导出") { Tag = "export",Enabled=hasSelected },
                //new ContextMenuStripItem("加载") { Tag = "load",Enabled=hasSelected },
                new ContextMenuStripItem("置顶") { Tag = "top" ,Enabled=hasSelected},
                new ContextMenuStripItem("上移") { Tag = "moveUp" ,Enabled=hasSelected},
                new ContextMenuStripItem("下移") { Tag = "moveDown" ,Enabled=hasSelected},
                new ContextMenuStripItem("添加") { Tag = "create" },
                new ContextMenuStripItem("删除") { Tag = "remove" ,Enabled=hasSelected},
            };

            AntdUI.ContextMenuStrip.open(dataGridView_SizeStation3, item =>
            {
                string tag = item.Tag as string;
                if (tag == "edit")
                {
                    using (Form_SizeStationEdit form = new Form_SizeStationEdit(VM.M_CraftParam.l_SizeCraftStation_3, index))
                    {
                        form.StartPosition = FormStartPosition.CenterParent;
                        form.Text = "编辑";
                        form.ShowDialog();
                        UpdateTable_SizeCraftStation3(VM.M_CraftParam.l_SizeCraftStation_3);
                    }
                }
                if (tag == "top")
                {
                    VM.Top(VM.M_CraftParam.l_SizeCraftStation_3, index);
                    UpdateTable_SizeCraftStation3(VM.M_CraftParam.l_SizeCraftStation_3);
                }
                if (tag == "moveUp")
                {
                    VM.MoveUp(VM.M_CraftParam.l_SizeCraftStation_3, index);
                    UpdateTable_SizeCraftStation3(VM.M_CraftParam.l_SizeCraftStation_3);
                }
                if (tag == "moveDown")
                {
                    VM.MoveDown(VM.M_CraftParam.l_SizeCraftStation_3, index);
                    UpdateTable_SizeCraftStation3(VM.M_CraftParam.l_SizeCraftStation_3);
                }
                else if (tag == "create")
                {
                    SizeCraftStation_DieCuttingCut craftTemp = new SizeCraftStation_DieCuttingCut();

                    if (!ShowNameDialog("添加类型", "", out string newName)) return;
                    craftTemp.Name = newName;
                    craftTemp.Describe = newName;
                    VM.AddSizeCraftStation_3(VM.M_CraftParam.l_SizeCraftStation_3, craftTemp);
                    UpdateTable_SizeCraftStation3(VM.M_CraftParam.l_SizeCraftStation_3);
                }
                else if (tag == "remove")
                {
                    string name = VM.M_CraftParam?.l_SizeCraftStation_3?.Get_SizeNames(index);
                    DialogResult result = System.Windows.Forms.MessageBox.Show($"是否删除当前检测项:{name}？", "询问", MessageBoxButtons.YesNo);
                    if (result == DialogResult.No)
                    {
                        return;
                    }
                    VM.DeleteSizeCraftStation_3(VM.M_CraftParam.l_SizeCraftStation_3, index);
                    UpdateTable_SizeCraftStation3(VM.M_CraftParam.l_SizeCraftStation_3);
                }
            }, items, 0);
        }
        private void dataGridView_SizeStation3_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            int index = e.RowIndex;
            bool hasSelet = index >= 0;
            if (e.Button == MouseButtons.Right)
            {
                ShowCraftContextMenu_SizeCraftStation3(hasSelet, index);
            }
        }
        #endregion

        #region 相机下拉与表格容错

        /// <summary>
        /// 相机下拉的一项。用具体类型而不是匿名类型：三处列都要引用同一份选项，
        /// 具名类型在调试时也能直接看到内容。
        /// </summary>
        private sealed class 相机选项项
        {
            public string Text { get; set; }
            public int Value { get; set; }
        }

        /// <summary>
        /// 生成相机下拉选项：覆盖到"当前配置的相机"与"配方里出现过的最大相机号"两者的较大值。
        ///
        /// 配方窗口允许切换到别的机型，而相机配置是全局唯一的一份。旧配方里可能引用
        /// 已经不存在的相机（例如 6 路配置下写的 cam5，换成 5 路机器后就没了）。
        /// 如果选项只按当前配置生成，这种值会让 DataGridViewComboBoxCell 直接抛
        /// "值无效" 并弹模态框；覆盖到配方最大值后，它会显示成"(5) 未配置"，
        /// 人能一眼看到并改掉，而不是被一个弹框挡住。
        /// </summary>
        private static List<相机选项项> 相机选项(int 配方最大相机号)
        {
            int 配置相机数 = Service_Camera.GetCam_Names().Length;
            int 上限 = Math.Max(配置相机数 - 1, 配方最大相机号);

            List<相机选项项> options = new List<相机选项项>();
            for (int i = 0; i <= 上限; i++)
            {
                options.Add(new 相机选项项
                {
                    Text = i < 配置相机数 ? Service_Camera.GetCam_Name(i) : $"({i}) 未配置",
                    Value = i,
                });
            }
            return options;
        }

        /// <summary>按配方里出现过的最大相机号刷新"所属相机"两列。</summary>
        private static void 刷新相机下拉(DataGridView grid, int 配方最大相机号)
        {
            if (grid == null) return;

            List<相机选项项> options = 相机选项(配方最大相机号);
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.Name != "CameraId1" && column.Name != "CameraId2") continue;

                DataGridViewComboBoxColumn combo = column as DataGridViewComboBoxColumn;
                if (combo != null) combo.DataSource = options;
            }
        }

        /// <summary>
        /// 表格数据不匹配时只记日志，不弹框。
        /// DataGridView 默认遇到非法值会抛异常并弹模态框，现场一旦弹出来就拦住操作，
        /// 而这类问题（配方引用了不存在的相机等）本该由人看表格发现，不该打断流程。
        /// </summary>
        private void 绑定表格容错()
        {
            绑定表格容错(dataGridView_Size);
            绑定表格容错(dataGridView_SizeStation3);
            绑定表格容错(dataGridView_GlobalVariable);
        }

        private void 绑定表格容错(DataGridView grid)
        {
            if (grid == null) return;

            grid.DataError -= 表格_DataError;
            grid.DataError += 表格_DataError;
        }

        private void 表格_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;

            DataGridView grid = sender as DataGridView;
            string columnName = grid != null && e.ColumnIndex >= 0 && e.ColumnIndex < grid.Columns.Count
                ? grid.Columns[e.ColumnIndex].HeaderText
                : e.ColumnIndex.ToString();

            // 滚动/重绘会重复触发，所以这里只记录不计数
            Log4netRecord.ErrorLog.Error($"参数表单元格值无效：第{e.RowIndex + 1}行「{columnName}」列，{e.Exception?.Message}");
        }

        #endregion

        #region 全局变量
        private void InitTableColumns_GlobalVariableitem()
        {
            dataGridView_GlobalVariable.Columns.Clear();
            dataGridView_GlobalVariable.AutoGenerateColumns = false;

            dataGridView_GlobalVariable.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
            dataGridView_GlobalVariable.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(31, 35, 41);
            dataGridView_GlobalVariable.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridView_GlobalVariable.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 244, 255);
            dataGridView_GlobalVariable.DefaultCellStyle.SelectionForeColor = Color.FromArgb(22, 119, 255);
            dataGridView_GlobalVariable.DefaultCellStyle.Font = Font;

            dataGridView_GlobalVariable.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ID",
                DataPropertyName = "ID",
                HeaderText = "ID",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ReadOnly = true,
            });

            dataGridView_GlobalVariable.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                DataPropertyName = "Name",
                HeaderText = "名称",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ReadOnly = true,
            });

            dataGridView_GlobalVariable.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Describe",
                DataPropertyName = "Describe",
                HeaderText = "描述",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });



            dataGridView_GlobalVariable.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "DataType",
                DataPropertyName = "DataType",
                HeaderText = "数据类型",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                DataSource = Enum.GetValues(typeof(DataTypeEnum)).Cast<DataTypeEnum>()
                                                                    .Select(e => new
                                                                    {
                                                                        Text = e.ToString(),
                                                                        Value = (int)e
                                                                    }).ToList(),

                DisplayMember = "Text",  // 界面显示枚举文字
                ValueMember = "Value",   // 绑定后台int数值，和DataTable匹配
                ReadOnly = true,
            });

            dataGridView_GlobalVariable.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Value",
                DataPropertyName = "Value",
                HeaderText = "数值",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });

            // `带AT` 改动后立即刷新 AT 参数行可见性，保存前就能看到效果
            dataGridView_GlobalVariable.CellValueChanged -= GlobalVariable_CellValueChanged;
            dataGridView_GlobalVariable.CellValueChanged += GlobalVariable_CellValueChanged;
            // 数据源刚赋值时 Rows 还没生成，必须等绑定完成再设行可见性
            dataGridView_GlobalVariable.DataBindingComplete -= GlobalVariable_DataBindingComplete;
            dataGridView_GlobalVariable.DataBindingComplete += GlobalVariable_DataBindingComplete;
            // 布尔值只允许 1（启用）/0（禁用），不再接受 true/false
            dataGridView_GlobalVariable.CellValidating -= GlobalVariable_CellValidating;
            dataGridView_GlobalVariable.CellValidating += GlobalVariable_CellValidating;
        }
        private DataTable BuildTable_GlobalVariableitem(List<GlobalVariableItem> l_GlobalVariable)
        {

            DataTable table = new DataTable();
            table.Columns.Add("ID");
            table.Columns.Add("Name");
            table.Columns.Add("Describe");
            table.Columns.Add("DataType", typeof(int));
            table.Columns.Add("Value");

            foreach (var row in l_GlobalVariable)
            {
                table.Rows.Add(
                    row.ID,
                    row.Name,
                    row.Describe,
                    row.DataType,
                    row.Value);
            }

            return table;
        }
        private void GetBuildTable_GlobalVariableitem(List<GlobalVariableItem> l_GlobalVariable)
        {
            try
            {
                for (int i = 0; i < dataGridView_GlobalVariable.Rows.Count; i++)
                {
                    GlobalVariableItem globalVariableItem = new GlobalVariableItem();

                    l_GlobalVariable[i].ID = Convert.ToInt32(dataGridView_GlobalVariable.Rows[i].Cells[0].Value);
                    l_GlobalVariable[i].Name = Convert.ToString(dataGridView_GlobalVariable.Rows[i].Cells[1].Value);
                    l_GlobalVariable[i].Describe = Convert.ToString(dataGridView_GlobalVariable.Rows[i].Cells[2].Value);

                    //校验数据类型是否和选择数据类型一致
                    globalVariableItem.DataType = (DataTypeEnum)dataGridView_GlobalVariable.Rows[i].Cells[3].Value;
                    globalVariableItem.Value = Convert.ToString(dataGridView_GlobalVariable.Rows[i].Cells[4].Value);
                    if (CheckInputData.CheckInputDataType(globalVariableItem.Value, globalVariableItem.DataType))
                    {
                        l_GlobalVariable[i].DataType = globalVariableItem.DataType;
                        l_GlobalVariable[i].Value = globalVariableItem.Value;
                    }
                    else
                    {
                        string showMessage = $"行【{l_GlobalVariable[i].ID}】\r\n数据类型和数值格式不匹配";
                        MessageBox.Show(showMessage, "取消保存");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }
        private void UpdateTable_GlobalVariableitem(List<GlobalVariableItem> l_GlobalVariable)
        {
            if (l_GlobalVariable != null)
            {
                dataGridView_GlobalVariable.DataSource = BuildTable_GlobalVariableitem(l_GlobalVariable);
                ApplyATParameterVisibility();
            }
        }

        /// <summary>配方参数里随 `带AT` 隐藏的 AT 相关变量（不含开关本身）。</summary>
        private static readonly HashSet<string> AT参数名 = new HashSet<string>(StringComparer.Ordinal)
        {
            "AtLftCol1", "AtLftCol2", "AtTopRow1", "AtBotRow2", "AtWidth",
        };

        /// <summary>
        /// 按 `带AT` 控制 AT 参数行的显示。只隐藏、不删数据：
        /// 保存按行索引写回（GetBuildTable_GlobalVariableitem），过滤数据源会让后面的值写到错误的变量上。
        /// </summary>
        private void ApplyATParameterVisibility(bool? isAT = null)
        {
            bool showAT = isAT ?? (VM.M_CraftParam != null && VM.M_CraftParam.IsAT);
            foreach (DataGridViewRow row in dataGridView_GlobalVariable.Rows)
            {
                string name = Convert.ToString(row.Cells[1].Value);
                if (!AT参数名.Contains(name)) continue;
                row.Visible = showAT;
            }
        }

        /// <summary>数据源绑定完成后应用行可见性（绑定前 Rows 为空）。</summary>
        private void GlobalVariable_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            ApplyATParameterVisibility();
        }

        /// <summary>`带AT` 单元格改动时即时切换，保存前也能看到隐藏效果。</summary>
        private void GlobalVariable_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            DataGridViewRow row = dataGridView_GlobalVariable.Rows[e.RowIndex];
            if (Convert.ToString(row.Cells[1].Value) != "带AT") return;

            bool isAT = Convert.ToString(row.Cells["Value"].Value).ToTryBool(false);
            ApplyATParameterVisibility(isAT);
        }

        /// <summary>布尔全局变量只允许 1（启用）或 0（禁用），阻止 true/false。</summary>
        private void GlobalVariable_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex != dataGridView_GlobalVariable.Columns["Value"].Index) return;

            DataGridViewRow row = dataGridView_GlobalVariable.Rows[e.RowIndex];
            object type = row.Cells["DataType"].Value;
            if (type == null || Convert.ToInt32(type) != (int)DataTypeEnum.Bool) return;

            string text = Convert.ToString(e.FormattedValue);
            if (text == "0" || text == "1")
            {
                row.ErrorText = string.Empty;
                return;
            }

            row.ErrorText = "布尔值只能填 1（启用）或 0（禁用）";
            e.Cancel = true;
        }
        #endregion

        #region 参数显示与获取
        private void CraftItem_SelectIndexChanged(object sender, IntEventArgs e)
        {
            ParamAndView_Select(true);
        }
        /// <summary>
        /// 页签 → 数据块。页签顺序是「每路相机 + 缺陷工位 + 极片检测工位 + 全局变量」，
        /// 工位下标由相机数量推导，而不是写死 6/7/8——激光 6 路与五金 5 路共用这一套。
        /// </summary>
        private void ParamAndView_Select(bool showOrSave)
        {
            if (VM.M_CraftParam == null) return;

            int index = CraftItem.SelectIndex;
            int camCount = VM.M_CraftParam.Crafts.Count;
            int indexStation1 = camCount;      // 缺陷检测工位
            int indexStation3 = camCount + 1;  // 极片检测工位
            if (index < 0) return;

            tabPage1.Visible = false;
            tabPage2.Visible = false;
            tabPage3.Visible = false;
            tabPage4.Visible = false;

            // 每路相机：只显示该相机的全局变量表
            if (index < camCount)
            {
                CamCraft craft = VM.M_CraftParam.Crafts[index];
                tabPage4.Visible = true;

                if (showOrSave)
                {
                    currentCamCraft = craft;
                    UpdateTable_GlobalVariableitem(craft.M_GlobalVariable.l_GlobalVariableItems);
                    tab_CraftTitle.SelectTab(tabPage4);
                }
                else
                {
                    GetBuildTable_GlobalVariableitem(craft.M_GlobalVariable.l_GlobalVariableItems);
                }
                return;
            }

            if (index == indexStation1)
            {
                tabPage3.Visible = true;

                if (showOrSave)
                {
                    UpdateTable_DefectCrafts(dataGridView_Defect, VM.M_CraftParam.l_DefectCraftStation_1);
                    tab_CraftTitle.SelectTab(tabPage3);
                }
                else
                {
                    GetBuildTable_DefectCraft(dataGridView_Defect, VM.M_CraftParam.l_DefectCraftStation_1);
                }
                return;
            }

            if (index == indexStation3)
            {
                tabPage2.Visible = true;
                tabPage3.Visible = true;

                if (showOrSave)
                {
                    UpdateTable_SizeCraftStation3(VM.M_CraftParam.l_SizeCraftStation_3);
                    UpdateTable_DefectCrafts(dataGridView_Defect, VM.M_CraftParam.l_DefectCraftStation_3);
                    tab_CraftTitle.SelectTab(tabPage2);
                }
                else
                {
                    GetBuildTable_SizeCraftStation3(VM.M_CraftParam.l_SizeCraftStation_3);
                    GetBuildTable_DefectCraft(dataGridView_Defect, VM.M_CraftParam.l_DefectCraftStation_3);
                }
                return;
            }

            // 机台全局变量
            tabPage4.Visible = true;
            if (showOrSave)
            {
                tab_CraftTitle.SelectTab(tabPage4);
                UpdateTable_GlobalVariableitem(VM.M_CraftParam.M_GlobalVariable.l_GlobalVariableItems);
            }
            else
            {
                GetBuildTable_GlobalVariableitem(VM.M_CraftParam.M_GlobalVariable.l_GlobalVariableItems);
            }
        }
        #endregion

    }
}
