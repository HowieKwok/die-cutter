using Frame_Core;
using Frame_MetalRun;
using LogTool;
using System;
using System.Collections.Generic;

namespace Mef_VisionFrame
{
    /// <summary>
    /// 五金模切的 PLC 触发侧：PLC 硬触发相机 → 出图 → 帧开始回调写"待检"。
    ///
    /// 这里只管"开始检"，不管"发结果"：结果由各工位判定完自己写
    /// （外观在 EA 判定后，尺寸在三路到齐后），一片一次，没有请求握手。
    ///
    /// 与激光模切的区别：激光是"PLC 请求 → 发已经到位的上一片"，
    /// 五金是"帧开始置待检 → 判定完写结论"，两侧没有中断、没有帧号推算。
    /// </summary>
    public partial class Server_五金模切
    {
        #region 注册

        /// <summary>
        /// 注册帧开始回调。任何一段失败只记日志，不让 Server 起不来
        /// （没有相机/没有 PLC 的调试机也要能进界面）。
        /// </summary>
        private void InitPlcResultCallbacks()
        {
            // 待检随帧号分配入队：只有真正被接受、拿到帧号的帧才产生 Pending，
            // 被去抖或帧号校验丢弃的帧不会留下多余的一条
            Frame_Core.业务参数类.五金模切帧序号.FrameAssigned -= OnFrameAssigned;
            Frame_Core.业务参数类.五金模切帧序号.FrameAssigned += OnFrameAssigned;

            // 告警的界面出口。核心层只发事件，这里决定显示成非模态窗；
            // 不订阅时告警只落日志，不会丢信息。
            Frame_Core.业务类.五金模切告警.Raising -= OnAlertRaised;
            Frame_Core.业务类.五金模切告警.Raising += OnAlertRaised;

            // 流程异常放弃的帧由这里补 NG：核心层只报"这一帧没了"，补值由业务层决定
            Frame_Core.业务参数类.五金模切帧序号.FrameAbandoned -= OnFrameAbandoned;
            Frame_Core.业务参数类.五金模切帧序号.FrameAbandoned += OnFrameAbandoned;

            try { RegisterUpCamFrameStart(); }
            catch (Exception ex) { Log4netRecord.ErrorLog.Error("五金模切注册上线扫帧开始回调失败：" + ex); }

            try { RegisterDownCamFrameStart(); }
            catch (Exception ex) { Log4netRecord.ErrorLog.Error("五金模切注册下线扫帧开始回调失败：" + ex); }

            try { RegisterSizeCamFrameStart(); }
            catch (Exception ex) { Log4netRecord.ErrorLog.Error("五金模切注册尺寸帧开始回调失败：" + ex); }
        }

        /// <summary>上线扫（模切主相机）取到图 → 写待检。</summary>
        private void RegisterUpCamFrameStart()
        {
            int camIndex;
            if (!Project.Instance.baseProDevice.TryGetCamIndex(CamRole.模切主相机, 0, out camIndex)) return;

            var camObj = GetCameraObj(camIndex);
            if (camObj == null) return;

            camObj.CamCallbackStartEvent -= OnUpFrameStart;
            camObj.CamCallbackStartEvent += OnUpFrameStart;
        }

        /// <summary>下线扫（模切从相机）取到图 → 写待检。与上线完全对称，互不影响。</summary>
        private void RegisterDownCamFrameStart()
        {
            int camIndex;
            if (!Project.Instance.baseProDevice.TryGetCamIndex(CamRole.模切从相机, 0, out camIndex)) return;

            var camObj = GetCameraObj(camIndex);
            if (camObj == null) return;

            camObj.CamCallbackStartEvent -= OnDownFrameStart;
            camObj.CamCallbackStartEvent += OnDownFrameStart;
        }

        /// <summary>
        /// 尺寸取到图 → 写待检。三路尺寸共用一个 Size 点位，三路都挂：
        /// 谁先出图谁写 3，三路写的是同一个值，不会覆盖已经判出的 1/2。
        ///
        /// 不能只挂第一路：那一路丢图时这一片就永远看不到"待检"，
        /// 另外两路却照常在跑，PLC 无从知道少检了一次。
        /// </summary>
        private void RegisterSizeCamFrameStart()
        {
            for (int occurrence = 0; occurrence < SizeStationCamCount; occurrence++)
            {
                int camIndex;
                if (!Project.Instance.baseProDevice.TryGetCamIndex(CamRole.尺寸检测, occurrence, out camIndex)) continue;

                var camObj = GetCameraObj(camIndex);
                if (camObj == null) continue;

                camObj.CamCallbackStartEvent -= OnSizeFrameStart;
                camObj.CamCallbackStartEvent += OnSizeFrameStart;
            }
        }

        /// <summary>取该路相机的 SDK 对象；相机未创建（禁用/未连接）时返回 null。</summary>
        private static DevMgmt.Cameras.SDK.CameraObj GetCameraObj(int camIndex)
        {
            var devs = Project.Instance.M_CamParam?.l_CameraDevs;
            if (devs == null || camIndex < 0 || camIndex >= devs.Count) return null;

            var dev = devs[camIndex];
            return dev?.Camera;
        }

        #endregion

        #region 帧开始回调

