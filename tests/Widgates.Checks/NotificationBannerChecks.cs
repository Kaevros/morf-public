using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Widgates
{
    internal static class NotificationBannerChecks
    {
        internal static void Run()
        {
            var store = new Store();
            store.Values.Add("Chat.App!App", BannerValue.Missing);
            store.Values.Add("Mail.App!App", BannerValue.On);
            store.Values.Add("Already.Quiet!App", BannerValue.Off);
            store.Values.Add("Windows.Defender.SecurityCenter", BannerValue.On);
            store.Values.Add("Windows.SystemToast.LowDisk", BannerValue.On);
            store.Values.Add("Unexpected.Kind!App", BannerValue.Unsupported);
            var session = new NotificationBannerSession(store); session.Enable();
            Check(session.Active && session.Count == 2 && store.Values["Chat.App!App"] == BannerValue.Off
                && store.Values["Mail.App!App"] == BannerValue.Off, "Banner routing journals existing app preferences before changing only their banners");
            Check(store.Values["Windows.Defender.SecurityCenter"] == BannerValue.On && store.Values["Windows.SystemToast.LowDisk"] == BannerValue.On
                && store.Values["Unexpected.Kind!App"] == BannerValue.Unsupported, "System/security senders and unknown registry types are never changed");
            int writes = store.Writes, saves = store.Saves;
            session.Reconcile();
            Check(store.Writes == writes && store.Saves == saves, "An unchanged registry event performs no setting or journal writes");
            store.Values.Add("New.App!App", BannerValue.On); session.Reconcile();
            Check(store.Values["New.App!App"] == BannerValue.Off && session.Count == 3, "New registered sources can join routing on a registry event");
            session.Restore();
            Check(store.Values["Chat.App!App"] == BannerValue.Missing && store.Values["Mail.App!App"] == BannerValue.On
                && store.Values["Already.Quiet!App"] == BannerValue.Off && store.Journal.Length == 0,
                "Turning routing off restores absent, enabled and already-disabled preferences exactly");
            session.Enable();
            store.Values["Mail.App!App"] = BannerValue.On; session.Reconcile(); session.Reconcile();
            Check(store.Values["Mail.App!App"] == BannerValue.On, "A user's later banner change wins and is not repeatedly forced off");
            store.Values.Remove("Chat.App!App"); session.Restore();
            Check(!store.Values.ContainsKey("Chat.App!App"), "Restoration never recreates a removed application key");
            var privacy = new Store(); privacy.Values.Add("A.App!App", BannerValue.On); privacy.Unroutable.Add("A.App!App");
            var privacySession = new NotificationBannerSession(privacy); privacySession.Enable();
            Check(privacy.Writes == 0, "Explicitly disabled notifications or notification-center delivery are never overridden");
            privacySession.Restore(); privacy.Unroutable.Clear(); privacySession.Enable(); privacy.Unroutable.Add("A.App!App"); privacySession.Reconcile();
            Check(privacy.Values["A.App!App"] == BannerValue.On && privacySession.Count == 0,
                "Disabling notification-center delivery during routing immediately gives the source's original banner back");

            var crash = new Store(); crash.Values.Add("Chat.App!App", BannerValue.Missing);
            new NotificationBannerSession(crash).Enable();
            new NotificationBannerSession(crash).Recover();
            Check(crash.Values["Chat.App!App"] == BannerValue.Missing && crash.Journal.Length == 0,
                "An interrupted routing session recovers original settings from its bounded journal");
            var deniedJournal = new Store { DenySave = true }; deniedJournal.Values.Add("Chat.App!App", BannerValue.On);
            ExpectFailure(() => new NotificationBannerSession(deniedJournal).Enable());
            Check(deniedJournal.Writes == 0 && deniedJournal.Values["Chat.App!App"] == BannerValue.On,
                "Denied backup storage prevents all Windows preference changes");
            var partial = new Store(); partial.Values.Add("A.App!App", BannerValue.On); partial.Values.Add("B.App!App", BannerValue.Missing);
            partial.FailWriteOnce = "B.App!App";
            ExpectFailure(() => new NotificationBannerSession(partial).Enable());
            Check(partial.Values["A.App!App"] == BannerValue.On && partial.Values["B.App!App"] == BannerValue.Missing && partial.Journal.Length == 0,
                "A partial enable failure rolls already-changed app banners back");
            var restoreFailure = new Store(); restoreFailure.Values.Add("A.App!App", BannerValue.On);
            var retry = new NotificationBannerSession(restoreFailure); retry.Enable(); restoreFailure.FailWriteOnce = "A.App!App";
            ExpectFailure(retry.Restore);
            Check(restoreFailure.Journal.Length > 0, "A failed restore retains its original setting for a later recovery");
            new NotificationBannerSession(restoreFailure).Recover();
            Check(restoreFailure.Values["A.App!App"] == BannerValue.On, "A later recovery completes a previously denied restoration");

            foreach (var bad in new[] { "", "\\Escape", "..\\Escape", "App\\..\\Escape", "App\\\\Escape", "Windows.Defender.SecurityCenter", "App/Other", "C:\\Other", new string('x', 513) })
                if (NotificationBannerSession.Candidate(bad)) throw new InvalidOperationException("Unsafe sender accepted");
            Check(NotificationBannerSession.Candidate(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe"),
                "Sender validation bounds registry-relative IDs while retaining legitimate nested legacy senders");
            var corrupt = new Store { Journal = new byte[] { 1, 2, 3 } };
            var corruptSession = new NotificationBannerSession(corrupt);
            ExpectFailure(corruptSession.Recover); ExpectFailure(corruptSession.Restore);
            Check(corrupt.Saves == 0 && corrupt.Writes == 0 && corrupt.Journal.SequenceEqual(new byte[] { 1, 2, 3 }),
                "Malformed recovery data cannot trigger writes or destroy the original journal");
            var decoded = new Dictionary<string, BannerValue>();
            ExpectFailure(() => NotificationBannerSession.Decode(new byte[NotificationBannerSession.MaximumJournal + 1], decoded));
            Check(decoded.Count == 0, "Oversized journals are rejected before parsing or Windows writes");
            using (var hostile = new MemoryStream())
            using (var writer = new BinaryWriter(hostile))
            {
                writer.Write(0x4d424e31); writer.Write(1); writer.Write("Windows.Defender.SecurityCenter"); writer.Write((byte)BannerValue.On); writer.Flush();
                ExpectFailure(() => NotificationBannerSession.Decode(hostile.ToArray(), decoded));
                Check(decoded.Count == 0, "A tampered journal cannot restore or disable a protected Windows security sender");
            }
            Check(NotificationBannerGuard.Run(new[] { NotificationBannerGuard.Argument, "not-a-pid", "0", "not-a-token" }) == 2
                && NotificationBannerGuard.Run(new[] { NotificationBannerGuard.Argument, System.Diagnostics.Process.GetCurrentProcess().Id.ToString(), "1", Guid.NewGuid().ToString("N") }) == 2,
                "The helper rejects malformed commands and callers that are not its validated parent before touching settings");
        }
        private static void ExpectFailure(Action action) { bool failed = false; try { action(); } catch (IOException) { failed = true; } catch (InvalidDataException) { failed = true; } if (!failed) throw new InvalidOperationException("Expected bounded failure"); }
        private static void Check(bool okay, string text) { if (!okay) throw new InvalidOperationException(text); Console.WriteLine("PASS " + text); }
        private sealed class Store : INotificationBannerStore
        {
            internal readonly Dictionary<string, BannerValue> Values = new Dictionary<string, BannerValue>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<string> Unroutable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal byte[] Journal = new byte[0]; internal int Writes, Saves; internal bool DenySave; internal string FailWriteOnce;
            public IEnumerable<string> Senders() { return Values.Keys.ToArray(); }
            public bool CanRoute(string sender) { return Values.ContainsKey(sender) && !Unroutable.Contains(sender); }
            public BannerValue Read(string sender) { BannerValue value; return Values.TryGetValue(sender, out value) ? value : BannerValue.Deleted; }
            public void Write(string sender, BannerValue value)
            {
                if (FailWriteOnce == sender) { FailWriteOnce = null; throw new IOException("Denied test write"); }
                if (Journal.Length == 0) throw new InvalidOperationException("Setting changed before journal");
                Writes++; if (Values.ContainsKey(sender)) Values[sender] = value;
            }
            public byte[] LoadJournal() { return Journal; }
            public void SaveJournal(byte[] data) { if (DenySave) throw new IOException("Denied test journal"); Saves++; Journal = data; }
        }
    }
}
