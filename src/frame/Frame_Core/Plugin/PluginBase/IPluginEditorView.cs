namespace Frame_Core
{
    /// <summary>
    /// 插件编辑器视图的标记契约。
    ///
    /// 存在理由：插件窗体基类（FormPluginBase / WPFFormPluginBase）已从 Frame_Core
    /// 上移到应用层，Frame_Core 不能再通过具体基类判断“哪个类型是插件编辑器”。
    /// 而具体的 UI 基类又不能下沉回 Frame_Core（门禁禁止 Frame_Core 引用 UI 层）。
    ///
    /// 因此改由这个与 UI 无关的空接口承担识别职责：
    /// 插件窗体基类实现它，插件容器只认这个契约。
    /// </summary>
    public interface IPluginEditorView
    {
    }
}