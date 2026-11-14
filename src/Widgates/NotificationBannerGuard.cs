using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Widgates
{
    // Same signed EXE, standard user, fixed HKCU keys only. No shell, service,
    // listening socket, arbitrary command/path argument or recurring timer.
    internal static class NotificationBannerGuard
    {
        internal const string Argument = "--notification-banner-guard";
        internal static int Run(string[] args)
        {
            if (args.Length != 4 || args[0] != Argument) return 2;
            int parentId; long ticks; Guid token;
            if (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out parentId) || parentId <= 0
                || !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out ticks)
                || !Guid.TryParseExact(args[3], "N", out token) || token == Guid.Empty) return 2;
            string owner = token.ToString("N");
            try
            {
                using (var parent = Process.GetProcessById(parentId))
                using (var current = Process.GetCurrentProcess())
                {
                    if (ParentId(current.Id) != parentId || parent.SessionId != current.SessionId
                        || parent.StartTime.ToUniversalTime().Ticks != ticks
                        || !string.Equals(parent.MainModule.FileName, current.MainModule.FileName, StringComparison.OrdinalIgnoreCase)) return 2;
                    string sid; using (var identity = WindowsIdentity.GetCurrent()) sid = identity.User.Value;
                    using (var mutex = new Mutex(false, @"Local\Morf.NotificationRouting." + sid))
                    {
                        bool entered;
                        try { entered = mutex.WaitOne(5000); } catch (AbandonedMutexException) { entered = true; }
                        if (!entered) return 3;
                        try { return Watch(parent, owner); } finally { mutex.ReleaseMutex(); }
                    }
                }
            }
            catch { return 1; }
        }

        private static int Watch(Process parent, string owner)
        {
            var session = new NotificationBannerSession(new NotificationBannerRegistry());
            RegistrySignal senders = null;
            try
            {
                session.Recover();
                using (var control = new RegistrySignal(NotificationBannerRegistry.ControlRoot))
                using (var died = new EventWaitHandle(false, EventResetMode.ManualReset))
                {
                    died.SafeWaitHandle.Dispose(); died.SafeWaitHandle = new SafeWaitHandle(parent.Handle, false);
                    bool first = true;
                    while (true)
                    {
                        // Parent death always wins over pending changes. The handle
                        // refers to that exact process, so PID reuse cannot extend ownership.
                        if (died.WaitOne(0)) break;
                        if (!OwnsControl(owner)) break;
                        bool desired = Desired();
                        using (var key = Registry.CurrentUser.OpenSubKey(NotificationBannerRegistry.ControlRoot))
                        {
                            string ruleText = NotificationBannerRegistry.ControlRules(key);
                            var rules = SourceRules.Parse(ruleText);
                            if (ruleText != rules.Text) throw new InvalidDataException("Invalid source preferences");
                            session.Selected = sender => rules.Get(sender) != 2;
                        }
                        if (desired && !session.Active)
                        {
                            senders = new RegistrySignal(NotificationBannerRegistry.SenderRoot);
                            session.Enable(); SetState(owner, 1, null);
                        }
                        else if (!desired && (first || session.Active))
                        {
                            if (senders != null) { senders.Dispose(); senders = null; }
                            session.Restore(); SetState(owner, 0, null);
                        }
                        else if (desired && session.Active) session.Reconcile();
                        first = false;
                        var handles = senders == null ? new WaitHandle[] { died, control.Event }
                            : new WaitHandle[] { died, control.Event, senders.Event };
                        int changed = WaitHandle.WaitAny(handles);
                        if (changed == 0) break;
                        if (changed == 1) control.Arm();
                        if (changed == 2)
                        {
                            senders.Arm();
                            session.Reconcile();
                        }
                    }
                }
                session.Restore(); SetState(owner, 0, null); return 0;
            }
            catch (Exception error)
            {
                try { session.Restore(); SetState(owner, 2, Code(error)); }
                catch (Exception restore) { SetState(owner, 3, Code(restore)); }
                return 1;
            }
            finally { if (senders != null) senders.Dispose(); }
        }
        internal static string Code(Exception error) { return error.GetType().Name + " " + error.HResult.ToString("X8"); }
        internal static bool OwnsControl(string owner)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(NotificationBannerRegistry.ControlRoot))
                return key != null && string.Equals(NotificationBannerRegistry.ControlString(key, "Owner"), owner, StringComparison.Ordinal);
        }
        private static bool Desired()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(NotificationBannerRegistry.ControlRoot))
                return key != null && NotificationBannerRegistry.ControlNumber(key, "Desired", 0) == 1;
        }
        internal static void SetState(string owner, int state, string failure)
        {
            if (!OwnsControl(owner)) return;
            using (var key = Registry.CurrentUser.OpenSubKey(NotificationBannerRegistry.ControlRoot, true))
            {
                key.SetValue("StateOwner", owner, RegistryValueKind.String);
                key.SetValue("State", state, RegistryValueKind.DWord);
                if (failure == null) key.DeleteValue("Failure", false);
                else key.SetValue("Failure", failure, RegistryValueKind.String);
            }
        }

        private static int ParentId(int process)
        {
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) return 0;
            try
            {
                var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                if (!Process32First(snapshot, ref entry)) return 0;
                do { if (entry.ProcessId == process) return (int)entry.ParentProcessId; } while (Process32Next(snapshot, ref entry));
                return 0;
            }
            finally { CloseHandle(snapshot); }
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProcessEntry
        {
            internal uint Size, Usage, ProcessId; internal UIntPtr Heap; internal uint Module, Threads, ParentProcessId;
            internal int Priority; internal uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string File;
        }
        [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint process);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    }
}
