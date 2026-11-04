using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace Lfc
{
    /// <summary>
    /// Runs the whole app. One flow only:
    ///   message arrives → show avatar + name + text → press the hotkey (or click) → type → Enter sends.
    /// Several people at once: Ctrl+Alt+PageUp/PageDown to look at each person and pick who to
    /// reply to. Inside the reply box: PageUp/PageDown. The popups ignore the mouse (keyboard-only).
    /// Nothing else is shown; only a failed send shows an error.
    /// </summary>
    internal sealed class BubbleApp : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = AppInfo.Name;
        private const int HeadVisibleMs = 12000;   // the message box hides after 12 s if you don't reply

        private readonly Settings _settings;
        private readonly float S;
        private readonly HeadForm _head;
        private readonly ReplyBox _reply;
        private readonly ChatForm _chat;             // hidden Messenger page (receives + sends)
        private readonly NotifyIcon _tray;
        private readonly HotkeyWindow _hotkey;
        private readonly SynchronizationContext _ui;
        private readonly Timer _hideTimer = new Timer { Interval = HeadVisibleMs };

        private readonly List<Person> _people = new List<Person>();   // people waiting, newest first
        private Person _current;                                       // person currently shown
        private bool _replying;                                        // reply box is open
        private bool _gettingMyAvatar;
        // What you just sent to each person: if the page reports that same text back, it is yours → ignore
        private readonly Dictionary<string, KeyValuePair<string, DateTime>> _sent =
            new Dictionary<string, KeyValuePair<string, DateTime>>();

        public BubbleApp(EventWaitHandle showSignal, bool autostart)
        {
            _settings = Settings.Load();
            LayeredForm.Alpha = ToAlpha(_settings.Opacity);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            // --- message box ---
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int bw = (int)Math.Ceiling(HeadForm.BoxWidth * S), m = (int)(HeadForm.EdgeMargin * S);
            var anchor = new Point(Math.Min(_settings.BubbleX ?? int.MaxValue, wa.Right - bw - m),
                                   _settings.BubbleY ?? wa.Top + wa.Height / 3);
            _head = new HeadForm(S, anchor);
            _hideTimer.Tick += delegate { _hideTimer.Stop(); if (!_replying) _head.Hide(); };

            // --- reply box ---
            _reply = new ReplyBox(S);
            _reply.SendRequested += OnSend;
            _reply.Dismissed += DismissCurrent;
            _reply.SwitchRequested += Switch;
            _reply.LostFocus2 += () =>
            {
                // Clicked elsewhere: put the reply box away, keep the unsent text
                if (!_replying) return;   // hidden because of send / dismiss, not a click elsewhere
                if (_current != null) _current.Draft = _reply.Text2;
                _reply.Hide();
                _replying = false;
                RestartHideTimer();
            };

            // --- hidden Messenger page ---
            _chat = new ChatForm(_settings, S);
            _chat.NotificationArrived += OnMessage;
            _chat.SignedInChanged += signedIn =>
            {
                if (!signedIn) ShowSignIn();                       // signed out → sign-in window
                else if (_chat.UserVisible) _chat.Hide();         // signed in → it disappears
            };
            _chat.Location = new Point(-32000, -32000);
            _chat.Show();
            _chat.Hide();
            _chat.InitWebAsync().ContinueWith(t => { var ignored = t.Exception; });   // runs in the background
            if (_settings.IsFirstRun) _settings.Save();

            // --- tray icon ---
            _tray = new NotifyIcon
            {
                Icon = Program.AppIcon,
                Text = TrayText(),
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };
            // Opened by hand: say that the app is running (it has no window until a message arrives).
            // Not shown when started with Windows, nor on the first run (the sign-in window opens instead).
            if (!autostart && !_settings.IsFirstRun) ShowRunningNotice();

            // Left-click on the tray icon does nothing; right-click opens the settings menu
            // (handled by ContextMenuStrip).

            // --- hotkeys ---
            _hotkey = new HotkeyWindow();
            RegisterHotkeys();

            // --- app started a second time ---
            ThreadPool.RegisterWaitForSingleObject(showSignal,
                (st, to) => _ui.Post(_ => { if (_people.Count > 0) BeginReply(); else ShowRunningNotice(); }, null),
                null, Timeout.Infinite, false);
        }

        // ================= Incoming messages =================
        private async void OnMessage(PageNotification n)
        {
            if (string.IsNullOrEmpty(n.Title) || string.IsNullOrEmpty(n.Body)) return;
            string key = Person.KeyOf(n.Title);
            KeyValuePair<string, DateTime> mine;
            if (_sent.TryGetValue(key, out mine) && (DateTime.Now - mine.Value).TotalSeconds < 60 &&
                Person.KeyOf(n.Body).EndsWith(Person.KeyOf(mine.Key)))
            {
                Log.Write("Ignored: this is the message you just sent");
                return;
            }
            Person p = _people.Find(x => x.Key == key);
            if (p == null)
            {
                p = new Person { Key = key };
                _people.Insert(0, p);
            }
            p.Name = n.Title;
            if (n.Id > 0) p.NotificationId = n.Id;
            if (!string.IsNullOrEmpty(n.Thread)) p.ThreadUrl = n.Thread;
            if (!string.IsNullOrEmpty(n.Icon) && n.Icon != p.IconUrl) { p.IconUrl = n.Icon; p.Avatar = null; }
            bool added = p.AddMessage(n.Body);

            if (!_replying)
            {
                // The newest sender moves to the front and is shown right away
                _people.Remove(p);
                _people.Insert(0, p);
                _current = p;
                if (added || !_head.Visible) ShowHead();
            }
            else if (p == _current && added)
            {
                ShowHead();   // replying to this very person → show their new message at once
            }
            // replying to someone else → this person waits; PageUp/PageDown gets to them

            if (p.Avatar == null && !string.IsNullOrEmpty(p.IconUrl))
            {
                string url = p.IconUrl;
                Image img = await DownloadAvatarAsync(url);
                if (img != null && p.IconUrl == url)
                {
                    p.Avatar = img;
                    if (p == _current && _head.Visible) ShowHead();
                }
            }
        }

        private void ShowHead()
        {
            if (_current == null) { _head.Hide(); return; }
            int count = _settings.HidePagerDots ? 0 : _people.Count;
            _head.SetContent(_current.Avatar, _current.Name, _current.Messages, Math.Max(0, _people.IndexOf(_current)), count);
            if (!_head.Visible) _head.Show();
            if (_replying) _reply.ShowUnder(_head.ReplyAnchorRect, _head.BubbleOnLeft);
            else RestartHideTimer();
        }

        private void RestartHideTimer()
        {
            _hideTimer.Stop();
            if (!_replying) _hideTimer.Start();
        }

        // ================= Replying =================
        /// <summary>Hotkey: opens the reply box for the person shown (or the newest sender).</summary>
        private void BeginReply()
        {
            if (_replying) return;
            if (_current == null || !_people.Contains(_current))
                _current = _people.Count > 0 ? _people[0] : null;
            if (_current == null) return;     // nobody has messaged → nothing to do

            _hideTimer.Stop();
            _replying = true;
            ShowHead();
            OpenReplyFor(_current);
            EnsureMyAvatar();
        }

        /// <summary>Your avatar for the reply box: the file from settings, otherwise taken from Facebook.</summary>
        private async void EnsureMyAvatar()
        {
            if (_reply.MyAvatar != null || _gettingMyAvatar) return;
            _gettingMyAvatar = true;
            try
            {
                Image img = null;
                if (!string.IsNullOrEmpty(_settings.MyAvatarPath) && File.Exists(_settings.MyAvatarPath))
                {
                    try { using (var src = Image.FromFile(_settings.MyAvatarPath)) img = Gfx.SquareCrop(src, 128); }
                    catch (Exception ex) { Log.Write("Could not read MyAvatarPath: " + ex.Message); }
                }
                if (img == null)
                {
                    string url = await _chat.GetMyAvatarUrlAsync();
                    if (!string.IsNullOrEmpty(url)) img = await DownloadAvatarAsync(url);
                }
                Log.Write("Your avatar: " + (img != null ? "found" : "not found yet"));
                if (img != null) _reply.MyAvatar = img;
            }
            finally { _gettingMyAvatar = false; }
        }

        private void OpenReplyFor(Person p)
        {
            _reply.Reset();
            if (p.Error != null) _reply.ShowError(p.Draft, p.Error);
            else if (!string.IsNullOrEmpty(p.Draft)) _reply.Text2 = p.Draft;
            _reply.ShowUnder(_head.ReplyAnchorRect, _head.BubbleOnLeft);
            // Open the conversation in the hidden page now so sending is fast
            _chat.OpenThreadAsync(p).ContinueWith(t => { var ignored = t.Exception; });
        }

        /// <summary>
        /// Switches to the previous (-1) / next (+1) person waiting.
        /// Works before opening the reply box too: browse, then pick who to reply to.
        /// Order: newest sender first, wraps around.
        /// </summary>
        private void Switch(int dir)
        {
            if (_people.Count == 0) return;
            if (_current == null || !_people.Contains(_current)) _current = _people[0];

            // Box is hidden → the first press just shows the current person again
            if (!_head.Visible) { ShowHead(); return; }
            if (_people.Count < 2) { if (!_replying) RestartHideTimer(); return; }

            if (_replying) _current.Draft = _reply.Text2;
            int i = Math.Max(0, _people.IndexOf(_current));
            _current = _people[(i + dir + _people.Count) % _people.Count];
            ShowHead();                          // restarts the hide timer when not replying
            if (_replying) OpenReplyFor(_current);
        }

        /// <summary>Tray notice: the app is running in the background.</summary>
        private void ShowRunningNotice()
        {
            _tray.ShowBalloonTip(5000, AppInfo.Name + " is running",
                "It waits in the tray for new messages (click ^ next to the clock to see the icon). " +
                "Press " + _settings.ReplyHotkey + " to reply.", ToolTipIcon.Info);
        }

        private static string Trim63(string s) { return s.Length <= 63 ? s : s.Substring(0, 62) + "…"; }

        private async void OnSend(string text)
        {
            Person p = _current;
            if (p == null) return;

            // Done once sent: put the reply box away, move on to the next person (if any), no status shown
            _replying = false;
            _reply.Reset();
            _reply.Hide();
            Remove(p);
            ShowNextOrHide();

            _sent[p.Key] = new KeyValuePair<string, DateTime>(text, DateTime.Now);
            string error = await _chat.SendAsync(p, text);
            Log.Write(error == null ? "Send: OK" : "Send: FAILED - " + error);
            if (error == null) return;

            // Send failed: bring this person back and reopen the reply box with the text + error
            p.Draft = text;
            p.Error = error;
            if (!_people.Contains(p)) _people.Insert(0, p);
            if (_replying) return;   // replying to someone else → the error waits until you switch here
            _current = p;
            _replying = true;
            _hideTimer.Stop();
            ShowHead();
            OpenReplyFor(p);
        }

        /// <summary>Esc: dismisses the message of the person shown.</summary>
        private void DismissCurrent()
        {
            if (_current == null) { _head.Hide(); return; }
            _replying = false;
            _reply.Reset();
            _reply.Hide();
            Remove(_current);
            ShowNextOrHide();
        }

        private void Remove(Person p)
        {
            _people.Remove(p);
            p.Messages.Clear();
            p.Draft = null;
            p.Error = null;
            if (_current == p) _current = null;
        }

        private void ShowNextOrHide()
        {
            _current = _people.Count > 0 ? _people[0] : null;
            if (_current == null) { _hideTimer.Stop(); _head.Hide(); }
            else ShowHead();
        }

        // ================= Sign-in window (only while signed out of Facebook) =================
        private void ShowSignIn()
        {
            _chat.PrepareForUser();
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            _chat.Location = new Point(wa.Left + (wa.Width - _chat.Width) / 2, wa.Top + (wa.Height - _chat.Height) / 2);
            _chat.Show();
            _chat.Activate();
            NativeMethods.SetForegroundWindow(_chat.Handle);
            _chat.FocusWeb();
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false };
            // Shown only while signed out of Facebook
            var signIn = new ToolStripMenuItem("Sign in to Facebook", null, (s, e) => ShowSignIn());
            menu.Items.Add(signIn);
            menu.Opening += (s, e) => signIn.Visible = !_chat.SignedIn;
            menu.Items.Add("Settings...", null, (s, e) => OpenSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitThread());
            return menu;
        }

        // ================= Settings =================
        private SettingsForm _settingsForm;

        private void OpenSettings()
        {
            if (_settingsForm != null) { _settingsForm.Activate(); return; }

            // Pause the global hotkeys so the Settings window can record new combinations
            _hotkey.Clear();
            byte oldAlpha = LayeredForm.Alpha;
            using (var f = new SettingsForm(_settings, IsStartupEnabled()))
            {
                _settingsForm = f;
                f.OpacityPreview += pct => { LayeredForm.Alpha = ToAlpha(pct); RefreshPopups(); };
                if (f.ShowDialog() == DialogResult.OK)
                {
                    _settings.Opacity = f.OpacityPercent;
                    _settings.ReplyHotkey = f.ReplyHotkey;
                    _settings.PrevHotkey = f.PrevHotkey;
                    _settings.NextHotkey = f.NextHotkey;
                    _settings.Save();
                    try { SetStartup(f.StartWithWindows); } catch (Exception ex) { Log.Write("Start with Windows: " + ex.Message); }
                    LayeredForm.Alpha = ToAlpha(_settings.Opacity);
                    Log.Write("Settings saved: opacity " + _settings.Opacity + "%");
                }
                else LayeredForm.Alpha = oldAlpha;
                _settingsForm = null;
            }
            RefreshPopups();
            _tray.Text = TrayText();

            string failed = RegisterHotkeys();
            if (failed != null)
                MessageBox.Show("These hotkeys are already used by another app and do not work:\n\n" + failed +
                                "\n\nChoose other keys in Settings.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Registers the three hotkeys; returns the ones that failed (null = all fine).</summary>
        private string RegisterHotkeys()
        {
            _hotkey.Clear();
            string failed = null;
            if (!_hotkey.Register(_settings.ReplyHotkey, Settings.DefaultHotkey, BeginReply)) failed += "Reply: " + _settings.ReplyHotkey + "\n";
            if (!_hotkey.Register(_settings.PrevHotkey, Settings.DefaultPrevHotkey, () => Switch(-1))) failed += "Previous person: " + _settings.PrevHotkey + "\n";
            if (!_hotkey.Register(_settings.NextHotkey, Settings.DefaultNextHotkey, () => Switch(1))) failed += "Next person: " + _settings.NextHotkey + "\n";
            return failed;
        }

        /// <summary>Redraws the popups that are on screen (after an opacity change).</summary>
        private void RefreshPopups()
        {
            if (_head.Visible) ShowHead();
            _reply.ApplyOpacity();
        }

        private static byte ToAlpha(int percent)
        {
            return (byte)Math.Round(Math.Max(Settings.MinOpacity, Math.Min(100, percent)) * 2.55);
        }

        private string TrayText()
        {
            return Trim63(AppInfo.Name + " · " + _settings.ReplyHotkey + " reply · " +
                          _settings.PrevHotkey + " / " + _settings.NextHotkey + " switch");
        }

        private static bool IsStartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        private static void SetStartup(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunName, AppInfo.RunCommand);
                else k.DeleteValue(RunName, false);
            }
        }

        private static async Task<Image> DownloadAvatarAsync(string url)
        {
            try
            {
                using (var wc = new WebClient())
                {
                    byte[] data = await wc.DownloadDataTaskAsync(url);
                    using (var ms = new MemoryStream(data))
                    using (var img = Image.FromStream(ms))
                        return Gfx.SquareCrop(img, 128);
                }
            }
            catch { return null; }
        }

        protected override void ExitThreadCore()
        {
            _tray.Visible = false;
            _tray.Dispose();
            _hotkey.Dispose();
            _hideTimer.Dispose();
            _reply.Dispose();
            _head.Dispose();
            _chat.Dispose();
            base.ExitThreadCore();
        }
    }
}
