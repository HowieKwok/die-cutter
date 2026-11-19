using System;
using System.IO;

namespace Frame_UI
{
    /// <summary>
    /// 主题名的持久化。
    ///
    /// 存在理由：令牌是纯内存对象，进程一退出就没了；
    /// 没有这一层，每次启动都会回到默认主题，用户切了等于白切。
    ///
    /// 存储位置跟着程序走（与日志、配方一致），不用注册表：
    /// 现场拷贝整个目录即可迁移，也不会因为换用户而丢设置。
    /// 读写失败一律静默——主题记不住不影响业务，不能因为它影响启动。
    /// </summary>
    public static class ThemeSettings
    {
        private const string FileName = "ui-theme.txt";

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName); }
        }

        /// <summary>
        /// 读取上次使用的主题名。
        /// 返回 null 表示没有记录，或记录的名字已经不在主题目录里（例如降级到旧版本）。
        /// </summary>
        public static string Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;

                string name = File.ReadAllText(FilePath).Trim();
                return UiThemeCatalog.Contains(name) ? name : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>记住当前主题名。名称为空时不写入，避免把“未指定”覆盖成空文件。</summary>
        public static void Save(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            try
            {
                File.WriteAllText(FilePath, name.Trim());
            }
            catch (Exception)
            {
                // 目录只读 / 被占用：本次设置只在当前进程内生效。
            }
        }
    }
}