using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Lfc
{
    /// <summary>Shared drawing helpers.</summary>
    internal static class Gfx
    {
        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Crops the image to a centered square and resizes it (for clean circular avatars).</summary>
        public static Bitmap SquareCrop(Image src, int size)
        {
            int side = Math.Min(src.Width, src.Height);
            var srcRect = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, size, size), srcRect, GraphicsUnit.Pixel);
            }
            return bmp;
        }

        /// <summary>Draws an image inside an anti-aliased circle.</summary>
        public static void FillCircleImage(Graphics g, Image img, RectangleF r)
        {
            using (var tb = new TextureBrush(img, WrapMode.Clamp))
            {
                tb.TranslateTransform(r.X, r.Y);
                tb.ScaleTransform(r.Width / img.Width, r.Height / img.Height);
                g.FillEllipse(tb, r);
            }
        }

        public static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }
    }
}
