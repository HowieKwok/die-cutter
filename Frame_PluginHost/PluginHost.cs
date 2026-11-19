using System;

namespace Frame_Core
{
    /// <summary>
    /// 插件允许访问的宿主能力。
    ///
    /// 之所以单独成契约，而不是让插件直接抓 Project.Instance：
    /// 插件一旦直接引用 Project，就把整个 Frame_Core 拉进自己的编译面与输出目录，
    /// 于是 Frame_Core.dll 随插件发布、类型身份分裂。
    /// 把插件真正需要的 4 个成员收敛成契约后，宿主实现可以整体替换，
    /// 而插件的编译面不再包含业务核心。
    ///
    /// 契约只描述"要什么能力"，不描述"能力从哪来"。
    /// </summary>
    public interface IPluginHostContext
    {
        /// <summary>按流程 id 取流程。moduleInfo.ProcessID 为 int。</summary>
        Process GetProcessById(int processId);

        /// <summary>按流程名取流程。找不到时返回 null，不抛异常。</summary>
        Process GetProcessByName(string processName);

        /// <summary>流程参数根目录（Project.Instance.paramPathSet.ProcessPath）。</summary>
        string ProcessPath { get; }

        /// <summary>日志根目录（Project.Instance.logParam.LogSavePath）。</summary>
        string LogSavePath { get; }
    }

    /// <summary>
    /// 契约的默认实现：转调现有 Project 单例。
    ///
    /// 这里刻意保留对 Project 的依赖，而不是现在就抽干净：
    /// Phase 1 的目标是让**插件**不再引用 Frame_Core，
    /// 宿主自身的单例改造属于 Phase 2，放在一起做会让编译面同时变两处。
    /// 实现类是 internal，插件只能看到接口。
    /// </summary>
    internal sealed class ProjectPluginHostContext : IPluginHostContext
    {
        public Process GetProcessById(int processId)
        {
            return Project.Instance.GetProcessById(processId);
        }

        public Process GetProcessByName(string processName)
        {
            return Project.Instance.GetProcessByName(processName);
        }

        public string ProcessPath
        {
            get { return Project.Instance.paramPathSet.ProcessPath; }
        }

        public string LogSavePath
        {
            get { return Project.Instance.logParam.LogSavePath; }
        }
    }

    /// <summary>
    /// 插件的宿主入口。只有一个实现，不可替换：
    /// 若允许运行期替换，就会出现"部分插件走旧路径、部分走新路径"的中间态，
    /// 与主题切换那里刻意关掉运行期替换是同一个理由。
    /// </summary>
    public static class PluginHost
    {
        private static readonly IPluginHostContext context = new ProjectPluginHostContext();

        public static IPluginHostContext Context
        {
            get { return context; }
        }
    }
}