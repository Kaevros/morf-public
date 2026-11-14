using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    internal sealed partial class MediaController
    {
        internal string ReturnStatus
        {
            get {
                if (!string.IsNullOrEmpty(arbitrationNotice)) return arbitrationNotice;
                if (!AutoPauseOthers || !ResumeInterrupted) return "Müziğe otomatik dönüş kapalı.";
                if (!AllowAutomaticResume()) return "Müziğe dönüş bu durumda bekletiliyor.";
                if (returning.Count == 0) return "Morf'un duraklattığı, dönüş bekleyen bir müzik yok.";
                return "Önceki müzik bekliyor. Araya giren medya kapandığında döner; yalnızca duraklatıldıysa aşağıdaki düğmeyi kullanabilirsiniz.";
            }
        }
        internal Task ResumePreviousAsync()
        {
            if (!CanReturn || manager == null || returning.Count == 0) { Notify(); return Task.FromResult(0); }
            var states = ReadReturnStates(manager.GetSessions().ToArray());
            if (states.Values.Any(item => item.State == ReturnPlaybackState.Playing || item.State == ReturnPlaybackState.Unknown))
            { arbitrationNotice = "Önce çalan diğer medyayı duraklatın. Durumu bilinmeyen kaynak varken müzik başlatılmaz."; Notify(); return Task.FromResult(0); }
            manualReturnRequested = true; returnDelay.Stop(); focusWork.Request(null);
            return Task.FromResult(0);
        }
        private void PlaybackStarted(Session winner, Session interrupted = null)
        {
            PlaybackRevision++;
            if (disposed) return;
            if (SourceRule(winner) == 2) return;
            // A manual source selection remains authoritative. Exclusions are never paused.
            if (preferred != null && !Equals(preferred, winner)) return;
            var favorite = Sessions.FirstOrDefault(item => SourceRule(item) == 1 && priority.IsPlaying(item));
            if (preferred == null && favorite != null && !Equals(favorite, winner)) winner = favorite;
            // Candidate discovery can inspect several media sessions. It is only needed
            // when automatic pausing is enabled, while source tracking remains active.
            Session candidate = AutoPauseOthers ? interrupted ?? ResolveInterruptedMusic(winner) : null;
            newestPlaying = winner;
            manualReturnRequested = false;
            if (!AutoPauseOthers) return;
            returnDelay.Stop(); returning.Forget(winner);
            returnCandidate = candidate;
            arbitrationNotice = null;
            arbitrationVersion++;
            focusWork.Request(winner);
        }
        private Session ResolveInterruptedMusic(Session winner)
        {
            return PlaybackReturn<Session>.ChooseCandidate(winner, new[] { newestPlaying, active }, priority.Others(winner), item => ReadReturnState(item, false));
        }
        private bool CanReturn { get { return !disposed && AutoPauseOthers && ResumeInterrupted && AllowAutomaticResume(); } }
        private async Task ApplyFocusAsync(Session winner, Func<bool> current)
        {
            var revoke = revokeReturnedPlay; revokeReturnedPlay = null;
            if (revoke != null && ReadReturnState(revoke, false).State == ReturnPlaybackState.Playing)
            {
                if (revoke.GetPlaybackInfo().Controls.IsPauseEnabled) await CompleteFocus(revoke.TryPauseAsync());
                if (!current()) return;
            }
            if (winner == null) { bool requested = manualReturnRequested; manualReturnRequested = false; await ResumeMusicAsync(current, requested); return; }
            int version = arbitrationVersion;
            Session candidate = returnCandidate;
            if (disposed || !AutoPauseOthers) return;
            bool succeeded = await priority.PauseOthersAsync(winner,
                other => SourceRule(other) != 2 && other.GetPlaybackInfo().PlaybackStatus == PlaybackStatus.Playing,
                other => other.GetPlaybackInfo().Controls.IsPauseEnabled,
                other => PauseForWinnerAsync(other, winner, Equals(other, candidate), () => current() && !disposed && AutoPauseOthers && version == arbitrationVersion), () =>
                {
                    bool playing;
                    return current() && !disposed && AutoPauseOthers && SourceRule(winner) != 2 && version == arbitrationVersion && TryPlaying(winner, out playing) && playing;
                });
            if (disposed || version != arbitrationVersion) return;
            bool winnerPlaying;
            if (!succeeded && TryPlaying(winner, out winnerPlaying) && winnerPlaying)
                arbitrationNotice = "Bazı medya kaynakları duraklatmayı desteklemiyor veya isteği reddetti.";
            Notify();
            QueueRefresh();
        }
        private async Task<bool> PauseForWinnerAsync(Session target, Session winner, bool candidate, Func<bool> current)
        {
            var state = ReadReturnState(target);
            if (state.State != ReturnPlaybackState.Playing || !current()) return true;
            string identity; knownTracks.TryGetValue(target, out identity);
            var pause = returning.Begin(target, winner, candidate && CanReturn && state.Music && !string.IsNullOrEmpty(identity), identity);
            try
            {
                if (!current()) { returning.Complete(pause, false, null); return true; }
                bool accepted = await CompleteFocus(target.TryPauseAsync());
                returning.Complete(pause, accepted, ReadReturnState(target)); QueueRefresh(); return accepted;
            }
            catch { returning.Complete(pause, false, null); throw; }
        }
        private async Task ResumeMusicAsync(Func<bool> current, bool explicitRequest = false)
        {
            if (!CanReturn || !current() || manager == null) return;
            var states = ReadReturnStates(manager.GetSessions().ToArray());
            var pause = returning.TakePause(states, explicitRequest);
            if (pause == null || !current() || !CanReturn) return;
            Session target = pause.Target;
            string expected = pause.Identity; int trackVersion;
            if (string.IsNullOrEmpty(expected)) return;
            trackVersions.TryGetValue(target, out trackVersion);
            var properties = await CompleteFocus(target.TryGetMediaPropertiesAsync());
            int latestVersion; trackVersions.TryGetValue(target, out latestVersion);
            if (!current() || !CanReturn || trackVersion != latestVersion || !string.Equals(expected, TrackKey(target, properties), StringComparison.Ordinal)) return;
            // Recheck immediately before an explicit Play. Never use toggle or retry an uncertain result.
            var fresh = ReadReturnStates(manager.GetSessions().ToArray());
            ReturnSnapshot state;
            if (!fresh.TryGetValue(target, out state) || state.State != ReturnPlaybackState.Paused || !state.CanPlay
                || !state.Music || !state.Position.HasValue || !states[target].Position.HasValue
                || Math.Abs((double)state.Position.Value - states[target].Position.Value) > TimeSpan.TicksPerMillisecond * 500
                || fresh.Values.Any(item => item.State == ReturnPlaybackState.Playing || item.State == ReturnPlaybackState.Unknown)) return;
            returnedPlay = target; returnedPlayVersion = arbitrationVersion; returnedPlayUntil = DateTimeOffset.UtcNow.AddSeconds(10);
            bool accepted = await CompleteFocus(target.TryPlayAsync());
            if (!accepted && Equals(returnedPlay, target)) returnedPlay = null;
            if (!disposed && current())
            {
                arbitrationNotice = accepted ? null : "Önceki müzik kaynağı devam etme isteğini reddetti.";
                if (accepted) newestPlaying = target;
                QueueRefresh(); Notify();
            }
        }
        private static Dictionary<Session, ReturnSnapshot> ReadReturnStates(Session[] sessions)
        {
            var states = new Dictionary<Session, ReturnSnapshot>();
            foreach (var session in sessions) states[session] = ReadReturnState(session);
            return states;
        }
        private async Task<T> CompleteFocus<T>(IAsyncOperation<T> operation)
        {
            focusOperations.Add(operation);
            try { return await Complete(operation); }
            finally { focusOperations.Remove(operation); }
        }
        private static ReturnSnapshot ReadReturnState(Session session, bool includePosition = true)
        {
            try
            {
                var playback = session.GetPlaybackInfo();
                var result = new ReturnSnapshot { State = playback.PlaybackStatus == PlaybackStatus.Playing ? ReturnPlaybackState.Playing
                    : playback.PlaybackStatus == PlaybackStatus.Paused ? ReturnPlaybackState.Paused
                    : playback.PlaybackStatus == PlaybackStatus.Stopped ? ReturnPlaybackState.Stopped
                    : playback.PlaybackStatus == PlaybackStatus.Closed ? ReturnPlaybackState.Closed : ReturnPlaybackState.Unknown,
                    CanPlay = playback.Controls.IsPlayEnabled,
                    Music = playback.PlaybackType.HasValue ? playback.PlaybackType == Windows.Media.MediaPlaybackType.Music
                        : MediaSourceNames.Friendly(session.SourceAppUserModelId) == "Apple Music" || MediaSourceNames.Friendly(session.SourceAppUserModelId) == "Spotify" };
                if (includePosition && result.State == ReturnPlaybackState.Paused) { try { result.Position = session.GetTimelineProperties().Position.Ticks; } catch { } }
                return result;
            }
            catch { return new ReturnSnapshot(); }
        }

    }
}
