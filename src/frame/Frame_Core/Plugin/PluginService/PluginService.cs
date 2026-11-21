using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Frame_Core
{


    public static class PluginService
    {

        //些特性表示加载实现了Iplugin接口的类
        //[ImportMany(/*"MeasLineObj",*/ typeof(IPlugin))]
        //private IEnumerable<IPlugin> Plugins1;

        private  static string PlugInsPath = Path.Combine(System.Environment.CurrentDirectory, "Plugins\\");
        private  static CompositionContainer container;
        public   static Dictionary<string, PluginsInfo> pluginsDictionary = new Dictionary<string, PluginsInfo>();
       // public List<IPlugin> list = new List<IPlugin>();

        public static T GetInstance<T>(string contractName = null)
        {
            var type = default(T);
            try
            {
                
                if (container == null) return type;
                if (!string.IsNullOrWhiteSpace(contractName))
                    type = container.GetExportedValue<T>(contractName);
                else
                    type = container.GetExportedValue<T>();
                return type;
            }
            catch (Exception ee)
            {
                return type;
                //throw;
            }
 
        }
        public static void BuildContainer()
        {       
            if (Directory.Exists(PlugInsPath) == false) return;
            // DirectoryCatalog catalog = new DirectoryCatalog(PlugInsPath);
            DirectoryCatalog catalog = new DirectoryCatalog(PlugInsPath, "Plugin.*.dll");
            container = new CompositionContainer(catalog);
           // container.SatisfyImportsOnce(this);
            container.ComposeParts();                           
           #region 三种获取程序集容器方法，选用容器法
                    ////查找程序集（需要设定本程序集内，暂时没研究太多）
                    //AssemblyCatalog catalog0 = new AssemblyCatalog(typeof(Program).Assembly);
                    ////查找目录,将插件DLL按规定格式命名，放置在指定位置。
                    ////设置插件目录为程序目录,插件名称为Plugin.*.dll
                    //DirectoryCatalog catalog1 = new DirectoryCatalog(AppDomain.CurrentDomain.BaseDirectory + "plugin\\", "Plugin.*.dll");
                    ////获取指定路径DLL
                    //AssemblyCatalog catalog2 = new AssemblyCatalog(Assembly.LoadFile(AppDomain.CurrentDomain.BaseDirectory + "Plugin.MeasureLine.dll"));
                    ////合并三种目录
                    //AggregateCatalog catalogAll = new AggregateCatalog(catalog0, catalog1, catalog2);

                    //container = new CompositionContainer(catalog1);

                    //container.SatisfyImportsOnce(this);
                    //container.ComposeParts();
                    #endregion          
        }
        /// <summary>
        /// 获取所有插件类别。
        /// </summary>
        /// <returns></returns>
        public static void InitializPlugin()
        {          
            if (Directory.Exists(PlugInsPath) == false) return;
            foreach (var file in Directory.GetFiles(PlugInsPath))
            {
                FileInfo fileInfo = new FileInfo(file);
                if (!fileInfo.Name.StartsWith("Plugin.") || !fileInfo.Name.EndsWith(".dll")) continue;
                Assembly assembly = AppDomain.CurrentDomain.Load(LoadPluginAssembly(fileInfo.FullName).GetName());
                PluginsInfo pluginsInformation = new PluginsInfo();
                foreach (Type plugInsObjBase in assembly.GetTypes())
                {                  
                    //判断是否是IPlugin的子类
                    if (typeof(IPlugin).IsAssignableFrom(plugInsObjBase))
                    {
                        object[] categoryObjs = plugInsObjBase.GetCustomAttributes(typeof(CategoryAttribute), true);
                        object[] dispNameObjs = plugInsObjBase.GetCustomAttributes(typeof(DisplayNameAttribute), true);
                        pluginsInformation.PluginsCategory = (((CategoryAttribute)categoryObjs[0]).Category);
                        pluginsInformation.PluginsDisplayName = (((DisplayNameAttribute)dispNameObjs[0]).DisplayName);

                        pluginsInformation.PluginsObjType = plugInsObjBase;
                        pluginsInformation.PluginsBuildName = plugInsObjBase.Name;
                       
                    }
                    // 判断是否是插件编辑器视图。
                    // 用 UI 无关的标记接口识别：具体窗体基类已上移到应用层，
                    // Frame_Core 不能再引用它（会被分层门禁拒绝）。
                    if (typeof(IPluginEditorView).IsAssignableFrom(plugInsObjBase))
                    {
                        pluginsInformation.PluginsFormType = plugInsObjBase;
                    }
                    //判断是否是IPluginParams的子类
                    if (typeof(PluginParamsBase).IsAssignableFrom(plugInsObjBase))
                    {
                        pluginsInformation.PluginsParamType = plugInsObjBase;
                    }
                }
                pluginsDictionary[pluginsInformation.PluginsDisplayName] = pluginsInformation;
            }
        }

        /// <summary>
        /// 加载插件程序集。优先 LoadFile：它与其他加载上下文隔离，插件之间不会互相污染。
        ///
        /// 但仓库所在目录一旦被加密终端托管，.NET Framework 会把该目录下的文件判定成
        /// "网络位置"，LoadFile 抛 NotSupportedException、LoadFrom 抛 FileLoadException
        /// （.NET 4 起 CAS 已停用，app.config 的 loadFromRemoteSources 对此实测无效）。
        /// 这两种情况退回 UnsafeLoadFrom：它跳过来源检查，正是为"位置不可信、
        /// 内容可信"准备的——这些插件都是本项目自己编出来的，符合该前提。
        /// </summary>
        private static Assembly LoadPluginAssembly(string path)
        {
            try
            {
                return Assembly.LoadFile(path);
            }
            catch (NotSupportedException)
            {
                // 目录被判定为网络位置，落到下面的 UnsafeLoadFrom
            }
            catch (FileLoadException)
            {
                // LoadFrom 上下文下的同源问题
            }
            return Assembly.UnsafeLoadFrom(path);
        }
        //public void CreateAllPlugins()
        //{
        //    try
        //    {
        //        BuildContainer();
        //        //查找程序集（需要设定本程序集内，暂时没研究太多）
        //        AssemblyCatalog catalog0 = new AssemblyCatalog(typeof(Program).Assembly);
        //        //查找目录,将插件DLL按规定格式命名，放置在指定位置。
        //        //设置插件目录为程序目录,插件名称为Plugin.*.dll
        //        DirectoryCatalog str0 = new DirectoryCatalog(AppDomain.CurrentDomain.BaseDirectory + "plugin\\", "Plugin.*.dll");
        //        //获取指定路径DLL
        //        AssemblyCatalog catalog = new AssemblyCatalog(Assembly.LoadFile(AppDomain.CurrentDomain.BaseDirectory + "Plugin.MeasureLine.dll"));
        //        AggregateCatalog catalogall = new AggregateCatalog(catalog0, str0, catalog);

        //        container = new CompositionContainer(str0);
        //        container.SatisfyImportsOnce(this);
        //        container.ComposeParts();


        //        //  string s = container.Providers;
        //        IPlugin Plugin1 = container.GetExportedValue<IPlugin>("MeasLineObj");
        //        IPlugin Plugin2 = container.GetExportedValueOrDefault<IPlugin>();
        //        IPlugin Plugin3 = PluginService.GetInstance<IPlugin>("MeasEarObj11");
        //        IEnumerable<IPlugin> Plugins4 = container.GetExportedValues<IPlugin>();
               

        //    }
        //    catch (Exception ex)
        //    {
        //        // Log.File(ex, "加载插件异常");
        //    }
        //}
    }
}

