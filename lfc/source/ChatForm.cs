using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static Lfc.NativeMethods;

namespace Lfc
{
    /// <summary>A message event reported by the Messenger page (via the script injected into the page).</summary>
    [DataContract]
    internal sealed class PageNotification
    {
        [DataMember(Name = "id")] public int Id { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; }
        [DataMember(Name = "body")] public string Body { get; set; }
        [DataMember(Name = "icon")] public string Icon { get; set; }
        [DataMember(Name = "tag")] public string Tag { get; set; }
        [DataMember(Name = "type")] public string Type { get; set; }   // "notif"
        [DataMember(Name = "thread")] public string Thread { get; set; } // conversation path, e.g. /messages/t/123
    }

    /// <summary>
    /// Hosts facebook.com/messages in WebView2 (Edge). Always runs hidden in the background to
    /// receive and send messages. It is shown ONLY as the sign-in window, automatically, when you are
    /// signed out of Facebook, and hides itself as soon as you are signed in.
    /// </summary>
    internal sealed class ChatForm : Form
    {
        public const string MessengerUrl = "https://www.facebook.com/messages/";

        public event Action<PageNotification> NotificationArrived;
        /// <summary>Raised when Facebook sign-in state changes (true = signed in).</summary>
        public event Action<bool> SignedInChanged;

        private bool? _signedIn;
        private CoreWebView2Environment _env;

        // ---- Light mode: keeps the hidden page as small as possible ----
        // Browser switches: no GPU (nobody looks at the page), one renderer process, no extensions,
        // no background updates / Edge extras.
        private const string LightBrowserArgs =
            "--disable-gpu --disable-gpu-compositing " +
            "--renderer-process-limit=1 --disable-extensions --disable-component-update " +
            "--disable-background-networking --disable-sync --no-first-run " +
            "--disable-features=msSmartScreenProtection,msEdgeShoppingAssistant,msWebOOUI,msPdfOOUI," +
            "SpareRendererForSitePerProcess,AutofillServerCommunication,MediaRouter";

        /// <summary>
        /// While signed in and hidden, the page loads no images, video, audio or web fonts:
        /// the app only needs text and the avatar links (avatars are downloaded separately).
        /// The sign-in window loads everything (captchas, profile pictures...).
        /// </summary>
        private bool LightMode { get { return SignedIn && !UserVisible; } }
        private readonly System.Windows.Forms.Timer _signInTimer = new System.Windows.Forms.Timer { Interval = 10000 };

        /// <summary>true once a Facebook session is detected.</summary>
        public bool SignedIn { get { return _signedIn == true; } }

        private readonly Settings _settings;
        private readonly float S;
        private readonly WebView2 _web;
        private readonly Panel _header;
        
        // Script that runs before any page script. It reports incoming messages to C# via postMessage:
        //  1) replaces window.Notification with a stand-in to learn sender, text and avatar;
        //  2) reads the conversation list to detect new messages (the main source).
        private const string HookScript = @"(() => {
  if (window.top !== window || window.__bubbleHooked) return;
  if (!window.chrome || !window.chrome.webview) return;
  window.__bubbleHooked = true;
  const store = {}; let seq = 0;
  const emit = (o) => { try { window.chrome.webview.postMessage(JSON.stringify(o)); } catch (e) {} };
  class BubbleNotification extends EventTarget {
    constructor(title, opts) {
      super(); opts = opts || {};
      this.title = String(title); this.body = String(opts.body || '');
      this.icon = String(opts.icon || ''); this.tag = opts.tag || ''; this.data = opts.data;
      this.onclick = this.onclose = this.onshow = this.onerror = null;
      const id = ++seq; store[id] = this;
      emit({ type: 'notif', id: id, title: this.title, body: this.body, icon: this.icon, tag: String(this.tag || '') });
      setTimeout(() => { const ev = new Event('show'); this.dispatchEvent(ev); if (this.onshow) this.onshow(ev); }, 0);
    }
    close() { const ev = new Event('close'); this.dispatchEvent(ev); if (this.onclose) this.onclose(ev); }
    static get permission() { return 'granted'; }
    static get maxActions() { return 0; }
    static requestPermission(cb) { if (cb) cb('granted'); return Promise.resolve('granted'); }
  }
  window.Notification = BubbleNotification;
  window.__bubbleClick = (id) => {
    const n = store[id]; if (!n) return;
    const ev = new Event('click'); n.dispatchEvent(ev); if (n.onclick) n.onclick(ev);
  };
  if (window.ServiceWorkerRegistration) {
    ServiceWorkerRegistration.prototype.showNotification = function (title, opts) {
      opts = opts || {};
      emit({ type: 'notif', id: 0, title: String(title), body: String(opts.body || ''), icon: String(opts.icon || ''), tag: String(opts.tag || '') });
      return Promise.resolve();
    };
  }

