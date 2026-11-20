using ControlBoard.Interface;
using ControlBoard.Model;
using ControlBoard.View;
using ControlBoard.板卡类型工具;
using FrameCommunity.DataOperation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
namespace ControlBoard.服务类
{
    public class ServiceCardManage
    {
        // 私有构造函数，防止外部 new
        private ServiceCardManage() { }

        // 静态只读锁对象，用于线程安全
        private static readonly object _lock = new object();


        public string strPath { get; set; } = "";

        // 静态实例变量
        private static ServiceCardManage _instance = null;

        // 公开的静态 Instance 属性
        public static ServiceCardManage Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ServiceCardManage();
                        }
                    }
                }
                return _instance;
            }
        }

        // 实例字段（原静态字段改为实例字段）
        private IControlCard _controlCard { get; set; }
        public ParamControlCord Param控制卡 { get; set; }

        // 实例方法 外部可调用
        public IControlCard GetIControlCard()
        {
            return _controlCard;
        }

        /// <summary>
        /// 装配控制卡。实现由调用方给出（机型画像提供），本类只负责持有与初始化。
        /// 卡为 null 属于设计违约，直接抛：宁可启动时明确报错，也不要让 Server 在远处空引用。
        /// </summary>
        public int Init(IControlCard controlCard, ParamControlCord paramControlCord)
        {
            if (controlCard == null)
            {
                throw new ArgumentNullException("controlCard",
                    "控制卡实现为空，请检查机型画像的 CreateControlCard。");
            }
            if (paramControlCord == null)
            {
                throw new ArgumentNullException("paramControlCord", "控制卡参数为空，请检查 ControlCordParam.json。");
            }

            Param控制卡 = paramControlCord;
            _controlCard = controlCard;

            int ret = _controlCard.Board_Init();
            if (ret == 0)
            {
                _controlCard.Set_Filter(paramControlCord.CardNo, paramControlCord.FilteTime);
            }
            return ret;
        }

        /// <summary>
        /// 按参数里的卡类型创建控制卡。只服务于"调试界面换卡类型"这条路径；
        /// 主流程走 Init，实现来自机型画像——仿真因此能换一张模拟卡，而这里不会成为第二条装配路径。
        /// </summary>
        public int InitServiceCard(ParamControlCord paramControlCord)
        {
            if (paramControlCord == null)
            {
                throw new ArgumentNullException("paramControlCord", "控制卡参数为空，请检查 ControlCordParam.json。");
            }

            switch (paramControlCord.ControlType)
            {
                case EnumControlType.IO0640:
                    return Init(new ControlIOC0640(), paramControlCord);
                default:
                    // 枚举新增成员时，编译器不会提醒这里漏了分支，只能显式拒绝。
                    throw new NotSupportedException("未支持的控制卡类型：" + paramControlCord.ControlType);
            }
        }


        MainView View ;
        public void ShowWindow()
        {
            View = new MainView(_controlCard, Param控制卡);
            View.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            View.ShowDialog();
            // 窗口显示逻辑
        }
    }
}
