using System;

namespace Frame_Core
{
    /// <summary>
    /// 一个相机槽位定义。槽位在 CamSlots 列表中的位置即物理相机下标，
    /// 该下标必须连续（0..N-1）并与 M_CamParam.l_CameraDevs 的顺序一一对应。
    /// </summary>
    public sealed class CamSlotDef
    {
        /// <param name="role">该相机的角色</param>
        /// <param name="displayName">相机名，用于流程名、存图前缀、参数文件名</param>
        /// <param name="createParam">按相机名创建该相机使用的参数类</param>
        public CamSlotDef(CamRole role, string displayName, Func<string, BaseProParam> createParam)
        {
            if (string.IsNullOrEmpty(displayName)) throw new ArgumentException("相机名不能为空", "displayName");
            if (createParam == null) throw new ArgumentNullException("createParam");

            Role = role;
            DisplayName = displayName;
            CreateParam = createParam;
        }

        public CamRole Role { get; private set; }

        public string DisplayName { get; private set; }

        public Func<string, BaseProParam> CreateParam { get; private set; }

        public BaseProParam NewParam()
        {
            return CreateParam(DisplayName);
        }
    }
}