using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.Foundation;

namespace Widgates
{
    internal interface IStartupStore
    {
        string Read();
        void Write(string command);
        void Remove();
    }

    internal interface IStartupTaskStore
    {
        Task<StartupTaskState> ReadAsync();
        Task<StartupTaskState> EnableAsync();
        Task<StartupTaskState> DisableAsync();
    }

    internal sealed class StartupRegistration
    {
        private readonly IStartupStore store;
        private readonly string command;
        private readonly IStartupTaskStore packaged;
        internal const string TaskId = "MorfStartup";
        internal string Notice { get; private set; }
        internal StartupRegistration() : this(new UserStartupStore(), ExecutablePath())
        { if (AppNotificationListener.HasIdentity) packaged = new WindowsStartupTaskStore(); }
        internal StartupRegistration(IStartupTaskStore startupTask) { packaged = startupTask; }
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
            if (packaged != null) return false; // Packaged operations must stay asynchronous.
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
            if (packaged != null) return false;
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

        internal async Task<bool> InitializeAsync(WidgetSettings settings)
        {
            if (packaged == null) return Initialize(settings);
            try
            {
                settings.StartWithWindows = IsEnabled(await packaged.ReadAsync());
                settings.StartupConfigured = true;
                return true;
            }
            catch (Exception) { return false; }
        }

        internal async Task<bool> ConfigureAsync(bool enabled)
        {
            Notice = null;
            if (packaged == null) return Configure(enabled);
            try
            {
                var current = await packaged.ReadAsync();
                if (IsEnabled(current) == enabled) return true;
                if (enabled && (current == StartupTaskState.DisabledByUser || current == StartupTaskState.DisabledByPolicy))
                {
                    Notice = "Windows başlangıcı kullanıcı veya ilke tarafından kapatılmış. Windows Ayarları > Uygulamalar > Başlangıç bölümünden Morf'u kontrol edin.";
                    return false;
                }
                var result = enabled ? await packaged.EnableAsync() : await packaged.DisableAsync();
                return IsEnabled(result) == enabled;
            }
            catch (Exception) { return false; }
        }

        private static bool IsEnabled(StartupTaskState state)
        { return state == StartupTaskState.Enabled || state == StartupTaskState.EnabledByPolicy; }

        private sealed class WindowsStartupTaskStore : IStartupTaskStore
        {
            private StartupTask task;
            private async Task<StartupTask> GetAsync()
            { if (task == null) task = await Complete(StartupTask.GetAsync(TaskId)); return task; }
            public async Task<StartupTaskState> ReadAsync() { return (await GetAsync()).State; }
            public async Task<StartupTaskState> EnableAsync() { return await Complete((await GetAsync()).RequestEnableAsync()); }
            public async Task<StartupTaskState> DisableAsync() { var current = await GetAsync(); current.Disable(); return current.State; }
            private static async Task<T> Complete<T>(IAsyncOperation<T> operation)
            {
                var completion = new TaskCompletionSource<T>();
                operation.Completed = (result, status) => { try { completion.TrySetResult(result.GetResults()); } catch (Exception error) { completion.TrySetException(error); } };
                return await OperationDeadline.Run(completion.Task, () => operation.Cancel(), 5000);
            }
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
