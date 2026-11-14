using System;
using System.Windows.Threading;

namespace Widgates
{
    internal sealed class TemporaryHide : IDisposable
    {
        private readonly DispatcherTimer timer;
        internal bool Active { get; private set; }
        internal event Action Changed;
        internal TemporaryHide(Dispatcher dispatcher) { timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher); timer.Tick += (_, __) => Cancel(); }
        internal void Start() { timer.Stop(); Active = true; timer.Interval = TimeSpan.FromMinutes(15); timer.Start(); Notify(); }
        internal void Cancel() { timer.Stop(); bool changed = Active; Active = false; if (changed) Notify(); }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose() { timer.Stop(); Active = false; }
    }
}
