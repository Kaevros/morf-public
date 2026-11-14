using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Widgates
{
    internal static class MediaReturnChecks
    {
        private static ReturnSnapshot Music(ReturnPlaybackState state = ReturnPlaybackState.Paused, long position = 200000000)
        { return new ReturnSnapshot { State = state, Music = true, CanPlay = true, Position = position }; }
        private static Dictionary<string, ReturnSnapshot> Sessions(params string[] others)
        {
            var result = new Dictionary<string, ReturnSnapshot> { { "music", Music() } };
            foreach (string other in others) result[other] = new ReturnSnapshot { State = ReturnPlaybackState.Paused };
            return result;
        }
        private static void Paused(PlaybackReturn<string> policy, string target = "music", string owner = "video")
        { var pause = policy.Begin(target, owner, true); policy.Complete(pause, true, Music()); }

        internal static async Task RunAsync(string output)
        {
            var priority = new PlaybackPriority<string>(); priority.Synchronize(new[] { "music", "video" }, item => item == "video");
            Check(!priority.Observe("music", true, false) && !priority.HasLatest && priority.Others("video").Length == 1,
                "An automatic Play acknowledgment updates state without stealing priority from a newer user source");
            Check(PlaybackReturn<string>.ChooseCandidate("video", new[] { "second", "first" }, new[] { "first", "second" }, _ => Music(ReturnPlaybackState.Playing)) == "second",
                "Return chooses the previously active music rather than the order of the session list");
            Check(PlaybackReturn<string>.ChooseCandidate("video", new string[0], new[] { "first", "second" }, _ => Music(ReturnPlaybackState.Playing)) == null,
                "Several baseline music players with unknown order do not produce an arbitrary return target");
            Check(PlaybackReturn<string>.ChooseCandidate("video", new[] { "video" }, new[] { "music" }, _ => Music(ReturnPlaybackState.Playing)) == "music",
                "A unique playing music source remains eligible when the current Windows session is misleading");
            var policy = new PlaybackReturn<string>(); var state = Sessions("video");
            var identityTicket = policy.Begin("music", "video", true, "original-track"); policy.Complete(identityTicket, true, Music());
            var immutable = policy.TakePause(Sessions());
            Check(immutable != null && immutable.Identity == "original-track", "Return retains the original paused track identity independently of later metadata cache refreshes");
            Check(policy.Take(state) == null, "A manually paused or startup-baseline source has no automatic return ownership");
            var ticket = policy.Begin("music", "video", false); policy.Complete(ticket, true, Music());
            Check(policy.Count == 0, "Non-music or disabled return never records a pause lease");
            ticket = policy.Begin("music", "video", true); policy.Complete(ticket, false, Music());
            Check(policy.Count == 0, "A refused pause cannot later start music");
            ticket = policy.Begin("music", "video", true); policy.Complete(ticket, true, Music(ReturnPlaybackState.Playing));
            state = Sessions(); state["music"] = Music(ReturnPlaybackState.Playing);
            Check(policy.Take(state) == null, "Accepted pause without an observed paused state cannot auto-resume");
            policy.Clear(); Paused(policy);
            Check(!policy.Ready(Sessions("video")), "A video paused by the user is not mistaken for a closed video");
            state = Sessions();
            Check(policy.Ready(state) && policy.Take(state) == "music" && policy.Take(state) == null, "Closing the interrupting session resumes only the owned music and consumes the request once");
            Paused(policy); state = Sessions("video"); state["video"].State = ReturnPlaybackState.Stopped;
            Check(policy.Take(state) == "music", "An explicitly stopped interruption can release paused music without treating Pause as Stop");
            Paused(policy); state = Sessions("third"); state["third"].State = ReturnPlaybackState.Playing;
            Check(!policy.Ready(state), "Closing a video cannot resume music over a third playing source");
            state["third"].State = ReturnPlaybackState.Unknown;
            Check(!policy.Ready(state), "An unreadable or transitioning source blocks automatic return");
            state.Remove("third"); Check(policy.Take(state) == "music", "A delayed return proceeds only after all other sources are known quiet");
            Paused(policy); state = Sessions(); state["music"] = Music(ReturnPlaybackState.Paused, 900000000);
            Check(policy.Take(state) == null && policy.Count == 0, "Seeking a Morf-paused track cancels its resume ownership");
            Paused(policy); policy.Forget("music");
            Check(policy.Take(Sessions()) == null, "A metadata change or explicit manual command cancels pending return");
            Paused(policy); state = Sessions(); state["music"].State = ReturnPlaybackState.Playing;
            Check(policy.Take(state) == null && policy.Count == 0, "Externally restarted music loses its old automatic resume lease");
            Paused(policy); state = Sessions(); state["music"].CanPlay = false;
            Check(policy.Take(state) == null && policy.Count == 0, "Unsupported explicit Play is consumed without falling back to toggle or retry");
            Paused(policy); state = Sessions(); state["music"].Position = null;
            Check(policy.Take(state) == null, "A missing paused-position snapshot fails closed");
            Paused(policy); state = Sessions(); state.Clear();
            Check(policy.Take(state) == null && policy.Count == 0, "Closed music sessions cannot be resurrected");
            ticket = policy.Begin("music", "video", true); policy.Clear(); policy.Complete(ticket, true, Music());
            Check(policy.Count == 0, "A late pause completion cannot recreate ownership after settings, sleep, lock or shutdown cancellation");
            var now = DateTimeOffset.UtcNow; var delayed = new PlaybackReturn<string>(() => now);
            ticket = delayed.Begin("music", "video", true); now = now.AddSeconds(6); delayed.Complete(ticket, true, Music());
            Check(delayed.Count == 0, "A late ambiguous pause acknowledgment is not retained for future playback");
            Paused(policy, "first", "second"); Paused(policy, "second", "video");
            state = new Dictionary<string, ReturnSnapshot> { { "first", Music() }, { "second", Music() } };
            Check(policy.Take(state) == "second" && policy.Count == 1 && policy.Take(state) == null,
                "Nested music interruptions restore the most recently paused music while its predecessor waits");
            state.Remove("second"); Check(policy.Take(state) == "first", "The older owned music can return only when its own interruption ends");
            Paused(policy); state = Sessions(); Check(policy.Ready(state), "Closure becomes an eligible candidate before the controller's settle delay");
            state["video"] = new ReturnSnapshot { State = ReturnPlaybackState.Playing };
            Check(policy.Take(state) == null, "A reappearing or restarted interruption cancels the pending closure decision");

            var settings = new WidgetSettings(); Check(settings.PeekTrackChanges && settings.ResumeInterrupted, "Both requested features default on without changing previous preferences");
            settings.PeekTrackChanges = settings.ResumeInterrupted = false; string path = Path.Combine(output, "media-return.settings"); settings.Save(path);
            var loaded = WidgetSettings.Load(path); Check(!loaded.PeekTrackChanges && !loaded.ResumeInterrupted, "The two feature switches persist independently of playback and shelf state");
            using (var first = new TrackPeek(Dispatcher.CurrentDispatcher))
            {
                first.Observe("new-source", true, true, true);
                Check(first.Active, "The first source started after the initial session baseline can show a peek");
            }
            using (var peek = new TrackPeek(Dispatcher.CurrentDispatcher))
            {
                int changes = 0; peek.Changed += () => changes++;
                peek.Observe("first", true, true);
                Check(!peek.Active && !peek.ClockRunning, "Existing playback at connection establishes a quiet notification baseline");
                peek.Observe("next", false, true); Check(!peek.Active, "A queued track while paused cannot show a playing notification");
                peek.Observe("next", true, true); Check(peek.Active && peek.ClockRunning && changes == 1, "A new playing track starts one four-second peek clock");
                peek.Observe("next", true, true); Check(changes == 1, "Repeated metadata/artwork updates do not restart the same track notification");
                await Task.Delay(4200);
                Check(!peek.Active && !peek.ClockRunning && changes == 2, "A real dispatcher expiry hides the notification and stops its clock");
                peek.Observe("blocked", true, false); peek.Observe("blocked", true, true);
                Check(!peek.Active, "A change suppressed by a game, settings or lock is consumed rather than replayed later");
                peek.Observe("last", true, true); peek.Cancel();
                Check(!peek.Active && !peek.ClockRunning, "User interaction, feature disable or shutdown can immediately cancel the peek");
            }
        }
        private static void Check(bool success, string label) { if (!success) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
    }
}
