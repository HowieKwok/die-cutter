using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Frame_Core;

namespace Mef_VisionFrame
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {          
            if (System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName).Length > 1)
            {
                System.Windows.Forms.MessageBox.Show("已有另一個程序在运行！", "提示信息");
                Application.Exit();
            }
            else
            {
                // 启动自检：配置缺失不阻断启动（各参数会退化到默认值），
                // 但必须把缺失项告诉操作员，否则会出现"能启动、但什么都不工作"。
                try
                {
                    List<string> problems = Frame_Core.StartupPreflight.Validate();
                    if (problems.Count > 0)
                    {
                        string text = "以下配置缺失，程序将以默认值继续启动："
                            + Environment.NewLine + Environment.NewLine
                            + string.Join(Environment.NewLine, problems.ToArray());
                        MessageBox.Show(text, "配置不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("启动自检异常（不影响启动）：" + ex.Message, "配置检查",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                //注入并激活HSL
                bool result = HookHSL.HSLAuthorization.SetAuthorizationCode();
                if (!result)
                    MessageBox.Show("通讯模块加载失败", "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);

                //加载插件        
                // pluginservice.CreateAllPlugins();
                PluginService.InitializPlugin();
                PluginService.BuildContainer();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // WinForm 统一样式的唯一接线点：
                // Frame_Core 只暴露 Action<Form>，不引用 UI 层；样式实现留在 Frame_UI.WinForms。
                // 未接线前所有窗体按各自 Designer 渲染，接线后全局一致。
                Frame_Core.FormStylingHook.OnFormCreated = Frame_UI.WinForms.WinFormsThemeApplier.Apply;

                // WPF 同理：Frame_Core 只暴露 Action<Window>，实现留在 Frame_UI.Wpf。
                Frame_Core.WpfStylingHook.OnWindowCreated = Frame_UI.Wpf.ThemeScope.Ensure;

                // AntdUI 自绘控件不吃 BackColor/ForeColor，必须走它自己的全局开关。
                // 工艺视图链（FormCraftBase : AntdUI.Window）依赖这一步才会跟着变深色。
                Frame_UI.WinForms.AntdUiThemeAdapter.Apply(Frame_UI.ThemeService.Current);

                // 主题切换（深/浅）：启用运行期切换，切换时重刷 AntdUI 全局方案
                // 与所有已打开的 WinForm 窗体；WPF 侧由 ThemeScope 订阅同一事件。
                // 已知边界：个别把画刷固化进字段的视图（如调试卡片）需重新打开才能完全跟随。
                Frame_UI.ThemeService.EnableRuntimeSwitch();
                Frame_UI.ThemeService.ThemeChanged += delegate
                {
                    Frame_UI.WinForms.AntdUiThemeAdapter.Apply(Frame_UI.ThemeService.Current);
                    foreach (System.Windows.Forms.Form openForm in System.Windows.Forms.Application.OpenForms)
                    {
                        Frame_UI.WinForms.WinFormsThemeApplier.Apply(openForm);
                    }
                };

                Application.Run(new Form_Main());
            }
         
        }
    }
}
