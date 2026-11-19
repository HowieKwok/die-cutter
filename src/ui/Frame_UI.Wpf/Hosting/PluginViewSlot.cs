using System;

namespace Frame_UI.Wpf
{
    /// <summary>
    /// 插件页面内容的唯一持有者。
    ///
    /// 存在理由：宿主此前用一个可空字段同时表达“当前内容”和“待释放资源”，
    /// 于是出现“字段已清空、Dispose 却再也没机会执行”的分叉。
    /// 把摘除与释放合并为同一个幂等操作后，分叉在类型层面不再可能发生。
    ///
    /// 约定：Release 是摘除内容的唯一路径；调用方不得绕过它直接改字段。
    /// </summary>
    public sealed class PluginViewSlot
    {
        private object content;

        /// <summary>当前内容。只读：写入必须经 Attach。</summary>
        public object Content
        {
            get { return content; }
        }

        /// <summary>放入内容。同一时刻只允许一个内容，重复放入会替换旧引用但不释放旧实例。</summary>
        public void Attach(object view)
        {
            content = view;
        }

        /// <summary>
        /// 释放并清空。幂等：内容为空或释放抛异常都不会留下半释放状态。
        /// 返回是否真的释放了一个实现了 IDisposable 的实例，供调用方判断。
        /// </summary>
        public bool Release()
        {
            object current = content;
            content = null;

            var disposable = current as IDisposable;
            if (disposable == null) return false;

            try
            {
                disposable.Dispose();
            }
            catch
            {
                // 释放失败也必须视为已摘除：向上传播会让窗口关闭流程中断。
            }

            return true;
        }
    }
}