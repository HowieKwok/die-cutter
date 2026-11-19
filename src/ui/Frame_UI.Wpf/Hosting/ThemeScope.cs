using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Frame_UI.Wpf
{
    /// <summary>
    /// 把 Frame_UI 的令牌投影成 WPF 资源键，并挂到目标元素上。
    ///
    /// 为什么需要它：本应用没有 WPF Application 对象（启动链是 WinForms 消息循环），
    /// 因此没有 Application.Current.Resources 可用来全局合并字典。
    /// 所有 WPF 窗口/控件必须在自身 Resources 里拿到同一份字典，
    /// 由 ThemedWindow / ThemedUserControl 在构造期调用 Ensure 统一完成。
    /// </summary>
    public static class ThemeScope
    {
        // 资源按清单资源名加载：本应用没有 WPF Application 对象（启动链是 WinForms 消息循环），
        // pack://application:,,,/ 在这类进程里解析不可靠；EmbeddedResource + XamlReader 与宿主形态无关。
        private const string ThemeResourceName = "Frame_UI.Wpf.Theme.ThemeResources.xaml";
        private const string ChromeResourceName = "Frame_UI.Wpf.Theme.Chrome.xaml";

        private static readonly ConditionalWeakTable<FrameworkElement, Injection> Injected =
            new ConditionalWeakTable<FrameworkElement, Injection>();

        // ConditionalWeakTable 不可枚举，重刷登记另用弱引用列表。
        private static readonly List<WeakReference<FrameworkElement>> Applied =
            new List<WeakReference<FrameworkElement>>();

        private static readonly Dictionary<string, SolidColorBrush> BrushCache =
            new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal);

        private static readonly object CacheLock = new object();

        static ThemeScope()
        {
            // 主题切换：清空画刷缓存（缓存按旧主题固化），并把登记过的元素重新注入一遍。
            // 注意：直接持有 Brush() 返回值（而非 DynamicResource）的视图不会自动更新，
            // 这类视图需要重新打开才能完全跟随新主题。
            ThemeService.ThemeChanged += delegate { ReapplyAll(); };
        }

        /// <summary>按当前令牌重刷所有登记过的元素。主题切换时由 ThemeChanged 驱动。</summary>
        public static void ReapplyAll()
        {
            List<FrameworkElement> targets = new List<FrameworkElement>();
            lock (CacheLock)
            {
                BrushCache.Clear();

                for (int i = Applied.Count - 1; i >= 0; i--)
                {
                    FrameworkElement element;
                    if (Applied[i].TryGetTarget(out element) && element != null)
                    {
                        targets.Add(element);
                    }
                    else
                    {
                        Applied.RemoveAt(i);
                    }
                }
            }

            // Ensure 会改动 Applied，必须在锁外、按快照执行。
            foreach (FrameworkElement element in targets)
            {
                Ensure(element);
            }
        }

        /// <summary>
        /// 一次注入的全部字典。三者必须一起记账：
        /// 只记令牌字典会让结构字典在重复 Ensure 时不断堆积，
        /// 而“重复调用不会堆积”正是本方法对外的承诺。
        /// </summary>
        private sealed class Injection
        {
            public ResourceDictionary Template;
            public ResourceDictionary Chrome;
            public ResourceDictionary Tokens;

            public void AttachTo(FrameworkElement element)
            {
                element.Resources.MergedDictionaries.Add(Template);
                element.Resources.MergedDictionaries.Add(Chrome);
                element.Resources.MergedDictionaries.Add(Tokens);
            }

            public void DetachFrom(FrameworkElement element)
            {
                var merged = element.Resources.MergedDictionaries;
                merged.Remove(Template);
                merged.Remove(Chrome);
                merged.Remove(Tokens);
            }
        }

        /// <summary>为元素注入主题字典；重复调用会替换旧字典，不会堆积。</summary>
        public static void Ensure(FrameworkElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            ResourceDictionary tokens = Build(ThemeService.Current);

            // 整批替换：上一次注入的三个字典一起摘除，再整体挂上新的。
            Injection previous;
            if (Injected.TryGetValue(element, out previous)) previous.DetachFrom(element);

            var injection = new Injection
            {
                Template = Load(ThemeResourceName),
                Chrome = Load(ChromeResourceName),
                Tokens = tokens
            };

            injection.AttachTo(element);

            Injected.Remove(element);
            Injected.Add(element, injection);

            lock (CacheLock)
            {
                Applied.RemoveAll(w => { FrameworkElement e; return !w.TryGetTarget(out e) || ReferenceEquals(e, element); });
                Applied.Add(new WeakReference<FrameworkElement>(element));
            }
        }

        /// <summary>按当前令牌取画刷。供代码构建 UI（卡片工厂）使用，避免散落 Color.FromRgb。</summary>
        public static SolidColorBrush Brush(string key)
        {
            lock (CacheLock)
            {
                SolidColorBrush cached;
                if (BrushCache.TryGetValue(key, out cached) && cached != null) return cached;

                UiColor color = Resolve(ThemeService.Current, key);
                var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
                brush.Freeze();
                BrushCache[key] = brush;
                return brush;
            }
        }

        /// <summary>分类标识色：未知分类回退到 Accent.Muted。</summary>
        public static SolidColorBrush CategoryBrush(string category)
        {
            UiColor color = ThemeService.ResolveCategoryColor(category);
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private static ResourceDictionary Load(string resourceName)
        {
            using (var stream = typeof(ThemeScope).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "主题资源未嵌入程序集（请确认 Frame_UI.Wpf 的 Theme/*.xaml 生成操作为 EmbeddedResource）：" + resourceName);
                }
                return (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);
            }
        }

        private static ResourceDictionary Build(UiTokens t)
        {
            var d = new ResourceDictionary();
            Add(d, "Surface.Canvas", t.Canvas);
            Add(d, "Surface.Panel", t.Panel);
            Add(d, "Surface.PanelDeep", t.PanelDeep);
            Add(d, "Grid.Line", t.GridLine);

            Add(d, "Line.Normal", t.Line);
            Add(d, "Line.Divider", t.Divider);
            Add(d, "Line.Subtle", t.LineSubtle);

            Add(d, "Text.Primary", t.TextPrimary);
            Add(d, "Text.Secondary", t.TextSecondary);
            Add(d, "Text.Hint", t.TextHint);

            Add(d, "Accent.Primary", t.AccentPrimary);
            Add(d, "Accent.Secondary", t.AccentSecondary);
            Add(d, "Accent.Muted", t.AccentMuted);

            Add(d, "State.Ok", t.StateOk);
            Add(d, "State.Fail", t.StateFail);
            Add(d, "State.Warn", t.StateWarn);
            Add(d, "State.Idle", t.StateIdle);

            Add(d, "Selection.Background", t.SelectionBg);
            Add(d, "Selection.Border", t.SelectionBorder);
            Add(d, "Button.Background", t.ButtonBg);
            Add(d, "Button.Border", t.ButtonBorder);
            Add(d, "Button.HoverBackground", t.ButtonHoverBg);

            d["Font.Family"] = new FontFamily(t.FontFamily);
            d["Font.Size.Title"] = (double)t.FontSizeTitle;
            d["Font.Size.Body"] = (double)t.FontSizeBody;
            d["Font.Size.Caption"] = (double)t.FontSizeCaption;
            d["Radius.Card"] = new CornerRadius(t.RadiusCard);
            d["Radius.Input"] = new CornerRadius(t.RadiusInput);
            d["Metric.ControlHeight"] = (double)t.ControlHeight;
            d["Spacing.Xs"] = new Thickness(t.SpacingXs);
            d["Spacing.Sm"] = new Thickness(t.SpacingSm);
            d["Spacing.Md"] = new Thickness(t.SpacingMd);
            d["Spacing.Lg"] = new Thickness(t.SpacingLg);
            return d;
        }

        private static void Add(ResourceDictionary d, string key, UiColor color)
        {
            var c = Color.FromRgb(color.R, color.G, color.B);
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            d[key] = brush;

            // 同时登记 Color 形态（键名加 .Color 后缀）。
            // 有些业务视图用本地调色板：<SolidColorBrush x:Key="PanelBg" .../>，
            // 它们被大量 StaticResource 引用。SolidColorBrush.Color 是依赖属性，
            // 但只吃 Color、不吃画刷，所以没有 Color 形态时这些文件只能逐个改使用点。
            // 有了 Color 形态，声明行改一处即可，使用点零改动。
            d[key + ".Color"] = c;
        }

        private static UiColor Resolve(UiTokens t, string key)
        {
            switch (key)
            {
                case "Surface.Canvas": return t.Canvas;
                case "Surface.Panel": return t.Panel;
                case "Surface.PanelDeep": return t.PanelDeep;
                case "Grid.Line": return t.GridLine;
                case "Line.Normal": return t.Line;
                case "Line.Divider": return t.Divider;
                case "Line.Subtle": return t.LineSubtle;
                case "Text.Primary": return t.TextPrimary;
                case "Text.Secondary": return t.TextSecondary;
                case "Text.Hint": return t.TextHint;
                case "Accent.Primary": return t.AccentPrimary;
                case "Accent.Secondary": return t.AccentSecondary;
                case "Accent.Muted": return t.AccentMuted;
                case "State.Ok": return t.StateOk;
                case "State.Fail": return t.StateFail;
                case "State.Warn": return t.StateWarn;
                case "State.Idle": return t.StateIdle;
                case "Selection.Background": return t.SelectionBg;
                case "Selection.Border": return t.SelectionBorder;
                case "Button.Background": return t.ButtonBg;
                case "Button.Border": return t.ButtonBorder;
                case "Button.HoverBackground": return t.ButtonHoverBg;
                default: return t.AccentMuted;
            }
        }
    }
}