  // ---- Read the conversation list to learn who just sent what ----
  // While the page runs hidden, Facebook usually shows no notification and only updates the list.
  // Each row has: name, latest message, avatar, conversation link.
  // A row counts as 'just messaged' only when it is UNREAD, among the FIRST few rows, the text is a
  // REAL MESSAGE (not typing / a reaction / your own message), NOT SEEN recently for that row, and
  // STABLE across two scans (filters text that flickers while Facebook re-renders).
  // The Vietnamese words in the patterns below match Facebook's Vietnamese UI and must stay.
  const TOP_ROWS = 6;          // a new message always moves the sender to the top
  const CONFIRM_MS = 500;      // text must stay unchanged at least this long
  const history = new Map();   // thread → last few texts seen
  const pending = new Map();   // thread → { body, t } waiting for confirmation
  let baseline = false;
  const threadRe = /\/messages\/(?:e2ee\/)?t\/[^/?#]+/;
  const timeRe = /^(\d+\s*(phút|giờ|ngày|tuần|tháng|năm|m|h|d|w|y|min|mins|hr|hrs|wk|wks)|vừa xong|now|just now|\d{1,2}:\d{2}(\s*(sa|ch|am|pm))?|th\s?\d|cn|t\d|mon|tue|wed|thu|fri|sat|sun)$/i;
  // Status labels (green dot, seen...): neither name nor message
  const statusRe = /^(đang hoạt động|vừa mới hoạt động|hoạt động\s.*(trước|ago)|active now|active\s.*ago|active|trực tuyến|online|offline|đã xem|seen|đã gửi|sent|delivered|đã nhận)$/i;
  const mineRe = /^(bạn|you)(\s*:|\s+đã\s|\s+sent|\s+reacted|\s+unsent|\s+replied)/i;
  const notMsgRe = /(đang nhập|is typing|typing\.\.\.|bày tỏ cảm xúc|đã thả|reacted|đã gỡ|unsent a message|đã tham gia|joined|đã rời|left the group|đã đặt biệt danh|set the nickname|đã đổi|changed the)/i;
  const clean = (s) => (s || '').replace(/\s+/g, ' ').trim();
  // Hidden screen-reader labels added by Facebook (not message text): removed
  const labelRe = /^(tin nhắn chưa đọc|chưa đọc|tin nhắn mới|unread messages?|unread|new messages?)(\s*[:：]\s*|$)/i;
  const unreadLabelRe = /^(tin nhắn chưa đọc|chưa đọc|unread)/i;
  const isBold = (el) => { try { return parseInt(getComputedStyle(el).fontWeight, 10) >= 600; } catch (e) { return false; } };

  function rowInfo(a) {
    const m = (a.getAttribute('href') || '').match(threadRe);
    if (!m) return null;
    let hadUnreadLabel = false;
    const lines = (a.innerText || a.textContent || '').split('\n').map(clean)
      .map((l) => { if (unreadLabelRe.test(l)) hadUnreadLabel = true; return clean(l.replace(labelRe, '')); })
      .filter(Boolean);
    // Drop status / time labels BEFORE picking the name (Facebook puts the active status before it)
    const kept = lines.filter((l) => l !== '·' && !statusRe.test(l) && !timeRe.test(l));
    if (kept.length < 2) return null;
    const name = kept[0];
    let body = clean(kept.slice(1).join(' '));
    const parts = body.split(' · ');
    if (parts.length > 1 && timeRe.test(parts[parts.length - 1])) { parts.pop(); body = parts.join(' · '); }
    let icon = '';
    const img = a.querySelector('img');
    if (img && img.src) icon = img.src;
    else {
      const im = a.querySelector('image');
      if (im) icon = im.getAttribute('xlink:href') || im.getAttribute('href') || '';
    }
    // Unread: bold text in the message part (not the name), or an unread label
    let unread = false;
    for (const el of a.querySelectorAll('span')) {
      if (el.querySelector('span')) continue;
      const t = clean(el.textContent);
      if (t && t !== name && body.indexOf(t) >= 0 && isBold(el)) { unread = true; break; }
    }
    if (!unread && (hadUnreadLabel || a.querySelector(""[aria-label*='chưa đọc' i], [aria-label*='unread' i]""))) unread = true;
    return { thread: m[0], name: name, body: body, icon: icon, unread: unread };
  }

  const isMessage = (body) => body && !mineRe.test(body) && !notMsgRe.test(body);

  function remember(thread, body) {
    const h = history.get(thread) || [];
    if (h.indexOf(body) < 0) h.push(body);
    while (h.length > 4) h.shift();
    history.set(thread, h);
  }

  function scan() {
    const rows = document.querySelectorAll(""a[href*='/messages/t/'], a[href*='/messages/e2ee/t/']"");
    // List not loaded yet → do nothing. First time the list is seen: only remember, don't report
    // (so messages that existed before the app started are not shown).
    if (rows.length === 0) return;
    const now = Date.now();
    const present = new Set();
    // The conversation open in the hidden page is NOT shown as unread (Facebook treats it as being read),
    // so the unread condition is skipped for that row.
    const am = (location.pathname || '').match(threadRe);
    const activeThread = am ? am[0] : '';
    let pos = 0;
    for (const a of rows) {
      const r = rowInfo(a);
      const top = pos++ < TOP_ROWS;
      if (!r || !r.body) continue;
      present.add(r.thread);
      const h = history.get(r.thread);
      if (!baseline) { remember(r.thread, r.body); continue; }
      if (h && h.indexOf(r.body) >= 0) { pending.delete(r.thread); continue; }   // already seen
      const isActive = r.thread === activeThread;
      if ((!r.unread && !isActive) || !top || !isMessage(r.body)) {
        remember(r.thread, r.body);       // not a new incoming message → just remember
        pending.delete(r.thread);
        continue;
      }
      const c = pending.get(r.thread);
      if (c && c.body === r.body) {
        if (now - c.t >= CONFIRM_MS) {
          pending.delete(r.thread);
          remember(r.thread, r.body);
          emit({ type: 'notif', id: 0, title: r.name, body: r.body, icon: r.icon, tag: '', thread: r.thread });
        }
      } else {
        pending.set(r.thread, { body: r.body, t: now });
      }
    }
    for (const k of Array.from(pending.keys())) if (!present.has(k)) pending.delete(k);
    baseline = true;
    if (pending.size) setTimeout(scan, CONFIRM_MS + 50);   // scan again to confirm
  }
  window.__bubbleScan = scan;

  let scanTimer = 0;
  const schedule = () => { if (!scanTimer) scanTimer = setTimeout(() => { scanTimer = 0; scan(); }, 600); };
  const start = () => {
    scan();
    new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true, characterData: true });
    setInterval(scan, 5000);
  };
  if (document.body) start(); else document.addEventListener('DOMContentLoaded', start);
})();";

        public ChatForm(Settings settings, float scale)
        {
            _settings = settings;
            S = scale;

            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(196, 202, 214);   // border colour
            Padding = new Padding((int)Math.Ceiling(3 * S));
            MinimumSize = new Size((int)(320 * S), (int)(400 * S));
            Size = new Size(Math.Max(settings.ChatWidth, MinimumSize.Width), Math.Max(settings.ChatHeight, MinimumSize.Height));
            Icon = Program.AppIcon;

            _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
            _header = BuildHeader();
            Controls.Add(_web);      // Fill must be added first
            Controls.Add(_header);   // Top added after → docked first
        }

        public bool WebReady { get { return _web.CoreWebView2 != null; } }

        // ---------------- Custom title bar ----------------
        private Panel BuildHeader()
        {
            var header = new GradientPanel { Dock = DockStyle.Top, Height = (int)(36 * S) };
            var title = new Label
            {
                Text = "Sign in to Facebook",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI Semibold", 10f),
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding((int)(8 * S), 0, 0, 0),
                AutoEllipsis = true
            };
            // Icon glyphs from the Segoe MDL2 Assets font (built into Windows 10/11)
            var btnHide = HeaderButton("\uE921", "Hide (sign in later from the tray menu)", delegate { Hide(); });
            var btnReload = HeaderButton("\uE72C", "Reload", delegate { Reload(); });

            header.Controls.Add(title);
            header.Controls.Add(btnReload);
            header.Controls.Add(btnHide);

            MouseEventHandler drag = (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
            };
            header.MouseDown += drag;
            title.MouseDown += drag;
            return header;
        }

        private Button HeaderButton(string glyph, string tip, EventHandler onClick)
        {
            var b = new Button
            {
                Text = glyph,
                Font = new Font("Segoe MDL2 Assets", 9f),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Right,
                Width = (int)(34 * S),
                TabStop = false,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 255, 255, 255);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(90, 255, 255, 255);
            b.Click += onClick;
            new ToolTip().SetToolTip(b, tip);
            return b;
        }

        private sealed class GradientPanel : Panel
        {
            public GradientPanel() { DoubleBuffered = true; }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                if (Width <= 0 || Height <= 0) return;
                using (var br = new LinearGradientBrush(ClientRectangle,
                    Color.FromArgb(64, 120, 255), Color.FromArgb(156, 80, 230), LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(br, ClientRectangle);
            }
        }

        // ---------------- Borderless but still resizable ----------------
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int g = Math.Max(Padding.Left, 4);
                bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < g, b = p.Y >= ClientSize.Height - g;
                int hit = 0;
                if (t && l) hit = 13; else if (t && r) hit = 14; else if (b && l) hit = 16; else if (b && r) hit = 17;
                else if (l) hit = 10; else if (r) hit = 11; else if (t) hit = 12; else if (b) hit = 15;
                if (hit != 0) m.Result = new IntPtr(hit);
                return;
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            _settings.ChatWidth = Width;
            _settings.ChatHeight = Height;
            _settings.Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 only hides the window, it does not quit the app
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        // ---------------- WebView2 ----------------
        public async Task InitWebAsync()
        {
            try
            {
                string dataDir = Path.Combine(AppInfo.LocalDir, "WebView2");
                var options = new CoreWebView2EnvironmentOptions(LightBrowserArgs);
                _env = await CoreWebView2Environment.CreateAsync(null, dataDir, options);
                await _web.EnsureCoreWebView2Async(_env);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                Log.Write("WebView2 Runtime missing");
                var r = MessageBox.Show(
                    "Microsoft Edge WebView2 Runtime is not installed (it is needed to run Messenger).\n\nOpen the download page now?",
                    AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) OpenExternal("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start WebView2:\n" + ex.Message, AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Log.Write("WebView2 ready, version " + _web.CoreWebView2.Environment.BrowserVersionString);
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = true;
            core.Settings.IsPasswordAutosaveEnabled = true;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;

            // Light mode: block images / video / audio / fonts while hidden
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Media);
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Font);
            core.WebResourceRequested += (s, e) =>
            {
                if (LightMode) e.Response = _env.CreateWebResourceResponse(null, 404, "Blocked by lfc", "");
            };
            SetMemoryLevel(low: true);
            Log.Write("Light mode on");

            await core.AddScriptToExecuteOnDocumentCreatedAsync(HookScript);

            core.WebMessageReceived += OnWebMessage;
            core.PermissionRequested += (s, e) =>
            {
                if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
                    e.State = CoreWebView2PermissionState.Allow;
            };
            core.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                if (IsFacebookUrl(e.Uri) && Regex.IsMatch(e.Uri, @"/(messages|t)/")) core.Navigate(e.Uri);
                else OpenExternal(e.Uri);
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(MessengerOnlyCss);

            // Full navigations (page loads) can be blocked up front
            core.NavigationStarting += (s, e) =>
            {
                if (!IsFacebookUrl(e.Uri)) { e.Cancel = true; OpenExternal(e.Uri); return; }
                switch (Classify(e.Uri))
                {
                    case PageKind.Blocked:
                        e.Cancel = true;
                        OpenExternal(e.Uri);    // e.g. a post link inside a message → open in the browser
                        break;
                }
            };
            // Facebook is a single-page app: "Home" only changes the URL without a page load.
            // SourceChanged also catches that kind of navigation → go back to Messenger.
            core.SourceChanged += async (s, e) => await EnforceMessengerOnlyAsync();

            // Sign-in state: checked after every page load and every 10 s (catches being signed out later)
            core.NavigationCompleted += async (s, e) => await CheckSignInAsync();
            _signInTimer.Tick += async (s, e) => await CheckSignInAsync();
            _signInTimer.Start();

            // Know the sign-in state before the first load, so light mode applies from the start
            await CheckSignInAsync();
            core.Navigate(MessengerUrl);
        }

        /// <summary>Logs out of Facebook: clears the cookies and reloads (the sign-in window then appears).</summary>
        public async Task LogoutAsync()
        {
            if (!WebReady) return;
            _web.CoreWebView2.CookieManager.DeleteAllCookies();
            Log.Write("Facebook: log out requested");
            _web.CoreWebView2.Navigate(MessengerUrl);
            await Task.Delay(1500);
            await CheckSignInAsync();
        }

        private bool _checkingSignIn;
        private async Task CheckSignInAsync()
        {
            if (_checkingSignIn || !WebReady) return;
            _checkingSignIn = true;
            try
            {
                bool now = await IsLoggedInAsync();
                if (_signedIn == now) return;
                _signedIn = now;
                Log.Write(now ? "Facebook: signed in" : "Facebook: signed out");
                if (SignedInChanged != null) SignedInChanged(now);
            }
            finally { _checkingSignIn = false; }
        }

        // ================= Messenger only, no news feed =================
        private enum PageKind { Messenger, Home, Blocked, Other }

        // facebook.com paths allowed inside the app:
        // messages + sign-in / account verification steps.
        private static readonly string[] AllowedPaths =
        {
            "/messages", "/t/", "/login", "/logout", "/checkpoint", "/two_step_verification", "/two_factor",
            "/recover", "/auth", "/cookie", "/privacy/consent", "/confirmemail", "/dialog", "/ajax", "/api",
            "/security", "/device-based", "/x/oauth"
        };

        private static PageKind Classify(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "https" && u.Scheme != "http"))
                return PageKind.Other;
            string host = u.Host.ToLowerInvariant();
            // Only the main Facebook hosts are controlled.
            // Other subdomains (accountscenter, l.facebook.com, photos, video...) are left alone.
            bool mainSite = host == "facebook.com" || host == "www.facebook.com" ||
                            host == "web.facebook.com" || host == "m.facebook.com";
            if (!mainSite) return PageKind.Other;

            string path = u.AbsolutePath.ToLowerInvariant();
            if (path == "/" || path == "/home.php" || path == "/index.php") return PageKind.Home;
            foreach (var p in AllowedPaths)
                if (path.StartsWith(p)) return PageKind.Messenger;
            return PageKind.Blocked;
        }

        private bool _enforcing;
        private async Task EnforceMessengerOnlyAsync()
        {
            if (_enforcing || !WebReady) return;
            var core = _web.CoreWebView2;
            string url = core.Source;
            PageKind kind = Classify(url);
            if (kind == PageKind.Messenger || kind == PageKind.Other) return;

            _enforcing = true;
            try
            {
                if (kind == PageKind.Home)
                {
                    // Signed out: the home page is the sign-in form → leave it.
                    // Signed in (just logged in, or clicked the Facebook logo) → go to messages.
                    if (await IsLoggedInAsync()) core.Navigate(MessengerUrl);
                    return;
                }
                // Any other page (news feed, Watch, Marketplace, profiles...): go back and open it in the browser
                if (core.CanGoBack) core.GoBack(); else core.Navigate(MessengerUrl);
                OpenExternal(url);
            }
            finally { _enforcing = false; }
        }

        private async Task<bool> IsLoggedInAsync()
        {
            try
            {
                var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.facebook.com");
                foreach (var c in cookies)
                    if (c.Name == "c_user") return true;
            }
            catch { }
            return false;
        }

        // Finds the signed-in account's avatar on the page (in the top bar, which is hidden but still present)
        internal const string MyAvatarJs = @"(() => {
  const sel = '[role=""banner""] svg image, [role=""banner""] img, ' +
              '[aria-label*=""Trang cá nhân"" i] image, [aria-label*=""Your profile"" i] image, ' +
              '[aria-label*=""Tài khoản"" i] image, [aria-label*=""Account"" i] image';
  for (const el of document.querySelectorAll(sel)) {
    const u = el.getAttribute('xlink:href') || el.getAttribute('href') || el.currentSrc || el.src || '';
    if (/^https:\/\/[^/]*(fbcdn\.net|fbsbx\.com)\//.test(u)) return u;
  }
  return '';
})()";

        /// <summary>URL of your avatar (null if not found yet).</summary>
        public async Task<string> GetMyAvatarUrlAsync()
        {
            if (!WebReady) return null;
            try
            {
                string json = await EvalAsync(MyAvatarJs);
                string url = null;
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "\"\"")))
                    url = new DataContractJsonSerializer(typeof(string)).ReadObject(ms) as string;
                if (!string.IsNullOrEmpty(url)) return url;

                // Fallback: public profile picture by account id (c_user cookie)
                var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.facebook.com");
                foreach (var c in cookies)
                    if (c.Name == "c_user" && !string.IsNullOrEmpty(c.Value))
                        return "https://graph.facebook.com/" + Uri.EscapeDataString(c.Value) + "/picture?type=large";
            }
            catch (Exception ex) { Log.Write("Could not get your avatar: " + ex.Message); }
            return null;
        }


        // CSS injected into the page: hides Facebook's top bar (Home, Watch, Marketplace, Groups,
        // notifications...) so only the messages UI remains.
        private const string MessengerOnlyCss = @"(() => {
  if (window.top !== window) return;
  const css = `
    div[role=""banner""] { display: none !important; }
    * { --header-height: 0px !important; }
  `;
  const add = () => {
    if (document.getElementById('__bubble_css')) return;
    const s = document.createElement('style');
    s.id = '__bubble_css'; s.textContent = css;
    (document.head || document.documentElement).appendChild(s);
  };
  if (document.documentElement) add();
  document.addEventListener('DOMContentLoaded', add);
})();";


        // ================= Sending quick replies through the hidden Messenger page =================
        // Opens the right conversation, checks the recipient's name is on the page, inserts the text
        // and presses Enter via the DevTools Protocol (just like typing), then confirms the composer is
        // empty again = Facebook accepted the message. It goes through Facebook's own page, so it is safe.

        /// <summary>The window is "parked" off-screen so the page behaves as if open (invisible to you).</summary>
        public bool IsParked { get; private set; }

        /// <summary>The full Messenger window is actually visible to you.</summary>
        public bool UserVisible { get { return Visible && !IsParked; } }

        protected override bool ShowWithoutActivation { get { return IsParked; } }

        /// <summary>Call before showing the window to you: restores your saved size.</summary>
        public void PrepareForUser()
        {
            IsParked = false;
            SetMemoryLevel(low: false);
            Size = new Size(Math.Max(_settings.ChatWidth, MinimumSize.Width), Math.Max(_settings.ChatHeight, MinimumSize.Height));
        }

        /// <summary>
        /// While hidden: keep the page wide so Facebook shows both the chat list and the conversation.
        /// (When narrow, Facebook shows only one of them and the chat list could not be read.)
        /// </summary>
        public void UseBackgroundSize()
        {
            if (UserVisible) return;
            Size = new Size((int)(1280 * S), (int)(860 * S));
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { UseBackgroundSize(); SetMemoryLevel(low: true); }
        }

        /// <summary>WebView2 memory target: Low while hidden (frees caches), Normal while you see it.</summary>
        private void SetMemoryLevel(bool low)
        {
            try
            {
                if (WebReady)
                    _web.CoreWebView2.MemoryUsageTargetLevel =
                        low ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch { /* older WebView2 runtime: not supported */ }
        }

        private readonly SemaphoreSlim _webLock = new SemaphoreSlim(1, 1);

        private const string ComposerJs =
            "(document.querySelector('[role=\"main\"] div[role=\"textbox\"][contenteditable=\"true\"]')" +
            " || document.querySelector('div[role=\"textbox\"][contenteditable=\"true\"]'))";

        // Scripts run inside the Messenger page
        internal static string HasNameJs(string name)
        {
            return "(() => { const m = document.querySelector('[role=\"main\"]') || document.body;" +
                   " const n = s => (s || '').normalize('NFC').toLowerCase().replace(/\\s+/g, ' ').trim();" +
                   " return n(m.innerText || m.textContent).includes(n(" + JsonString(name) + ")); })()";
        }

        internal const string ComposerLengthJs =
            "(() => { const el = " + ComposerJs + "; if (!el) return -1;" +
            " return (el.innerText || el.textContent || '').replace(/\\u200b/g, '').trim().length; })()";

        internal const string FocusComposerJs =
            "(() => { const el = " + ComposerJs + "; el.focus();" +
            " const r = document.createRange(); r.selectNodeContents(el); r.collapse(false);" +
            " const s = getSelection(); s.removeAllRanges(); s.addRange(r); })()";

        private static readonly Regex ThreadRe = new Regex(@"/messages/(?:e2ee/)?t/[^/?#]+", RegexOptions.IgnoreCase);

        /// <summary>Hidden labels such as "Unread message:" at the start of a message.</summary>
        private static readonly Regex LabelRe = new Regex(
            @"^\s*(tin nhắn chưa đọc|chưa đọc|tin nhắn mới|unread messages?|unread|new messages?)(\s*[:：]\s*|$)",
            RegexOptions.IgnoreCase);

        /// <summary>Removes a leading "Unread message:"-style label.</summary>
        internal static string StripLabel(string body)
        {
            return LabelRe.Replace(body ?? "", "").Trim();
        }

        /// <summary>Conversation path, e.g. "/messages/t/123", from a full URL or a path.</summary>
        internal static string ThreadOf(string url)
        {
            var m = ThreadRe.Match(url ?? "");
            return m.Success ? m.Value : null;
        }

        /// <summary>Clicks the conversation's row in the chat list (fast switch, no page load).</summary>
        internal static string ClickThreadJs(string threadPath)
        {
            return "(() => { const want = " + JsonString(threadPath) + ";" +
                   " const re = /\\/messages\\/(?:e2ee\\/)?t\\/[^/?#]+/;" +
                   " for (const a of document.querySelectorAll('a[href]')) {" +
                   "   const m = (a.getAttribute('href') || '').match(re);" +
                   "   if (m && m[0] === want) { a.click(); return true; } }" +
                   " return false; })()";
        }

        private void Park()
        {
            if (Visible) return;
            IsParked = true;
            Location = new Point(-32000, -32000);
            UseBackgroundSize();
            Show();
        }

        private void Unpark()
        {
            if (!IsParked) return;
            IsParked = false;
            Hide();
        }

        private async Task<string> EvalAsync(string js)
        {
            return await _web.CoreWebView2.ExecuteScriptAsync(js);
        }

        private Task<string> CdpAsync(string method, string json)
        {
            return _web.CoreWebView2.CallDevToolsProtocolMethodAsync(method, json);
        }

        internal static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20 || c == '\u2028' || c == '\u2029') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Opens this person's conversation in the hidden page ahead of time (faster sending).</summary>
        public async Task<bool> OpenThreadAsync(Person item)
        {
            if (!WebReady || item == null) return false;
            await _webLock.WaitAsync();
            try { return await OpenThreadCoreAsync(item); }
            catch { return false; }
            finally { _webLock.Release(); }
        }

        private async Task<bool> OpenThreadCoreAsync(Person item)
        {
            var core = _web.CoreWebView2;
            string before = core.Source;
            if (item.ThreadUrl != null)
            {
                if (ThreadOf(before) == item.ThreadUrl) return true;

                // Fast way: click this person's row in the chat list
                if (await EvalAsync(ClickThreadJs(item.ThreadUrl)) == "true")
                {
                    for (int i = 0; i < 25; i++)
                    {
                        await Task.Delay(200);
                        if (ThreadOf(core.Source) == item.ThreadUrl) return true;
                    }
                }

                // Not in the list → open the link directly (page load, slower)
                Log.Write("Opened conversation by link (not found in the list)");
                var done = new TaskCompletionSource<bool>();
                EventHandler<CoreWebView2NavigationCompletedEventArgs> h = (s, e) => done.TrySetResult(e.IsSuccess);
                core.NavigationCompleted += h;
                try
                {
                    core.Navigate("https://www.facebook.com" + item.ThreadUrl);
                    await Task.WhenAny(done.Task, Task.Delay(15000));
                }
                finally { core.NavigationCompleted -= h; }
                return ThreadOf(core.Source) == item.ThreadUrl;
            }

            // Link unknown: click the notification, Messenger opens the right conversation
            if (item.NotificationId <= 0) return false;
            await EvalAsync("window.__bubbleClick && window.__bubbleClick(" + item.NotificationId + ")");
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(200);
                string now = ThreadOf(core.Source);
                if (now != null && now != ThreadOf(before))
                {
                    item.ThreadUrl = now;
                    return true;
                }
            }
            // URL unchanged: the page may already show the right conversation → check by name
            if (ThreadOf(core.Source) != null && await HasNameAsync(item.Name))
            {
                item.ThreadUrl = ThreadOf(core.Source);
                return true;
            }
            return false;
        }

        private async Task<bool> HasNameAsync(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return await EvalAsync(HasNameJs(name)) == "true";
        }

        private async Task<int> ComposerLengthAsync()
        {
            string r = await EvalAsync(ComposerLengthJs);
            int n;
            return int.TryParse(r, out n) ? n : -1;
        }

        /// <summary>Sends <paramref name="text"/> to <paramref name="item"/>. Returns null on success, otherwise the reason.</summary>
        public async Task<string> SendAsync(Person item, string text)
        {
            if (!WebReady) return "Messenger is not ready";
            await _webLock.WaitAsync();
            bool parkedByMe = false;
            try
            {
                if (!Visible) { Park(); parkedByMe = true; }
                // Make the page believe it has focus so the composer accepts keys
                await CdpAsync("Emulation.setFocusEmulationEnabled", "{\"enabled\":true}");

                if (!await OpenThreadCoreAsync(item)) return "could not open the conversation";

                int len = -1;
                for (int i = 0; i < 40 && len < 0; i++)
                {
                    len = await ComposerLengthAsync();
                    if (len < 0) await Task.Delay(200);
                }
                if (len < 0) return "message field not found";

                // Safety: only send when the recipient's name is visible in the open conversation
                if (!await HasNameAsync(item.Name)) return "could not confirm the recipient, so nothing was sent";

                await EvalAsync(FocusComposerJs);
                await CdpAsync("Input.insertText", "{\"text\":" + JsonString(text) + "}");
                if (await ComposerLengthAsync() <= 0) return "could not insert the text";

                const string key = "\"key\":\"Enter\",\"code\":\"Enter\",\"windowsVirtualKeyCode\":13,\"nativeVirtualKeyCode\":13";
                await CdpAsync("Input.dispatchKeyEvent", "{\"type\":\"keyDown\"," + key + ",\"text\":\"\\r\",\"unmodifiedText\":\"\\r\"}");
                await CdpAsync("Input.dispatchKeyEvent", "{\"type\":\"keyUp\"," + key + "}");

                // Composer empty again = Facebook accepted the message
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(200);
                    if (await ComposerLengthAsync() == 0) return null;
                }
                return "Facebook did not accept the message, please try again";
            }
            catch (Exception ex) { return ex.Message; }
            finally
            {
                try { await CdpAsync("Emulation.setFocusEmulationEnabled", "{\"enabled\":false}"); } catch { }
                if (parkedByMe) Unpark();
                _webLock.Release();
            }
        }

        public void Reload()
        {
            if (WebReady) _web.CoreWebView2.Reload();
        }

        public void FocusWeb()
        {
            if (WebReady) _web.Focus();
        }



        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string json;
            try { json = e.TryGetWebMessageAsString(); } catch { return; }
            try
            {
                var ser = new DataContractJsonSerializer(typeof(PageNotification));
                PageNotification n;
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    n = (PageNotification)ser.ReadObject(ms);
                if (n.Type != "notif") return;
                if (n.Icon != null && !n.Icon.StartsWith("https://")) n.Icon = null;
                n.Body = StripLabel(n.Body);
                Log.Write("Incoming [" + (string.IsNullOrEmpty(n.Thread) ? "notification" : "chat list") + "]" +
                          " name:" + (string.IsNullOrEmpty(n.Title) ? "NO" : "yes") +
                          " text:" + (n.Body ?? "").Length + " chars" +
                          " avatar:" + (string.IsNullOrEmpty(n.Icon) ? "no" : "yes") +
                          " link:" + (string.IsNullOrEmpty(n.Thread) ? "no" : "yes") +
                          " id:" + n.Id);
                if (NotificationArrived != null) NotificationArrived(n);
            }
            catch (Exception ex) { Log.Write("Error reading message from page: " + ex.Message); }
        }

        private static bool IsFacebookUrl(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return true;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return true; // about:, data:, blob:
            string h = u.Host.ToLowerInvariant();
            foreach (var d in new[] { "facebook.com", "messenger.com", "fbcdn.net", "fbsbx.com", "meta.com" })
                if (h == d || h.EndsWith("." + d)) return true;
            return false;
        }

        public static void OpenExternal(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
