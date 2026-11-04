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
    /// Runs the whole app.
    ///  - A message arrives → the sender's avatar + name + text show for 5 s (notification).
    ///  - Ctrl+Alt+R is an ON/OFF switch for the reply panel (message box + reply box):
    ///      ON  → starts at the newest person waiting (or the person you talked to last);
    ///            Ctrl+Alt+PageUp/PageDown (or PageUp/PageDown) switch between everyone waiting and
    ///            the people you talked to recently; Enter sends and stays on that person.
    ///      OFF → press Ctrl+Alt+R again, or Esc, or click elsewhere. Unsent text is kept.
    /// The popups ignore the mouse (keyboard-only). Only a failed send shows an error.
    /// </summary>
    internal sealed class BubbleApp : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = AppInfo.Name;
        private const int HeadVisibleMs = 5000;    // the message box hides after 5 s if you don't reply

        private readonly Settings _settings;
        private readonly float S;
        private readonly HeadForm _head;
        private readonly ReplyBox _reply;
        private readonly ChatForm _chat;             // hidden Messenger page (receives + sends)
        private readonly NotifyIcon _tray;
        private readonly HotkeyWindow _hotkey;
        private readonly SynchronizationContext _ui;
        private readonly Timer _hideTimer = new Timer { Interval = HeadVisibleMs };

        // Everyone in this session: people waiting for a reply and people you talked to recently,
        // most recent activity first. Switching cycles through this list.
        private readonly List<Person> _people = new List<Person>();
        private const int MaxPeople = 15;
        private Person _current;                                       // person currently shown
        private bool _replying;                                        // reply panel is ON
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
            _reply.Dismissed += ClosePanel;                 // Esc
            _reply.SwitchRequested += Switch;
            _reply.LostFocus2 += () => { if (_replying) ClosePanel(); };   // clicked elsewhere

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
                (st, to) => _ui.Post(_ => ShowRunningNotice(), null),
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
            if (p == null) p = new Person { Key = key };
            p.Name = n.Title;
            if (n.Id > 0) p.NotificationId = n.Id;
            if (!string.IsNullOrEmpty(n.Thread)) p.ThreadUrl = n.Thread;
            if (!string.IsNullOrEmpty(n.Icon) && n.Icon != p.IconUrl) { p.IconUrl = n.Icon; p.Avatar = null; }
            bool added = p.AddMessage(n.Body);
            if (added) p.Waiting = true;

            // Most recent activity first
            _people.Remove(p);
            _people.Insert(0, p);
            TrimPeople();

            if (_replying)
            {
                // Panel ON: never jump away from the person you are replying to; the new sender is just
                // added to the list (the dots update). Same person → their new message shows at once.
                ShowHead();
            }
            else if (added || !_head.Visible)
            {
                // Panel OFF: show the new message as a notification (hides after 5 s)
                _current = p;
                ShowHead();
            }

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

        private void TrimPeople()
        {
            for (int i = _people.Count - 1; i >= 0 && _people.Count > MaxPeople; i--)
                if (_people[i] != _current && !_people[i].Waiting) _people.RemoveAt(i);
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

        // ================= Reply panel (ON / OFF switch) =================
        /// <summary>Ctrl+Alt+R: turns the reply panel on, or off if it is already on.</summary>
        private void TogglePanel()
        {
            if (_replying) ClosePanel();
            else OpenPanel();
        }

        /// <summary>Turns the panel on: newest person waiting, otherwise the person you talked to last.</summary>
        private void OpenPanel()
        {
            if (_replying) return;
            Person start = _people.Find(x => x.Waiting) ?? (_people.Count > 0 ? _people[0] : null);
            if (start == null)
            {
                _tray.ShowBalloonTip(3000, AppInfo.Name, "No messages yet.", ToolTipIcon.Info);
                return;
            }
            _current = start;
            _hideTimer.Stop();
            _replying = true;
            ShowHead();
            OpenReplyFor(_current);
            EnsureMyAvatar();
        }

        /// <summary>Turns the panel off (hotkey again, Esc, or a click elsewhere). Unsent text is kept.</summary>
        private void ClosePanel()
        {
            if (!_replying) return;
            if (_current != null) _current.Draft = _reply.Text2;
            _replying = false;
            _reply.Hide();
            _hideTimer.Stop();
            _head.Hide();
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
        /// Previous (-1) / next (+1) person: everyone waiting and everyone you talked to recently,
        /// most recent first, wrapping around. When the panel is off, the first press turns it on.
        /// </summary>
        private void Switch(int dir)
        {
            if (!_replying) { OpenPanel(); return; }
            if (_people.Count < 2 || _current == null) return;
            _current.Draft = _reply.Text2;
            int i = Math.Max(0, _people.IndexOf(_current));
            _current = _people[(i + dir + _people.Count) % _people.Count];
            ShowHead();
            OpenReplyFor(_current);
        }

        /// <summary>Tray notice: the app is running in the background.</summary>
        private void ShowRunningNotice()
        {
            _tray.ShowBalloonTip(5000, AppInfo.Name + " is running",
                "It waits in the tray for new messages (click ^ next to the clock to see the icon). " +
                "Press " + _settings.ReplyHotkey + " to open / close the reply panel.", ToolTipIcon.Info);
        }

        private static string Trim63(string s) { return s.Length <= 63 ? s : s.Substring(0, 62) + "…"; }

        /// <summary>Enter: sends, then stays on the same person (send more, or switch to someone else).</summary>
        private async void OnSend(string text)
        {
            Person p = _current;
            if (p == null) return;

            p.Waiting = false;          // answered
            p.Draft = null;
            p.Error = null;
            _reply.Reset();
            if (_replying) ShowHead();  // dots / panel stay as they are

            _sent[p.Key] = new KeyValuePair<string, DateTime>(text, DateTime.Now);
            string error = await _chat.SendAsync(p, text);
            Log.Write(error == null ? "Send: OK" : "Send: FAILED - " + error);
            if (error == null) return;

            // Send failed: keep the text and show the error (now if this person is on screen,
            // otherwise when you switch back to them)
            p.Draft = text;
            p.Error = error;
            if (_replying && _current == p) OpenReplyFor(p);
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
            SettingsForm.Account account = SettingsForm.Account.None;
            using (var f = new SettingsForm(_settings, IsStartupEnabled(), _chat.SignedIn))
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
                    account = f.AccountAction;
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

            if (account == SettingsForm.Account.SignIn) ShowSignIn();
            else if (account == SettingsForm.Account.LogOut) LogOut();
        }

        /// <summary>Logs out of Facebook and clears everyone waiting; the sign-in window then appears.</summary>
        private async void LogOut()
        {
            _replying = false;
            _reply.Reset();
            _reply.Hide();
            _people.Clear();
            _current = null;
            _hideTimer.Stop();
            _head.Hide();
            await _chat.LogoutAsync();
        }

        /// <summary>Registers the three hotkeys; returns the ones that failed (null = all fine).</summary>
        private string RegisterHotkeys()
        {
            _hotkey.Clear();
            string failed = null;
            if (!_hotkey.Register(_settings.ReplyHotkey, Settings.DefaultHotkey, TogglePanel)) failed += "Open / close: " + _settings.ReplyHotkey + "\n";
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
            return Trim63(AppInfo.Name + " · " + _settings.ReplyHotkey + " open · " +
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
