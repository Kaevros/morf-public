using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;
using RepeatMode = Windows.Media.MediaPlaybackAutoRepeatMode;

namespace Widgates
{
    internal sealed partial class MediaController : IDisposable
    {
        private readonly Dispatcher dispatcher;
        private readonly DispatcherTimer refresh;
        private readonly DispatcherTimer timelineRefresh;
        private Manager manager;
        private Session active;
        private Session preferred;
        private bool disposed;
        private bool initializing;
        private readonly LatestMediaRequest<Session> metadata;
        private readonly MediaArtworkCache artwork = new MediaArtworkCache();
        private int refreshQueued;
        private readonly LatestMediaRequest<Session> focusWork;
        private readonly PlaybackReturn<Session> returning = new PlaybackReturn<Session>();
        private readonly DispatcherTimer returnDelay;
        private readonly HashSet<IAsyncInfo> focusOperations = new HashSet<IAsyncInfo>();
        private readonly Dictionary<Session, string> knownTracks = new Dictionary<Session, string>();
        private readonly Dictionary<Session, int> trackVersions = new Dictionary<Session, int>();

        private readonly HashSet<Session> observed = new HashSet<Session>();
        private readonly PlaybackPriority<Session> priority = new PlaybackPriority<Session>();
        private Session newestPlaying;
        private Session returnCandidate;
        private bool manualReturnRequested;
        private bool autoPauseOthers = true;
        private bool resumeInterrupted = true;
        private int arbitrationVersion;
        private string notice;
        private string arbitrationNotice;
        private Session returnedPlay, revokeReturnedPlay;
        private int returnedPlayVersion;
        private DateTimeOffset returnedPlayUntil;

        internal event Action Changed;
        internal event Action TimelineChanged;
        internal IReadOnlyList<Session> Sessions { get; private set; }
        internal Session Active { get { return active; } }
        internal string SourceId { get; private set; }
        internal bool HasPreference { get { return preferred != null; } }
        private SourceRules sourceRules = new SourceRules();
        private string sourceRuleText = "";
        internal void ConfigureRules(string text)
        {
            text = SourceRules.Parse(text).Text;
            if (sourceRuleText == text) return;
            sourceRuleText = text; sourceRules = SourceRules.Parse(text); CancelAutomaticReturn(); newestPlaying = null; arbitrationNotice = null; QueueRefresh();
        }
        private int SourceRule(Session session) { return session == null ? 0 : sourceRules.Get(session.SourceAppUserModelId); }
        internal string Title { get; private set; }
        internal string Artist { get; private set; }
        internal string Album { get; private set; }
        internal string TrackIdentity { get; private set; }
        internal long PlaybackRevision { get; private set; }
        internal ImageSource Thumbnail { get; private set; }
        internal string Notice { get { return string.IsNullOrEmpty(arbitrationNotice) ? notice : arbitrationNotice; } private set { notice = value; } }
        internal bool Ready { get { return manager != null; } }
        internal bool Busy { get; private set; }
        internal bool AutoPauseOthers
        {
            get { return autoPauseOthers; }
            set { if (autoPauseOthers != value) { autoPauseOthers = value; arbitrationNotice = null; CancelAutomaticReturn(); } }
        }
        internal bool ResumeInterrupted { get { return resumeInterrupted; } set { if (resumeInterrupted != value) { resumeInterrupted = value; CancelAutomaticReturn(); } } }
        internal Func<bool> AllowAutomaticResume { get; set; }
        internal int PendingReturns { get { return returning.Count; } }
        internal void CancelAutomaticReturn()
        {
            manualReturnRequested = false; returning.Clear(); returnDelay.Stop(); focusWork.Invalidate(); arbitrationVersion++;
            foreach (var operation in focusOperations.ToArray()) { try { operation.Cancel(); } catch { } }
        }
        internal bool IsSessionPlaying(Session session) { return priority.IsPlaying(session); }
        internal bool IsPlaying
        {
            get { return Playback != null && Playback.PlaybackStatus == PlaybackStatus.Playing; }
        }
        internal GlobalSystemMediaTransportControlsSessionPlaybackInfo Playback { get; private set; }
        internal MediaTimeline Timeline { get; private set; }