        public void OnUpFrameStart(long currentFrame)
        {
            // 待检不在这里投递：帧开始回调早于图像回调，此刻还没有帧号，无法与结论配对，
            // 而且被 50ms 去抖丢弃的重复触发会多写一次待检。
            // 投递点已移到帧号分配（五金模切帧序号.FrameAssigned）。
        }

        public void OnDownFrameStart(long currentFrame)
        {
            // 同上：待检随帧号分配投递
        }

        public void OnSizeFrameStart(long currentFrame)
        {
            // 同上：待检随帧号分配投递
        }

        /// <summary>
        /// 某一帧拿到帧号：立刻把"待检"投递给它所在工位的结果队列。
        /// 这是 Pending 与结论成对入队的唯一入口，顺序天然是 Pending 在前。
        /// </summary>
        private void OnFrameAssigned(int frameId, Frame_Core.业务参数类.五金模切帧序号.FrameStation station)
        {
            if (!Project.Instance.Is_Run) return;

            PlcResultChannel channel =
                station == Frame_Core.业务参数类.五金模切帧序号.FrameStation.上线扫 ? UpResultChannel :
                station == Frame_Core.业务参数类.五金模切帧序号.FrameStation.下线扫 ? DownResultChannel :
                SizeResultChannel;

            EnqueueResult(channel, frameId, PlcResult.Pending);
        }

        /// <summary>
        /// 某帧被流程放弃：补一条 NG，保证 PLC 每片必有一拍。
        ///
        /// 必须判重：异常可能发生在结论已入队之后（落盘、界面投递等环节），
        /// 无条件补会变成同一帧两条结论，把下一片错位。
        /// </summary>
        private void OnFrameAbandoned(int frameId,
            Frame_Core.业务参数类.五金模切帧序号.FrameStation station,
            string reason)
        {
            // 认领成功才入队。已有结论的帧（正常判完后又抛异常）由守卫直接拒绝，
            // 不再依赖「扫队列」判重——结论发布出队后队列里就找不到了
            if (!SettleAndEnqueue(frameId, station, true,
                    Frame_Core.业务参数类.SettlementSource.ServerJudge,
                    "帧被放弃：" + reason))
            {
                return;
            }

            Frame_Core.业务类.五金模切告警.Raise(
                Frame_Core.业务类.五金模切告警.帧结算异常,
                "帧结算异常",
                $"帧 {frameId} 未产生结论（{reason}），已按 NG 回传。");
        }

        /// <summary>
        /// 告警的跨线程投递。告警点分布在相机回调线程、视觉线程、发布器线程，
        /// 而窗体只能在 UI 线程创建——这里统一转一次，不让调用方各自处理。
        /// </summary>
        private void OnAlertRaised(string key, string title, string message)
        {
            System.Windows.Forms.Form main =
                System.Windows.Forms.Application.OpenForms.Count > 0
                    ? System.Windows.Forms.Application.OpenForms[0]
                    : null;
            if (main == null) return;

            Action retry = BuildRetryAction(key);
            Action show = () => Form_五金告警.ShowAlert(key, title, message, retry);
            if (main.InvokeRequired) main.BeginInvoke(show);
            else show();
        }

        /// <summary>
        /// 只有"通道暂停"类告警提供重试：帧号错位那类靠下一帧自动恢复，
        /// 给它们挂重试按钮会让人以为必须手动干预。
        /// </summary>
        private static Action BuildRetryAction(string key)
        {
            const string prefix = "PLC发布暂停_";
            if (key == null || !key.StartsWith(prefix)) return null;

            string name = key.Substring(prefix.Length);
            return delegate
            {
                foreach (PlcResultChannel channel in AllResultChannels)
                {
                    if (channel.Name != name) continue;
                    channel.Resume();
                    return;
                }
            };
        }

        #endregion

        #region 回传日志

        /// <summary>外观每一次回传写一行：哪条线、哪一帧、给了什么值。发和写的时机相同，便于对账。</summary>
        private void WriteAppearanceSendLog(bool isUp, bool isNG, long frameId)
        {
            List<RowData> row = new List<RowData>
            {
                new RowData { dataStr = DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_fff") },
                new RowData { dataStr = isUp ? "上线" : "下线" },
                new RowData { dataStr = frameId.ToString() },
                new RowData { dataStr = isNG ? "NG" : "OK" },
            };
            FileOperationCSV.WriteData_CSV(Data.Station1_DefectCamSendPath, Data.Station1_CamSendTitle.ToArray(), row.ToArray(), "缺陷工位");
            Data.Station1_SendShowQueue.Enqueue(row, 200);
        }

        /// <summary>尺寸每一次回传写一行。一个 SN 一行，等齐失败也记一行 NG。</summary>
        private void WriteSizeSendLog(long frameId, bool isNG)
        {
            List<RowData> row = new List<RowData>
            {
                new RowData { dataStr = DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_fff") },
                new RowData { dataStr = frameId.ToString() },
                new RowData { dataStr = isNG ? "NG" : "OK" },
            };
            FileOperationCSV.WriteData_CSV(Data.Station3_SizeCamSendPath, Data.Station3_CamSendTitle.ToArray(), row.ToArray(), "尺寸工位");
            Data.Station3_SendShowQueue.Enqueue(row, 200);
        }

        #endregion
    }
}