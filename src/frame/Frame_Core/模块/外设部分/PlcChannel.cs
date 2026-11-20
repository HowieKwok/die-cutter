using System;
using System.Collections.Generic;
using System.Reflection;
using FrameCommunity.DataOperation;

namespace Frame_Core
{
    /// <summary>
    /// 一个机型与 PLC 打交道的全部知识：参数加载、服务创建、连接/断开、协议形态。
    ///
    /// 新增机型 = 新增一个本接口的实现（注册表按特性/类型自动发现），
    /// Project、仿真、Bootstrap 都不需要再出现机型分支。
    /// </summary>
    public interface IPlcChannel
    {
        /// <summary>本通道负责的工艺类型。</summary>
        MaterialCrafEnum Craft { get; }

        /// <summary>是否为符号寻址（CIP）。仿真服务端据此决定用哪种协议。</summary>
        bool UseCip { get; }

        /// <summary>本通道的通讯服务实例（LoadParams 之后非空）。</summary>
        PLCCommunicationBase Service { get; }

        /// <summary>读取本机型的 PLC 参数并创建服务。</summary>
        void LoadParams(Project project, string machinePath);

        void Connect(Project project);

        void Disconnect(Project project);
    }

    /// <summary>
    /// 机型 → PLC 通道的注册表。扫描本程序集里所有 IPlcChannel 实现自动注册，
    /// 因此新增机型只加一个类，不改任何现有文件。
    /// </summary>
    public static class PlcChannelRegistry
    {
        private static readonly Dictionary<MaterialCrafEnum, IPlcChannel> Map = Build();

        private static Dictionary<MaterialCrafEnum, IPlcChannel> Build()
        {
            Dictionary<MaterialCrafEnum, IPlcChannel> map = new Dictionary<MaterialCrafEnum, IPlcChannel>();
            foreach (Type type in typeof(IPlcChannel).Assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!typeof(IPlcChannel).IsAssignableFrom(type)) continue;
                // 通道类是 internal，默认构造器也是 internal，必须显式带 NonPublic 才找得到。
                if (type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null) == null) continue;

                IPlcChannel channel = (IPlcChannel)Activator.CreateInstance(type);
                map[channel.Craft] = channel;
            }
            return map;
        }

        /// <summary>取当前工艺类型对应的通道；未注册时返回 null，由调用方决定是否阻断。</summary>
        public static IPlcChannel Resolve(MaterialCrafEnum craft)
        {
            IPlcChannel channel;
            return Map.TryGetValue(craft, out channel) ? channel : null;
        }
    }

    /// <summary>激光模切一体机：FINS + D 寄存器，无符号概念。</summary>
    internal sealed class PlcChannel_DieCuttingCut : IPlcChannel
    {
        public MaterialCrafEnum Craft { get { return MaterialCrafEnum.激光模切一体机; } }

        public bool UseCip { get { return false; } }

        public PLCCommunicationBase Service { get; private set; }

        public void LoadParams(Project project, string machinePath)
        {
            project.M_PLCDevParam = project.M_PLCDevParam
                .ReadExtension<PLCDevParam>(machinePath, "PLCDevParamr");
            Service = project.server_DiecuttingCutPLC = new Server_DiecuttingCutPLC(project.M_PLCDevParam);
        }

        public void Connect(Project project)
        {
            PLCDevParam p = project.M_PLCDevParam;
            if (p == null) return;
            Service.Connect(p.IsConnect, p.GetPLCType(), (CommunicationProtocolEnum)p.ComType,
                p.PLC_IP, p.DevPort, (emDataFormat)p.nByteTrans);
        }

        public void Disconnect(Project project)
        {
            if (Service != null) Service.Disconnect();
        }
    }

    /// <summary>五金模切：CIP + 符号寻址，服务端必须先注册符号。</summary>
    internal sealed class PlcChannel_五金模切 : IPlcChannel
    {
        public MaterialCrafEnum Craft { get { return MaterialCrafEnum.五金模切; } }

        public bool UseCip { get { return true; } }

        public PLCCommunicationBase Service { get; private set; }

        public void LoadParams(Project project, string machinePath)
        {
            project.M_PLCDevParam_五金 = project.M_PLCDevParam_五金
                .ReadExtension<PLCDevParam_五金>(machinePath, "PLCDevParamr");
            Service = project.server_五金模切PLC = new Server_五金模切PLC(project.M_PLCDevParam_五金);
        }

        public void Connect(Project project)
        {
            PLCDevParam_五金 p = project.M_PLCDevParam_五金;
            if (p == null) return;
            Service.Connect(p.IsConnect, p.GetPLCType(), (CommunicationProtocolEnum)p.ComType,
                p.PLC_IP, p.DevPort, (emDataFormat)p.nByteTrans);
        }

        public void Disconnect(Project project)
        {
            if (Service != null) Service.Disconnect();
        }
    }
}