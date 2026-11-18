using ControlBoard.板卡库;
using ControlBoard.服务类;
using Frame_Core;
using LogTool;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切的 PLC 结果回传：请求驱动的发送，不是"判完就发"。
    ///
    /// 三条触发链，与激光模切一一对应：
    ///   1. 缺陷工位：PLC 硬件中断（控制卡输入口 2）→ 取合片队列里对应产品的结论 → 发 重度NG/轻度NG/OK；
    ///   2. 尺寸工位：第一路尺寸相机"下一帧开始"→ 发上一片的 NG/OK；
    ///   3. 仿真：没有硬件中断，用第一路相机帧开始回调模拟请求，并且先补上当前帧号再发。
    ///
    /// 这样发出去的结果永远是"已经走到位的上一片"，PLC 拿到结果才放下一个触发。
    /// </summary>
    public partial class Server_五金模切
    {
        private IOC0640_OPERATE_DATA iOC0640_OPERATE_DATA = new IOC0640_OPERATE_DATA();
        private IOC0640_OPERATE m_IniCallBack;

        #region 注册

        /// <summary>
        /// 注册结果回传用到的所有回调。任何一段失败只记日志，不让 Server 起不来
        /// （没有板卡/没有相机的调试机也要能进界面）。
        /// </summary>
        private void InitPlcResultCallbacks()
        {
            try
            {
                注册缺陷相机帧开始();
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error("五金模切注册缺陷相机帧开始回调失败：" + ex);
            }

            try
            {
                注册尺寸相机帧开始();
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error("五金模切注册尺寸相机帧开始回调失败：" + ex);
            }

            try
            {
                注册缺陷请求中断();
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error("五金模切注册缺陷请求中断失败：" + ex);
            }
        }

        /// <summary>
        /// 缺陷相机（模切主相机）帧开始：只更新时间/帧号，发送由 PLC 请求触发。
        /// 仿真下不走这里——仿真以"合片完成"为请求，见 DataProcess。
        /// </summary>
        private void 注册缺陷相机帧开始()
        {
            int camIndex;
            if (!Project.Instance.baseProDevice.TryGetCamIndex(CamRole.模切主相机, 0, out camIndex)) return;

            var camObj = 取相机对象(camIndex);
            if (camObj != null)
            {
                camObj.FrameStartEvent -= 缺陷相机帧开始;
                camObj.FrameStartEvent += 缺陷相机帧开始;
            }
        }

        /// <summary>
        /// 第一路尺寸相机帧开始：发送上一片尺寸结果。
        /// 仿真下不走这里——仿真以"判定完成"为发送点，避免同一片被连发多次。
        /// </summary>
        private void 注册尺寸相机帧开始()
        {
            int camIndex;
            if (!Project.Instance.baseProDevice.TryGetCamIndex(CamRole.尺寸检测, 0, out camIndex)) return;

            var camObj = 取相机对象(camIndex);
            if (camObj != null)
            {
                camObj.CamCallbackStartEvent -= SizeCam_CamCallbackStart;
                camObj.CamCallbackStartEvent += SizeCam_CamCallbackStart;
            }
        }

        /// <summary>取该路相机的 SDK 对象；相机未创建（禁用/未连接）时返回 null。</summary>
        private static DevMgmt.Cameras.SDK.CameraObj 取相机对象(int camIndex)
        {
            var devs = Project.Instance.M_CamParam?.l_CameraDevs;
            if (devs == null || camIndex < 0 || camIndex >= devs.Count) return null;

            var dev = devs[camIndex];
            return dev?.Camera;
        }

        /// <summary>控制卡输入口 2 中断 = PLC 请求缺陷结果。</summary>
        private void 注册缺陷请求中断()
        {
            var card = ServiceCardManage.Instance.GetIControlCard();
            if (card == null)
            {
                Log4netRecord.ErrorLog.Error("五金模切无控制卡，缺陷工位结果中断未注册（仅仿真可发结果）");
                return;
            }

            ushort cardNo = Project.Instance.M_ParamControlCord.CardNo;

            card.Config_Intbitmode(cardNo, 2, 1, 1);

            iOC0640_OPERATE_DATA.cardno = cardNo;
            iOC0640_OPERATE_DATA.on_off = 1;
            iOC0640_OPERATE_DATA.bitno = 2;

            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(iOC0640_OPERATE_DATA));
            Marshal.StructureToPtr(iOC0640_OPERATE_DATA, ptr, false);

            m_IniCallBack = new IOC0640_OPERATE(IOC0640_OPERATE_Callback);
            card.Int_Enable(cardNo, m_IniCallBack, ptr);
        }

        #endregion

        #region 回调

        /// <summary>实机：缺陷相机帧开始，记录当前帧号与请求时刻。</summary>
        public void 缺陷相机帧开始(long currentFrame)
        {
            Data.DefectStation_CurrentFrame = currentFrame;
            Data.DefectStation_UpdateTime = DateTime.Now;
            MessageNotify.AddCommLog($"缺陷工位——帧开始信号：帧号{currentFrame}");
        }

        /// <summary>控制卡中断回调：输入口 2 有效表示 PLC 要缺陷结果。</summary>
        public uint IOC0640_OPERATE_Callback(IntPtr operate_data)
        {
            try
            {
                ExecuteMethodClass.ExecuteMethod(() =>
                {
                    int status = IOC0640.ioc_read_intportstatus(0, 0);

                    if ((status & 0x2) == 0x2) // 输入口2：缺陷结果请求
                    {
                        DefectStation_IOSendResult(0);
                    }
                });
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Error("五金模切控制卡中断处理异常：" + ex);
            }

            return 0;
        }

        public void SizeCam_CamCallbackStart(long currentFrame)
        {
            ExecuteMethodClass.ExecuteMethod(() => SizeStation_SendResult(currentFrame));
        }

        #endregion

        #region 结果发送

        /// <summary>
        /// 缺陷工位结果：按 PLC 请求的帧号取合片队列里对应的一片。
        /// 取不到（超时/丢片）就按重度 NG 发，与激光模切一致——宁可误判也不能让机构一直等。
        /// </summary>
        public void DefectStation_IOSendResult(long currentFrame)
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                DateTime startTime = DateTime.Now;
                double time = startTime.Subtract(Data.DefectStation_UpdateTime).TotalMilliseconds;

                int BackSideCount = Data.Craft?.Get_GlobalVariableItem("BackSideCount")?.Value.ToInt() ?? 0;
                long resultID = Data.DefectStation_CurrentFrame - BackSideCount;

                MessageNotify.AddCommLog($"缺陷工位——请求结果ID{resultID}=={Data.DefectStation_CurrentFrame}-{BackSideCount}");

                发送缺陷结果(resultID, time);
            });
        }

        /// <summary>
        /// 按产品号取合片并发结果。实机由 PLC 请求触发（帧号算产品号），
        /// 仿真由合片完成触发（直接给刚合出的产品号），两边都是一片一次。
        /// </summary>
        private void 发送缺陷结果(long resultID, double 等待毫秒)
        {
            DateTime startTime = DateTime.Now;

            Station1Product product = null;
            if (等待毫秒 > 0)
            {
                product = Data.Station1_Product.DequeueUntilMatch(p => p.ProductID == resultID);
            }

            if (product == null)
            {
                product = new Station1Product();
                product.Frame = -1;
                product.ProductID = -1;
                product.IsNG = true;
                product.ALine_DieCutEAProduct.IsSevereDefect = true;
                product.BLine_DieCutEAProduct.IsSevereDefect = true;
                MessageNotify.AddErrorLog($"缺陷工位——未找到结果ID{resultID}的合片数据，按重度NG发送");
            }

            bool 重度 = product.ALine_DieCutEAProduct.IsSevereDefect || product.BLine_DieCutEAProduct.IsSevereDefect;
            bool continuousNG = 缺陷工位_发送结果(product.IsNG, 重度, $"结果ID{resultID},等待{等待毫秒:0}ms");

            // 发送日志：列数必须与 Station1_CamSendTitle 一致
            string 结果 = !product.IsNG ? "OK" : 重度 ? "重度NG" : "轻度NG";
            List<RowData> 行 = new List<RowData>
            {
                new RowData { dataStr = DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_fff") },
                new RowData { dataStr = Data.DefectStation_CurrentFrame.ToString() },
                new RowData { dataStr = product.ProductID.ToString() },
                new RowData { dataStr = 等待毫秒.ToString("0") },
                new RowData { dataStr = 结果 },
                new RowData { dataStr = continuousNG.ToString() },
                new RowData { dataStr = ((int)(DateTime.Now - startTime).TotalMilliseconds).ToString() },
            };
            FileOperationCSV.WriteData_CSV(Data.Station1_DefectCamSendPath, Data.Station1_CamSendTitle.ToArray(), 行.ToArray(), "缺陷工位");
            Data.Station1_SendShowQueue.Enqueue(行, 200);
        }

        /// <summary>
        /// 尺寸工位结果：发的是"上一次判定"的结论，并校验它确实属于刚刚过去的那一片。
        /// 对不上就按 NG 发，避免把上一片的 OK 贴到这一片。
        /// </summary>
        public void SizeStation_SendResult(long currentFrame)
        {
            ExecuteMethodClass.ExecuteMethod(() =>
            {
                DateTime startTime = DateTime.Now;
                Station3Result product = Data.Station3_LastResult.GetValue();

                MessageNotify.AddCommLog($"尺寸工位——请求结果：帧号{currentFrame},目标帧号{currentFrame - 1}");

                if (currentFrame > 1 && currentFrame - 1 != product.Frame_Cam4)
                {
                    // 帧号对不上：结果已过期，按 NG 发
                    product.IsNG = true;
                }

                bool continuousNG = 尺寸工位_发送结果(product.IsNG,
                    $"请求帧{currentFrame},上一片帧号{product.Frame_Cam4}");

                写尺寸发送日志(currentFrame, product.Frame_Cam4, product.IsNG, continuousNG, startTime);
            });
        }

        /// <summary>尺寸工位发送日志。实机与仿真共用，保证两条路径的输出格式一致。</summary>
        private void 写尺寸发送日志(long currentFrame, long 发送帧号, bool isNG, bool continuousNG, DateTime startTime)
        {
            // 列数必须与 Station3_CamSendTitle 一致
            List<RowData> 行 = new List<RowData>
            {
                new RowData { dataStr = DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_fff") },
                new RowData { dataStr = currentFrame.ToString() },
                new RowData { dataStr = 发送帧号.ToString() },
                new RowData { dataStr = isNG ? "NG" : "OK" },
                new RowData { dataStr = continuousNG.ToString() },
                new RowData { dataStr = ((int)(DateTime.Now - startTime).TotalMilliseconds).ToString() },
            };
            FileOperationCSV.WriteData_CSV(Data.Station3_SizeCamSendPath, Data.Station3_CamSendTitle.ToArray(), 行.ToArray(), "尺寸工位");
            Data.Station3_SendShowQueue.Enqueue(行, 200);
        }

        #endregion
    }
}