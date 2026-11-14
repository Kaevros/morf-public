using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Win32;

namespace Widgates
{
    internal sealed class NotificationBannerRouting : IDisposable
    {
        private readonly Dispatcher dispatcher;
        private readonly string owner = Guid.NewGuid().ToString("N");
        private Process guard;
        private RegistrySignal signal;
        private RegisteredWaitHandle registration;
        private bool desired, disposed, failed, recoveryAttempted, initialized;
        private int queued;
        private string rules = "";
        internal void ConfigureRules(string value)
        {
            value = SourceRules.Parse(value).Text;
            if (rules == value) return;
            rules = value;
            if (guard == null || disposed) return;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(NotificationBannerRegistry.ControlRoot, true))
                    if (key != null && string.Equals(NotificationBannerRegistry.ControlString(key, "Owner"), owner, StringComparison.Ordinal)) key.SetValue("Rules", rules);
            }
            catch (Exception error) { Fail(error); }
        }
        internal bool Active { get; private set; }
        internal string Status { get; private set; }
        internal string LastFailure { get; private set; }
        internal void Retry() { failed = false; recoveryAttempted = false; initialized = false; }
        internal event Action Changed;
        internal static bool Supported
        {
            get
            {
                if (!AppNotificationListener.HasIdentity || Environment.OSVersion.Version.Build < 22000) return false;
                try
                {
                    bool capability = false, senders = false, control = false;
                    var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 };
                    using (var reader = XmlReader.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppxManifest.xml"), options))
                        while (reader.Read())
                        {
                            if (reader.LocalName == "Capability" && reader.GetAttribute("Name") == "unvirtualizedResources") capability = true;
                            if (reader.LocalName == "ExcludedKey")
                            {
                                string key = reader.ReadElementContentAsString();
                                if (key == "HKEY_CURRENT_USER\\" + NotificationBannerRegistry.SenderRoot) senders = true;
                                if (key == "HKEY_CURRENT_USER\\" + NotificationBannerRegistry.ControlRoot) control = true;
                            }
                        }
                    return capability && senders && control;
                }
                catch { return false; }
            }
        }
        private readonly bool supported;
        internal NotificationBannerRouting(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher; supported = Supported;
            Status = supported ? "Windows gösterimi etkin" : "Otomatik başlık yönetimi için güncel Windows 11 paketi gerekli";
        }
        internal void SetEnabled(bool value)
        {
            if (disposed || !supported) return;
            if (initialized && desired == value) return;
            if (!value) { failed = false; recoveryAttempted = false; }
            if (failed) return;
            initialized = true; desired = value;
            try
            {
                // A leftover journal is recovered even when mirroring starts off.
                if (guard == null && !value && new NotificationBannerRegistry().LoadJournal().Length == 0) return;
                if (guard == null) Start();
                using (var key = Registry.CurrentUser.OpenSubKey(NotificationBannerRegistry.ControlRoot, true))
                {
                    if (!string.Equals(NotificationBannerRegistry.ControlString(key, "Owner"), owner, StringComparison.Ordinal)) throw new IOException("Routing ownership changed");
                    key.SetValue("Desired", value ? 1 : 0, RegistryValueKind.DWord);
                }
                if (!value) Active = false;
                SetStatus(value ? "Bildirimler Morf'a aktarılıyor…" : "Windows gösterimi geri yükleniyor…");
                ReadState();
            }
            catch (Exception error) { Fail(error); }
        }
        private void Start()
        {
            if (registration != null) { registration.Unregister(null); registration = null; }
            if (signal != null) { signal.Dispose(); signal = null; }
            using (var key = Registry.CurrentUser.CreateSubKey(NotificationBannerRegistry.ControlRoot))
            {
                key.SetValue("Owner", owner, RegistryValueKind.String);
                key.SetValue("Desired", desired ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("State", -1, RegistryValueKind.DWord);
                key.SetValue("StateOwner", owner, RegistryValueKind.String);
                key.SetValue("Rules", rules, RegistryValueKind.String);
            }
            signal = new RegistrySignal(NotificationBannerRegistry.ControlRoot);
            registration = ThreadPool.RegisterWaitForSingleObject(signal.Event, (_, expired) => QueueState(), null, Timeout.Infinite, false);
            using (var current = Process.GetCurrentProcess())
            {
                string executable = current.MainModule.FileName;
                if (!string.Equals(Path.GetFileName(executable), "WindowsWidgates.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("A packaged apphost is required");
                var start = new ProcessStartInfo(executable, NotificationBannerGuard.Argument + " " + current.Id.ToString(CultureInfo.InvariantCulture)
                    + " " + current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) + " " + owner)
                    { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory };
                guard = Process.Start(start);
            }
            var started = guard;
            guard.Exited += (_, __) =>
            {
                if (disposed || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (disposed || !ReferenceEquals(guard, started)) return;
                    Active = false; failed = true; desired = false;
                    if (registration != null) { registration.Unregister(null); registration = null; }
                    if (signal != null) { signal.Dispose(); signal = null; }
                    guard.Dispose(); guard = null;
                    SetStatus("Windows gösterimine dönülüyor…");
                    if (recoveryAttempted) { SetStatus("Windows başlıkları geri yüklenemedi · Yeniden deneyin"); return; }
                    recoveryAttempted = true;
                    // One recovery-only replacement, never an automatic restart loop.
                    try { Start(); } catch { SetStatus("Windows başlıkları geri yüklenemedi · Yeniden deneyin"); }
                }));
            };
            guard.EnableRaisingEvents = true;
        }
        private void QueueState()
        {
            if (disposed || dispatcher.HasShutdownStarted) return;
            try
            {
                signal.Arm();
                if (Interlocked.CompareExchange(ref queued, 1, 0) != 0) return;
                dispatcher.BeginInvoke(new Action(() => { Interlocked.Exchange(ref queued, 0); if (!disposed) ReadState(); }));
            }
            catch (Exception error)
            {
                Interlocked.Exchange(ref queued, 0);
                if (!disposed && !dispatcher.HasShutdownStarted)
                    dispatcher.BeginInvoke(new Action(() => { if (!disposed) Fail(error); }));
            }
        }
        private void ReadState()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(NotificationBannerRegistry.ControlRoot))
                {
                    if (key == null || !string.Equals(NotificationBannerRegistry.ControlString(key, "StateOwner"), owner, StringComparison.Ordinal)) return;
                    int state = NotificationBannerRegistry.ControlNumber(key, "State", -1);
                    if (object.Equals(state, 1) && desired) { Active = true; LastFailure = null; SetStatus("Bildirimler Morf'ta · Windows başlıkları otomatik yönetiliyor"); }
                    else if (object.Equals(state, 0) && !desired) { Active = false; SetStatus("Windows gösterimi etkin · Önceki ayarlar korundu"); }
                    else if (object.Equals(state, 2) || object.Equals(state, 3))
                    {
                        Active = false; failed = true;
                        LastFailure = NotificationBannerRegistry.ControlString(key, "Failure");
                        SetStatus(object.Equals(state, 3) ? "Windows başlıkları geri yüklenemedi · Yeniden deneyin" : "Windows gösterimi etkin · Morf aktarımı kurulamadı");
                    }
                }
            }
            catch (Exception error) { Fail(error); }
        }
        internal bool CanPresent(string sender)
        {
            if (!Active || !desired || SourceRules.Parse(rules).Get(sender) == 2 || !NotificationBannerSession.Candidate(sender)) return false;
            try { var store = new NotificationBannerRegistry(); return store.CanRoute(sender) && store.Read(sender) == BannerValue.Off; } catch { return false; }
        }
        private void SetStatus(string value) { if (Status == value) return; Status = value; if (Changed != null) Changed(); }
        private void Fail(Exception error)
        {
            Active = false; failed = true;
            LastFailure = NotificationBannerGuard.Code(error);
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(NotificationBannerRegistry.ControlRoot, true))
                    if (key != null && string.Equals(NotificationBannerRegistry.ControlString(key, "Owner"), owner, StringComparison.Ordinal)) key.SetValue("Desired", 0, RegistryValueKind.DWord);
            }
            catch { }
            SetStatus("Otomatik aktarım kurulamadı · Bağlantıyı yeniden deneyin");
        }
        public void Dispose()
        {
            disposed = true; Active = false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(NotificationBannerRegistry.ControlRoot, true))
                    if (key != null && string.Equals(NotificationBannerRegistry.ControlString(key, "Owner"), owner, StringComparison.Ordinal)) { key.SetValue("Desired", 0); key.SetValue("Owner", ""); }
                if (guard != null) guard.WaitForExit(2000);
            }
            catch { }
            if (registration != null) registration.Unregister(null);
            if (signal != null) signal.Dispose();
            if (guard != null) guard.Dispose(); Changed = null;
        }
    }
}
