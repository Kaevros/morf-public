using System;
using System.Windows.Threading;

namespace Widgates
{
    internal sealed class TrackPeek : IDisposable
    {
        private readonly DispatcherTimer timer;
        private string last;
        private bool pending;
        private bool played;
        private DateTimeOffset due;
        private TimeSpan remaining;
        private bool held;
        internal int DurationSeconds { get; set; }
        internal bool Active { get; private set; }
        internal event Action Changed;
        internal TrackPeek(Dispatcher dispatcher)
        {
            DurationSeconds = 4;
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (_, __) => Cancel();
        }
        internal void Observe(string identity, bool playing, bool allowed, bool newPlayback = false)
        {
            if (string.IsNullOrEmpty(identity)) return;
            if (last == null) { last = identity; pending = newPlayback; played = playing; } // Existing playback at connection stays quiet.
            else if (!string.Equals(last, identity, StringComparison.Ordinal)) { last = identity; pending = true; played = false; }
            if (playing && newPlayback && !played) pending = true;
            if (playing) played = true;
            if (!playing || !pending) return;
            pending = false;
            if (!allowed) return;
            Active = true; timer.Stop(); remaining = TimeSpan.FromSeconds(DurationSeconds == 2 || DurationSeconds == 6 ? DurationSeconds : 4);
            due = DateTimeOffset.UtcNow + remaining; timer.Interval = remaining; if (!held) timer.Start(); Notify();
        }
        internal void Hold()
        {
            if (!Active || held) return;
            remaining = due - DateTimeOffset.UtcNow; held = true; timer.Stop();
        }
        internal void Release()
        {
            if (!held) return; held = false;
            if (!Active) return;
            timer.Interval = remaining < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining;
            due = DateTimeOffset.UtcNow + timer.Interval; timer.Start();
        }
        internal void Cancel(bool notify = true)
        {
            timer.Stop(); held = false; bool changed = Active; Active = false;
            if (changed && notify) Notify();
        }
        internal bool ClockRunning { get { return timer.IsEnabled; } }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose() { Cancel(false); last = null; pending = false; }
    }
}
