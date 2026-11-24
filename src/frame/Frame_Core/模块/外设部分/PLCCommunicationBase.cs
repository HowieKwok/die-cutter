using HslCommunication.Core.Net;
using HslCommunication.Profinet.Omron;
using HslCommunication;
using LogTool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HslCommunication.Robot.EFORT;
//using NPOI.SS.Formula.Functions;
using HslCommunication.Profinet.Siemens;
using HslCommunication.ModBus;
using NPOI.SS.Formula.Functions;
using static log4net.Appender.RollingFileAppender;
using System.Threading;
using HslCommunication.Core.Device;

namespace Frame_Core
{
    public class PLCCommunicationBase
    {
        public bool InitState = false;

        /// <summary>
        /// 本机型需要在符号型服务端（CIP）预注册的地址。
        /// 基类不猜：默认空，由持有地址表的子类覆写。
        /// 仿真侧据此注册符号，因此不需要认识任何具体机型。
        /// </summary>
        public virtual IEnumerable<string> GetRegisterSymbols()
        {
            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// 本连接自己的 PLC 参数（IP/端口/协议/字节序）。
        /// 声明在基类是因为每个机型各连一台 PLC，参数必须各存一份：
        /// 五金是 CIP 符号寻址，激光是 Fins 的 D 寄存器，共用一份会互相改掉对方的协议与地址。
        /// </summary>
        public PLCDevParam m_pLCDevParam = null;

        /// <summary>
        /// 写入失败数量
        /// </summary>
        public int WriteFailNum = 2;
        /// <summary>
        /// 读取失败数量
        /// </summary>
        public int ReadFailNum = 2;
        /// <summary>
        /// 写入失败计数
        /// </summary>
        public int WriteFailCount = 0;
        /// <summary>
        /// 读取失败计数
        /// </summary>
        public int ReadFailCount = 0;

        DeviceTcpNet networkDeviceBase = null;
        /// <summary>
        /// 读取失败警告监控
        /// </summary>
        public void ReadDataAlarmMonitoring()
        {
            if (ReadFailCount > ReadFailNum)
            {
                Disconnect();
                TJBJInfo.AlarmWriteState(AlarmName.主设备连接失败, new StopAlarmMessage() { StopAlarmState = StopAlarmStateEnum.停机, describe = "读取数据失败次数过多" });

            }
        }

        /// <summary>
        /// 读取失败警告监控处理
        /// </summary>
        private void ReadDataAlarmMonitoringProcessing(bool isSuccess)
        {
            if (!isSuccess) ReadFailCount++;
            ReadDataAlarmMonitoring();
        }

        /// <summary>
        /// 发送失败警告监控
        /// </summary>
        public void SendDataAlarmMonitoring()
        {
            if (WriteFailCount > WriteFailNum)
            {
                Disconnect();
                TJBJInfo.AlarmWriteState(AlarmName.主设备连接失败, new StopAlarmMessage() { StopAlarmState = StopAlarmStateEnum.停机, describe = "发送数据失败次数过多" });

            }
        }

        /// <summary>
        /// 发送失败警告监控处理
        /// </summary>
        private void SendDataAlarmMonitoringProcessing(bool isSuccess)
        {
            if (!isSuccess) WriteFailCount++;
            SendDataAlarmMonitoring();
        }

        /// <summary>
        /// 连接PLC
        /// </summary>
        /// <param name="plcType"></param>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public bool Connect(bool isConnect, PLCTypeEnum pLCTypeEnum, CommunicationProtocolEnum protocolEnum, string ip, int port,emDataFormat dataFormat=emDataFormat.ABCD,int resultWriteTimeoutMs=0)
        {
            try
            {
                if (!isConnect) return false;

                Disconnect();
                Thread.Sleep(200);

                if (true/*networkDeviceBase == null*/)
                {
                    switch (protocolEnum)
                    {
                        case CommunicationProtocolEnum.Fins:
                            networkDeviceBase = new OmronFinsNet();
                            networkDeviceBase.ByteTransform.DataFormat = (HslCommunication.Core.DataFormat)dataFormat;
                            break;
                        case CommunicationProtocolEnum.CIP:
                            networkDeviceBase = new OmronCipNet();
                            networkDeviceBase.ByteTransform.DataFormat = (HslCommunication.Core.DataFormat)dataFormat;
                            break;
                        case CommunicationProtocolEnum.S7:
                            if (pLCTypeEnum == PLCTypeEnum.Siemens1200)
                            {
                                networkDeviceBase = new SiemensS7Net(SiemensPLCS.S1200);
                                networkDeviceBase.ByteTransform.DataFormat = (HslCommunication.Core.DataFormat)dataFormat;
                            }
                            else if (pLCTypeEnum == PLCTypeEnum.Siemens1500)
                            {
                                networkDeviceBase = new SiemensS7Net(SiemensPLCS.S1500);
                                networkDeviceBase.ByteTransform.DataFormat = (HslCommunication.Core.DataFormat)dataFormat;
                            }

                            break;
                        case CommunicationProtocolEnum.ModbusTCP:
                            networkDeviceBase = new ModbusTcpNet();
                            networkDeviceBase.ByteTransform.DataFormat = (HslCommunication.Core.DataFormat)dataFormat;
                            break;
                        case CommunicationProtocolEnum.TCP:
                            break;
                        case CommunicationProtocolEnum.UDP:
                            break;
                        default:
                            break;
                    }
                }
                

                if (networkDeviceBase == null) return false;

                // 结果发布要求"单次写不阻塞超过 300ms"：一次 2000ms 的写会跨过两个心跳周期（1s），
                // "心跳优先"只在取下一个任务时生效，救不了正在阻塞的这次调用，PLC 必然判通讯断开。
                // 库默认 5000/10000，只能在实例上收紧。
                // 默认 0 = 不干预，激光等其它机型保持库默认行为。
                if (resultWriteTimeoutMs > 0)
                {
                    networkDeviceBase.ReceiveTimeOut = resultWriteTimeoutMs;
                    networkDeviceBase.ConnectTimeOut = System.Math.Max(resultWriteTimeoutMs, 1000);
                }

                networkDeviceBase.IpAddress = ip;
                networkDeviceBase.Port = port;
                networkDeviceBase.ByteTransform.DataFormat = (HslCommunication.Core.DataFormat)dataFormat;
                OperateResult connect = networkDeviceBase.ConnectServer();
                if (connect.IsSuccess) InitState = true;
                else InitState = false;

            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(ex);
            }
            return InitState;
        }

        /// <summary>
        /// 断开PLC
        /// </summary>
        public void Disconnect()
        {
            try
            {
                WriteFailCount = 0;
                ReadFailCount = 0;
                InitState = false;

                networkDeviceBase?.ConnectClose();
                networkDeviceBase?.Dispose();
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(ex);
            }
        }

        private void ReadResultProcessing<T>(CommunicationData<T> data, bool isSuccess, T value)
        {
            //保存旧值
            data.ReadDifferentStateFlag = false;
            if (isSuccess)
            {
                if (IsContentDifferent(value, data.Value))
                {
                    data.ReadDifferentStateFlag = true;
                    data.OldReadValue = data.Value;
                }
                data.Value = value;
            }
        }
        private bool IsContentDifferent<T>(T value1, T value2)
        {
            if (typeof(T).IsArray)
            {
                // 如果是数组类型，使用 SequenceEqual 方法比较内容
                Array array1 = value1 as Array;
                Array array2 = value2 as Array;
                return !array1.Cast<object>().SequenceEqual(array2.Cast<object>());
            }
            else
            {
                // 对于其他类型，使用 Equals 方法比较内容
                return !value1.Equals(value2);
            }
        }
        private void SaveWriteLog<T>(CommunicationData<T> data, OperateResult operateResult, log4net.ILog log)
        {
            //保存日志        
            string resultStr = "";
            string message = "";
            if (operateResult.IsSuccess) resultStr += "写入成功";
            else resultStr += "写入失败";
            message = $"{resultStr}-名称：{data.Name}-地址：[{data.Address}]-值：[{string.Join(",", data.Value)}]";

            log.Debug(message + "_" + operateResult.Message);
        }
        private void SaveReadLog<T>(CommunicationData<T> data, bool isSuccess, string resultMessage, log4net.ILog log)
        {
            //保存日志        
            string resultStr = "";
            string message = "";
            if (isSuccess) resultStr += "读取成功";
            else resultStr += "读取失败";
            message = $"{resultStr}-名称：{data.Name}-地址：[{data.Address}]-值：[{string.Join(",", data.Value)}]";

            log.Debug(message + "_" + resultMessage);
        }
        private void SaveReadLog<T>(CommunicationData<T>[] data, bool isSuccess, string resultMessage, log4net.ILog log)
        {
            //保存日志        
            string resultStr = "";
            string message = "";
            if (isSuccess) resultStr += "读取成功";
            else resultStr += "读取失败";
            message = $"{resultStr}-名称：{data[0].Name}-地址：[{data[0].Address}]-值：[{string.Join(",", data.Select(d => d.Value))}]";

            log.Debug(message + "_" + resultMessage);
        }
        private bool GetIsSaveReadLog(ReadLogSaveType readLogSaveType, bool isReadDifferentStateFlag)
        {
            bool isSaveLog = false;
            if (readLogSaveType == ReadLogSaveType.ReadDifferentState && isReadDifferentStateFlag) isSaveLog = true;
            else if (readLogSaveType == ReadLogSaveType.True) isSaveLog = true;
            return isSaveLog;
        }

        public void Write(CommunicationData<bool> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<bool[]> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<short> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        private volatile bool _resultWriteTimedOut;

        /// <summary>
        /// 结果专用写入：返回真实成败，且**不参与**"写失败次数过多"停机报警。
        ///
        /// 为什么必须另开一个入口：现有 Write 是同步 void，把 OperateResult 吞掉了，
        /// 调用方拿不到成败，无法做"失败重试 3 次"。而它的失败计数一旦累加，
        /// 第 3 次就会触发停机报警——与"3 次失败只提示、不停机"的业务决策直接冲突。
        ///
        /// 停机报警的语义是"通讯链路坏了"，结果重试是业务层策略，两者不能共用一个计数。
        ///
        /// 失败后下次调用会先断开连接再写：超时的那次写即使已返回，
        /// 底层连接也可能处于半死状态，继续在同一个 socket 上发下一条会让报文穿插。
        /// 断开后由库在下一次写时自动重连，代价是一次 TCP 握手，远小于错配风险。
        /// </summary>
        public bool TryWriteWithoutAlarm(CommunicationData<short> data, log4net.ILog log)
        {
            if (!InitState || data == null) return false;

            // 上一次写失败过：先断开，让本次写在干净连接上发生
            if (_resultWriteTimedOut)
            {
                _resultWriteTimedOut = false;
                try { networkDeviceBase.ConnectClose(); }
                catch (Exception ex) { Log4netRecord.ErrorLog.Debug("结果写重连前断开失败", ex); }
            }

            try
            {
                OperateResult result = networkDeviceBase.Write(data.Address, data.Value);
                if (!result.IsSuccess)
                {
                    _resultWriteTimedOut = true;
                    Log4netRecord.ErrorLog.Debug($"{data.Name} 写入失败：{result.Message}");
                }
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                _resultWriteTimedOut = true;
                Log4netRecord.ErrorLog.Debug(data.Name, ex);
                return false;
            }
        }

        public void Write(CommunicationData<short[]> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<int> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }
        public void Write(CommunicationData<int[]> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<long> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }
        public void Write(CommunicationData<long[]> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<ushort> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }
        public void Write(CommunicationData<ushort[]> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<uint> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }
        public void Write(CommunicationData<uint[]> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<ulong> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }
        public void Write(CommunicationData<ulong[]> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<float> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }
        public void Write(CommunicationData<float[]> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<double> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }
        public void Write(CommunicationData<double[]> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<byte[]> data, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }

        public void Write(CommunicationData<string> data, int length, bool isSaveLog, log4net.ILog log)
        {
            if (!InitState) return;
            string message = data.Name;
            try
            {
                OperateResult operateResult = networkDeviceBase.Write(data.Address, data.Value, length);

                SendDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                if (isSaveLog) SaveWriteLog(data, operateResult, log);
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
        }



        public bool Read(CommunicationData<bool> data, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<bool> operateResult = networkDeviceBase.ReadBool(data.Address);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<bool[]> data, ushort length, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<bool[]> operateResult = networkDeviceBase.ReadBool(data.Address, length);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<short> data, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<short> operateResult = networkDeviceBase.ReadInt16(data.Address);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<short[]> data, ushort length, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<short[]> operateResult = networkDeviceBase.ReadInt16(data.Address, length);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<int> data, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<int> operateResult = networkDeviceBase.ReadInt32(data.Address);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<int[]> data, ushort length, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<int[]> operateResult = networkDeviceBase.ReadInt32(data.Address, length);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<long> data, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<long> operateResult = networkDeviceBase.ReadInt64(data.Address);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<long[]> data, ushort length, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<long[]> operateResult = networkDeviceBase.ReadInt64(data.Address, length);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<ushort> data, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<ushort> operateResult = networkDeviceBase.ReadUInt16(data.Address);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<ushort[]> data, ushort length, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<ushort[]> operateResult = networkDeviceBase.ReadUInt16(data.Address, length);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<uint> data, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<uint> operateResult = networkDeviceBase.ReadUInt32(data.Address);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<uint[]> data, ushort length, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<uint[]> operateResult = networkDeviceBase.ReadUInt32(data.Address, length);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<ulong> data, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<ulong> operateResult = networkDeviceBase.ReadUInt64(data.Address);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<ulong[]> data, ushort length, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<ulong[]> operateResult = networkDeviceBase.ReadUInt64(data.Address, length);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<float> data, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<float> operateResult = networkDeviceBase.ReadFloat(data.Address);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<float[]> data, ushort length, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<float[]> operateResult = networkDeviceBase.ReadFloat(data.Address, length);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<double> data, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<double> operateResult = networkDeviceBase.ReadDouble(data.Address);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<double[]> data, ushort length, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<double[]> operateResult = networkDeviceBase.ReadDouble(data.Address, length);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        public bool Read(CommunicationData<string> data, ushort length, ReadLogSaveType readLogSaveType, log4net.ILog log)
        {
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<string> operateResult = networkDeviceBase.ReadString(data.Address, length);

                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                operateResult.Content = operateResult.Content.Trim(new char[] { '\0' });
                operateResult.Content = operateResult.Content.Trim(new char[] { ' ' });

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

              

                if (GetIsSaveReadLog(readLogSaveType, data.ReadDifferentStateFlag))
                {
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }


        /// <summary>
        /// 读取非零值并写入零值
        /// </summary>
        /// <param name="data"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        public bool ReadAndWriteZero(CommunicationData<short> data, log4net.ILog log, out short readValue)
        {
            readValue = 0;
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<short> operateResult = networkDeviceBase.ReadInt16(data.Address);
                readValue = operateResult.Content;
                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (operateResult.IsSuccess && operateResult.Content != 0)
                {
                    //保存日志
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);

                    // 写入零值
                    data.Value = 0;
                    Write(data, true, log);
                    data.Value = operateResult.Content;//返回读取到的原值
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }
        /// <summary>
        /// 读取非零值并写入零值
        /// </summary>
        /// <param name="data"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        public bool ReadAndWriteZero(CommunicationData<short> data, log4net.ILog log, bool isSaveLog, out short readValue)
        {
            readValue = 0;
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<short> operateResult = networkDeviceBase.ReadInt16(data.Address);
                readValue = operateResult.Content;
                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if ((operateResult.IsSuccess && operateResult.Content != 0)||
                    operateResult.IsSuccess && isSaveLog)
                {
                    //保存日志
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);

                    // 写入零值
                    data.Value = 0;
                    Write(data, true, log);
                    data.Value = operateResult.Content;//返回读取到的原值
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        /// <summary>
        /// 读取非零值并写入零值
        /// </summary>
        /// <param name="data"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        public bool ReadAndWriteZero(CommunicationData<float> data, log4net.ILog log, out float readValue)
        {
            readValue = 0;
            if (!InitState) return false;
            string message = data.Name;
            bool result = false;
            try
            {
                OperateResult<float> operateResult = networkDeviceBase.ReadFloat(data.Address);
                readValue = operateResult.Content;
                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                ReadResultProcessing(data, operateResult.IsSuccess, operateResult.Content);

                if (operateResult.IsSuccess && operateResult.Content != 0)
                {
                    //保存日志
                    SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);

                    // 写入零值
                    data.Value = 0;
                    Write(data, true, log);
                    data.Value = operateResult.Content;//返回读取到的原值
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }

        /// <summary>
        /// 读取非零值并写入零值
        /// </summary>
        /// <param name="data"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        public bool ReadAndWriteZero(CommunicationData<short>[] data, log4net.ILog log, out short[] shorts)
        {
            shorts = new short[data.Length];
            if (!InitState) return false;
            string message = "";
            bool result = false;
            try
            {
                message = data[0].Name;
                OperateResult<short[]> operateResult = networkDeviceBase.ReadInt16(data[0].Address, (ushort)data.Length);
                shorts = operateResult.Content;
                ReadDataAlarmMonitoringProcessing(operateResult.IsSuccess);

                for (int i = 0; i < data.Length; i++)
                {
                    ReadResultProcessing(data[i], i == 0 ? operateResult.IsSuccess : true, operateResult.Content[i]);
                }

                if (operateResult.IsSuccess)
                {
                    if (data.Where(t => t.Value != 0).Count() > 0)
                    {
                        //保存日志
                        SaveReadLog(data, operateResult.IsSuccess, operateResult.Message, log);

                        // 写入零值
                        for (int i = 0; i < data.Length; i++)
                        {
                            if (data[i].Value != 0)
                            {
                                data[i].Value = 0;
                                Write(data[i], true, log);
                                data[i].Value = operateResult.Content[i];//返回读取到的原值
                            }
                        }
                    }
                }

                result = operateResult.IsSuccess;
            }
            catch (Exception ex)
            {
                Log4netRecord.ErrorLog.Debug(message, ex);
            }
            return result;
        }
    }
}
