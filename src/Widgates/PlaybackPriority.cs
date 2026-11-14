using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Widgates
{
    // Event snapshots, not a clock or a polling loop. Startup is a non-disruptive baseline.
    internal sealed class PlaybackPriority<T>
    {
        private readonly Dictionary<T, bool> playing = new Dictionary<T, bool>();
        private bool initialized;
        private T latest;
        internal bool HasLatest { get; private set; }

        internal void Synchronize(IEnumerable<T> sessions, Func<T, bool> isPlaying)
        {
            var current = new HashSet<T>(sessions);
            foreach (var removed in playing.Keys.Where(item => !current.Contains(item)).ToArray()) playing.Remove(removed);
            if (HasLatest && !current.Contains(latest)) HasLatest = false;
            foreach (var item in current)
            {
                bool state = isPlaying(item);
                if (!playing.ContainsKey(item))
                {
                    playing[item] = state;
                    if (initialized && state) { latest = item; HasLatest = true; }
                }
            }
            initialized = true;
        }

        internal bool Observe(T session, bool state, bool countAsStart = true)
        {
            bool previous;
            bool started = playing.TryGetValue(session, out previous) && !previous && state && countAsStart;
            playing[session] = state;
            if (started) { latest = session; HasLatest = true; }
            return started;
        }

        internal bool IsPlaying(T session) { bool state; return playing.TryGetValue(session, out state) && state; }
        internal T Latest { get { return latest; } }
        internal T[] Others(T winner) { return playing.Where(item => item.Value && !EqualityComparer<T>.Default.Equals(item.Key, winner)).Select(item => item.Key).ToArray(); }

        internal async Task<bool> PauseOthersAsync(T winner, Func<T, bool> stillPlaying, Func<T, bool> canPause,
            Func<T, Task<bool>> pause, Func<bool> isCurrent)
        {
            bool succeeded = true;
            foreach (var other in Others(winner))
            {
                if (!isCurrent()) return false;
                try
                {
                    if (!stillPlaying(other)) continue;
                    if (!canPause(other)) { succeeded = false; continue; }
                    if (!isCurrent()) return false;
                    if (!await pause(other)) succeeded = false;
                }
                catch (Exception) { succeeded = false; }
            }
            return succeeded;
        }
    }
}
