using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Lfc
{
    /// <summary>
    /// Settings window (tray icon → right-click → Settings):
    ///   Opacity of the popups, hotkeys (bindings), start with Windows, Facebook account (sign in / log out).
    /// Global hotkeys are paused while this window is open so new combinations can be recorded.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        /// <summary>Values chosen by the user (read after DialogResult.OK).</summary>
        public int OpacityPercent { get { return _opacity.Value; } }
        public string ReplyHotkey { get { return _reply.Text; } }
        public string PrevHotkey { get { return _prev.Text; } }
        public string NextHotkey { get { return _next.Text; } }
        public bool StartWithWindows { get { return _startup.Checked; } }

        public enum Account { None, SignIn, LogOut }
        /// <summary>Account action chosen with the Sign in / Log out button (done after the window closes).</summary>
        public Account AccountAction { get; private set; }

        /// <summary>Raised while the opacity slider moves (live preview on screen).</summary>
        public event Action<int> OpacityPreview;

        private readonly TrackBar _opacity;
        private readonly Label _opacityValue;
        private readonly PreviewBox _preview;
        private readonly TextBox _reply, _prev, _next;
        private readonly CheckBox _startup;
        private readonly Label _error;

        public SettingsForm(Settings s, bool startWithWindows, bool signedIn)
        {
            Text = AppInfo.Name + " settings";
            Icon = Program.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(420, 460);
            BackColor = Color.White;
            KeyPreview = true;

            int x = 20, y = 16, w = 380;

            // ---- Opacity ----
            Controls.Add(Header("Opacity", x, y)); y += 28;
            _opacity = new TrackBar
            {
                Minimum = Settings.MinOpacity, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10,
                Value = Math.Max(Settings.MinOpacity, Math.Min(100, s.Opacity)),
                Location = new Point(x - 4, y), Size = new Size(w - 56, 32)
            };
            _opacityValue = new Label
            {
                AutoSize = false, TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(x + w - 56, y + 2), Size = new Size(56, 24)
            };
            _preview = new PreviewBox { Location = new Point(x, y + 40), Size = new Size(w, 70) };
            _opacity.ValueChanged += delegate
            {
                _opacityValue.Text = _opacity.Value + " %";
                _preview.Alpha = _opacity.Value;
                if (OpacityPreview != null) OpacityPreview(_opacity.Value);
            };
            _opacityValue.Text = _opacity.Value + " %";
            _preview.Alpha = _opacity.Value;
            Controls.Add(_opacity);
            Controls.Add(_opacityValue);
            Controls.Add(_preview);
            y += 40 + 70 + 18;

            // ---- Hotkeys ----
            Controls.Add(Header("Hotkeys", x, y)); y += 26;
            Controls.Add(Note("Click a box, then press the new key combination (with Ctrl, Alt, Shift or Win).", x, y, w)); y += 38;
            _reply = HotkeyRow("Open / close", s.ReplyHotkey, Settings.DefaultHotkey, x, ref y, w);
            _prev = HotkeyRow("Previous person", s.PrevHotkey, Settings.DefaultPrevHotkey, x, ref y, w);
            _next = HotkeyRow("Next person", s.NextHotkey, Settings.DefaultNextHotkey, x, ref y, w);
            Controls.Add(Note("Inside the reply box: Enter = send, Esc = close, PageUp / PageDown = switch person.", x, y, w));
            y += 44;

            // ---- Startup ----
            Controls.Add(Header("Startup", x, y)); y += 26;
            _startup = new CheckBox { Text = "Start with Windows", Checked = startWithWindows, Location = new Point(x, y), AutoSize = true };
            Controls.Add(_startup);
            y += 38;

            // ---- Account ----
            Controls.Add(Header("Facebook account", x, y)); y += 28;
            var status = new Label
            {
                AutoSize = false, Location = new Point(x, y + 5), Size = new Size(w - 110, 20),
                Text = signedIn ? "Signed in" : "Not signed in",
                ForeColor = signedIn ? Color.FromArgb(40, 130, 70) : Color.FromArgb(200, 40, 50)
            };
            var account = new Button
            {
                Text = signedIn ? "Log out" : "Sign in",
                Location = new Point(x + w - 100, y), Size = new Size(100, 28)
            };
            account.Click += delegate
            {
                if (signedIn)
                {
                    var r = MessageBox.Show(this, "Log out of Facebook in " + AppInfo.Name + "?\n\n" +
                        "New messages will not appear until you sign in again.", AppInfo.Name,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes) return;
                    AccountAction = Account.LogOut;
                }
                else AccountAction = Account.SignIn;
                if (Validate2()) { DialogResult = DialogResult.OK; Close(); }   // also saves the other settings
            };
            Controls.Add(status);
            Controls.Add(account);
            y += 40;

            // ---- Error line + buttons ----
            _error = new Label
            {
                AutoSize = false, ForeColor = Color.FromArgb(200, 40, 50),
                Location = new Point(x, y), Size = new Size(w, 20)
            };
            Controls.Add(_error);
            y += 24;

            var ok = new Button { Text = "Save", DialogResult = DialogResult.None, Size = new Size(88, 30), Location = new Point(x + w - 88 * 2 - 8, y) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(88, 30), Location = new Point(x + w - 88, y) };
            ok.Click += delegate { if (Validate2()) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(420, y + 30 + 16);
        }

        private bool Validate2()
        {
            string a = _reply.Text, b = _prev.Text, c = _next.Text;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, c, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b, c, StringComparison.OrdinalIgnoreCase))
            {
                _error.Text = "Each hotkey must be different.";
                return false;
            }
            return true;
        }

        // ================= Building blocks =================
        private static Label Header(string text, int x, int y)
        {
            return new Label
            {
                Text = text, AutoSize = true, Location = new Point(x, y),
                Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Color.FromArgb(28, 30, 33)
            };
        }

        private static Label Note(string text, int x, int y, int w)
        {
            return new Label
            {
                Text = text, AutoSize = false, Location = new Point(x, y), Size = new Size(w, 34),
                ForeColor = Color.FromArgb(110, 112, 118)
            };
        }

        private TextBox HotkeyRow(string label, string value, string fallback, int x, ref int y, int w)
        {
            Controls.Add(new Label { Text = label, AutoSize = false, Location = new Point(x, y + 4), Size = new Size(120, 20) });
            var box = new TextBox
            {
                Text = value, ReadOnly = true, BackColor = Color.FromArgb(244, 245, 247), Cursor = Cursors.Hand,
                Location = new Point(x + 124, y), Size = new Size(w - 124 - 70, 24), ShortcutsEnabled = false
            };
            string before = value;
            box.Enter += delegate { before = box.Text; box.BackColor = Color.FromArgb(255, 248, 220); _error.Text = ""; };
            box.Leave += delegate { box.BackColor = Color.FromArgb(244, 245, 247); };
            box.KeyDown += (s, e) =>
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (e.KeyCode == Keys.Tab) { SelectNextControl(box, !e.Shift, true, true, true); return; }
                string spec = HotkeyWindow.FromKeyEvent(e);
                if (spec != null) { box.Text = spec; _error.Text = ""; }
                else if (e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.Menu)
                    _error.Text = "Use Ctrl, Alt, Shift or Win together with a key.";
            };
            var reset = new Button { Text = "Default", Location = new Point(x + w - 64, y - 1), Size = new Size(64, 26) };
            reset.Click += delegate { box.Text = fallback; _error.Text = ""; };
            Controls.Add(box);
            Controls.Add(reset);
            y += 32;
            return box;
        }

        // Keys like Escape / Enter typed into a hotkey box must not close the dialog
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (ActiveControl is TextBox && ((TextBox)ActiveControl).ReadOnly &&
                (keyData & Keys.KeyCode) != Keys.Tab)
                return false;
            return base.ProcessDialogKey(keyData);
        }

        /// <summary>Small preview of the message box at the chosen opacity, over a striped background.</summary>
        private sealed class PreviewBox : Control
        {
            private int _alpha = 100;
            public int Alpha { get { return _alpha; } set { _alpha = value; Invalidate(); } }

            public PreviewBox() { DoubleBuffered = true; ResizeRedraw = true; }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // striped "desktop" so the transparency is visible
                using (var bg = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(120, 140, 200), Color.FromArgb(70, 90, 150)))
                using (var bgPath = Gfx.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 8))
                    g.FillPath(bg, bgPath);

                int a = (int)Math.Round(_alpha * 2.55);
                var box = new RectangleF(Width - 12 - 260, (Height - 50) / 2f, 260, 50);
                using (var path = Gfx.RoundRect(box, 16))
                using (var b = new SolidBrush(Color.FromArgb(a, HeadForm.BoxColor)))
                    g.FillPath(b, path);
                var av = new RectangleF(box.Right - 7 - 36, box.Y + 7, 36, 36);
                using (var img = new System.Drawing.Imaging.ImageAttributes())
                {
                    var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = _alpha / 100f };
                    img.SetColorMatrix(cm);
                    using (var clip = new GraphicsPath())
                    {
                        clip.AddEllipse(av);
                        var state = g.Save();
                        g.SetClip(clip);
                        var src = Program.DefaultAvatar;
                        g.DrawImage(src, Rectangle.Round(av), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, img);
                        g.Restore(state);
                    }
                }
                using (var nf = new Font("Segoe UI", 9f, FontStyle.Bold))
                using (var tf = new Font("Segoe UI", 9f))
                using (var nb = new SolidBrush(Color.FromArgb(a, HeadForm.NameColor)))
                using (var tb = new SolidBrush(Color.FromArgb(a, HeadForm.TextColor)))
                {
                    g.DrawString("Preview", nf, nb, box.X + 14, box.Y + 8);
                    g.DrawString("This is how a message looks", tf, tb, box.X + 14, box.Y + 25);
                }
            }
        }
    }
}
