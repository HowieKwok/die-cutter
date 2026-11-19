using System;
using System.Windows.Forms;

namespace Frame_Core
{
    /// <summary>
    /// WinForm 统一样式的接线点。
    ///
    /// 存在理由：Frame_Core 下有 35 个 Form 子类且没有公共基类，
    /// 而样式实现位于 Frame_UI.WinForms —— Frame_Core 不能引用它（分层门禁禁止）。
    /// 因此这里只暴露一个 Action&lt;Form&gt;，由宿主在启动时接到 WinFormsThemeApplier.Apply。
    /// 两边互不认识，依赖方向保持：Frame_Core ← 宿主 → Frame_UI.WinForms。
    ///
    /// 调用方约定：窗体在 InitializeComponent() 之后、Show 之前调用一次 Style(this)。
    /// 该方法幂等，重复调用无副作用。
    /// </summary>
    public static class FormStylingHook
    {
        /// <summary>宿主注册的样式下发器。未注册时为 null，调用即静默跳过。</summary>
        public static Action<Form> OnFormCreated;

        /// <summary>对单个窗体下发统一样式。未接线时为无操作。</summary>
        public static void Style(Form form)
        {
            if (form == null) return;
            var handler = OnFormCreated;
            if (handler == null) return;
            handler(form);
        }
    }
}