using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Widgates
{
    internal enum MediaApplication { AppleMusic, Spotify, YouTube }

    internal sealed class MediaAppLauncher
    {
        private readonly Func<MediaApplication, bool> launch;
        private readonly Func<string, bool> notificationLaunch;
        private readonly Func<string, bool> notificationPackageLaunch;
        private int busy;
        internal MediaAppLauncher(Func<MediaApplication, bool> launch = null, Func<string, bool> notificationLaunch = null, Func<string, bool> notificationPackageLaunch = null)
        {
            this.launch = launch ?? LaunchKnownApplication; this.notificationLaunch = notificationLaunch ?? NotificationAppActivation.Open;
            this.notificationPackageLaunch = notificationPackageLaunch ?? ActivateKnownApplication;
        }
        internal async Task<string> OpenAsync(MediaApplication application)
        {
            if (!Enum.IsDefined(typeof(MediaApplication), application)) return "Bu uygulama desteklenmiyor.";
            return await OpenKnownAsync(() => launch(application), Name(application));
        }
        internal Task<string> OpenWhatsAppAsync() { return OpenNotificationAppAsync(AppMessage.WhatsAppId, "WhatsApp"); }
        internal Task<string> OpenNotificationAppAsync(string appId, string name)
        {
            // Only a registered Windows identity from AppInfo; never toast text, arguments or paths.
            if (!ValidNotificationAppId(appId)) return Task.FromResult("Bu bildirimi Windows bildirim merkezinden açabilirsiniz.");
            return OpenKnownAsync(() => AppMessage.IsWhatsApp(appId)
                ? WhatsAppNotificationActivation.Open(appId, notificationLaunch, notificationPackageLaunch)
                : notificationLaunch(appId), AppMessage.Clean(name, 80));
        }
        internal static bool ValidNotificationAppId(string appId)
        {
            if (string.IsNullOrEmpty(appId) || appId.Length > 256 || appId == "." || appId == ".."
                || !appId.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == '!' || c == '{' || c == '}')) return false;
            int guidStart = appId.IndexOf('{'); Guid identifier;
            return guidStart < 0 ? appId.IndexOf('}') < 0
                : appId.IndexOf('}') == appId.Length - 1 && Guid.TryParse(appId.Substring(guidStart), out identifier);
        }
        private async Task<string> OpenKnownAsync(Func<bool> start, string name)
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return "Önceki açma isteği sürüyor.";
            Task<bool> pending = null;
            try
            {
                // Callers provide a fixed target or a validated registered Windows application identity.
                pending = StartOnSta(start);
                bool opened = await OperationDeadline.Run(pending, () => { }, 5000);
                return opened ? "Açma isteği Windows'a iletildi." : name + " açılamadı. Uygulamanın yüklü ve kullanılabilir olduğunu kontrol edin.";
            }
            catch (TimeoutException) { return "Windows henüz yanıt vermedi. Uygulama geç açılabilir; durumunu kontrol edin."; }
            catch (Exception error)
            {
                if (!(error is COMException) && !(error is Win32Exception) && !(error is InvalidOperationException)
                    && !(error is UnauthorizedAccessException) && !(error is System.Security.SecurityException)) throw;
                return name + " açılamadı. Windows uygulama kaydını kontrol edin.";
            }
            finally
            {
                if (pending == null || pending.IsCompleted) Interlocked.Exchange(ref busy, 0);
                else { Task released = pending.ContinueWith(_ => Interlocked.Exchange(ref busy, 0)); }
            }
        }
        private static Task<bool> StartOnSta(Func<bool> start)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => { try { completion.TrySetResult(start()); } catch (Exception error) { completion.TrySetException(error); } });
            thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
            return completion.Task;
        }
        internal static bool TryIdentify(string source, out MediaApplication application)
        {
            string friendly = MediaSourceNames.Friendly(source ?? "");
            application = friendly == "Apple Music" ? MediaApplication.AppleMusic : MediaApplication.Spotify;
            return friendly == "Apple Music" || friendly == "Spotify";
        }
        internal static string Name(MediaApplication application) { return application == MediaApplication.AppleMusic ? "Apple Music" : application == MediaApplication.Spotify ? "Spotify" : "YouTube"; }
        private static bool LaunchKnownApplication(MediaApplication application)
        {
            if (application == MediaApplication.AppleMusic)
                return ActivateKnownApplication("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App");
            string target;
            if (application == MediaApplication.Spotify)
            {
                var association = new StringBuilder(2048); uint count = (uint)association.Capacity;
                if (AssocQueryString(0x1000, 20, "spotify", null, association, ref count) != 0) return false;
                target = "spotify:";
            }
            else if (application == MediaApplication.YouTube) target = "https://www.youtube.com/";
            else return false;
            using (var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })) { }
            return true;
        }
        private static bool ActivateKnownApplication(string id)
        {
                object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
                try
                {
                    CoAllowSetForegroundWindow(instance, IntPtr.Zero);
                    uint process;
                    return ((IApplicationActivationManager)instance).ActivateApplication(id, null, 2, out process) >= 0;
                }
                finally { if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
        }
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int AssocQueryString(uint flags, uint type, string association, string extra, StringBuilder output, ref uint count);
        [DllImport("ole32.dll")] private static extern int CoAllowSetForegroundWindow([MarshalAs(UnmanagedType.IUnknown)] object instance, IntPtr reserved);
        [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint process);
            [PreserveSig] int ActivateForFile(IntPtr id, IntPtr items, IntPtr verb, out uint process);
            [PreserveSig] int ActivateForProtocol(IntPtr id, IntPtr items, out uint process);
        }
    }
}
