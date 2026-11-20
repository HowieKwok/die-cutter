using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace Frame_Core
{
    /// <summary>
    /// 启动前的配置自检：只读不写，不构造参数对象（否则会反向生成默认文件）。
    ///
    /// 不阻断启动——缺失的项会由各参数自己的 Init() 用默认值补上，
    /// 但默认值往往是"0 路相机""只有 admin 账号"，界面上看不出来，
    /// 所以这里把所有缺失项一次性列给操作员。
    /// </summary>
    public static class StartupPreflight
    {
        /// <summary>机型层文件：缺一个就少一块功能，名称与代码里的读取名一致（不含扩展名）。</summary>
        private static readonly string[] MachineFiles =
        {
            "AllCamParam.json",
            "AllLightControllerParam.json",
            "ImageSaveParamer.json",
            "PointInspectionParamer.json",
            "PLCDevParamr.json",
        };

        /// <summary>通用层文件：与机型无关。</summary>
        private static readonly string[] CommonFiles =
        {
            "SystemParam.json",
            "UserParam.xml",
            "ControlCordParam.json",
        };

        /// <summary>最近一次自检结果，供主界面常驻提示复用（弹窗点掉后仍可查看）。</summary>
        public static List<string> LastProblems { get; private set; } = new List<string>();

        /// <summary>返回缺失项与异常项；为空表示配置完整。</summary>
        public static List<string> Validate()
        {
            LastProblems = ValidateCore();
            return LastProblems;
        }

        private static List<string> ValidateCore()
        {
            List<string> problems = new List<string>();

            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZSystemConfigurationFile");
            if (!Directory.Exists(root))
            {
                problems.Add("配置目录不存在：" + root);
                problems.Add("以下检查无法继续，程序将全部使用代码默认值。");
                return problems;
            }

            foreach (string file in CommonFiles)
            {
                string path = Path.Combine(root, file);
                if (!File.Exists(path))
                {
                    problems.Add("缺少通用配置：" + path);
                }
            }

            // 机型决定机型层目录，读不到就按默认机型检查
            MaterialCrafEnum craftType = MaterialCrafEnum.激光模切一体机;
            string systemParam = Path.Combine(root, "SystemParam.json");
            if (File.Exists(systemParam) && !TryReadCraftType(systemParam, out craftType))
            {
                problems.Add("机型无法解析（CraftType 不是合法值）：" + systemParam);
            }

            string machineName = craftType.GetDescription();
            string machineDir = Path.Combine(root, machineName);
            if (!Directory.Exists(machineDir))
            {
                problems.Add("机型配置目录不存在：" + machineDir
                    + "（当前机型：" + machineName + "）");
                return problems;
            }

            foreach (string file in MachineFiles)
            {
                string path = Path.Combine(machineDir, file);
                if (!File.Exists(path))
                {
                    problems.Add("缺少机型配置：" + path);
                }
            }

            string camParam = Path.Combine(machineDir, "AllCamParam.json");
            if (File.Exists(camParam) && CountCameras(camParam) <= 0)
            {
                problems.Add("相机配置为空（无任何相机条目）：" + camParam);
            }

            return problems;
        }

        private static bool TryReadCraftType(string path, out MaterialCrafEnum craftType)
        {
            craftType = MaterialCrafEnum.激光模切一体机;
            try
            {
                JObject json = JObject.Parse(File.ReadAllText(path));
                JToken token = json["CraftType"];
                if (token == null) return false;

                int value = token.Value<int>();
                if (!Enum.IsDefined(typeof(MaterialCrafEnum), value)) return false;

                craftType = (MaterialCrafEnum)value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int CountCameras(string path)
        {
            try
            {
                JObject json = JObject.Parse(File.ReadAllText(path));
                JArray array = json["l_CameraDevs"] as JArray;
                return array == null ? 0 : array.Count;
            }
            catch
            {
                return 0;
            }
        }
    }
}