using Frame_Core;
using System;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 一个相机槽位：Index 与 BaseProDevice.m_DicCamOperaParam 的键一致，
    /// BindCallbacks 负责把 Server 的处理函数挂到该相机的 BaseProParam 上。
    /// </summary>
    public sealed class CamSlot
    {
        public CamSlot(int index, string name, Action<ServerBase, BaseProParam> bindCallbacks)
        {
            if (bindCallbacks == null) throw new ArgumentNullException("bindCallbacks");

            Index = index;
            Name = name;
            BindCallbacks = bindCallbacks;
        }

        public int Index { get; private set; }

        public string Name { get; private set; }

        public Action<ServerBase, BaseProParam> BindCallbacks { get; private set; }
    }
}