        internal MediaController(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
            AllowAutomaticResume = () => true;
            focusWork = new LatestMediaRequest<Session>(ApplyFocusAsync, error =>
            {
                if (!disposed) { arbitrationNotice = "Medya önceliği uygulanamadı; otomatik tekrar yapılmadı."; Notify(); }
            });
            returnDelay = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(450) };
            returnDelay.Tick += (_, __) => { returnDelay.Stop(); if (!disposed) focusWork.Request(null); };
            metadata = new LatestMediaRequest<Session>(LoadMetadataAsync, error =>
            {
                if (!disposed) { Notice = "Medya bilgisi alınamadı; kaynak değiştiğinde yeniden denenecek."; Notify(); }
            });
            Sessions = new Session[0];
            Title = "";
            Artist = "";
            Album = "";
            Notice = "Windows medyasına bağlanılıyor…";
            refresh = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(80) };
            refresh.Tick += RefreshTick;
            timelineRefresh = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(80) };
            timelineRefresh.Tick += (_, __) =>
            {
                timelineRefresh.Stop();
                if (disposed || active == null) return;
                var previous = Timeline;
                Timeline = ReadTimeline(active, Playback);
                if (Timeline != null && (previous == null || previous.End != Timeline.End)) metadata.Request(active);
                if (TimelineChanged != null) TimelineChanged();
            };
        }

        internal async Task ConnectAsync()
        {
            if (disposed || initializing || Ready)
                return;
            initializing = true;
            Notice = "Windows medyasına bağlanılıyor…";
            Notify();
            try
            {
                var result = await Complete(Manager.RequestAsync());
                if (disposed)
                    return;

                result.SessionsChanged += OnSessionsChanged;
                try { result.CurrentSessionChanged += OnCurrentChanged; }
                catch { result.SessionsChanged -= OnSessionsChanged; throw; }
                manager = result;
                QueueRefresh();
            }
            catch (Exception ex)
            {
                Notice = ex is UnauthorizedAccessException ? "Windows medya erişimine izin vermedi." : "Windows medyasına erişilemiyor.";
            }
            finally
            {
                initializing = false;
                Notify();
            }
        }

        internal async Task RefreshConnectionAsync()
        {
            if (disposed || initializing) return;
            CancelAutomaticReturn();
            if (manager != null)
            {
                try { manager.SessionsChanged -= OnSessionsChanged; manager.CurrentSessionChanged -= OnCurrentChanged; } catch { }
                manager = null;
            }
            await ConnectAsync();
            if (!disposed && active != null) metadata.Request(active);
        }

        internal void Select(Session session)
        {
            CancelAutomaticReturn();
            preferred = session;
            QueueRefresh();
            if (session != null && Equals(session, active)) metadata.Request(session);
        }

        internal async Task CommandAsync(string command, double? position = null, Session expected = null)
        {
            var target = active;
            if (target == null || Busy || (expected != null && !Equals(target, expected)))
                return;

            CancelAutomaticReturn();
            returnedPlay = revokeReturnedPlay = null; // An explicit Morf command takes precedence over automatic playback.

            Busy = true;
            Notify();
            try
            {
                arbitrationNotice = null;
                Notice = "";
                bool accepted;
                switch (command)
                {
                    case "previous": accepted = await Complete(target.TrySkipPreviousAsync()); break;
                    case "next": accepted = await Complete(target.TrySkipNextAsync()); break;
                    case "shuffle":
                        var shuffle = target.GetPlaybackInfo();
                        accepted = shuffle.Controls.IsShuffleEnabled && shuffle.IsShuffleActive.HasValue
                            && await Complete(target.TryChangeShuffleActiveAsync(!shuffle.IsShuffleActive.Value));
                        break;
                    case "repeat":
                        var repeat = target.GetPlaybackInfo();
                        var mode = repeat.AutoRepeatMode.GetValueOrDefault();
                        accepted = repeat.Controls.IsRepeatEnabled && repeat.AutoRepeatMode.HasValue
                            && await Complete(target.TryChangeAutoRepeatModeAsync(mode == RepeatMode.None ? RepeatMode.Track : mode == RepeatMode.Track ? RepeatMode.List : RepeatMode.None));
                        break;
                    case "restart":
                    case "seek":
                        var seek = target.GetPlaybackInfo();
                        var timeline = ReadTimeline(target, seek);
                        accepted = seek.Controls.IsPlaybackPositionEnabled && timeline != null && timeline.Seekable
                            && await Complete(target.TryChangePlaybackPositionAsync(timeline.SeekTicks(command == "restart" ? 0 : position.GetValueOrDefault())));
                        break;
                    default:
                        var playback = target.GetPlaybackInfo();
                        if (playback.Controls.IsPlayPauseToggleEnabled)
                            accepted = await Complete(target.TryTogglePlayPauseAsync());
                        else
                            accepted = playback.PlaybackStatus == PlaybackStatus.Playing
                                ? await Complete(target.TryPauseAsync())
                                : await Complete(target.TryPlayAsync());
                        break;
                }
                if (!accepted && Equals(target, active))
                    Notice = "Medya kaynağı komutu reddetti.";
            }
            catch (Exception error)
            {
                if (Equals(target, active))
                    Notice = error is TimeoutException ? "Medya kaynağı yanıt vermedi; komut geç uygulanabilir. Durumu kontrol edin." : "Medya kaynağına erişilemiyor.";
            }
            finally
            {
                Busy = false;
                Notify();
                QueueRefresh();
            }
        }

        private void OnSessionsChanged(Manager sender, SessionsChangedEventArgs args) { QueueRefresh(); }
        internal async Task PausePlayingAsync(Func<bool> current)
        {
            CancelAutomaticReturn();
            if (manager == null) { Notice = "Uyku zamanlayıcısı: medya erişimi yok."; Notify(); return; }
            bool failed = false;
            Session[] targets;
            try { targets = manager.GetSessions().ToArray(); } catch { targets = new Session[0]; failed = true; }
            foreach (var target in targets)
            {
                if (disposed || !current()) return;
                try
                {
                    var state = target.GetPlaybackInfo();
                    if (state.PlaybackStatus != PlaybackStatus.Playing) continue;
                    if (!state.Controls.IsPauseEnabled || !await Complete(target.TryPauseAsync())) failed = true;
                }
                catch { failed = true; }
            }
            if (!disposed && current()) { Notice = failed ? "Uyku zamanlayıcısı: bazı kaynaklar duraklatılamadı." : "Uyku zamanlayıcısı tamamlandı."; Notify(); QueueRefresh(); }
        }
        private void OnCurrentChanged(Manager sender, CurrentSessionChangedEventArgs args) { QueueRefresh(); }
        private void OnMediaChanged(Session sender, MediaPropertiesChangedEventArgs args)
        {
            if (dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (disposed || !observed.Contains(sender)) return;
                // Providers may re-publish unchanged metadata/artwork on Pause. Validate the immutable track at return time.
                int version; trackVersions.TryGetValue(sender, out version); trackVersions[sender] = version + 1;
                if (Equals(sender, active)) metadata.Request(sender);
            }));
        }
        private void OnTimelineChanged(Session sender, TimelinePropertiesChangedEventArgs args)
        {
            if (dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (disposed || !observed.Contains(sender)) return;
                if (returning.Contains(sender)) returning.Observe(sender, ReadReturnState(sender));
                if (Equals(active, sender)) timelineRefresh.Start();
            }));
        }
        private void OnPlaybackChanged(Session sender, PlaybackInfoChangedEventArgs args)
        {
            if (dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (disposed || !observed.Contains(sender)) return;
                var state = ReadReturnState(sender, returning.Contains(sender));
                returning.Observe(sender, state);
                if (state.State == ReturnPlaybackState.Unknown) { QueueRefresh(); return; }
                bool playing = state.State == ReturnPlaybackState.Playing;
                if (playing && Equals(sender, returnedPlay) && DateTimeOffset.UtcNow <= returnedPlayUntil)
                {
                    returnedPlay = null;
                    priority.Observe(sender, true, false); // Our own Play is never a new user-selected winner.
                    Session competing = null;
                    try
                    {
                        var others = ReadReturnStates(manager.GetSessions().Where(item => !Equals(item, sender)).ToArray());
                        if (others.Values.Any(item => item.State == ReturnPlaybackState.Unknown)) { revokeReturnedPlay = sender; focusWork.Request(null); QueueRefresh(); return; }
                        competing = others.Where(item => item.Value.State == ReturnPlaybackState.Playing).Select(item => item.Key).FirstOrDefault();
                    }
                    catch { revokeReturnedPlay = sender; focusWork.Request(null); QueueRefresh(); return; }
                    if (competing != null) PlaybackStarted(competing, sender);
                    else if (!CanReturn || returnedPlayVersion != arbitrationVersion) { revokeReturnedPlay = sender; focusWork.Request(null); }
                    else newestPlaying = sender;
                    QueueRefresh(); return;
                }
                if (priority.Observe(sender, playing)) PlaybackStarted(sender);
                QueueRefresh();
            }));
        }

        private static bool TryPlaying(Session session, out bool playing)
        {
            try { playing = session.GetPlaybackInfo().PlaybackStatus == PlaybackStatus.Playing; return true; }
            catch { playing = false; return false; }
        }

        private void SynchronizeSessions(Session[] sessions)
        {
            foreach (var removed in knownTracks.Keys.Where(item => !sessions.Contains(item)).ToArray()) { knownTracks.Remove(removed); trackVersions.Remove(removed); }
            foreach (var removed in observed.Where(item => !sessions.Contains(item)).ToArray())
            {
                try { removed.PlaybackInfoChanged -= OnPlaybackChanged; } catch { }
                try { removed.MediaPropertiesChanged -= OnMediaChanged; removed.TimelinePropertiesChanged -= OnTimelineChanged; } catch { }
                observed.Remove(removed);
                knownTracks.Remove(removed); trackVersions.Remove(removed);
            }
            foreach (var item in sessions)
                if (!observed.Contains(item))
                {
                    try
                    {
                        item.PlaybackInfoChanged += OnPlaybackChanged; item.MediaPropertiesChanged += OnMediaChanged; item.TimelinePropertiesChanged += OnTimelineChanged;
                        observed.Add(item);
                    }
                    catch
                    {
                        try { item.PlaybackInfoChanged -= OnPlaybackChanged; item.MediaPropertiesChanged -= OnMediaChanged; item.TimelinePropertiesChanged -= OnTimelineChanged; } catch { }
                    }
                }
            Session before = priority.HasLatest ? priority.Latest : null;
            priority.Synchronize(sessions, item => { bool playing; return TryPlaying(item, out playing) && playing; });
            if (priority.HasLatest && !Equals(before, priority.Latest)) PlaybackStarted(priority.Latest);
            if (newestPlaying != null && !sessions.Contains(newestPlaying)) newestPlaying = null;
            if (returnedPlay != null && (!sessions.Contains(returnedPlay) || DateTimeOffset.UtcNow > returnedPlayUntil)) returnedPlay = null;
            if (returning.Count > 0)
            {
                var states = ReadReturnStates(sessions);
                bool ready = returning.Ready(states);
                if (!CanReturn || !ready) returnDelay.Stop();
                else if (!returnDelay.IsEnabled) returnDelay.Start();
            }
        }

        private void QueueRefresh()
        {
            if (disposed || dispatcher.HasShutdownStarted || Interlocked.CompareExchange(ref refreshQueued, 1, 0) != 0) return;
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    Interlocked.Exchange(ref refreshQueued, 0);
                    if (!disposed) refresh.Start();
                }));
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref refreshQueued, 0); }
        }
        private void RefreshTick(object sender, EventArgs args)
        {
            refresh.Stop();
            if (disposed || manager == null)
                return;


            try
            {
                var sessions = manager.GetSessions().ToArray();
                SynchronizeSessions(sessions);
                if (!Sessions.SequenceEqual(sessions))
                    Sessions = sessions;
                if (preferred != null && !sessions.Contains(preferred))
                    preferred = null;
                var current = MediaPreference.Choose(sessions, preferred, newestPlaying, manager.GetCurrentSession(), priority.IsPlaying, SourceRule);
                if (!Equals(active, current))
                {
                    Detach();
                    active = current;
                    SourceId = current == null ? "" : current.SourceAppUserModelId;
                    Title = "";
                    Artist = "";
                    Album = "";
                    TrackIdentity = "";
                    Thumbnail = null;
                    Notice = "";
                    Playback = null;
                    Timeline = null;
                    metadata.Invalidate();


                    if (active != null)
                    {
                        metadata.Request(active);
                    }
                }

                Playback = current == null ? null : current.GetPlaybackInfo();
                Timeline = current == null ? null : ReadTimeline(current, Playback);
                if (current == null)
                    Notice = "Bir medya uygulaması veya tarayıcı sekmesi açın.";
                else if (Notice == "Windows medyasına bağlanılıyor…")
                    Notice = "";
                Notify();
            }
            catch (Exception)
            {
                if (disposed) return;
                Notice = "Medya bilgisi geçici olarak alınamıyor.";
                Notify();
            }
        }

        private async Task LoadMetadataAsync(Session source, Func<bool> current)
        {
            var properties = await CompleteMetadata(source.TryGetMediaPropertiesAsync());
            if (!current() || !Equals(source, active)) return;
            string title = properties == null ? "" : properties.Title ?? "";
            string artist = properties == null ? "" : properties.Artist ?? "";
            string identity = TrackKey(source, properties);
            knownTracks[source] = identity;
            PublishMetadataText(title, artist, identity, properties == null ? "" : properties.AlbumTitle ?? "");
            ImageSource thumb = null;
            if (properties != null && properties.Thumbnail != null)
            {
                try
                {
                    using (var stream = await CompleteMetadata(properties.Thumbnail.OpenReadAsync()))
                    {
                        if (!current()) return;
                        if (stream != null && stream.Size > 0 && stream.Size <= PreviewImage.MaximumBytes)
                        {
                            using (var reader = new DataReader(stream))
                            {
                                await CompleteMetadata(reader.LoadAsync((uint)stream.Size));
                                if (!current()) return;
                                var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
                                thumb = await Task.Run<ImageSource>(() => artwork.Decode(bytes));
                            }
                        }
                    }
                }
                catch { thumb = null; }
            }
            if (!current() || !Equals(source, active)) return;
            if (!ReferenceEquals(Thumbnail, thumb)) { Thumbnail = thumb; Notify(); }
        }

        private void PublishMetadataText(string title, string artist, string identity, string album = "")
        {
            bool changed = Title != title || Artist != artist || Album != album || TrackIdentity != identity || !string.IsNullOrEmpty(notice);
            if (TrackIdentity != identity || Title != title || Artist != artist || Album != album) Thumbnail = null;
            Title = title; Artist = artist; Album = album; TrackIdentity = identity;
            Notice = "";
            // Text must not wait for artwork. Unchanged metadata must not rebuild the UI.
            if (changed) Notify();
        }

        private static Task<T> CompleteMetadata<T>(IAsyncOperation<T> operation) { return Complete(operation); }
        private static string TrackKey(Session source, GlobalSystemMediaTransportControlsSessionMediaProperties properties)
        {
            return properties == null || string.IsNullOrWhiteSpace(properties.Title) ? "" : source.SourceAppUserModelId + "\0" + properties.Title + "\0"
                + properties.Artist + "\0" + properties.AlbumTitle + "\0" + properties.TrackNumber;
        }

        private void Notify()
        {
            if (!disposed && Changed != null)
                Changed();
        }

        private static MediaTimeline ReadTimeline(Session session, GlobalSystemMediaTransportControlsSessionPlaybackInfo playback)
        {
            try
            {
                var value = session.GetTimelineProperties();
                if (value == null) return null;
                var updated = value.LastUpdatedTime;
                if (updated.Year < 2000 || updated > DateTimeOffset.UtcNow) updated = DateTimeOffset.UtcNow;
                return new MediaTimeline { Start = value.StartTime, End = value.EndTime, Position = value.Position,
                    MinimumSeek = value.MinSeekTime, MaximumSeek = value.MaxSeekTime, Updated = updated,
                    Rate = playback == null ? 0 : playback.PlaybackRate.GetValueOrDefault(1) };
            }
            catch { return null; }
        }

        private void Detach()
        {
            if (active == null)
                return;
            Thumbnail = null;
        }

        public void Dispose()
        {
            disposed = true;
            CancelAutomaticReturn(); focusWork.Dispose();
            arbitrationVersion++;
            metadata.Dispose();
            refresh.Stop();
            Detach();
            timelineRefresh.Stop();
            foreach (var session in observed) { try { session.PlaybackInfoChanged -= OnPlaybackChanged; session.MediaPropertiesChanged -= OnMediaChanged; session.TimelinePropertiesChanged -= OnTimelineChanged; } catch { } }
            observed.Clear();
            knownTracks.Clear(); trackVersions.Clear(); returnedPlay = revokeReturnedPlay = returnCandidate = null;
            if (manager == null)
                return;
            manager.SessionsChanged -= OnSessionsChanged;
            manager.CurrentSessionChanged -= OnCurrentChanged;
        }

        // The same WinRT completion path works with both the SDK and the inbox preview build.
        private static Task<T> Complete<T>(IAsyncOperation<T> operation)
        {
            var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.Completed = (result, status) =>
            {
                try { source.TrySetResult(result.GetResults()); }
                catch (OperationCanceledException) { source.TrySetCanceled(); }
                catch (Exception ex) { source.TrySetException(ex); }
            };
            return OperationDeadline.Run(source.Task, () => operation.Cancel(), 5000);
        }
    }
}
