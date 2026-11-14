using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Widgates
{
    internal sealed class AppNotificationListener : IDisposable
    {
        private readonly Dispatcher dispatcher;
        private readonly AppMessageTracker tracker = new AppMessageTracker();
        private UserNotificationListener listener;
        private bool enabled, disposed, subscribed, syncing, dirty;
        private int generation;
        private IAsyncInfo pending;
        private int syncQueued;
        private int configuration;
        private readonly Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal SourceChoice[] Sources { get { return sources.Select(x => new SourceChoice { Id = x.Key, Name = x.Value }).OrderBy(x => x.Name).ToArray(); } }
        internal bool Connecting { get; private set; }
        internal event Action Changed;
        internal event Action<AppMessage> Received;
        internal event Action<ISet<string>> Snapshot;
        internal bool Connected { get { return subscribed; } }
        internal bool ReadingReady { get; private set; }
        internal string Status { get; private set; }
        // Diagnostic metadata only: never include exception messages or notification content.
        internal string LastFailure { get; private set; }
        internal static bool HasIdentity
        {
            get { uint size = 0; return GetCurrentPackageFullName(ref size, null) == 122; }
        }
        internal AppNotificationListener(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
            Status = HasIdentity ? "Bağlı değil" : "Bildirimli paket sürümü gerekli";
        }
        internal void Configure(bool value)
        {
            if (disposed || enabled == value) return;
            enabled = value; configuration++;
            if (!enabled) { Stop(); SetStatus("Kapalı"); }
            else { Task started = RefreshAsync(false); }
        }
        internal Task RefreshAsync(bool request)
        {
            // Configure can run in the window constructor, before WPF installs its
            // synchronization context. Always enter through the running Dispatcher
            // so WinRT awaits and subsequent reads stay on their owning UI thread.
            return dispatcher.InvokeAsync(() => RefreshOnDispatcherAsync(request)).Task.Unwrap();
        }
        private async Task RefreshOnDispatcherAsync(bool request)
        {
            if (disposed) return;
            if (!HasIdentity) { SetStatus("Bildirimli paket sürümü gerekli"); return; }
            if (Connecting) return;
            Connecting = true;
            int version = generation, requestedConfiguration = configuration;
            try
            {
                SetStatus(request ? "Windows izni bekleniyor…" : "Bağlantı denetleniyor…");
                listener = UserNotificationListener.Current;
                var access = listener.GetAccessStatus();
                if (request && access != UserNotificationListenerAccessStatus.Allowed) access = await Complete(listener.RequestAccessAsync(), 60000);
                if (disposed || version != generation) return;
                if (access != UserNotificationListenerAccessStatus.Allowed)
                {
                    Stop(); SetStatus(access == UserNotificationListenerAccessStatus.Denied ? "Windows bildirim izni kapalı" : "Windows bildirim izni gerekli"); return;
                }
                if (!enabled) { SetStatus("İzin hazır · Gösterim kapalı"); return; }
                if (!subscribed)
                {
                    tracker.Reset(DateTimeOffset.UtcNow); listener.NotificationChanged += OnChanged; subscribed = true;
                }
                SetStatus("Bağlı · Yeni Windows bildirimleri izleniyor");
                await SyncAsync();
            }
            catch (Exception error) { if (!disposed && version == generation) { RecordFailure(error); Stop(); SetStatus("Bildirim bağlantısı kurulamadı · Yeniden deneyin"); } }
            finally
            {
                Connecting = false; if (!disposed && Changed != null) Changed();
                if (!disposed && enabled && requestedConfiguration != configuration) { Task restarted = RefreshAsync(false); }
            }
        }
        private void OnChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
        {
            if (disposed || dispatcher.HasShutdownStarted || Interlocked.CompareExchange(ref syncQueued, 1, 0) != 0) return;
            try { dispatcher.BeginInvoke(new Action(() => { Interlocked.Exchange(ref syncQueued, 0); if (!disposed) { Task sync = SyncAsync(); } })); }
            catch (InvalidOperationException) { Interlocked.Exchange(ref syncQueued, 0); }
        }
        private async Task SyncAsync()
        {
            if (disposed || !enabled || !subscribed) return;
            if (syncing) { dirty = true; return; }
            syncing = true; int version = generation;
            try
            {
                do
                {
                    dirty = false;
                    if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) { Stop(); SetStatus("Windows bildirim izni kapalı"); return; }
                    List<AppMessage> messages = null;
                    try { messages = await ReadMessagesAsync(); }
                    catch (Exception error)
                    {
                        if (AccessDenied(error) || disposed || version != generation) throw;
                        ReadingReady = false; RecordFailure(error); SetStatus("Bildirim bağlantısı yeniden deneniyor…");
                    }
                    if (messages == null)
                    {
                        // One bounded retry for startup/service races, never an idle poll.
                        await Task.Delay(300);
                        if (disposed || version != generation) return;
                        messages = await ReadMessagesAsync();
                    }
                    if (disposed || version != generation) return;
                    LastFailure = null; ReadingReady = true;
                    SetStatus("Bağlı · Yeni Windows bildirimleri izleniyor");
                    var fresh = tracker.Observe(messages);
                    // Offer replacements before pruning so a coalesced conversation keeps its deadline.
                    foreach (var message in fresh) if (Received != null) Received(message);
                    if (Snapshot != null) Snapshot(new HashSet<string>(messages.Select(x => x.Key), StringComparer.Ordinal));
                } while (dirty && !disposed && version == generation);
            }
            catch (Exception error)
            {
                if (!disposed && version == generation)
                {
                    ReadingReady = false; RecordFailure(error);
                    if (AccessDenied(error)) { Stop(); SetStatus("Windows bildirim izni kapalı"); }
                    else SetStatus("Bildirim okunamadı · Sonraki bildirimde yeniden denenecek");
                    // Keep the event subscription after a read failure. The next Windows
                    // notification can recover without a manual reconnect or idle timer.
                }
            }
            finally
            {
                syncing = false;
                if (dirty && subscribed && !disposed) { dirty = false; Task next = SyncAsync(); }
            }
        }
        private async Task<List<AppMessage>> ReadMessagesAsync()
        {
            // Called only from the running Dispatcher, like the verified live reader.
            // Keep the bounded WinRT snapshot in memory; never write notification text.
            var notifications = await Complete(listener.GetNotificationsAsync(NotificationKinds.Toast));
            var messages = new List<AppMessage>();
            string ownAppId = CurrentAppId();
            foreach (var item in notifications.OrderByDescending(x => x.CreationTime).Take(256))
            {
                try
                {
                    string appId = item.AppInfo.AppUserModelId;
                    string appName = item.AppInfo.DisplayInfo.DisplayName;
                    if (string.IsNullOrWhiteSpace(appId) || string.Equals(appId, ownAppId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (SourceRules.ValidId(appId) && (sources.ContainsKey(appId) || sources.Count < 64)) sources[appId] = string.IsNullOrWhiteSpace(appName) ? appId : AppMessage.Clean(appName, 80);
                    var binding = item.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)
                        ?? (item.Notification.Visual.Bindings != null ? item.Notification.Visual.Bindings.FirstOrDefault() : null);
                    if (binding == null) continue;
                    var message = AppMessage.Create(item.Id, item.CreationTime, binding.GetTextElements().Select(x => x.Text), appId, appName);
                    if (message != null) messages.Add(message);
                }
                catch { /* A malformed or disappearing toast must not break the subscription. */ }
            }
            return messages;
        }
        private async Task<T> Complete<T>(IAsyncOperation<T> operation, int deadline = 5000)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = operation;
            operation.Completed = (value, state) => { try { completion.TrySetResult(value.GetResults()); } catch (Exception error) { completion.TrySetException(error); } };
            try { return await OperationDeadline.Run(completion.Task, () => operation.Cancel(), deadline); }
            finally { if (ReferenceEquals(pending, operation)) pending = null; }
        }
        private void Stop()
        {
            generation++; dirty = false;
            if (pending != null) { try { pending.Cancel(); } catch { } }
            if (subscribed) { try { listener.NotificationChanged -= OnChanged; } catch { } }
            subscribed = false; ReadingReady = false; tracker.Reset();
            if (Snapshot != null) Snapshot(new HashSet<string>());
        }
        private void SetStatus(string value) { Status = value; if (Changed != null) Changed(); }
        private static bool AccessDenied(Exception error) { return error is UnauthorizedAccessException || error.HResult == unchecked((int)0x80070005); }
        private void RecordFailure(Exception error) { LastFailure = error.GetType().Name + " " + error.HResult.ToString("X8"); }
        public void Dispose() { disposed = true; Stop(); Received = null; Snapshot = null; Changed = null; }
        private static string CurrentAppId()
        {
            uint size = 0;
            if (GetCurrentApplicationUserModelId(ref size, null) != 122 || size > 512) return "";
            var name = new StringBuilder((int)size);
            return GetCurrentApplicationUserModelId(ref size, name) == 0 ? name.ToString() : "";
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentApplicationUserModelId(ref uint length, StringBuilder name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref uint length, StringBuilder name);
    }
}
