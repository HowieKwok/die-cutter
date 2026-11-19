using Frame_Core;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Mef_VisionFrame.调试设置.WPF
{
    internal sealed class PluginDialogService
    {

        public void Open(IPlugin plugin, Window owner)
        {
            if (plugin == null) return;

            Type wpfContentType = ResolveWpfContentType(plugin);
            Type formType = PluginService.pluginsDictionary.ContainsKey(plugin.moduleInfo.PluginName)
                ? PluginService.pluginsDictionary[plugin.moduleInfo.PluginName].PluginsFormType
                : null;

            if (wpfContentType != null)
            {
                OpenWpfPlugin(plugin, wpfContentType, owner);
                return;
            }

            if (formType == null)
            {
                throw new InvalidOperationException(
                    "插件 " + plugin.moduleInfo.PluginName + " 既没有 WPF 视图，也没有登记编辑器类型，无法打开。");
            }

            OpenWinFormsPlugin(plugin, formType);
        }

        private void OpenWpfPlugin(IPlugin plugin, Type wpfContentType, Window owner)
        {
            WPFFormPluginBase window = new WPFFormPluginBase();
            object content = Activator.CreateInstance(wpfContentType);
            TryBindPlugin(content, plugin);

            window.pluginsObjBase = plugin;
            window.PluginContent = content;
            window.Init();
            window.Owner = owner;
            window.ShowDialog();
        }

        private void OpenWinFormsPlugin(IPlugin plugin, Type formType)
        {
            // 识别与创建收敛在此处：Open 只做路由，不再重复判定基类。
            // 失败必须是可见的：静默 return 在现场表现为“点了没反应”，无法定位到具体插件。
            if (!typeof(IPluginEditorView).IsAssignableFrom(formType))
            {
                throw new InvalidOperationException(
                    "插件 " + plugin.moduleInfo.PluginName + " 的编辑器 " + formType.FullName +
                    " 未实现 IPluginEditorView。");
            }

            var form = Activator.CreateInstance(formType) as FormPluginBase;
            if (form == null)
            {
                throw new InvalidOperationException(
                    "插件 " + plugin.moduleInfo.PluginName + " 的编辑器 " + formType.FullName +
                    " 不是 FormPluginBase，当前宿主只能承载 WinForm 插件编辑器。");
            }

            try
            {
                form.pluginsObjBase = plugin;
                form.Init();
                form.ShowDialog();
            }
            finally
            {
                form.Dispose();
            }
        }


        private Type ResolveWpfContentType(IPlugin plugin)
        {
            Assembly assembly = plugin.GetType().Assembly;
            return assembly.GetTypes()
                .FirstOrDefault(t => !t.IsAbstract
                    && typeof(UserControl).IsAssignableFrom(t)
                    && t.GetMethod("BindPlugin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null);
        }

        private void TryBindPlugin(object target, IPlugin plugin)
        {
            if (target == null) return;

            MethodInfo method = target.GetType().GetMethod("BindPlugin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
            {
                method.Invoke(target, new object[] { plugin });
                return;
            }

            PropertyInfo property = target.GetType().GetProperty("pluginsObjBase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite) property.SetValue(target, plugin, null);
        }
    }
}
