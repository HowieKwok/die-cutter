using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogTool
{
    /// <summary>
    /// 日志参数
    /// </summary>
    public static class LogParamManage
    {

        /// <summary>
        /// 获取日志参数，获取异常回返回Null值
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static void GetLogParam(string path, ref LogParam logParam)
        {
            try
            {
                if (logParam == null) logParam = new LogParam();
                logParam.filePath = path;

                // 读失败（文件损坏、或默认文件写不出去）会返回 null。
                // 不兜底的话 InitLog4netConfigure 会对 null 取 LogSavePath，
                // 结果是一个异常框 + 整场日志不初始化。
                logParam = logParam.Read() as LogParam;
                if (logParam == null) logParam = new LogParam();

                logParam.filePath = path;

            }
            catch (Exception)
            {


            }
        }
        /// <summary>
        /// 保存日志参数
        /// </summary>
        /// <param name="path"></param>
        /// <param name="logParam"></param>
        public static void SaveLogParam(string path, LogParam logParam)
        {
            try
            {
                logParam.filePath = path;
                logParam.Save();
            }
            catch (Exception)
            {


            }

        }
    }
}
