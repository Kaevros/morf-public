using System;
using System.Windows;

namespace Widgates
{
    // Local pointer events only. A drag never becomes an application-opening click.
    internal sealed class TrackSwipe
    {
        private Point? start;
        private double horizontal;
        private bool vertical;
        internal bool Active { get { return start.HasValue; } }
        internal bool Moved { get; private set; }
        internal string Command { get { return !vertical && Math.Abs(horizontal) >= 52 ? (horizontal < 0 ? "next" : "previous") : null; } }

        internal void Begin(Point point) { Cancel(); start = point; }
        internal void Move(Point point)
        {
            if (!start.HasValue) return;
            horizontal = point.X - start.Value.X;
            double y = Math.Abs(point.Y - start.Value.Y);
            if (Math.Abs(horizontal) >= 8 || y >= 8) Moved = true;
            // Once a vertical intent is clear, returning diagonally cannot skip a track.
            if (y >= 12 && y > Math.Abs(horizontal)) vertical = true;
            if (y > Math.Abs(horizontal) / 1.5) horizontal = 0;
        }
        internal void Cancel() { start = null; horizontal = 0; vertical = false; Moved = false; }
    }
}
