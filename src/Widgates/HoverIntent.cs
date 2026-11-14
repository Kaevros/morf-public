using System;
using System.Windows.Threading;

namespace Widgates
{
    internal sealed class HoverIntent : IDisposable
    {
        private readonly DispatcherTimer timer;
        private readonly Action open;
        private readonly Func<bool> allowed;
        internal bool Pending { get { return timer.IsEnabled; } }
        internal HoverIntent(Dispatcher dispatcher, Action open, Func<bool> allowed)
        {
            this.open = open; this.allowed = allowed;
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            timer.Tick += (_, __) => { Cancel(); if (allowed()) open(); };
        }
        internal void Request(int milliseconds)
        {
            Cancel(); if (milliseconds <= 0) { if (allowed()) open(); return; }
            timer.Interval = TimeSpan.FromMilliseconds(milliseconds); timer.Start();
        }
        internal void Cancel() { timer.Stop(); }
        public void Dispose() { Cancel(); }
    }
}
