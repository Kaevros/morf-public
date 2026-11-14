using System;
using System.Windows;

namespace Widgates
{
    internal static class PanelSizing
    {
        internal static Size Fit(Size desired, Size available)
        { return new Size(Math.Min(desired.Width, Math.Max(1, available.Width)), Math.Min(desired.Height, Math.Max(1, available.Height))); }
    }
}
