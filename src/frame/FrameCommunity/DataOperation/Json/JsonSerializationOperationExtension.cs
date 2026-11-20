using LogTool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FrameCommunity.DataOperation
{
    public static class JsonSerializationOperationExtension
    {
        public static void SaveExtension(this JsonSerializationOperation param, string path, string fileName)
        {
            if (param == null) return;
            string strPath = Path.Combine(path, fileName);
            param.filePath = strPath;
            param.Save();

        }

        public static T ReadExtension<T>(this JsonSerializationOperation param, string path, string fileName) where T : JsonSerializationOperation, new()
        {
            if (param == null) param = new T();
            string strPath = Path.Combine(path, fileName);
            T paramTemp;
            param.filePath = strPath;
            paramTemp = param.Read<T>();
            bool isNew = paramTemp == null;
            if (isNew)
            {
                paramTemp = new T();
                //ErrorWorks.ErrorMessageShow(strPath, ErrorWorks.GetErrorInfo((uint)ErrorCodeEnum.参数加载失败));
                Log4netRecord.ErrorLog.Error($"参数加载失败\r\n路径：{strPath}");
            }

            paramTemp.filePath = strPath;
            paramTemp.Init();

            // 首次生成或文件损坏：把 Init 之后的默认值落盘。
            // 不落盘的话每次启动都读到同一个空壳，现场永远看不到"缺了什么"。
            if (isNew) paramTemp.Save();
            return paramTemp;
        }

        public static T ReadExtension<T>(this JsonSerializationOperation param, string path, string fileName,int length) where T : JsonSerializationOperation, new()
        {
            if (param == null) param = new T();
            string strPath = Path.Combine(path, fileName);
            T paramTemp;
            param.filePath = strPath;
            paramTemp = param.Read<T>();
            bool isNew = paramTemp == null;
            if (isNew)
            {
                paramTemp = new T();
                //ErrorWorks.ErrorMessageShow(strPath, ErrorWorks.GetErrorInfo((uint)ErrorCodeEnum.参数加载失败));
                Log4netRecord.ErrorLog.Error($"参数加载失败\r\n路径：{strPath}");
            }

            paramTemp.filePath = strPath;
            paramTemp.Init(length);

            // 首次生成或文件损坏：把 Init 之后的默认值落盘。
            if (isNew) paramTemp.Save();
            return paramTemp;
        }
    }
}
