using System;

namespace Frame_Core
{
    /// <summary>
    /// 流程种类。机型画像只声明"要建哪些流程"，
    /// 具体装什么模块、怎么跑由该机型的设备业务类实现。
    /// </summary>
    public enum ProcessKind
    {
        相机裁图,
        极片EA,
        纠偏检测,
        极片尺寸检测,
        极片尺寸带极耳检测,
    }

    /// <summary>一条流程的声明：挂在哪路相机上、叫什么名字、属于哪种流程。</summary>
    public sealed class ProcessSpec
    {
        public ProcessSpec(int cameraIndex, string processName, ProcessKind kind)
        {
            if (string.IsNullOrEmpty(processName)) throw new ArgumentException("流程名不能为空", "processName");

            CameraIndex = cameraIndex;
            ProcessName = processName;
            Kind = kind;
        }

        /// <summary>该流程取哪一路相机的图，对应 CamSlots 的下标。</summary>
        public int CameraIndex { get; private set; }

        /// <summary>流程名，同时是 Process 子目录名与存图前缀。</summary>
        public string ProcessName { get; private set; }

        public ProcessKind Kind { get; private set; }
    }
}