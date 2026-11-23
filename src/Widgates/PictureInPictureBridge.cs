using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
#if INBOX_PREVIEW
using System.Security.AccessControl;
#endif

namespace Widgates
{
    // An asynchronous, current-user-only pipe. No polling, capture or browser process is
    // started here. The browser connects for its selected player.
    internal sealed class PictureInPictureBridge : IDisposable
    {
        private readonly object sync = new object();
        private readonly SemaphoreSlim writes = new SemaphoreSlim(1);
        private readonly string pipeName;
        private NamedPipeServerStream listener;
        private PipMessage current;
        private int generation, seekSeconds = 10;
        private bool enabled;
        private bool advanced;
        internal event Action Changed;
        internal Task Completion { get; private set; }
        internal string Failure { get; private set; }
        internal bool Listening { get { lock (sync) return listener != null; } }
        internal PipMessage Current { get { lock (sync) return current; } }
        internal static string DefaultPipeName
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return "Morf.PiP.v1." + identity.User.Value + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId;
            }
        }
        internal PictureInPictureBridge(string name = null) { pipeName = name ?? DefaultPipeName; }

        internal void Configure(bool value, int seconds, bool advancedPlayer = false)
        {
            int version;
            lock (sync)
            {
                int nextSeconds = PictureInPictureProtocol.SeekSeconds(seconds);
                bool changedSeconds = nextSeconds != seekSeconds || advanced != advancedPlayer;
                advanced = advancedPlayer;
                seekSeconds = nextSeconds;
                if (enabled == value) { if (enabled && changedSeconds) SendConfig(); return; }
                enabled = value; version = ++generation; current = null; Failure = null;
                if (listener != null) { listener.Dispose(); listener = null; }
            }
            Notify();
            if (value) Completion = ListenAsync(version);
        }
        private NamedPipeServerStream CreatePipe()
        {
#if INBOX_PREVIEW
            var security = new PipeSecurity();
            using (var identity = WindowsIdentity.GetCurrent())
            {
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
                security.SetOwner(identity.User);
            }
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, PictureInPictureProtocol.MaximumBytes, PictureInPictureProtocol.MaximumBytes, security);
#else
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, PictureInPictureProtocol.MaximumBytes, PictureInPictureProtocol.MaximumBytes);
#endif
        }
        private async Task ListenAsync(int version)
        {
            while (true)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    lock (sync)
                    {
                        if (!enabled || version != generation) return;
                        listener = pipe = CreatePipe();
                    }
                    await pipe.WaitForConnectionAsync().ConfigureAwait(false);
                    var hello = await ReadWithDeadlineAsync(pipe).ConfigureAwait(false);
                    if (hello == null || hello.Type != "hello") throw new InvalidDataException("PiP handshake rejected.");
                    await SendConfigAsync(pipe).ConfigureAwait(false);
                    while (true)
                    {
                        // An active video connection is event-driven; no heartbeat or recurring timeout.
                        var message = await PictureInPictureProtocol.ReadAsync(pipe).ConfigureAwait(false);
                        if (message == null) break;
                        if (message.Type != "status" || message.Session != hello.Session || message.Browser != hello.Browser)
                            throw new InvalidDataException("PiP session changed.");
                        lock (sync)
                        {
                            if (!enabled || version != generation) return;
                            current = message; Failure = null;
                        }
                        Notify();
                    }
                }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is InvalidDataException) && !(error is ObjectDisposedException) && !(error is UnauthorizedAccessException)
                        && !(error is System.Runtime.Serialization.SerializationException) && !(error is System.Xml.XmlException)
                        && !(error is System.Text.DecoderFallbackException) && !(error is InvalidOperationException)) throw;
                    // A denied/busy pipe must not become a retry loop. Disconnections from an
                    // established listener can accept the next explicit browser connection.
                    if (pipe == null)
                    {
                        lock (sync) { if (version == generation) Failure = "PiP bağlantısı açılamadı. Özelliği kapatıp yeniden açabilirsiniz."; }
                        Notify(); return;
                    }
                }
                finally
                {
                    if (pipe != null) pipe.Dispose();
                    lock (sync) { if (ReferenceEquals(listener, pipe)) { listener = null; current = null; } }
                    Notify();
                }
            }
        }
        private static async Task<PipMessage> ReadWithDeadlineAsync(Stream pipe)
        {
            var read = PictureInPictureProtocol.ReadAsync(pipe);
            using (var timeout = new CancellationTokenSource())
            {
                var delay = Task.Delay(3000, timeout.Token);
                if (await Task.WhenAny(read, delay).ConfigureAwait(false) != read)
                {
                    pipe.Dispose();
                    try { await read.ConfigureAwait(false); } catch (IOException) { } catch (ObjectDisposedException) { }
                    throw new IOException("PiP handshake timed out.");
                }
                timeout.Cancel(); return await read.ConfigureAwait(false);
            }
        }
        private async void SendConfig()
        {
            NamedPipeServerStream pipe;
            lock (sync) pipe = current == null ? null : listener;
            if (pipe != null) await SendConfigAsync(pipe).ConfigureAwait(false);
        }
        private Task<bool> SendConfigAsync(NamedPipeServerStream pipe)
        {
            bool active, custom; int seconds;
            lock (sync) { active = enabled; seconds = seekSeconds; custom = advanced; }
            return SendAsync(pipe, new PipMessage { Type = "config", Enabled = active, SeekSeconds = seconds, Advanced = custom });
        }
        private async Task<bool> SendAsync(NamedPipeServerStream pipe, PipMessage message)
        {
            await writes.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (sync) if (!enabled || !ReferenceEquals(listener, pipe) || !pipe.IsConnected) return false;
                // Closing the feature cancels a blocked pipe write by disposing the pipe.
                var write = PictureInPictureProtocol.WriteAsync(pipe, message);
                using (var timeout = new CancellationTokenSource())
                {
                    if (await Task.WhenAny(write, Task.Delay(2000, timeout.Token)).ConfigureAwait(false) != write) pipe.Dispose();
                    timeout.Cancel(); await write.ConfigureAwait(false);
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }
            finally { writes.Release(); }
        }
        internal async Task<bool> OpenAsync(string source, string title, string artist)
        {
            NamedPipeServerStream pipe; PipMessage selected;
            lock (sync) { pipe = listener; selected = current; }
            if (!Matches(selected, source, title, artist)) return false;
            if (selected.Inline && !selected.Open && selected.BrowserProcess != 0)
            {
                bool invoked = await PictureInPicturePlacement.InvokeInlineAsync(selected, () =>
                {
                    lock (sync) return ReferenceEquals(pipe, listener) && current != null && !current.Open
                        && Matches(current, source, title, artist);
                }).ConfigureAwait(false);
                if (invoked) return true;
            }
            return await SendAsync(pipe, new PipMessage { Type = "command", Session = selected.Session, Command = "open" }).ConfigureAwait(false);
        }
        private static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string text = value.Trim().Replace('\u00A0', ' ');
            if (text.EndsWith(" - YouTube", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - 10).TrimEnd();
            else if (text.EndsWith(" | YouTube", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - 10).TrimEnd();
            else if (text.EndsWith(" - Netflix", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - 10).TrimEnd();
            return text;
        }

        internal static bool Matches(PipMessage message, string source, string title, string artist)
        {
            if (message == null || !PictureInPictureProtocol.Valid(message) || message.Type != "status") return false;
            string name = MediaSourceNames.Friendly(source);
            bool browser = message.Browser == "chrome" ? name == "Google Chrome" : message.Browser == "edge" ? name == "Microsoft Edge"
                : message.Browser == "brave" ? name == "Brave" : message.Browser == "firefox" ? name == "Firefox"
                : message.Browser == "opera" ? name == "Opera" : message.Browser == "vivaldi" && name == "Vivaldi";
            if (!browser || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message.Title)) return false;
            string normSource = NormalizeTitle(title);
            string normMsg = NormalizeTitle(message.Title);
            bool titleMatches = string.Equals(normSource, normMsg, StringComparison.OrdinalIgnoreCase)
                || (normSource.Length > 0 && normMsg.Length > 0 && (normSource.StartsWith(normMsg, StringComparison.OrdinalIgnoreCase) || normMsg.StartsWith(normSource, StringComparison.OrdinalIgnoreCase)));
            if (!titleMatches) return false;
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(message.Artist)) return true;
            return string.Equals(artist.Trim(), message.Artist.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        private void Notify() { var changed = Changed; if (changed != null) changed(); }
        public void Dispose() { Configure(false, 10); }
    }
}
