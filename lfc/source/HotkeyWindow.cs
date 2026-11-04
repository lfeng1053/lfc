using System;
using System.Collections.Generic;
using System.Windows.Forms;
using static Lfc.NativeMethods;

namespace Lfc
{
    /// <summary>
    /// Global hotkeys, written like "Ctrl+Alt+R", "Ctrl+Alt+PageUp", "Alt+Q".
    /// Changed in the Settings window (tray icon → right-click → Settings).
    /// </summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const uint MOD_ALT = 0x1, MOD_WIN = 0x8;
        private readonly Dictionary<int, Action> _actions = new Dictionary<int, Action>();
        private int _nextId = 0xB0B;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        /// <summary>Registers a hotkey. An invalid string falls back to <paramref name="fallback"/>.</summary>
        public bool Register(string spec, string fallback, Action action)
        {
            uint mods; Keys key;
            if (!TryParse(spec, out mods, out key))
            {
                spec = fallback;
                if (!TryParse(spec, out mods, out key)) return false;
            }
            int id = _nextId++;
            bool ok = RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, (uint)key);
            if (ok) _actions[id] = action;
            Log.Write("Hotkey: " + spec + (ok ? "" : " (could NOT be registered - maybe another app uses it)"));
            return ok;
        }

        /// <summary>Unregisters every hotkey (e.g. while the Settings window records new ones).</summary>
        public void Clear()
        {
            foreach (int id in _actions.Keys) UnregisterHotKey(Handle, id);
            _actions.Clear();
        }

        /// <summary>
        /// Turns a key press into a hotkey string like "Ctrl+Alt+R".
        /// Returns null while only modifier keys are held, or when no Ctrl/Alt/Shift/Win is used.
        /// </summary>
        public static string FromKeyEvent(KeyEventArgs e)
        {
            Keys k = e.KeyCode;
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin ||
                k == Keys.None) return null;
            bool win = (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0;
            if (!e.Control && !e.Alt && !e.Shift && !win) return null;
            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Alt) parts.Add("Alt");
            if (e.Shift) parts.Add("Shift");
            if (win) parts.Add("Win");
            parts.Add(KeyName(k));
            return string.Join("+", parts);
        }

        /// <summary>Readable key name (PageUp instead of Prior, 1 instead of D1...).</summary>
        public static string KeyName(Keys k)
        {
            switch (k)
            {
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Return: return "Enter";
                case Keys.Escape: return "Escape";
            }
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            return k.ToString();
        }

        /// <summary>Parses a string like "Ctrl+Alt+R".</summary>
        public static bool TryParse(string spec, out uint mods, out Keys key)
        {
            mods = 0; key = Keys.None;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            foreach (string raw in spec.Split('+'))
            {
                string p = raw.Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") mods |= MOD_CONTROL;
                else if (p == "alt") mods |= MOD_ALT;
                else if (p == "shift") mods |= MOD_SHIFT;
                else if (p == "win") mods |= MOD_WIN;
                else if (p == "pgup") key = Keys.PageUp;
                else if (p == "pgdn" || p == "pagedn") key = Keys.PageDown;
                else if (p.Length == 1 && p[0] >= '0' && p[0] <= '9') key = Keys.D0 + (p[0] - '0');
                else
                {
                    Keys k;
                    if (!Enum.TryParse(raw.Trim(), true, out k)) return false;
                    key = k;
                }
            }
            return key != Keys.None;
        }

        protected override void WndProc(ref Message m)
        {
            Action a;
            if (m.Msg == WM_HOTKEY && _actions.TryGetValue(m.WParam.ToInt32(), out a)) a();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (int id in _actions.Keys) UnregisterHotKey(Handle, id);
            _actions.Clear();
            DestroyHandle();
        }
    }
}
