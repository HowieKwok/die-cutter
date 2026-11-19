using Frame_Core.toosls;
using Frame_UI.WinForms;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Frame_Core
{
    /// <summary>
    /// WinForm 插件编辑器基类（归属 Frame_PluginHost 程序集）。
    ///
    /// 这里刻意继承 System.Windows.Forms.Form，而不是继承某个 UI 适配器窗体：
    /// C# 要求解析整条基类链，若基类落在 Frame_UI.WinForms，
    /// 19 个插件的编译期就必须引用该程序集，插件的编译面被迫暴露 UI 适配器。
    /// 改为组合后，插件只需引用 Frame_PluginHost。
    ///
    /// 外壳（深色标题栏、动作条、状态区）由 WinFormsShell 在构造期装配进来，
    /// 对子类是透明的：子类 Designer 里看不到它，也不需要知道它。
    /// </summary>
    public class FormPluginBase : Form, IPluginEditorView
    {
        // private 字段：类型不出现在任何公开签名里，插件编译期无需解析 Frame_UI.WinForms。
        private readonly WinFormsShell shell;
        private bool executeCompletedSubscribed;

        public LanguageEnum FormLanguage;
        public IPlugin pluginsObjBase { get; set; }
        public PluginParamsBase pluginsObjParams_Back { get; set; }

        public FormPluginBase()
        {
            // DoubleBuffered 是 Control 的 protected 属性，只能由派生类设置，装配器无权访问。
            DoubleBuffered = true;

            shell = WinFormsShell.Attach(this, ShellMode.Standalone);
            shell.RunRequested += delegate { Btn_RUN_Click(this, EventArgs.Empty); };
            shell.SaveRequested += delegate { Btn_Save_Click(this, EventArgs.Empty); };
            shell.CloseRequested += delegate { Close(); };
        }

        public virtual void Init()
        {
            if (pluginsObjBase == null || pluginsObjBase.moduleInfo == null) return;

            Text = pluginsObjBase.moduleInfo.ModuleName;
            SubscribeExecuteCompleted();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            shell.ApplyTheme();

            if (pluginsObjBase == null) return;
            Init();
            LanguageProject.ChangeLanguage(FormLanguage, this);
        }

        public virtual void Btn_RUN_Click(object sender, EventArgs e)
        {
            if (pluginsObjBase == null)
            {
                shell.SetCostTime("耗时：000ms");
                return;
            }

            var watch = Stopwatch.StartNew();
            try
            {
                bool success = pluginsObjBase.ExecuteObj();
                watch.Stop();

                long elapsedMs = watch.ElapsedMilliseconds;
                shell.SetCostTime("耗时：" + elapsedMs.ToString("0.0") + "ms");

                if (pluginsObjBase.PluginParams != null)
                {
                    pluginsObjBase.PluginParams.m_bRunSuccess = success;
                    pluginsObjBase.PluginParams.m_fElapsedMs = elapsedMs;
                    if (string.IsNullOrWhiteSpace(pluginsObjBase.PluginParams.m_strRunMessage))
                    {
                        pluginsObjBase.PluginParams.m_strRunMessage = success ? "执行完成" : "执行失败";
                    }
                }
            }
            catch (Exception ex)
            {
                if (watch.IsRunning) watch.Stop();
                shell.SetCostTime("耗时：" + watch.ElapsedMilliseconds.ToString("0.0") + "ms");

                if (pluginsObjBase.PluginParams != null)
                {
                    pluginsObjBase.PluginParams.m_bRunSuccess = false;
                    pluginsObjBase.PluginParams.m_fElapsedMs = watch.ElapsedMilliseconds;
                    pluginsObjBase.PluginParams.m_strRunMessage = "执行异常：" + ex.Message;
                }
            }
        }

        public virtual void Btn_Save_Click(object sender, EventArgs e)
        {
            if (pluginsObjBase == null || pluginsObjBase.moduleInfo == null) return;

            pluginsObjParams_Back = pluginsObjBase.PluginParams;
            string processPath = Path.Combine(Project.Instance.paramPathSet.ProcessPath, pluginsObjBase.moduleInfo.ProcessName);
            pluginsObjBase.moduleInfo.filePath = Path.Combine(processPath, pluginsObjBase.moduleInfo.ModuleName);
            if (pluginsObjBase.PluginParams != null)
            {
                pluginsObjBase.PluginParams.filePath = pluginsObjBase.moduleInfo.filePath;
            }
            pluginsObjBase.saveParams();
        }

        protected virtual void OnPluginExecutedFromOutside(PluginExecuteCompletedEventArgs e)
        {
        }

        private void SubscribeExecuteCompleted()
        {
            if (pluginsObjBase == null || executeCompletedSubscribed) return;

            pluginsObjBase.ExecuteCompleted += OnPluginExecuteCompleted;
            executeCompletedSubscribed = true;
        }

        private void OnPluginExecuteCompleted(object sender, PluginExecuteCompletedEventArgs e)
        {
            if (!ReferenceEquals(sender, pluginsObjBase)) return;

            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(new Action(delegate { OnPluginExecuteCompleted(sender, e); }));
                return;
            }

            shell.SetCostTime("耗时：" + e.ElapsedMs.ToString("0.0") + "ms");
            OnPluginExecutedFromOutside(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (pluginsObjBase != null && executeCompletedSubscribed)
            {
                try
                {
                    pluginsObjBase.ExecuteCompleted -= OnPluginExecuteCompleted;
                }
                catch
                {
                }
                executeCompletedSubscribed = false;
            }

            base.OnFormClosed(e);
        }
    }
}