using System;
using System.Collections.Generic;
using System.Linq;

namespace Widgates
{
    internal enum ReturnPlaybackState { Unknown, Playing, Paused, Stopped, Closed }
    internal sealed class ReturnSnapshot
    {
        internal ReturnPlaybackState State;
        internal bool Music, CanPlay;
        internal long? Position;
    }

    // Session-scoped pause ownership only. No persisted history and no speculative toggles.
    internal sealed class PlaybackReturn<T> where T : class
    {
        internal sealed class Pause
        {
            internal T Target, Owner;
            internal long Order;
            internal DateTimeOffset Issued;
            internal bool Accepted, Confirmed, OwnerEnded;
            internal long? Position;
            internal string Identity;
        }
        private readonly Dictionary<T, Pause> pauses = new Dictionary<T, Pause>();
        private readonly Func<DateTimeOffset> now;
        private long order;
        internal int Count { get { return pauses.Count; } }
        internal bool Contains(T target) { return target != null && pauses.ContainsKey(target); }
        internal PlaybackReturn(Func<DateTimeOffset> now = null) { this.now = now ?? (() => DateTimeOffset.UtcNow); }
        internal static T ChooseCandidate(T winner, IEnumerable<T> preferred, IEnumerable<T> others, Func<T, ReturnSnapshot> read)
        {
            Func<T, bool> eligible = candidate =>
            {
                if (candidate == null || Equals(candidate, winner)) return false;
                var state = read(candidate); return state != null && state.Music && state.State == ReturnPlaybackState.Playing;
            };
            T selected = preferred.FirstOrDefault(eligible);
            if (selected != null) return selected;
            var music = others.Where(eligible).Distinct().Take(2).ToArray();
            return music.Length == 1 ? music[0] : null; // Ambiguous startup order must never become an arbitrary music choice.
        }
        internal Pause Begin(T target, T owner, bool eligible, string identity = null)
        {
            Forget(target);
            if (!eligible || target == null || owner == null || Equals(target, owner)) return null;
            if (pauses.Count >= 32) pauses.Remove(pauses.Values.OrderBy(item => item.Order).First().Target);
            var pause = new Pause { Target = target, Owner = owner, Order = ++order, Issued = now(), Identity = identity };
            pauses[target] = pause; return pause;
        }
        internal void Complete(Pause pause, bool accepted, ReturnSnapshot state)
        {
            if (!Owns(pause)) return;
            if (!accepted) { Forget(pause.Target); return; }
            pause.Accepted = true; Observe(pause.Target, state);
        }
        internal void Observe(T target, ReturnSnapshot state)
        {
            Pause pause;
            if (target == null || !pauses.TryGetValue(target, out pause)) return;
            if (state == null || state.State == ReturnPlaybackState.Unknown || state.State == ReturnPlaybackState.Stopped || state.State == ReturnPlaybackState.Closed)
            { Forget(target); return; }
            if (state.State == ReturnPlaybackState.Playing)
            {
                if (pause.Confirmed) Forget(target);
                return;
            }
            if (!pause.Confirmed)
            {
                if (now() - pause.Issued > TimeSpan.FromSeconds(5)) { Forget(target); return; }
                if (pause.Accepted && state.Position.HasValue) { pause.Confirmed = true; pause.Position = state.Position; }
            }
            else if (!state.Position.HasValue || Math.Abs((double)state.Position.Value - pause.Position.GetValueOrDefault()) > TimeSpan.TicksPerMillisecond * 500)
                Forget(target); // Seeking/changing a paused track takes ownership away from Morf.
        }
        internal void Synchronize(IDictionary<T, ReturnSnapshot> sessions)
        {
            foreach (var pause in pauses.Values.ToArray())
            {
                ReturnSnapshot state;
                if (!sessions.TryGetValue(pause.Target, out state)) { Forget(pause.Target); continue; }
                Observe(pause.Target, state);
                ReturnSnapshot owner;
                if (!sessions.TryGetValue(pause.Owner, out owner) || owner.State == ReturnPlaybackState.Closed || owner.State == ReturnPlaybackState.Stopped)
                    pause.OwnerEnded = true;
                else pause.OwnerEnded = false;
            }
        }
        internal bool Ready(IDictionary<T, ReturnSnapshot> sessions)
        {
            Synchronize(sessions);
            if (sessions.Values.Any(state => state.State == ReturnPlaybackState.Playing || state.State == ReturnPlaybackState.Unknown)) return false;
            return pauses.Values.Any(pause => pause.Accepted && pause.Confirmed && pause.OwnerEnded);
        }
        internal T Take(IDictionary<T, ReturnSnapshot> sessions)
        {
            var pause = TakePause(sessions); return pause == null ? null : pause.Target;
        }
        internal Pause TakePause(IDictionary<T, ReturnSnapshot> sessions, bool explicitRequest = false)
        {
            bool ready = Ready(sessions);
            if (!ready && (!explicitRequest || sessions.Values.Any(snapshot => snapshot.State == ReturnPlaybackState.Playing || snapshot.State == ReturnPlaybackState.Unknown))) return null;
            var pause = pauses.Values.Where(item => item.Accepted && item.Confirmed && (item.OwnerEnded || explicitRequest)).OrderByDescending(item => item.Order).FirstOrDefault();
            if (pause == null) return null;
            Forget(pause.Target); // Consume before sending; failure/late completion must never auto-retry.
            ReturnSnapshot state = sessions[pause.Target];
            return state.State == ReturnPlaybackState.Paused && state.CanPlay && state.Music ? pause : null;
        }
        internal void Forget(T target) { if (target != null) pauses.Remove(target); }
        internal void Clear() { pauses.Clear(); }
        private bool Owns(Pause pause) { Pause current; return pause != null && pauses.TryGetValue(pause.Target, out current) && ReferenceEquals(pause, current); }
    }
}
