using System;
using System.Windows;

namespace Widgates
{
    internal enum DockEdge { Top, Bottom }
    internal enum TaskbarEdge { Left, Top, Right, Bottom }

    internal static class DockLayout
    {
        internal static DockEdge Resolve(DockPreference preference, TaskbarEdge taskbar)
        {
            if (preference == DockPreference.Top) return DockEdge.Top;
            if (preference == DockPreference.Bottom) return DockEdge.Bottom;
            return taskbar == TaskbarEdge.Top ? DockEdge.Bottom : DockEdge.Top;
        }

        internal static Point Position(Rect monitor, Rect work, Size window, DockEdge edge)
        {
            double x = Math.Max(work.Left, work.Left + (work.Width - window.Width) / 2);
            return new Point(x, edge == DockEdge.Top ? monitor.Top : Math.Max(monitor.Top, monitor.Bottom - window.Height));
        }

        internal static CornerRadius Corners(DockEdge edge, double radius)
        {
            return edge == DockEdge.Top ? new CornerRadius(0, 0, radius, radius) : new CornerRadius(radius, radius, 0, 0);
        }

        internal static Thickness Outline(DockEdge edge)
        {
            return edge == DockEdge.Top ? new Thickness(1, 0, 1, 1) : new Thickness(1, 1, 1, 0);
        }
    }
}
