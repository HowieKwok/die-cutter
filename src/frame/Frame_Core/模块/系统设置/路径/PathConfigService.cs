using LogTool;
using System;
using System.IO;

namespace Frame_Core
{
    /// <summary>
    /// 路径设置的保存与应用。
    ///
    /// 保存顺序固定：先校验全部目录 → 写 PathParameter → 刷新 ParamPathSet
    /// → 更新相机点检图像路径 → 切换日志目录。全部在运行期完成，不需要重启。
    /// </summary>
    public static class PathConfigService
    {
        /// <summary>保存并立即应用。返回 null 表示成功，否则为错误信息。</summary>
        /// <param name="param">当前路径参数（复用同一实例，filePath 已由 ReadExtension 设好）。</param>
        /// <param name="logPath">日志目录（由 LogParam 维护）。</param>
        /// <param name="oldImageRoot">修改前的点检图像根，用于只覆盖"跟随根目录"的相机路径。</param>
        public static string Apply(PathParameter param, string logPath, string oldImageRoot)
        {
            if (param == null) return "路径参数为空。";
            if (Project.Instance.Is_MojuanStart || Project.Instance.Is_Run)
                return "膜卷运行或暂停中，不能修改路径；请先结束膜卷。";

            string error;
            if (!CheckWritable(param.DataRoot, "生产数据根", out error)) return error;
            if (!CheckWritable(param.CalibrationRoot, "标定目录", out error)) return error;
            if (!CheckWritable(param.PointCheckRoot, "点检数据根", out error)) return error;
            if (!CheckWritable(param.PointCheckImageRoot, "点检图像根", out error)) return error;

            if (!string.IsNullOrWhiteSpace(logPath) &&
                !CheckWritable(logPath, "日志目录", out error)) return error;

            param.Normalize();

            if (string.IsNullOrEmpty(param.filePath))
                param.filePath = Path.Combine(Project.Instance.paramPathSet.SystemPath, "PathParameter");
            if (!param.Save())
                return "路径配置写盘失败：" + param.filePath + ".json";

            Project.Instance.pathParameter = param;
            Project.Instance.paramPathSet.ApplyPathParameter(param);

            ApplyPointCheckImages(param.PointCheckImageRoot, oldImageRoot);

            if (!string.IsNullOrWhiteSpace(logPath))
            {
                string newLog = logPath.Trim().TrimEnd('\\');
                Project.Instance.logParam.LogSavePath = newLog;
                LogParamManage.SaveLogParam(Project.Instance.logParam.filePath, Project.Instance.logParam);
                Log4netRecord.InitLog4netConfigure(Project.Instance.logParam);
            }

            // 状态栏的磁盘容量检查跟随数据根所在卷
            string dataDrive = Path.GetPathRoot(param.DataRoot);
            if (!string.IsNullOrEmpty(dataDrive))
            {
                Project.Instance.driveParameter.driveName = dataDrive;
                Project.Instance.driveParameter.Save();
            }

            return null;
        }

        /// <summary>目录必须存在盘符且可创建、可写。校验时会创建目标目录。</summary>
        public static bool CheckWritable(string path, string title, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = title + "不能为空。";
                return false;
            }
            bool rooted;
            try { rooted = Path.IsPathRooted(path); }
            catch
            {
                error = title + "含非法字符。";
                return false;
            }
            if (!rooted)
            {
                error = title + "必须是绝对路径。";
                return false;
            }

            string full;
            try { full = Path.GetFullPath(path.Trim()); }
            catch (Exception ex)
            {
                error = title + "路径无效：" + ex.Message;
                return false;
            }

            string root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                error = title + "所在磁盘不存在：" + root;
                return false;
            }

            try
            {
                Directory.CreateDirectory(full);
                string probe = Path.Combine(full, ".write_probe_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                error = title + "目录不可写：" + ex.Message;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 相机点检图像路径只在"未设置、仍是旧的 E: 默认值、或仍跟随旧根目录"时替换，
        /// 现场手工改到别处的相机保持不动。
        /// </summary>
        public static bool IsDefaultPointCheckImage(string path, string oldRoot)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            if (path.StartsWith(@"E:\点检图像", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrWhiteSpace(oldRoot) &&
                path.StartsWith(oldRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static void ApplyPointCheckImages(string newRoot, string oldRoot)
        {
            if (string.IsNullOrWhiteSpace(newRoot)) return;

            string sizePath = Path.Combine(newRoot, "尺寸点检图像");
            string defectPath = Path.Combine(newRoot, "缺陷点检图像");

            foreach (var item in Project.Instance.M_CameraRelevancies)
            {
                CameraRelevancy relevancy = item.Value.Item2;
                if (relevancy == null) continue;

                SizePointInspectionParam sizeParam =
                    relevancy.m_SizePointInspectionManage.sizePointInspectionParam;
                if (sizeParam != null && IsDefaultPointCheckImage(sizeParam.PointCheckImgPath, oldRoot))
                {
                    sizeParam.PointCheckImgPath = sizePath;
                    sizeParam.Save();
                }

                DefectPointInspectionParam defectParam =
                    relevancy.m_DefectPointInspectionManage.defectPointInspectionParam;
                if (defectParam != null && IsDefaultPointCheckImage(defectParam.PointCheckImgPath, oldRoot))
                {
                    defectParam.PointCheckImgPath = defectPath;
                    defectParam.Save();
                }
            }
        }
    }
}
