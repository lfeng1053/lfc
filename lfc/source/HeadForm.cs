using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Lfc
{
    /// <summary>
    /// Incoming message box (design: Des/Asset 2 + Asset 3):
    /// grey rounded box with a fixed width; the sender's avatar sits INSIDE the box in the top
    /// corner on the screen-edge side; name (bold) + message on the other side. Grows for long messages.
    /// Keyboard-only: the mouse goes through it. Position comes from settings (default: right edge).
    /// </summary>
    internal sealed class HeadForm : LayeredForm
    {
        // ---- Layout values (at 1x), taken from design Asset 2 / Asset 3 ----
        public const float BoxWidth = 298;         // box width (fixed)
        public const float MinHeight = 52;         // box height for a short message
        public const float Radius = 16;            // corner radius
        public const float AvatarSize = 38;        // round avatar
        public const float AvatarMargin = 7;       // avatar distance from the box top / side
        public const float TextPadSide = 16;       // text padding on the side without the avatar
        public const float TextGap = 10;           // gap between text and avatar
        public const float TextPadY = 8;           // text padding top / bottom
        public const int MaxLines = 6;             // message text: at most 6 lines
        public static readonly Color BoxColor = Color.FromArgb(211, 210, 205);   // #D3D2CD
        public static readonly Color NameColor = Color.FromArgb(28, 30, 33);
        public static readonly Color TextColor = Color.FromArgb(44, 46, 50);
        public const string FontName = "Segoe UI";
        public const float FontSize = 13;          // px
        // Small dots under the box when several people are waiting
        public static readonly Color DotActive = Color.FromArgb(85, 84, 79);
        public static readonly Color DotIdle = Color.FromArgb(200, 211, 210, 205);
        public const float DotSize = 6, DotGap = 5, DotTop = 7;
        public const int MaxDots = 8;
        public const float EdgeMargin = 10;        // distance from the screen edge

        private readonly float S;
        private Image _avatar;
        private string _text = "", _name = "";
        private int _pageIndex, _pageCount;
        private Point _anchor;                     // top-left of the box on screen
        private RectangleF _boxRect;

        public HeadForm(float scale, Point anchor)
        {
            S = scale;
            _anchor = anchor;
            _avatar = Program.DefaultAvatar;
            Size = new Size(1, 1);
        }

        /// <summary>Top-left of the box (saved in settings).</summary>
        public Point AvatarAnchor { get { return _anchor; } }

        /// <summary>The box is on the right half of the screen → sender avatar on the right.</summary>
        public bool AvatarOnRight
        {
            get
            {
                Rectangle wa = Screen.FromPoint(_anchor).WorkingArea;
                return _anchor.X + BoxWidth * S / 2 > wa.Left + wa.Width / 2;
            }
        }

        /// <summary>Alias used by BubbleApp: true when the box is on the right half of the screen.</summary>
        public bool BubbleOnLeft { get { return AvatarOnRight; } }

        public Rectangle BubbleScreenRect
        {
            get { return new Rectangle(Left + (int)_boxRect.X, Top + (int)_boxRect.Y, (int)_boxRect.Width, (int)_boxRect.Height); }
        }

        /// <summary>Box + dots row (if any): the reply box is placed right below this area.</summary>
        public Rectangle ReplyAnchorRect
        {
            get
            {
                Rectangle r = BubbleScreenRect;
                if (_pageCount > 1) r.Height += (int)Math.Ceiling((DotTop + DotSize) * S);
                return r;
            }
        }

        public void SetContent(Image avatar, string name, IList<string> messages, int pageIndex, int pageCount)
        {
            _avatar = avatar ?? Program.DefaultAvatar;
            _name = name ?? "";
            _text = messages == null ? "" : string.Join("\n", messages);
            _pageIndex = pageIndex;
            _pageCount = pageCount;
            Render();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Render();
        }

        // ================= Drawing =================
        private void Render()
        {
            bool right = AvatarOnRight;
            float bw = BoxWidth * S, d = AvatarSize * S, am = AvatarMargin * S;
            float textW = bw - TextPadSide * S - (am + d + TextGap * S);

            using (var nameFont = new Font(FontName, FontSize * S, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var font = new Font(FontName, FontSize * S, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var mbmp = new Bitmap(1, 1))
            using (var mg = Graphics.FromImage(mbmp))
            {
                var nameFmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                var fmt = new StringFormat { Trimming = StringTrimming.EllipsisWord };
                float lineH = font.GetHeight(mg);
                float nameH = _name.Length == 0 ? 0 : nameFont.GetHeight(mg);
                SizeF ts = mg.MeasureString(_text.Length == 0 ? " " : _text, font, new SizeF(textW, lineH * MaxLines), fmt);
                float textH = Math.Min((float)Math.Ceiling(ts.Height), lineH * MaxLines);
                float contentH = nameH + textH;
                float bh = Math.Max(MinHeight * S, contentH + TextPadY * 2 * S);

                bool dots = _pageCount > 1;
                float dotsH = dots ? (DotTop + DotSize + 1) * S : 0;
                int W = (int)Math.Ceiling(bw), H = (int)Math.Ceiling(bh + dotsH);
                _boxRect = new RectangleF(0, 0, bw, bh);

                // Always keep it on screen
                Rectangle wa = Screen.FromPoint(_anchor).WorkingArea;
                _anchor = new Point(Math.Max(wa.Left, Math.Min(_anchor.X, wa.Right - W)),
                                    Math.Max(wa.Top, Math.Min(_anchor.Y, wa.Bottom - H)));
                Bounds = new Rectangle(_anchor.X, _anchor.Y, W, H);
                if (!IsHandleCreated) return;

                using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        Gfx.Setup(g);
                        using (var path = Gfx.RoundRect(_boxRect, Radius * S))
                        using (var b = new SolidBrush(BoxColor))
                            g.FillPath(b, path);

                        // avatar inside the box, top corner, on the screen-edge side
                        var av = new RectangleF(right ? bw - am - d : am, am, d, d);
                        Gfx.FillCircleImage(g, _avatar, av);

                        // text on the other side; vertically centered for short messages
                        float tx = right ? TextPadSide * S : am + d + TextGap * S;
                        float ty = (bh - contentH) / 2;
                        if (nameH > 0)
                            using (var b = new SolidBrush(NameColor))
                                g.DrawString(_name, nameFont, b, new RectangleF(tx, ty, textW, nameH + 1), nameFmt);
                        using (var b = new SolidBrush(TextColor))
                            g.DrawString(_text, font, b, new RectangleF(tx, ty + nameH, textW, textH + 1), fmt);

                        if (dots) DrawDots(g, bw / 2, bh + DotTop * S + DotSize * S / 2);
                    }
                    SetBitmap(bmp);
                }
            }
        }

        private void DrawDots(Graphics g, float cx, float cy)
        {
            int n = Math.Min(_pageCount, MaxDots);
            int first = Math.Max(0, Math.Min(_pageIndex - MaxDots / 2, _pageCount - n));
            float ds = DotSize * S, gap = DotGap * S;
            float x = cx - (n * ds + (n - 1) * gap) / 2;
            for (int i = 0; i < n; i++)
            {
                bool on = first + i == _pageIndex;
                float sz = on ? ds : ds * 0.8f;
                var r = new RectangleF(x + (ds - sz) / 2, cy - sz / 2, sz, sz);
                using (var b = new SolidBrush(on ? DotActive : DotIdle)) g.FillEllipse(b, r);
                x += ds + gap;
            }
        }

    }
}
