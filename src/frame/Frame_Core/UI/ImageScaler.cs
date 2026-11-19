using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Frame_Core
{
    /// <summary>
    /// 资源图标等比缩放。
    ///
    /// 存在理由：WinForms 的 Button 按图片原始像素绘制 Image，
    /// 资源里的 32/48px 图标塞进 34px 的工具栏按钮时只剩一圈边框，
    /// 缩放能力也不存在（BackgroundImageLayout 只对 BackgroundImage 生效，
    /// 且 Zoom 会铺满整个按钮，同样贴边）。工具栏图标统一走这里指定视觉尺寸。
    /// </summary>
    public static class ImageScaler
    {
        /// <summary>
        /// 按可用区域等比缩放，返回新图（不改动原图，原图通常是 Properties.Resources 的单例）。
        /// 源图为空时返回 null，调用方无需判空分支。
        /// </summary>
        public static Image Fit(Image source, int maxWidth, int maxHeight)
        {
            if (source == null || maxWidth <= 0 || maxHeight <= 0) return null;

            double scale = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            if (width == source.Width && height == source.Height) return source;

            Bitmap result = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return result;
        }
    }
}