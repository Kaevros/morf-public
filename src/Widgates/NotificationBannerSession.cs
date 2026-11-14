using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Widgates
{
    internal enum BannerValue { Missing, Off, On, Unsupported, Deleted }

    internal interface INotificationBannerStore
    {
        IEnumerable<string> Senders();
        bool CanRoute(string sender);
        BannerValue Read(string sender);
        void Write(string sender, BannerValue value);
        byte[] LoadJournal();
        void SaveJournal(byte[] data);
    }

    // The entire write surface is an existing sender's ShowBanner DWORD. No app
    // activation, paths, commands, notification contents, global enable or policy writes.
    internal sealed class NotificationBannerSession
    {
        internal const int MaximumSenders = 2048, MaximumJournal = 512 * 1024;
        private readonly INotificationBannerStore store;
        private readonly Dictionary<string, BannerValue> owned = new Dictionary<string, BannerValue>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> relinquished = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool journalTrusted;
        internal bool Active { get; private set; }
        internal int Count { get { return owned.Count; } }
        internal Func<string, bool> Selected = sender => true;

        internal NotificationBannerSession(INotificationBannerStore store) { this.store = store; }

        internal static bool Candidate(string sender)
        {
            if (string.IsNullOrWhiteSpace(sender) || sender.Length > 512 || sender[0] == '\\' || sender.EndsWith("\\", StringComparison.Ordinal)) return false;
            if (sender.Any(c => char.IsControl(c) || char.IsSurrogate(c) || c == '/' || c == ':' || c == '"')) return false;
            var parts = sender.Split('\\');
            if (parts.Length > 8 || parts.Any(p => p.Length == 0 || p == "." || p == "..")) return false;
            // Critical/system sources stay in Windows and are never mirrored by this mode.
            return !new[] { "Windows.", "Microsoft.Windows.", "MicrosoftWindows.", "Microsoft.Explorer.Notification.",
                "Microsoft.AAD.", "Microsoft.AccountsControl", "Microsoft.SecHealthUI", "Microsoft.WindowsDefender", "Morf." }.Any(p => sender.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        internal void Recover()
        {
            if (Active) throw new InvalidOperationException();
            Decode(store.LoadJournal(), owned);
            journalTrusted = true;
            Restore();
        }

        internal void Enable()
        {
            if (Active) { Reconcile(); return; }
            Recover(); relinquished.Clear(); Active = true;
            try { Reconcile(); }
            catch { Restore(); throw; }
        }

        internal void Reconcile()
        {
            if (!Active) return;
            var senders = store.Senders().Take(MaximumSenders + 1).ToArray();
            if (senders.Length > MaximumSenders) throw new InvalidDataException("Sender limit");
            var additions = new Dictionary<string, BannerValue>(StringComparer.OrdinalIgnoreCase);
            bool journalChanged = false;
            foreach (var entry in owned.ToArray())
            {
                if (Selected(entry.Key) && store.CanRoute(entry.Key)) continue;
                // If a user disables notification-center delivery, immediately give
                // their banner preference back rather than muting that source.
                if (store.Read(entry.Key) == BannerValue.Off)
                {
                    store.Write(entry.Key, entry.Value);
                    if (store.Read(entry.Key) != entry.Value && store.Read(entry.Key) != BannerValue.Deleted) throw new IOException("Banner restoration not observed");
                }
                owned.Remove(entry.Key); if (!store.CanRoute(entry.Key)) relinquished.Add(entry.Key); journalChanged = true;
            }
            foreach (string sender in senders)
            {
                if (!Candidate(sender) || !Selected(sender) || relinquished.Contains(sender) || !store.CanRoute(sender)) continue;
                BannerValue current = store.Read(sender);
                if (owned.ContainsKey(sender))
                {
                    // A user/system change after takeover wins. Never keep fighting it.
                    if (current != BannerValue.Off)
                    {
                        owned.Remove(sender); relinquished.Add(sender); journalChanged = true;
                    }
                    continue;
                }
                if (current != BannerValue.On && current != BannerValue.Missing) continue;
                additions.Add(sender, current); owned.Add(sender, current);
            }
            // One durable batch before any setting writes. No FlushKey per sender.
            if (additions.Count == 0 && !journalChanged) return;
            try { Persist(); }
            catch { foreach (string sender in additions.Keys) owned.Remove(sender); throw; }
            foreach (var addition in additions)
            {
                // Recheck after the write-ahead journal, and never recreate a deleted key.
                if (store.Read(addition.Key) != addition.Value) { owned.Remove(addition.Key); relinquished.Add(addition.Key); journalChanged = true; continue; }
                store.Write(addition.Key, BannerValue.Off);
                if (store.Read(addition.Key) != BannerValue.Off) throw new IOException("Banner write not observed");
            }
            if (journalChanged) Persist();
        }

        internal void Restore()
        {
            Active = false;
            if (!journalTrusted) throw new InvalidDataException("Recovery journal has not been validated");
            Exception failure = null;
            foreach (var entry in owned.ToArray())
            {
                try
                {
                    // Restore only values still carrying our exact write. A changed,
                    // removed or unsupported value belongs to the user/Windows now.
                    if (store.Read(entry.Key) == BannerValue.Off)
                    {
                        store.Write(entry.Key, entry.Value);
                        if (store.Read(entry.Key) != entry.Value && store.Read(entry.Key) != BannerValue.Deleted)
                            throw new IOException("Banner restoration not observed");
                    }
                    owned.Remove(entry.Key);
                }
                catch (Exception error) { failure = error; }
            }
            // On a partial failure the remaining originals survive for recovery.
            Persist(); relinquished.Clear();
            if (failure != null) throw new IOException("Banner restoration incomplete", failure);
        }

        private void Persist() { store.SaveJournal(Encode(owned)); }

        internal static byte[] Encode(IDictionary<string, BannerValue> entries)
        {
            if (entries.Count > MaximumSenders) throw new InvalidDataException();
            if (entries.Count == 0) return new byte[0];
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write(0x4d424e31); writer.Write(entries.Count);
                foreach (var entry in entries)
                {
                    if (!Candidate(entry.Key) || (entry.Value != BannerValue.On && entry.Value != BannerValue.Missing)) throw new InvalidDataException();
                    writer.Write(entry.Key); writer.Write((byte)entry.Value);
                }
                writer.Flush(); if (memory.Length > MaximumJournal) throw new InvalidDataException(); return memory.ToArray();
            }
        }

        internal static void Decode(byte[] data, IDictionary<string, BannerValue> target)
        {
            target.Clear(); if (data == null || data.Length == 0) return;
            if (data.Length > MaximumJournal) throw new InvalidDataException();
            var parsed = new Dictionary<string, BannerValue>(StringComparer.OrdinalIgnoreCase);
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                if (reader.ReadInt32() != 0x4d424e31) throw new InvalidDataException();
                int count = reader.ReadInt32(); if (count < 0 || count > MaximumSenders) throw new InvalidDataException();
                for (int i = 0; i < count; i++)
                {
                    string sender = reader.ReadString(); BannerValue value = (BannerValue)reader.ReadByte();
                    if (!Candidate(sender) || (value != BannerValue.On && value != BannerValue.Missing) || parsed.ContainsKey(sender)) throw new InvalidDataException();
                    parsed.Add(sender, value);
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException();
            }
            foreach (var entry in parsed) target.Add(entry.Key, entry.Value);
        }
    }
}
