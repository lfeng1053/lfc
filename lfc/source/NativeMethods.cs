using System;
using System.Runtime.InteropServices;

namespace Lfc
{
    /// <summary>Win32 functions used by the app (layered windows, global hotkeys, DPI...).</summary>
    internal static class NativeMethods
    {
        public const int WS_EX_TOPMOST = 0x8;
        public const int WS_EX_TRANSPARENT = 0x20;      // mouse goes through the window
        public const int LWA_ALPHA = 0x2;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x8000000;
        public const int CS_DROPSHADOW = 0x20000;

        public const int WM_NCHITTEST = 0x84;
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int WM_HOTKEY = 0x312;
        public const int WM_MOUSEACTIVATE = 0x21;
        public const int MA_NOACTIVATE = 3;
        public const int HTCAPTION = 2;

        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0;
        public const byte AC_SRC_ALPHA = 1;

        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_SHIFT = 0x4;
        public const uint MOD_NOREPEAT = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] public static extern short GetKeyState(int nVirtKey);
        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int crKey, byte bAlpha, int dwFlags);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public const int EM_SETCUEBANNER = 0x1501;

        /// <summary>Windows 11 rounded window corners (ignored on Windows 10).</summary>
        public static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
            }
            catch { }
        }

        /// <summary>Enables DPI awareness so text stays sharp at 125%, 150%... scaling.</summary>
        public static void EnableDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = -2 (Windows 10 1703+)
                if (SetProcessDpiAwarenessContext(new IntPtr(-2))) return;
            }
            catch (EntryPointNotFoundException) { }
            try { SetProcessDPIAware(); } catch { }
        }
    }
}
