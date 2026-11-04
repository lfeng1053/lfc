using System;
using System.IO;
using System.Text;

namespace Lfc
{
    /// <summary>
    /// Simple log at %LOCALAPPDATA%\lfc\log.txt: incoming messages, sends, errors.
    /// Used for troubleshooting. Message text is never written, only its length.
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 512 * 1024;

        public static string FilePath
        {
            get { return Path.Combine(AppInfo.LocalDir, "log.txt"); }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    string path = FilePath;
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* logging must never break the app */ }
        }
    }
}
