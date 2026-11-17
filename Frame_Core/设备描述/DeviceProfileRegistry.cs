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