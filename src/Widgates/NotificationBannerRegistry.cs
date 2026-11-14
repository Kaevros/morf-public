using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Widgates
{
    internal sealed class NotificationBannerRegistry : INotificationBannerStore
    {
        internal const string SenderRoot = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        internal const string ControlRoot = @"Software\Morf\NotificationRouting";
        internal const string ValueName = "ShowBanner";

        public IEnumerable<string> Senders()
        {
            var result = new List<string>();
            using (var root = Registry.CurrentUser.OpenSubKey(SenderRoot))
            {
                if (root == null) throw new IOException("Notification settings unavailable");
                int visited = 0; Collect(root, "", 0, result, ref visited);
            }
            return result;
        }
        private static void Collect(RegistryKey key, string prefix, int depth, List<string> result, ref int visited)
        {
            if (depth >= 8) return;
            if (key.SubKeyCount > NotificationBannerSession.MaximumSenders) throw new InvalidDataException();
            foreach (string name in key.GetSubKeyNames())
            {
                if (++visited > NotificationBannerSession.MaximumSenders * 2) throw new InvalidDataException();
                string sender = prefix.Length == 0 ? name : prefix + "\\" + name;
                if (!NotificationBannerSession.Candidate(sender)) continue;
                using (var child = key.OpenSubKey(name))
                {
                    if (child == null) continue;
                    // Nested legacy AUMIDs (for example the inbox PowerShell sender)
                    // have container keys. Only actual registered senders are changed.
                    string[] names = child.ValueCount <= 64 ? child.GetValueNames() : new string[0];
                    if (Array.Exists(names, n => n == "LastNotificationAddedTime" || n == "Enabled" || n == "PeriodicNotificationCount" || n == ValueName))
                    {
                        result.Add(sender);
                        if (result.Count > NotificationBannerSession.MaximumSenders) throw new InvalidDataException();
                    }
                    Collect(child, sender, depth + 1, result, ref visited);
                }
            }
        }

        public BannerValue Read(string sender)
        {
            Validate(sender);
            using (var key = Registry.CurrentUser.OpenSubKey(SenderRoot + "\\" + sender))
            {
                if (key == null) return BannerValue.Deleted;
                if (key.ValueCount > 64) return BannerValue.Unsupported;
                if (!Array.Exists(key.GetValueNames(), n => string.Equals(n, ValueName, StringComparison.OrdinalIgnoreCase))) return BannerValue.Missing;
                if (key.GetValueKind(ValueName) != RegistryValueKind.DWord) return BannerValue.Unsupported;
                object value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (!(value is int)) return BannerValue.Unsupported;
                return (int)value == 0 ? BannerValue.Off : (int)value == 1 ? BannerValue.On : BannerValue.Unsupported;
            }
        }
        public bool CanRoute(string sender)
        {
            if (!NotificationBannerSession.Candidate(sender)) return false;
            using (var key = Registry.CurrentUser.OpenSubKey(SenderRoot + "\\" + sender))
                return key != null && FlagAllows(key, "Enabled") && FlagAllows(key, "ShowInActionCenter");
        }
        private static bool FlagAllows(RegistryKey key, string name)
        {
            if (key.ValueCount > 64) return false;
            if (!Array.Exists(key.GetValueNames(), n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return true;
            return key.GetValueKind(name) == RegistryValueKind.DWord && object.Equals(key.GetValue(name), 1);
        }
        public void Write(string sender, BannerValue value)
        {
            Validate(sender);
            if (value != BannerValue.Missing && value != BannerValue.Off && value != BannerValue.On) throw new InvalidDataException();
            using (var key = Registry.CurrentUser.OpenSubKey(SenderRoot + "\\" + sender, true))
            {
                if (key == null) return; // Never recreate an uninstalled sender.
                if (value == BannerValue.Missing) key.DeleteValue(ValueName, false);
                else key.SetValue(ValueName, value == BannerValue.Off ? 0 : 1, RegistryValueKind.DWord);
            }
        }
        public byte[] LoadJournal()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(ControlRoot))
            {
                if (key == null) return new byte[0];
                return ReadBounded(key, "Journal", 3, NotificationBannerSession.MaximumJournal) ?? new byte[0];
            }
        }
        public void SaveJournal(byte[] data)
        {
            if (data == null || data.Length > NotificationBannerSession.MaximumJournal) throw new InvalidDataException();
            using (var key = Registry.CurrentUser.CreateSubKey(ControlRoot))
            {
                if (data.Length == 0) key.DeleteValue("Journal", false);
                else key.SetValue("Journal", data, RegistryValueKind.Binary);
                key.Flush();
            }
        }
        private static void Validate(string sender) { if (!NotificationBannerSession.Candidate(sender)) throw new InvalidDataException(); }
        internal static string ControlString(RegistryKey key, string name)
        {
            var data = ReadBounded(key, name, 1, 256);
            return data == null ? null : System.Text.Encoding.Unicode.GetString(data).TrimEnd('\0');
        }
        internal static string ControlRules(RegistryKey key)
        {
            var data = ReadBounded(key, "Rules", 1, SourceRules.MaximumLength * 2 + 2);
            return data == null ? "" : System.Text.Encoding.Unicode.GetString(data).TrimEnd('\0');
        }
        internal static int ControlNumber(RegistryKey key, string name, int fallback)
        {
            var data = ReadBounded(key, name, 4, 4);
            if (data == null) return fallback;
            if (data.Length != 4) throw new InvalidDataException();
            return BitConverter.ToInt32(data, 0);
        }
        private static byte[] ReadBounded(RegistryKey key, string name, uint expectedType, int maximum)
        {
            uint type, size = 0;
            int result = RegQueryValueEx(key.Handle, name, IntPtr.Zero, out type, null, ref size);
            if (result == 2) return null;
            if (result != 0 || type != expectedType || size > maximum) throw new InvalidDataException();
            var data = new byte[size];
            result = RegQueryValueEx(key.Handle, name, IntPtr.Zero, out type, data, ref size);
            if (result != 0 || type != expectedType || size != data.Length) throw new InvalidDataException();
            return data;
        }
        [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode)] private static extern int RegQueryValueEx(SafeRegistryHandle key, string name,
            IntPtr reserved, out uint type, [Out] byte[] data, ref uint size);
    }

    // Native one-shot registry notification; no timers or registry polling.
    internal sealed class RegistrySignal : IDisposable
    {
        private readonly RegistryKey key;
        internal readonly AutoResetEvent Event = new AutoResetEvent(false);
        internal RegistrySignal(string path)
        {
            key = Registry.CurrentUser.OpenSubKey(path, false);
            if (key == null) { Event.Dispose(); throw new IOException("Registry watch unavailable"); }
            try { Arm(); } catch { Dispose(); throw; }
        }
        internal void Arm()
        {
            int result = RegNotifyChangeKeyValue(key.Handle, true, 0x10000005,
                Event.SafeWaitHandle, true);
            if (result != 0) throw new System.ComponentModel.Win32Exception(result);
        }
        public void Dispose() { key.Dispose(); Event.Dispose(); }
        [DllImport("advapi32.dll")] private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key,
            [MarshalAs(UnmanagedType.Bool)] bool subtree, uint filter, SafeWaitHandle signal, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
    }
}
