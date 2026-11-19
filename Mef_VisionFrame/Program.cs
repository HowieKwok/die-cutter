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

                // AntdUI 自绘控件不吃 BackColor/ForeColor，必须走它自己的全局开关。
                // 工艺视图链（FormCraftBase : AntdUI.Window）依赖这一步才会跟着变深色。
                Frame_UI.WinForms.AntdUiThemeAdapter.Apply(Frame_UI.ThemeService.Current);

                Application.Run(new Form_Main());
            }
         
        }
    }
}
