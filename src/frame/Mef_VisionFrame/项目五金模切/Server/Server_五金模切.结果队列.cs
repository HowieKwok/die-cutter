using Frame_MetalRun;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金的三个发布通道。只独立队列与状态机，不各开 socket：
    /// 五金只有一个 Server_五金模切PLC 实例，三条线程并发写同一连接会让报文交错。
    ///
    /// PlcResultChannel / PlcResultItem / PlcResultPublisher 已迁到 Frame_MetalRun，
    /// 便于脱离界面与 Halcon 单独测试；这里只保留三条通道的静态实例供 Server/仿真共用。
    /// </summary>
    public partial class Server_五金模切
    {
        public static readonly PlcResultChannel UpResultChannel = new PlcResultChannel("上线扫结果");
        public static readonly PlcResultChannel DownResultChannel = new PlcResultChannel("下线扫结果");
        public static readonly PlcResultChannel SizeResultChannel = new PlcResultChannel("尺寸结果");

        public static readonly PlcResultChannel[] AllResultChannels =
        {
            UpResultChannel,
            DownResultChannel,
            SizeResultChannel,
        };
    }
}
