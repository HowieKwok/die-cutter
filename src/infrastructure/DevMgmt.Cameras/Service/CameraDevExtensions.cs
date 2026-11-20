using DevMgmt.Cameras.Models;
using DevMgmt.Cameras.SDK;
using FrameCommunity.错误代码;
using LogTool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevMgmt
{
    public static class CameraDevExtensions
    {
        #region 设备连接、关闭
        /// <summary>
        /// 连接所有相机
        /// </summary>
        public static void ConnectAllCam(this List<CameraDev> cameraDevs)
        {
            try
            {
                foreach (var item in cameraDevs)
                {
                    ConnectCam(item);
                }
            
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
        }

        /// <summary>
        /// 关闭所有相机
        /// </summary>
        public static void CloseAllCam(this List<CameraDev> cameraDevs)
        {
            try
            {
                foreach (var item in cameraDevs)
                {
                    if (item.Camera != null)
                    {
                        CloseCam(item);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
        }

        /// <summary>
        /// 宿主注入：仿真初始化（体检 + PLC 服务端 + 相机工厂）。返回 false 表示阻断，项目初始化应中止。
        /// </summary>
        public static Func<bool> SimInitialize;

        /// <summary>宿主注入：相机参数 → 仿真图片目录。</summary>
        public static Func<CameraParameterBase, string> SimImageDirResolver;

        /// <summary>
        /// 仿真相机工厂。由宿主在初始化项目之前注入；保持为 null 即走真机采集。
        /// 这样核心层既不需要引用仿真项目，也不需要知道运行模式。
        /// </summary>
        public static Func<CameraDev, CameraObj> SimCameraFactory;

        /// <summary>
        /// 连接相机
        /// </summary>
        /// <param name="cameraDev"></param>
        public static void ConnectCam(this CameraDev cameraDev)
        {
            if (cameraDev == null)
            {
                ErrorWorks.ErrorMessageShow("cameraDev", ErrorWorks.GetErrorInfo((uint)ErrorCodeEnum.参数为空));
                return;
            }

            if (cameraDev.CameraParameter == null)
            {
                ErrorWorks.ErrorMessageShow("CameraParameter", ErrorWorks.GetErrorInfo((uint)ErrorCodeEnum.参数为空));
                return;
            }

            CamTypeEnum camTypeEnum = (CamTypeEnum)cameraDev.CameraParameter.DevType;


            try
            {
                CloseCam(cameraDev);

                // 仿真：宿主注入工厂时用仿真相机替换 SDK 采集实现，后续连接流程与真机一致
                if (SimCameraFactory != null)
                {
                    cameraDev.Camera = SimCameraFactory(cameraDev);
                    if (cameraDev.Camera != null)
                    {
                        cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                        cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                        cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                        return;
                    }
                }

                // 灰点面阵
                if (camTypeEnum == CamTypeEnum.灰点面阵)
                {
                    cameraDev.Camera = new CameraFly(cameraDev.CameraParameter.CameraSerial);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // Basler黑白相机
                if (camTypeEnum == CamTypeEnum.Basler黑白面阵)
                {
                    cameraDev.Camera = new CameraBasler(cameraDev.CameraParameter.CameraSerial);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // Basler彩色相机
                if (camTypeEnum == CamTypeEnum.Basler彩色面阵)
                {
                    cameraDev.Camera = new CameraBasler(cameraDev.CameraParameter.CameraSerial);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // 海康黑白面阵
                if (camTypeEnum == CamTypeEnum.海康黑白面阵)
                {
                    cameraDev.Camera = new CameraHK(cameraDev.CameraParameter.CameraSerial, cameraDev.CameraParameter.PCIP, cameraDev.CameraParameter.CameraIP);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // 海康彩色面阵
                if (camTypeEnum == CamTypeEnum.海康彩色面阵)
                {
                    cameraDev.Camera = new CameraHKRGB(cameraDev.CameraParameter.CameraSerial, cameraDev.CameraParameter.PCIP, cameraDev.CameraParameter.CameraIP);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // 海康U口黑白相机
                if (camTypeEnum == CamTypeEnum.海康U口黑白相机)
                {
                    cameraDev.Camera = new CameraHIK(cameraDev.CameraParameter.CameraSerial, CameraType.USB3Mono);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // 海康U口彩色相机
                if (camTypeEnum == CamTypeEnum.海康U口彩色相机)
                {
                    cameraDev.Camera = new CameraHIK(cameraDev.CameraParameter.CameraSerial, CameraType.USB3Color);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // 海康黑白线扫
                if (camTypeEnum == CamTypeEnum.海康线扫)
                {
                    cameraDev.Camera = new CameraHKControl(cameraDev.CameraParameter.CardSerial, cameraDev.CameraParameter.CameraSerial);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();

                    //cameraDev.Camera = new HIKCard(cameraDev.CameraParameter.CardSerial, cameraDev.CameraParameter.ConfigFiles, cameraDev.CameraParameter.Port);
                    //cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    //cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // DALSA采集卡线扫
                if (camTypeEnum == CamTypeEnum.DALSA采集卡线扫)
                {
                    cameraDev.Camera = new CameraDalsaCameraLink(cameraDev.CameraParameter.CardModel, cameraDev.CameraParameter.ConfigFiles, cameraDev.CameraParameter.Port);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // 埃科采集卡黑白线扫
                if (camTypeEnum == CamTypeEnum.埃科采集卡黑白线扫)
                {
                    cameraDev.Camera = new IKBoardSDK(cameraDev.CameraParameter.CardModel, cameraDev.CameraParameter.ConfigFiles, cameraDev.CameraParameter.Port, false);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // 埃科采集卡彩色线扫
                if (camTypeEnum == CamTypeEnum.埃科采集卡彩色线扫)
                {
                    cameraDev.Camera = new IKBoardSDK(cameraDev.CameraParameter.CardModel, cameraDev.CameraParameter.ConfigFiles, cameraDev.CameraParameter.Port, true);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // 埃科CXP黑白相机
                if (camTypeEnum == CamTypeEnum.埃科CXP黑白相机)
                {
                    cameraDev.Camera = new IKCXPBoard(cameraDev.CameraParameter.CardModel, cameraDev.CameraParameter.ConfigFiles, false);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }

                // 埃科CXP彩色相机
                if (camTypeEnum == CamTypeEnum.埃科CXP彩色相机)
                {
                    cameraDev.Camera = new IKCXPBoard(cameraDev.CameraParameter.CardModel, cameraDev.CameraParameter.ConfigFiles, true);
                    cameraDev.Camera.CamID = cameraDev.CameraParameter.ID;
                    cameraDev.ConnStatus = cameraDev.Camera.InitCam();
                    cameraDev.ConnStatus &= cameraDev.Camera.OpenCam();
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
                cameraDev.ConnStatus = false;
                return;
            }

            // 不支持的相机类型
            ErrorWorks.ErrorMessageShow($"{camTypeEnum}", ErrorWorks.GetErrorInfo((uint)ErrorCodeEnum.不支持此相机类型));
            return;
        }

        /// <summary>
        /// 关闭相机
        /// </summary>
        /// <param name="cameraDev"></param>
        public static void CloseCam(this CameraDev cameraDev)
        {
            try
            {
                if (cameraDev.Camera != null)
                {
                    cameraDev.Camera.DestroyCam();
                    cameraDev.Camera.DestroyObjects2();
                }
                cameraDev.ConnStatus = false;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
        }

        public static void StartGrabImage(this List<CameraDev> cameraDevs)
        {
            try
            {
                foreach (var item in cameraDevs)
                {
                    StartGrabImage(item);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
        }

        public static void StartGrabImage(this CameraDev cameraDev)
        {
            try
            {
                if (cameraDev.Camera?.ConnStatus ?? false)
                    if (!cameraDev.Camera.IsCameraStatue)
                        cameraDev.Camera.StartGrabImage();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
        }

        public static void StopGrabImage(this List<CameraDev> cameraDevs)
        {
            try
            {
                foreach (var item in cameraDevs)
                {
                    StopGrabImage(item);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
        }
        public static void StopGrabImage(this CameraDev cameraDev)
        {
            try
            {
                if (cameraDev.Camera?.ConnStatus ?? false)
                    cameraDev.Camera.StopGrabImage();

            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
        }

        public static void ResetEncoderCounter(this List<CameraDev> cameraDevs)
        {
            try
            {
                foreach (var item in cameraDevs)
                {
                    ResetEncoderCounter(item);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
        }

        public static void ResetEncoderCounter(this CameraDev cameraDev)
        {
            try
            {
                if (cameraDev.Camera?.ConnStatus ?? false)
                    cameraDev.Camera.ResetEncoderCounter();

            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
        }

 

        public static bool TryGetEncoderValue(this CameraDev cameraDev, out long encoderValue)
        {
            encoderValue = 0;
            try
            {
                if (cameraDev.Camera?.ConnStatus ?? false)
                   return  cameraDev.Camera.TryGetEncoderValue(out encoderValue);

            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
            return false;
        }

        public static CameraObj GetCamObj(this CamParam camParam, int camIndex)
        {
            try
            {
                if (camParam == null) return null;
                if (camParam.l_CameraDevs == null) return null;
                if (camIndex < 0 || camIndex >= camParam.l_CameraDevs.Count) return null;

                return camParam.l_CameraDevs[camIndex].Camera;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
            return null;
        }

        public static CameraParameterBase GetCamParam(this CamParam camParam, int camIndex)
        {
            try
            {
                if (camParam == null) return null;
                if (camParam.l_CameraDevs == null) return null;
                if (camIndex < 0 || camIndex >= camParam.l_CameraDevs.Count) return null;

                return camParam.l_CameraDevs[camIndex].CameraParameter;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.ToString());
                Log4netRecord.ErrorLog.Error(ex);
            }
            return null;
        }


        // 1. 获取所有相机名称
        public static string[] GetCam_Names(this CamParam camParam)
        {
            if (camParam == null) return new string[0];
            if (camParam.l_CameraDevs == null) return new string[0];


            return camParam.l_CameraDevs
                .Select(d => d?.CameraParameter?.CameraName ?? "NaN")
                .ToArray();

        }

        // 2. 获取单个相机名称
        public static string GetCam_Name(this CamParam camParam, int camIndex)
        {
            var param = camParam.GetCamParam(camIndex);
            return param?.CameraName ?? "NaN";
        }

        // 3. 获取X方向标定值
        public static double GetCam_CalibrationValue_X(this CamParam camParam, int camIndex)
        {
            var param = camParam.GetCamParam(camIndex);
            return param?.CalibrationValue_X ?? 1;
        }

        // 4. 获取Y方向标定值
        public static double GetCam_CalibrationValue_Y(this CamParam camParam, int camIndex)
        {
            var param = camParam.GetCamParam(camIndex);
            return param?.CalibrationValue_Y ?? 1;
        }

        // 5. 获取图像宽度
        public static double GetCam_ImageWidth(this CamParam camParam, int camIndex)
        {
            var param = camParam.GetCamParam(camIndex);
            return param?.ImageWidth ?? 1000;
        }

        // 6. 获取图像高度
        public static double GetCam_ImageHeight(this CamParam camParam, int camIndex)
        {
            var param = camParam.GetCamParam(camIndex);
            return param?.ImageHeight ?? 1000;
        }
        #endregion
    }
}
