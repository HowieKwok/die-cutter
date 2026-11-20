
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FrameCommunity.DataOperation
{
    /// <summary>
    /// json序列化操作类
    /// </summary>
    [Serializable]
    public class JsonSerializationOperation
    {
        /// <summary>
        /// json文件路径
        /// </summary>
        public string filePath = string.Empty;
        /// <summary>
        /// json文件路径+  filePath +".json"
        /// </summary>
        public string fileFullPath = string.Empty;
        /// <summary>
        /// 保存二进制文件
        /// </summary>
        /// <returns>true=保存成功，false=保存失败</returns>
        public bool Save()
        {
            try
            {
                fileFullPath = string.Format(filePath + ".json");
                return JsonSerialization.ObjectToJsonFile(fileFullPath, this);
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        /// <summary>
        /// 读取二进制文件 --泛型方式，使用： testCraft1 = testCraft.Read<testCraft>();
        /// </summary>
        /// <returns>读取结果</returns>
        /// <remarks>
        /// 只负责读取，不再在文件缺失时顺手创建。原因是它保存的是调用方对象本身
        /// （例如 new CraftBase()），此时还没执行 Init，磁盘上会留下一个只有默认构造值的空壳，
        /// 而 Init 补出来的默认值只在内存里。
        /// 文件缺失由调用方（ReadExtension / ReadCraft）在 Init 之后统一落盘；
        /// 内容损坏则先改名成 .bak 再返回 null，避免现场参数被默认值静默覆盖。
        /// </remarks>
        public T Read<T>()
        {
            T result = default(T);
            fileFullPath = string.Format(filePath + ".json");
            try
            {
                if (!File.Exists(fileFullPath))
                {
                    return default(T);
                }

                try
                {
                    result = JsonSerialization.JsonFileToObject<T>(fileFullPath);
                }
                catch
                {
                    BackupBrokenFile();
                    return default(T);
                }
            }
            catch (Exception ex)
            {
                return result;
            }
            return result;
        }

        /// <summary>损坏的配置文件改名保留，供现场排查；失败不影响启动。</summary>
        private void BackupBrokenFile()
        {
            try
            {
                string bak = fileFullPath + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(fileFullPath, bak);
            }
            catch
            {
                // 备份失败不抛：调用方会按"读不到"处理，重新生成默认配置
            }
        }

        /// <summary>
        /// 读取参数后执行初始化
        /// </summary>
        public virtual void Init()
        {

        }

        /// <summary>
        /// 读取参数后执行初始化
        /// </summary>
        public virtual void Init(int length)
        {

        }

        ///// <summary>
        ///// 读取二进制文件 --强转方式，使用： testCraft1 = (testCraft)(object)testCraft;
        ///// </summary>
        ///// <returns>读取结果</returns>
        //public object ReadObj()
        //{
        //    object result = null;
        //    fileFullPath = string.Format(filePath + ".json");
        //    try
        //    {
        //        if (File.Exists(fileFullPath))
        //        {
        //            result = JsonSerialization.JsonFileToObj(fileFullPath);
        //        }
        //        else
        //        {
        //            if (Save())
        //            {
        //                return ReadObj();
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        return result;
        //    }
        //    return result;
        //}
    }
}
