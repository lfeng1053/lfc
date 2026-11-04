using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using static Lfc.NativeMethods;

namespace Lfc
{
    /// <summary>
    /// Quick reply box (design: Des/Asset 1): grey rounded box like the message box,
    /// YOUR avatar inside the box, opposite the sender's avatar; the text field fills the rest.
    ///   Enter         → send
    ///   Esc           → close the reply panel
    ///   PageUp/PageDn → switch to another person
    /// Only when sending fails is a red error line shown inside the box.
    /// Keyboard-only: the mouse goes through both layers.
    ///
    /// Two layers: ReplyBackground (smooth rounded box + avatar, per-pixel alpha) and this
    /// form (only the text field, same background colour) placed exactly inside the box.
    /// </summary>
    internal sealed class ReplyBox : Form
    {
        public event Action<string> SendRequested;
        public event Action Dismissed;            // Esc → close the panel
        public event Action<int> SwitchRequested; // -1 PageUp, +1 PageDown
        public event Action LostFocus2;           // clicked elsewhere

        // ---- Layout values (at 1x), taken from design Asset 1 ----
        public const float BoxHeight = 52;         // box height (without error)
        public const float GapAbove = 6;           // gap below the message box
        public const float ErrorHeight = 34;       // extra height when sending failed
        public static readonly Color BoxColor = HeadForm.BoxColor;
        public static readonly Color InputText = Color.FromArgb(28, 30, 33);
        public static readonly Color ErrorColor = Color.FromArgb(200, 40, 50);
        public const float FontSize = 13;          // px

        private readonly float S;
        private readonly ReplyBackground _bg;
        private readonly TextBox _box;
        private string _error;

        public ReplyBox(float scale)
        {
            S = scale;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = BoxColor;

            _box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = BoxColor,
                ForeColor = InputText,
                Font = new Font(HeadForm.FontName, FontSize * S, FontStyle.Regular, GraphicsUnit.Pixel),
                Dock = DockStyle.Fill
            };
            Controls.Add(_box);
            _box.HandleCreated += delegate
            {
                try { NativeMethods.SendMessage(_box.Handle, EM_SETCUEBANNER, new IntPtr(1), "Reply..."); } catch { }
            };

            _bg = new ReplyBackground(S);
            Owner = _bg;   // keeps the text field above its background
        }

        /// <summary>Your avatar (taken from your Facebook account).</summary>
        public Image MyAvatar
        {
            get { return _bg.Avatar; }
            set { _bg.Avatar = value; }
        }

        /// <summary>
        /// Shows the reply box right below the message box, same width.
        /// incomingAvatarOnRight: sender avatar on the right → your avatar on the left (and vice versa).
        /// </summary>
        public void ShowUnder(Rectangle above, bool incomingAvatarOnRight)
        {
            bool myAvatarLeft = incomingAvatarOnRight;
            int w = above.Width;
            int h = (int)Math.Ceiling((BoxHeight + (_error != null ? ErrorHeight : 0)) * S);
            int x = above.Left;
            int y = above.Bottom + (int)(GapAbove * S);
            Rectangle wa = Screen.FromRectangle(above).WorkingArea;
            if (y + h > wa.Bottom) y = above.Top - h - (int)(GapAbove * S);   // near the bottom of the screen → place above
            var box = new Rectangle(x, y, w, h);

            _bg.LayoutBox(box, myAvatarLeft, _error);
            if (!_bg.Visible) _bg.Show();

            // text field: the part without the avatar, centered in the first row (BoxHeight tall)
            float d = HeadForm.AvatarSize * S, am = HeadForm.AvatarMargin * S;
            int left = myAvatarLeft ? (int)(am + d + HeadForm.TextGap * S) : (int)(HeadForm.TextPadSide * S);
            int right = myAvatarLeft ? (int)(HeadForm.TextPadSide * S) : (int)(am + d + HeadForm.TextGap * S);
            int ih = _box.PreferredHeight;
            Bounds = new Rectangle(box.Left + left, box.Top + (int)((BoxHeight * S - ih) / 2), w - left - right, ih);

            ApplyOpacity();
            if (!Visible) Show();
            Activate();
            SetForegroundWindow(Handle);
            _box.Focus();
            _box.SelectionStart = _box.TextLength;
        }

