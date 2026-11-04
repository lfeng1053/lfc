using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Lfc
{
    /// <summary>Settings stored in %APPDATA%\lfc\settings.json</summary>
    [DataContract]
    internal sealed class Settings
    {
        public const string DefaultHotkey = "Ctrl+Alt+R";
        public const string DefaultPrevHotkey = "Ctrl+Alt+PageUp";
        public const string DefaultNextHotkey = "Ctrl+Alt+PageDown";

        /// <summary>Hotkey to open the reply box, e.g. "Ctrl+Alt+R", "Alt+Q".</summary>
        [DataMember] public string ReplyHotkey = DefaultHotkey;

        /// <summary>Hotkeys to switch to the previous / next person waiting.</summary>
        [DataMember] public string PrevHotkey = DefaultPrevHotkey;
        [DataMember] public string NextHotkey = DefaultNextHotkey;

        /// <summary>true = hide the small dots shown when several people are waiting.</summary>
        [DataMember] public bool HidePagerDots;

        /// <summary>Your avatar in the reply box. Empty = taken from your Facebook account.
        /// Example: "D:\\Mes\\lfc\\me.png".</summary>
        [DataMember] public string MyAvatarPath;

        /// <summary>Top-left position of the message box on screen (null = right edge, 1/3 down).</summary>
        [DataMember] public int? BubbleX;
        [DataMember] public int? BubbleY;

        /// <summary>Size of the sign-in window.</summary>
        [DataMember] public int ChatWidth = 420;
        [DataMember] public int ChatHeight = 620;

        public bool IsFirstRun { get; private set; }

        private static string FilePath
        {
            get { return Path.Combine(AppInfo.RoamingDir, "settings.json"); }
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var s = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(fs);
                        if (s != null)
                        {
                            // Older settings files lack newer entries: fill in defaults
                            if (string.IsNullOrWhiteSpace(s.ReplyHotkey)) s.ReplyHotkey = DefaultHotkey;
                            if (string.IsNullOrWhiteSpace(s.PrevHotkey)) s.PrevHotkey = DefaultPrevHotkey;
                            if (string.IsNullOrWhiteSpace(s.NextHotkey)) s.NextHotkey = DefaultNextHotkey;
                            return s;
                        }
                    }
                }
            }
            catch { /* corrupt file: use defaults */ }
            return new Settings { IsFirstRun = true };
        }

        public void Save()
        {
            try
            {
                using (var fs = File.Create(FilePath))
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(fs, this);
            }
            catch { }
        }
    }
}
