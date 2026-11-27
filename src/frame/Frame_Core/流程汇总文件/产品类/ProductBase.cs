using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace Frame_Core
{
    //产品基类
    /// <summary>
    /// 产品参数基类
    /// </summary>
    public class ProductBase : IDisposable
    {
        public ShowHalconWindow showHalconWindow { get; set; }
        public ObservableCollection<ShowHalconWindow> showHalconGroup { get; set; }


        //抓边插件查找的边，用于计算。
        public List<LineInfo> lineInfos { get; set; }
        public List<NgTypeInfo> NgTypeInfos { get; set; }
        public List<SizeInfo> sizeInfos { get; set; }


        /// <summary>
        /// 产品结果数据
        /// </summary>
        public ProductDataResult productDataResult { get; set; } = new ProductDataResult();

        /// <summary>
        /// 尺寸监控信息
        /// </summary>
        public List<SizeInfo> sizeMonitorInfos { get; set; } = new List<SizeInfo>();

        public List<LineInfo> m_lineShowInfos { get; set; }

        public DetectionTime detectionTime { get; set; } = new DetectionTime();

        /// <summary>本产品对应的业务帧号。异常补结算时用它，不依赖显示窗口是否已构造。</summary>
        public int FrameId { get; set; }

        /// <summary>
        /// 流程层判定失败但仍有可交付产物时置位：委托 Server 按 NG 结算。
        /// 默认 false，激光与其它机型行为不变。
        /// </summary>
        public bool ForceNg { get; set; }

        /// <summary>置位原因，用于告警与日志。</summary>
        public string ForceNgReason { get; set; }

        public ProductBase()
        {
            showHalconWindow = new ShowHalconWindow();
            lineInfos = new List<LineInfo>();
            NgTypeInfos = new List<NgTypeInfo>();
            sizeInfos = new List<SizeInfo>();
        }


        public virtual void Dispose()
        {
            if (showHalconGroup?.Count > 0)
            {
                foreach (var item in showHalconGroup)
                {
                    item?.Dispose();
                }
                showHalconGroup?.Clear();
            }

            showHalconWindow?.Dispose();
            lineInfos.Clear();
            foreach (var item in NgTypeInfos)
            {
                item?.Dispose();
            }
            NgTypeInfos.Clear();
            foreach (var item in sizeInfos)
            {
                item?.Dispose();
            }
            sizeInfos.Clear();
        }
    }
}
