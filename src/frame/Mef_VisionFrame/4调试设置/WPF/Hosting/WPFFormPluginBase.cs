using Frame_UI.Wpf;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Frame_Core
{
    /// <summary>
    /// WPF 插件编辑器基类。
    ///
    /// 位置说明：本文件物理上属于 Mef_VisionFrame（应用层），命名空间保留 Frame_Core
    /// 以兼容既有调用点。不能留在 Frame_Core 程序集里：它需要继承
    /// Frame_UI.Wpf.WpfPluginViewHost，而 Frame_Core 被分层门禁禁止引用 UI 层。
    ///
    /// 职责边界：外壳（窗口边框、标题栏、动作条、状态区）全部由 WpfPluginViewHost 提供，
    /// 本类只做“按钮 → 插件业务”的转发与生命周期解绑。
    /// </summary>
    public class WPFFormPluginBase : WpfPluginViewHost
    {
        private readonly PluginViewSlot viewSlot = new PluginViewSlot();
        private bool beforeCloseCalled;
        private bool executeCompletedSubscribed;

        public LanguageEnum FormLanguage;
        public IPlugin pluginsObjBase { get; set; }
        public PluginParamsBase pluginsObjParams_Back { get; set; }

        public WPFFormPluginBase()
        {
            RunRequested += delegate { Btn_RUN_Click(this, EventArgs.Empty); };
            SaveRequested += delegate { Btn_Save_Click(this, EventArgs.Empty); };
            CloseRequested += delegate { Btn_Close_Click(this, EventArgs.Empty); };
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        /// <summary>插件页面内容。宿主只把它当作 ContentControl 的内容，不做类型判断。</summary>
        public object PluginContent
        {
            get { return viewSlot.Content; }
            set
            {
                viewSlot.Attach(value);
                SetContent(value);
            }
        }

        private IPluginWpfView PluginView
        {
            get { return viewSlot.Content as IPluginWpfView; }
        }

        public virtual void Init()
        {
            if (pluginsObjBase == null || pluginsObjBase.moduleInfo == null) return;

            Title = pluginsObjBase.moduleInfo.ModuleName;
            SetStatus("模块：" + pluginsObjBase.moduleInfo.ModuleName);
            SubscribeExecuteCompleted();
        }

        public virtual void Btn_RUN_Click(object sender, EventArgs e)
        {
            IPluginWpfView view = PluginView;
            if (view == null)
            {
                ApplyActionResult(PluginViewActionResult.Fail("执行失败：插件页面未实现 WPF 接口"), "执行失败");
                return;
            }

            try
            {
                pluginsObjBase.ExecuteObj();

                var result = new PluginViewActionResult();
                result.Success = pluginsObjBase.PluginParams.m_bRunSuccess;
                result.Message = pluginsObjBase.PluginParams.m_strRunMessage;
                result.ElapsedMs = (long)pluginsObjBase.PluginParams.m_fElapsedMs;
                ApplyActionResult(result, result.Success ? "执行完成" : "执行失败");
            }
            catch (Exception ex)
            {
                ApplyActionResult(PluginViewActionResult.Fail("执行异常：" + ex.Message), "执行异常");
            }
        }

        public virtual void Btn_Save_Click(object sender, EventArgs e)
        {
            if (pluginsObjBase == null || pluginsObjBase.moduleInfo == null)
            {
                SetStatus("保存失败：插件对象为空");
                return;
            }

            IPluginWpfView view = PluginView;
            if (view == null)
            {
                SetStatus("保存失败：插件页面未实现 WPF 接口");
                return;
            }

            try
            {
                string filePath = PreparePluginSavePath();
                PluginViewActionResult result = view.SaveFromHost(filePath);
                ApplyActionResult(result, result != null && result.Success ? "参数已保存" : "参数保存失败");
            }
            catch (Exception ex)
            {
                SetStatus("保存异常：" + ex.Message);
            }
        }

        public virtual void Btn_Close_Click(object sender, EventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!beforeCloseCalled)
            {
                beforeCloseCalled = true;
                try
                {
                    IPluginWpfView view = PluginView;
                    if (view != null) view.CloseFromHost();
                }
                catch (Exception ex)
                {
                    SetStatus("关闭异常：" + ex.Message);
                }
            }

            // 摘掉 WindowsFormsHost 是真实需求（释放 Halcon 等 WinForm 控件的句柄），
            // 但不再强制 GC：靠正确解绑即可，强制回收只会带来停顿且掩盖真实泄漏。
            try
            {
                DetachWindowsFormsHosts(this);
            }
            catch
            {
            }

            // 摘除与释放只能走 viewSlot.Release()，避免“已清空但未释放”。
            viewSlot.Release();
            SetContent(null);
            base.OnClosing(e);
        }

        private static void DetachWindowsFormsHosts(DependencyObject current)
        {
            if (current == null) return;

            var host = current as System.Windows.Forms.Integration.WindowsFormsHost;
            if (host != null)
            {
                host.Child = null;
                host.Dispose();
                return;
            }

            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                DetachWindowsFormsHosts(VisualTreeHelper.GetChild(current, i));
            }
        }

        private string PreparePluginSavePath()
        {
            pluginsObjParams_Back = pluginsObjBase.PluginParams;
            string processPath = Path.Combine(Project.Instance.paramPathSet.ProcessPath, pluginsObjBase.moduleInfo.ProcessName);
            pluginsObjBase.moduleInfo.filePath = Path.Combine(processPath, pluginsObjBase.moduleInfo.ModuleName);
            if (pluginsObjBase.PluginParams != null)
            {
                pluginsObjBase.PluginParams.filePath = pluginsObjBase.moduleInfo.filePath;
            }
            return pluginsObjBase.moduleInfo.filePath;
        }

        private void ApplyActionResult(PluginViewActionResult result, string defaultMessage)
        {
            if (result == null)
            {
                SetCostTime("耗时：00ms");
                SetStatus(defaultMessage);
                return;
            }

            SetCostTime("耗时：" + result.ElapsedMs.ToString("0.0") + "ms");
            SetStatus(string.IsNullOrWhiteSpace(result.Message) ? defaultMessage : result.Message);
        }

        protected virtual void OnLoaded(object sender, RoutedEventArgs e)
        {
            Init();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            UnsubscribeExecuteCompleted();

            // Release 幂等：OnClosing 已释放过时本次为空操作。
            viewSlot.Release();
            SetContent(null);
        }

        private void SubscribeExecuteCompleted()
        {
            if (pluginsObjBase == null || executeCompletedSubscribed) return;

            pluginsObjBase.ExecuteCompleted += OnPluginExecuteCompleted;
            executeCompletedSubscribed = true;
        }

        private void UnsubscribeExecuteCompleted()
        {
            if (pluginsObjBase == null || !executeCompletedSubscribed) return;

            try
            {
                pluginsObjBase.ExecuteCompleted -= OnPluginExecuteCompleted;
            }
            catch
            {
            }

            executeCompletedSubscribed = false;
        }

        private void OnPluginExecuteCompleted(object sender, PluginExecuteCompletedEventArgs e)
        {
            if (!ReferenceEquals(sender, pluginsObjBase)) return;

            Dispatcher.BeginInvoke(new Action(delegate
            {
                SetCostTime("耗时：" + e.ElapsedMs.ToString("0.0") + "ms");
                SetStatus(string.IsNullOrWhiteSpace(e.Message)
                    ? (e.Success ? "执行完成" : "执行失败")
                    : e.Message);

                IPluginWpfView view = PluginView;
                if (view != null)
                {
                    view.OnPluginExecutedFromOutside(e);
                }
            }));
        }
    }
}