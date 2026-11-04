using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace Lfc
{
    /// <summary>Someone who just messaged you and is waiting for a reply (or to be dismissed).</summary>
    internal sealed class Person
    {
        public string Key;            // identity: normalized display name
        public string Name;           // display name (also checked on the page before sending)
        public string IconUrl;
        public Image Avatar;
        public int NotificationId;    // page notification id (if any), used to open the right conversation
        public string ThreadUrl;      // conversation path, e.g. /messages/t/123

        /// <summary>Unsent text you typed for this person (kept when switching people / clicking away).</summary>
        public string Draft;
        /// <summary>Reason the last send failed (null = no error).</summary>
        public string Error;
        /// <summary>Sent you a message you have not answered yet.</summary>
        public bool Waiting;

        /// <summary>The message they just sent. Only the newest one is kept.</summary>
        public readonly List<string> Messages = new List<string>();
        private DateTime _lastAt;
        public const int MaxMessages = 1;

        public static string KeyOf(string name)
        {
            string t = (name ?? "").Normalize(NormalizationForm.FormC).Trim().ToLowerInvariant();
            return Regex.Replace(t, @"\s+", " ");
        }

        /// <summary>Adds a new message. The same text arriving twice within 15 s (from both sources) is ignored.</summary>
        public bool AddMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (Messages.Count > 0 && Messages[Messages.Count - 1] == text && (DateTime.Now - _lastAt).TotalSeconds < 15)
                return false;
            Messages.Add(text);
            _lastAt = DateTime.Now;
            while (Messages.Count > MaxMessages) Messages.RemoveAt(0);
            return true;
        }
    }
}
