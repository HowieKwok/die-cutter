using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

using LogTool;

namespace Frame_Core
{
    /// <summary>
    /// “用系统默认程序打开路径”的唯一入口。
    ///
    /// 存在理由：Process.Start(path) 在路径不存在时抛 Win32Exception
    /// （系统找不到指定的文件），调用方是菜单点击事件，没有反馈渠道，
    /// 用户看到的是未处理异常对话框。凡是菜单/按钮打开目录或文件的地方都走这里：
    /// 路径判定、启动、失败提示集中一处，调用方只关心成功与否。
    /// </summary>
    public static class ShellPathLauncher
    {
        /// <summary>
        /// 打开目录。目录不存在时不抛异常，提示用户并返回 false。
        /// </summary>
        /// <param name="folderPath">目标目录</param>
        /// <param name="friendlyName">提示语里的业务名，例如“生产统计”</param>
        public static bool OpenFolder(string folderPath, string friendlyName = null)
        {
            string name = string.IsNullOrWhiteSpace(friendlyName) ? "目录" : friendlyName;

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                Notify($"{name}路径未配置，无法打开。");
                return false;
            }

            if (!Directory.Exists(folderPath))
            {
                Notify($"{name}目录不存在：\r\n{folderPath}\r\n\r\n请确认设备已开始生产并生成了该卷数据。");
                return false;
            }

            return Start(folderPath, name);
        }

        /// <summary>
        /// 打开文件（用其关联程序）。文件不存在时不抛异常。
        /// </summary>
        public static bool OpenFile(string filePath, string friendlyName = null)
        {
            string name = string.IsNullOrWhiteSpace(friendlyName) ? "文件" : friendlyName;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                Notify($"{name}路径未配置，无法打开。");
                return false;
            }

            if (!File.Exists(filePath))
            {
                Notify($"{name}不存在：\r\n{filePath}");
                return false;
            }

            return Start(filePath, name);
        }

        /// <summary>
        /// 在资源管理器中选中并定位到该文件/目录（不存在时退化为打开其父目录）。
        /// </summary>
        public static bool RevealInExplorer(string path, string friendlyName = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Notify($"{friendlyName ?? "路径"}未配置，无法定位。");
                return false;
            }

            if (File.Exists(path) || Directory.Exists(path))
            {
                return StartArguments("/select,\"" + path + "\"", "explorer.exe", friendlyName);
            }

            Notify($"{friendlyName ?? "路径"}不存在：\r\n{path}");
            return false;
        }

        private static bool Start(string target, string name)
        {
            try
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error(ex);
                Notify($"打开{name}失败：\r\n{ex.Message}");
                return false;
            }
        }

        private static bool StartArguments(string arguments, string fileName, string friendlyName)
        {
            try
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error(ex);
                Notify($"定位{friendlyName ?? "路径"}失败：\r\n{ex.Message}");
                return false;
            }
        }

        private static void Notify(string message)
        {
            MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}