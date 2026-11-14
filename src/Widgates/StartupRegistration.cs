using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Widgates
{
    internal interface IStartupStore
    {
        string Read();
        void Write(string command);
        void Remove();
    }

    internal sealed class StartupRegistration
    {
        private readonly IStartupStore store;
        private readonly string command;
        internal StartupRegistration() : this(new UserStartupStore(), ExecutablePath()) { }
        internal StartupRegistration(IStartupStore store, string executable)
        {
            this.store = store;
            command = Command(executable);
        }

        internal static string Command(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable) || executable.Length < 3 || executable[1] != ':'
                || !((executable[0] >= 'A' && executable[0] <= 'Z') || (executable[0] >= 'a' && executable[0] <= 'z'))
                || (executable[2] != '\\' && executable[2] != '/')
                || executable.IndexOf('"') >= 0 || executable.AnyControl()) return null;
            string result = "\"" + executable + "\"";
            return result.Length <= 260 ? result : null;
        }

        private static string ExecutablePath()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    string executable = process.MainModule.FileName;
                    if (!string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase)) return executable;
                    // A DLL started by the SDK must register its apphost, never the SDK host itself.
                    string apphost = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WindowsWidgates.exe");
                    return File.Exists(apphost) ? apphost : null;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal bool Configure(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    if (command == null) return false;
                    if (!string.Equals(store.Read(), command, StringComparison.OrdinalIgnoreCase)) store.Write(command);
                    return string.Equals(store.Read(), command, StringComparison.OrdinalIgnoreCase);
                }
                if (store.Read() != null) store.Remove();
                return store.Read() == null;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
            catch (IOException) { return false; }
        }

        internal bool Initialize(WidgetSettings settings)
        {
            bool changed;
            return Initialize(settings, out changed);
        }

        internal bool Initialize(WidgetSettings settings, out bool changed)
        {
            changed = false;
            try
            {
                // Startup is a user action. Launching or upgrading the app must not
                // create, repair, retarget or remove a Windows startup registration.
                string current = store.Read();
                bool enabled = command != null && string.Equals(current, command, StringComparison.OrdinalIgnoreCase);
                changed = settings.StartWithWindows != enabled || !settings.StartupConfigured;
                settings.StartWithWindows = enabled;
                settings.StartupConfigured = true;
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
            catch (IOException) { return false; }
        }

        private sealed class UserStartupStore : IStartupStore
        {
            private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            private const string ValueName = "WindowsWidgates.Morf";
            public string Read() { using (var key = Registry.CurrentUser.OpenSubKey(KeyPath)) return key == null ? null : key.GetValue(ValueName) as string; }
            public void Write(string command) { using (var key = Registry.CurrentUser.CreateSubKey(KeyPath)) key.SetValue(ValueName, command, RegistryValueKind.String); }
            public void Remove() { using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, true)) if (key != null) key.DeleteValue(ValueName, false); }
        }
    }

    internal static class StartupPathValidation
    {
        internal static bool AnyControl(this string value)
        {
            foreach (char character in value) if (char.IsControl(character)) return true;
            return false;
        }
    }
}
