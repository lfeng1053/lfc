using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using static Lfc.NativeMethods;

namespace Lfc
{
    /// <summary>
    /// Window with per-pixel alpha, used for the message box and the reply box background
    /// so rounded corners stay smooth. Subclasses draw into a Bitmap and call <see cref="SetBitmap"/>.
    /// The mouse goes straight through it (WS_EX_TRANSPARENT): popups are keyboard-only.
    /// </summary>
    internal class LayeredForm : Form
    {
        /// <summary>Opacity of every popup (0–255), set from the Settings window.</summary>
        public static byte Alpha = 255;

        /// <summary>true = clicking it does not take focus from other windows.</summary>
        protected bool NoActivate = true;

        public LayeredForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
                if (NoActivate) cp.ExStyle |= WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (NoActivate && m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(MA_NOACTIVATE);
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Pushes a 32-bit ARGB bitmap to the window.</summary>
        public void SetBitmap(Bitmap bmp)
        {
            if (!IsHandleCreated) return;
            if (bmp.PixelFormat != PixelFormat.Format32bppArgb)
                throw new ArgumentException("Bitmap must be 32bppArgb");

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memDc, hBitmap);
                var size = new SIZE(bmp.Width, bmp.Height);
                var src = new POINT(0, 0);
                var dst = new POINT(Left, Top);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = Alpha,
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    SelectObject(memDc, oldBitmap);
                    DeleteObject(hBitmap);
                }
                DeleteDC(memDc);
            }
        }
    }
}
