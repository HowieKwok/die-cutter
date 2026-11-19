using System;
using System.Globalization;

namespace Frame_UI
{
    /// <summary>
    /// 与 UI 框架无关的颜色值。
    /// 令牌层不引用 System.Drawing / System.Windows.Media，由各适配器自行转换，
    /// 这样同一份令牌可以同时喂给 WPF 与 WinForms，且不会把任一方拖进令牌程序集。
    /// </summary>
    public readonly struct UiColor : IEquatable<UiColor>
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public UiColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static UiColor FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) throw new ArgumentException("色值不能为空", nameof(hex));

            string text = hex.Trim().TrimStart('#');
            if (text.Length == 3)
            {
                text = string.Concat(
                    text[0], text[0], text[1], text[1], text[2], text[2]);
            }
            if (text.Length != 6) throw new FormatException("色值必须是 #RGB 或 #RRGGBB：" + hex);

            byte r = byte.Parse(text.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new UiColor(r, g, b);
        }

        public string ToHex()
        {
            return "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
        }

        public bool Equals(UiColor other)
        {
            return R == other.R && G == other.G && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is UiColor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (R << 16) | (G << 8) | B;
        }

        public override string ToString()
        {
            return ToHex();
        }
    }
}