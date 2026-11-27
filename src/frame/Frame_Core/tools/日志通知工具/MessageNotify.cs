using FrameCommunity;
using LogTool;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Frame_Core
{
    public static class MessageNotify
    {
        /// <summary>
        /// 接管 AppLog 的界面输出。启动时调用一次即可（幂等）；不调用则 AppLog 只写文件。
        /// </summary>
        public static void AttachUiSink()
        {
            AppLog.UiSink = Publish;
        }

        /// <summary>AppLog 已经把时间拼好，这里只负责入队与置“有更新”标记。</summary>
        private static void Publish(AppLogChannel channel, string text)
        {
            switch (channel)
            {
                case AppLogChannel.错误:
                    ErrorLogs.Add(text + "\r\n", (uint)ShowCount);
                    ErrorLogEnable = true;
                    break;
                case AppLogChannel.通讯:
                    CommLogs.Add(text + "\r\n", (uint)ShowCount);
                    CommLogEnable = true;
                    break;
                default:
                    DebugLogs.Add(text + "\r\n", (uint)ShowCount);
                    DebugLogEnable = true;
                    break;
            }
        }

        public static int ShowCount { get; } = 500;
        public static QueuePackage<StopAlarmMessage> StopAlarmQueue { get; } = new QueuePackage<StopAlarmMessage>();
        public static ListPackage<string> DebugLogs { get; } = new ListPackage<string>();
        public static ListPackage<string> ErrorLogs { get; } = new ListPackage<string>();
        public static ListPackage<string> CommLogs { get; } = new ListPackage<string>();



        public static bool DebugLogEnable { get; set; } = true;

        public static bool ErrorLogEnable { get; set; } = true;

        public static bool CommLogEnable { get; set; } = true;

        public static void AddStopAlarm(StopAlarmMessage message)
        {
            StopAlarmQueue.Enqueue(message, (uint)ShowCount);
        }
        // 保留旧入口给既有调用方；落文件与进队列的统一实现在 AppLog。
        public static void AddDebugLog(string log)
        {
            AppLog.Debug(log);
        }

        public static void AddErrorLog(string log)
        {
            AppLog.Error(log);
        }

        public static void AddErrorLog(string log, Exception ex)
        {
            AppLog.Error(log, ex);
        }

        public static void AddCommLog(string log)
        {
            AppLog.Comm(log);
        }
    }

}
