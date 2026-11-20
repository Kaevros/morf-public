using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Widgates
{
    internal sealed class AppMessage
    {
        internal const string WhatsAppId = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App";
        internal const string WhatsAppBetaId = "5319275A.WhatsAppDesktopBeta_cv1g1gvanyjgm!App";
        internal const string WhatsAppCurrentBetaId = "5319275A.51895FA4EA97F_cv1g1gvanyjgm!App";
        internal string Key, Sender, Body;
        internal string AppId, AppName;
        internal DateTimeOffset Created;
        internal bool Sample;
        internal string MediaTrackIdentity;
        internal string DisplayTitle(bool content)
        {
            return !content || string.IsNullOrWhiteSpace(Sender) ? "Yeni bildirim" : Sender;
        }
        internal string DisplayBody(bool content) { return !content ? "İçerik gizli" : Body ?? ""; }
        internal static bool IsWhatsApp(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (string.Equals(id, WhatsAppId, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(id, WhatsAppBetaId, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(id, WhatsAppCurrentBetaId, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(id, "WhatsApp", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "WhatsApp.Desktop", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        internal static string Clean(string value, int limit)
        {
            return new string((value ?? "").Where(c => !char.IsControl(c) && c != '\u202e' && c != '\u202d').Take(limit).ToArray()).Trim();
        }
        internal static AppMessage Create(uint id, DateTimeOffset created, IEnumerable<string> text, string appId = "", string appName = "")
        {
            appId = Clean(appId, 256); appName = Clean(appName, 80);
            string[] parts = text.Take(32).Select(value => Clean(value, 16384)).Where(value => value.Length > 0).ToArray();
            if (parts.Length == 0) return null;
            string sender = Clean(parts[0], 4096), body = Clean(string.Join(" · ", parts.Skip(1)), 16384);
            string digest;
            using (var hash = SHA256.Create()) digest = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(sender + "\n" + body)));
            return new AppMessage { Key = appId + ":" + id + ":" + created.UtcDateTime.Ticks + ":" + digest, Sender = sender, Body = body, Created = created,
                AppId = appId, AppName = appName };
        }
    }

    // Keeps only the current Windows snapshot's bounded identities, never a message history.
    internal sealed class AppMessageTracker
    {
        private HashSet<string> previous = new HashSet<string>(StringComparer.Ordinal);
        private bool baseline;
        private DateTimeOffset? connectedAt;
        internal AppMessage[] Observe(IEnumerable<AppMessage> snapshot)
        {
            var messages = snapshot.Where(x => x != null).OrderByDescending(x => x.Created).Take(256).ToArray();
            var fresh = messages.Where(x => baseline ? !previous.Contains(x.Key)
                : connectedAt.HasValue && x.Created >= connectedAt.Value).OrderBy(x => x.Created).ToArray();
            previous = new HashSet<string>(messages.Select(x => x.Key), StringComparer.Ordinal); baseline = true;
            return fresh;
        }
        internal void Reset(DateTimeOffset? start = null) { previous.Clear(); baseline = false; connectedAt = start; }
    }
}