        /// <summary>Clears the text and error state (when switching people or after sending).</summary>
        public void Reset()
        {
            _box.Clear();
            _error = null;
        }

        /// <summary>Send failed: keeps the typed text and shows the reason (on the next ShowUnder).</summary>
        public void ShowError(string text, string reason)
        {
            _box.Text = text ?? "";
            _error = "Not sent: " + reason + ". Press Enter to try again.";
        }

        public bool HasError { get { return _error != null; } }

        /// <summary>Text in the input field.</summary>
        public string Text2
        {
            get { return _box.Text; }
            set { _box.Text = value ?? ""; _box.SelectionStart = _box.TextLength; }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible && _bg.Visible) _bg.Hide();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Enter:
                    string text = _box.Text.Trim();
                    if (text.Length > 0 && SendRequested != null) SendRequested(text);
                    return true;
                case Keys.Escape:
                    if (Dismissed != null) Dismissed();
                    return true;
                case Keys.PageUp:
                    if (SwitchRequested != null) SwitchRequested(-1);
                    return true;
                case Keys.PageDown:
                    if (SwitchRequested != null) SwitchRequested(1);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible && LostFocus2 != null) LostFocus2();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;   // not shown in Alt+Tab
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT;   // mouse goes through (keyboard-only)
                return cp;
            }
        }

        /// <summary>Applies <see cref="LayeredForm.Alpha"/> to the text field layer.</summary>
        public void ApplyOpacity()
        {
            if (IsHandleCreated) SetLayeredWindowAttributes(Handle, 0, LayeredForm.Alpha, LWA_ALPHA);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyOpacity();   // layered (click-through) with the popup opacity
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bg.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Reply box background: smooth grey rounded box + your avatar + error line (if any).</summary>
    internal sealed class ReplyBackground : LayeredForm
    {
        private readonly float S;
        private Image _avatar;
        private Rectangle _box;
        private bool _avatarLeft;
        private string _error;

        public ReplyBackground(float scale)
        {
            S = scale;
            Size = new Size(1, 1);
        }

        public Image Avatar
        {
            get { return _avatar; }
            set { _avatar = value; if (Visible) Render(); }
        }

        public void LayoutBox(Rectangle box, bool avatarLeft, string error)
        {
            _box = box;
            _avatarLeft = avatarLeft;
            _error = error;
            Bounds = box;
            Render();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_box.Width > 0) Render();
        }

        private void Render()
        {
            if (!IsHandleCreated || _box.Width <= 0 || _box.Height <= 0) return;
            int W = _box.Width, H = _box.Height;
            float d = HeadForm.AvatarSize * S, am = HeadForm.AvatarMargin * S;
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    Gfx.Setup(g);
                    using (var path = Gfx.RoundRect(new RectangleF(0, 0, W, H), HeadForm.Radius * S))
                    using (var b = new SolidBrush(ReplyBox.BoxColor))
                        g.FillPath(b, path);

                    var av = new RectangleF(_avatarLeft ? am : W - am - d, am, d, d);
                    Gfx.FillCircleImage(g, _avatar ?? Program.DefaultAvatar, av);

                    if (_error != null)
                    {
                        float pad = HeadForm.TextPadSide * S;
                        var r = new RectangleF(pad, ReplyBox.BoxHeight * S - 4 * S, W - pad * 2, H - ReplyBox.BoxHeight * S);
                        using (var f = new Font(HeadForm.FontName, 11 * S, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var b = new SolidBrush(ReplyBox.ErrorColor))
                            g.DrawString(_error, f, b, r, new StringFormat { Trimming = StringTrimming.EllipsisWord });
                    }
                }
                SetBitmap(bmp);
            }
        }
    }
}
