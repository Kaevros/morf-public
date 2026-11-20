using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace Widgates
{
    internal sealed class AppMessagePeek : IDisposable
    {
        private readonly DispatcherTimer timer;
        private readonly Func<DateTimeOffset> now;
        private readonly List<AppMessage> queue = new List<AppMessage>();
        private DateTimeOffset due, batchStart, cooldown;
        private TimeSpan remaining;
        private bool held;
        internal AppMessage Current { get; private set; }
        internal int Seconds { get; set; }
        internal bool Active { get { return Current != null; } }
        internal bool ClockRunning { get { return timer.IsEnabled; } }
        internal int Queued { get { return queue.Count; } }
        internal event Action Changed;
        internal AppMessagePeek(Dispatcher dispatcher, Func<DateTimeOffset> now = null)
        {
            this.now = now ?? (() => DateTimeOffset.UtcNow); Seconds = 5;
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            timer.Tick += (_, __) => Expire();
        }
        internal void Offer(AppMessage message, bool allowed)
        {
            if (!allowed || message == null || now() - message.Created > TimeSpan.FromSeconds(10)
                || message.Created - now() > TimeSpan.FromSeconds(2) || now() < cooldown) return;
            if (!Active) { batchStart = now(); Show(message); return; }
            if (Current.Key == message.Key) return;
            if (SameConversation(Current, message))
            {
                Current = message; Notify(); // Preserve the original deadline under a burst.
                return;
            }
            queue.RemoveAll(x => SameConversation(x, message) || now() - x.Created > TimeSpan.FromSeconds(10));
            if (queue.Count == 3) queue.RemoveAt(0);
            queue.Add(message);
        }
        private static bool SameConversation(AppMessage first, AppMessage second)
        {
            return string.Equals(first.AppId, second.AppId, StringComparison.OrdinalIgnoreCase)
                && (first.Sender == second.Sender || (first.MediaTrackIdentity != null && second.MediaTrackIdentity != null));
        }
        private void Show(AppMessage message)
        {
            timer.Stop(); held = false; Current = message;
            remaining = TimeSpan.FromSeconds(Seconds == 3 || Seconds == 8 ? Seconds : 5);
            var budget = batchStart + TimeSpan.FromSeconds(10) - now();
            if (remaining > budget) remaining = budget < TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250) : budget;
            due = now() + remaining; timer.Interval = remaining; timer.Start(); Notify();
        }
        internal void Expire()
        {
            timer.Stop();
            if (now() - batchStart >= TimeSpan.FromSeconds(10)) { cooldown = now() + TimeSpan.FromSeconds(2); Cancel(); return; }
            queue.RemoveAll(x => now() - x.Created > TimeSpan.FromSeconds(10));
            if (queue.Count == 0) { cooldown = now() + TimeSpan.FromSeconds(2); Cancel(); return; }
            var next = queue[0]; queue.RemoveAt(0); Show(next);
        }
        internal void Hold()
        {
            if (!Active || held) return;
            remaining = due - now(); held = true; timer.Stop();
        }
        internal void Release()
        {
            if (!Active || !held) return;
            held = false; timer.Interval = remaining < TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250) : remaining;
            due = now() + timer.Interval; timer.Start();
        }
        internal void Retain(ISet<string> keys)
        {
            queue.RemoveAll(x => !x.Sample && !keys.Contains(x.Key));
            if (Current != null && !Current.Sample && !keys.Contains(Current.Key)) Expire();
        }
        internal void Filter(Func<AppMessage, bool> keep, bool reserveQuietInterval = true)
        {
            queue.RemoveAll(x => !x.Sample && !keep(x));
            if (Current == null || Current.Sample || keep(Current)) return;
            if (reserveQuietInterval) { Expire(); return; }
            // Opening the player consumes its media toast, not the next app's time slot.
            timer.Stop(); held = false; Current = null;
            queue.RemoveAll(x => now() - x.Created > TimeSpan.FromSeconds(10));
            if (queue.Count > 0 && now() - batchStart < TimeSpan.FromSeconds(10))
            {
                var next = queue[0]; queue.RemoveAt(0); Show(next);
            }
            else { queue.Clear(); Notify(); }
        }
        internal void Cancel(bool notify = true)
        {
            bool changed = Active; Current = null; queue.Clear(); held = false; timer.Stop();
            if (notify && changed) Notify();
        }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose() { Cancel(false); }
    }
}
