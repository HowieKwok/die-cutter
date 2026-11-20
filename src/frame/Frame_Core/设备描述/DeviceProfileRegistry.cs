using System;
using System.Collections.Generic;
using System.Linq;

namespace Frame_Core
{
    /// <summary>
    /// 设备类型 → 设备画像 的唯一注册表。
    /// 取不到时立刻抛出带机型名的异常，避免设备业务对象为 null 后在远处空引用。
    /// </summary>
    public static class DeviceProfileRegistry
    {
        private static readonly Dictionary<MaterialCrafEnum, IDeviceProfile> profiles =
            new Dictionary<MaterialCrafEnum, IDeviceProfile>();

        static DeviceProfileRegistry()
        {
            Register(new LaserDieCuttingProfile());
            Register(new 五金模切Profile());
        }

        /// <summary>注册一个机型画像；同一机型重复注册时抛异常。</summary>
        public static void Register(IDeviceProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (profiles.ContainsKey(profile.CraftType))
            {
                throw new InvalidOperationException(
                    "设备类型重复注册：" + profile.CraftType);
            }
            profiles.Add(profile.CraftType, profile);
        }

        /// <summary>
        /// 用另一份画像替换已注册的机型画像。
        /// 仿真启动时用它把控制卡换成本地模拟实现（其余成员委托给原画像），
        /// 于是"这台机器由哪些实现组成"只有注册这一个入口，运行期不再出现模式分支。
        /// 只在启动阶段调用。
        /// </summary>
        public static void Override(IDeviceProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            profiles[profile.CraftType] = profile;
        }

        public static IDeviceProfile Resolve(MaterialCrafEnum craftType)
        {
            IDeviceProfile profile;
            if (profiles.TryGetValue(craftType, out profile)) return profile;

            throw new NotSupportedException(string.Format(
                "未注册的设备类型：{0}。已注册：{1}。请在 DeviceProfileRegistry 中补充该机型的设备画像。",
                craftType, string.Join("、", profiles.Keys.Select(k => k.ToString()).ToArray())));
        }

        public static bool TryResolve(MaterialCrafEnum craftType, out IDeviceProfile profile)
        {
            return profiles.TryGetValue(craftType, out profile);
        }
    }
}