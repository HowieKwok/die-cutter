namespace Mef_VisionFrame
{
    /// <summary>五金模切的视图模型：Model + Server 的聚合，逻辑与激光模切一致。</summary>
    public class VM_五金模切 : VMBase
    {
        public Model_五金模切 Data
        {
            get { return (Model_五金模切)BaseData; }
        }

        public Server_五金模切 Server
        {
            get { return (Server_五金模切)BaseServer; }
        }

        public VM_五金模切(Model_五金模切 modelBase, Server_五金模切 serverBase)
            : base(modelBase, serverBase)
        {
        }
    }
}