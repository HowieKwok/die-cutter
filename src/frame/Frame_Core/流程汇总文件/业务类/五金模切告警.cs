using LogTool;
using System;
using System.Collections.Generic;

namespace Frame_Core.业务类
{
    /// <summary>
    /// 五金模切的非模态告警通道。
    ///
    /// 为什么不直接弹窗：告警点都在相机回调线程或视觉线程上，
    /// WinForms 窗体只能在 UI 线程创建，核心层直接 Show 会跨线程崩。
    /// 这里只发事件，由界面层决定怎么呈现；没有订阅者时照样写日志，
    /// 保证无人值守也不会把信息丢掉。
    ///
    /// 同一个 key 在 Dismiss 之前只发一次：错位是持续触发的，
    /// 不去重会把屏幕和日志同时刷满。
    /// </summary>
    public static class 五金模切告警
    {
        public const string 线扫帧号错位 = "线扫帧号错位";
        public const string 尺寸帧号错位 = "尺寸帧号错位";
        public const string 帧结算异常 = "帧结算异常";

        private static readonly object _sync = new object();
        private static readonly HashSet<string> _pending = new HashSet<string>();

        /// <summary>参数为 (key, 标题, 正文)。订阅者负责在界面关闭时调用 Dismiss(key)。</summary>
        public static event Action<string, string, string> Raising;

        public static void Raise(string key, string title, string message)
        {
            Action<string, string, string> handler;
            lock (_sync)
            {
                if (!_pending.Add(key)) return;
                handler = Raising;
            }

            // 日志无条件写：它是现场唯一的追查依据，不能因为界面上有提示就省掉
            AppLog.Warn($"[{title}] {message}");

            if (handler == null) return;
            try { handler(key, title, message); }
            catch (Exception ex) { AppLog.Error($"五金告警投递失败：{ex}"); }
        }

        public static void Dismiss(string key)
        {
            lock (_sync) { _pending.Remove(key); }
        }

        public static void DismissAll()
        {
            lock (_sync) { _pending.Clear(); }
        }
    }
}