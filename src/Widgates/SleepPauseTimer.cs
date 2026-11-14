using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Widgates
{
    internal sealed class SleepPauseTimer : IDisposable
    {
        private readonly DispatcherTimer timer;
        private readonly Func<Func<bool>, Task> pause;
        private readonly Func<DateTimeOffset> now;
        private int generation;
        internal event Action Changed;
        internal DateTimeOffset? Due { get; private set; }
        internal bool Active { get { return Due.HasValue; } }
        internal int SelectedMinutes { get; private set; }
        internal SleepPauseTimer(Dispatcher dispatcher, Func<Func<bool>, Task> pause, Func<DateTimeOffset> now = null)
        {
            this.pause = pause; this.now = now ?? (() => DateTimeOffset.UtcNow); SelectedMinutes = 30;
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            timer.Tick += (_, __) => CheckDue();
        }
        internal void Start(int minutes)
        {
            if (minutes != 15 && minutes != 30 && minutes != 60) throw new ArgumentOutOfRangeException("minutes");
            Cancel(); SelectedMinutes = minutes; Due = now().AddMinutes(minutes); timer.Interval = TimeSpan.FromMinutes(minutes); timer.Start(); Notify();
        }
        internal void Cancel() { generation++; timer.Stop(); Due = null; Notify(); }
        internal async void CheckDue()
        {
            if (!Due.HasValue) return;
            timer.Stop();
            if (Due.Value > now()) { timer.Interval = Due.Value - now(); timer.Start(); return; }
            int version = ++generation; Due = null; Notify();
            try { await pause(() => generation == version); } catch { }
        }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose() { Cancel(); }
    }
}
