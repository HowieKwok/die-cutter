using Frame_Core;
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
            if (!Project.Instance.Is_Run) return;
            WriteAppearancePending(true);
        }

        public void OnDownFrameStart(long currentFrame)
        {
            if (!Project.Instance.Is_Run) return;
            WriteAppearancePending(false);
        }

        public void OnSizeFrameStart(long currentFrame)
        {
            if (!Project.Instance.Is_Run) return;
            WriteSizePending();
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