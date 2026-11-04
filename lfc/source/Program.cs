using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Lfc
{
    /// <summary>App name and data folders.</summary>
    internal static class AppInfo
    {
        public const string Name = "lfc";
        private const string OldName = "BongBongChat";   // previous name, migrated on first start

        /// <summary>Command-line flag used by the "Start with Windows" entry.</summary>
        public const string AutostartArg = "--autostart";

        /// <summary>Value written to the Windows "Run" key.</summary>
        public static string RunCommand
        {
            get { return "\"" + Application.ExecutablePath + "\" " + AutostartArg; }
        }

        /// <summary>%APPDATA%\lfc — settings.json</summary>
        public static string RoamingDir
        {
            get { return Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name)); }
        }

        /// <summary>%LOCALAPPDATA%\lfc — log.txt and the WebView2 profile (Facebook login)</summary>
        public static string LocalDir
        {
            get { return Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name)); }
        }

        private static string Ensure(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Moves data from the old "BongBongChat" folders to "lfc" so settings and the
        /// Facebook login are kept, and replaces the old "start with Windows" entry.
        /// </summary>
        public static void MigrateFromOldName()
        {
            MoveFolder(Environment.SpecialFolder.ApplicationData);
            MoveFolder(Environment.SpecialFolder.LocalApplicationData);
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k != null && k.GetValue(OldName) != null)
                    {
                        k.DeleteValue(OldName, false);
                        k.SetValue(Name, RunCommand);
                    }
                    // Older lfc entry without the autostart flag → update it
                    var cur = k == null ? null : k.GetValue(Name) as string;
                    if (cur != null && cur.IndexOf(AutostartArg, StringComparison.Ordinal) < 0)
                        k.SetValue(Name, RunCommand);
                }
            }
            catch { }
        }

        private static void MoveFolder(Environment.SpecialFolder root)
        {
            try
            {
                string baseDir = Environment.GetFolderPath(root);
                string oldDir = Path.Combine(baseDir, OldName), newDir = Path.Combine(baseDir, Name);
                if (Directory.Exists(oldDir) && !Directory.Exists(newDir)) Directory.Move(oldDir, newDir);
            }
            catch { /* old folder in use or locked: start fresh */ }
        }
    }

    internal static class Program
    {
        public static Image DefaultAvatar { get; private set; }
        public static Icon AppIcon { get; private set; }

        [STAThread]
        private static void Main(string[] args)
        {
            NativeMethods.EnableDpiAwareness();

            // Single instance. Starting it again tells the running copy to open the reply box (or show a notice).
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\lfc.SingleInstance", out createdNew))
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\lfc.Show"))
            {
                if (!createdNew)
                {
                    showSignal.Set();
                    return;
                }

                AppInfo.MigrateFromOldName();
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lfc.icon.png"))
                using (var img = Image.FromStream(s))
                    DefaultAvatar = Gfx.SquareCrop(img, 128);
                try { AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { AppIcon = SystemIcons.Application; }

                bool autostart = Array.IndexOf(args, AppInfo.AutostartArg) >= 0;
                Application.Run(new BubbleApp(showSignal, autostart));
                GC.KeepAlive(mutex);
            }
        }
    }
}
