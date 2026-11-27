using System;

namespace LogTool
{
    /// <summary>日志流向：界面按这个分页签。</summary>
    public enum AppLogChannel
    {
        调试,
        错误,
        通讯,
    }

    /// <summary>
    /// 统一日志门面。
    ///
    /// 一次调用写两份：
    ///   1. log4net 日志文件（现场追查，进程退出后仍在）；
    ///   2. 通过 <see cref="UiSink"/> 交给界面层（主界面“日志”页，本次运行最近 500 条）。
    ///
    /// 业务代码只调本类，不要再直接调 Log4netRecord，否则界面又会出现“文件里有、页面是空的”。
    /// 本类放在 LogTool：底层与插件都能直接引用；界面队列由 Frame_Core 在启动时注入，
    /// 避免 LogTool 反向依赖 Frame_Core。
    /// </summary>
    public static class AppLog
    {
        /// <summary>
        /// 界面输出回调：参数为（分页签，已带时间前缀的整行）。
        /// 未注入时只写文件（例如控制台测试、界面创建前）。
        /// </summary>
        public static Action<AppLogChannel, string> UiSink { get; set; }

        /// <summary>文件与界面共用同一枚时间前缀，方便按时间对齐两边。</summary>
        private static string Stamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff");
        }

        /// <summary>异常：写 ErrorLog 文件，并进界面“错误”页。</summary>
        public static void Error(string log)
        {
            string text = Stamp() + "   " + log;
            Log4netRecord.ErrorLog.Error(text);
            UiSink?.Invoke(AppLogChannel.错误, text);
        }

        /// <summary>异常（带堆栈）：写 ErrorLog 文件，并进界面“错误”页。</summary>
        public static void Error(string log, Exception ex)
        {
            string text = Stamp() + "   " + log;
            Log4netRecord.ErrorLog.Error(text, ex);
            UiSink?.Invoke(AppLogChannel.错误, text);
        }

        /// <summary>告警：写 RunLog 文件，并进界面“调试”页。</summary>
        public static void Warn(string log)
        {
            string text = Stamp() + "   " + log;
            Log4netRecord.RunLog.Warn(text);
            UiSink?.Invoke(AppLogChannel.调试, text);
        }

        /// <summary>运行信息：写 RunLog 文件，并进界面“调试”页。</summary>
        public static void Info(string log)
        {
            string text = Stamp() + "   " + log;
            Log4netRecord.RunLog.Info(text);
            UiSink?.Invoke(AppLogChannel.调试, text);
        }

        /// <summary>运行调试：写 RunLog 文件，并进界面“调试”页。</summary>
        public static void Debug(string log)
        {
            string text = Stamp() + "   " + log;
            Log4netRecord.RunLog.Debug(text);
            UiSink?.Invoke(AppLogChannel.调试, text);
        }

        /// <summary>通讯：写 CommLog 文件，并进界面“通讯”页。</summary>
        public static void Comm(string log)
        {
            string text = Stamp() + "   " + log;
            // 沿用旧 MessageNotify.AddCommLog 的 Error 级别，避免改变现场已有日志的筛选习惯
            Log4netRecord.CommLog.Error(text);
            UiSink?.Invoke(AppLogChannel.通讯, text);
        }
    }
}
