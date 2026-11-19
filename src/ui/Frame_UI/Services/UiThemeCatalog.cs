using System;
using System.Collections.Generic;

namespace Frame_UI
{
    /// <summary>
    /// 主题目录：按名称登记可用的主题工厂。
    ///
    /// 存在理由：浅色主题的预留不能只靠一个 DefaultLightTheme 类——
    /// 如果切换入口仍然写死 DefaultDarkTheme，"预留"就只是一句注释。
    /// 目录把"有哪些主题、按名字怎么造"集中成唯一事实，
    /// ThemeService 只负责"当前用哪套、什么时候允许换"。
    ///
    /// 登记用的是工厂而不是实例：令牌实例可被子类意外改写，
    /// 每次按名新建可以保证换主题时拿到的是干净副本。
    /// </summary>
    public static class UiThemeCatalog
    {
        /// <summary>默认深色主题的名称，也是启动默认主题。</summary>
        public const string DarkThemeName = "Dark";

        /// <summary>浅色主题骨架的名称。</summary>
        public const string LightThemeName = "Light";

        private static readonly Dictionary<string, Func<UiTokens>> Factories =
            new Dictionary<string, Func<UiTokens>>(StringComparer.OrdinalIgnoreCase)
            {
                { DarkThemeName, DefaultDarkTheme.Create },
                { LightThemeName, DefaultLightTheme.Create },
            };

        /// <summary>已登记的主题名列表（只读副本，避免调用方改动目录）。</summary>
        public static string[] Names
        {
            get
            {
                var names = new string[Factories.Count];
                Factories.Keys.CopyTo(names, 0);
                return names;
            }
        }

        /// <summary>登记或覆盖一个主题工厂。名称不区分大小写。</summary>
        public static void Register(string name, Func<UiTokens> factory)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("主题名不能为空", nameof(name));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            Factories[name.Trim()] = factory;
        }

        /// <summary>按名创建一套全新的令牌实例。名称未登记时抛 ArgumentException。</summary>
        public static UiTokens Create(string name)
        {
            Func<UiTokens> factory;
            if (string.IsNullOrWhiteSpace(name) || !Factories.TryGetValue(name.Trim(), out factory))
            {
                throw new ArgumentException("主题未登记：" + (name ?? "<null>"), nameof(name));
            }
            return factory();
        }

        /// <summary>名称是否已登记。</summary>
        public static bool Contains(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && Factories.ContainsKey(name.Trim());
        }
    }
}
