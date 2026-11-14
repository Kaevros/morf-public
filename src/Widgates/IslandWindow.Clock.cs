using System;
using System.Windows;

namespace Widgates
{
    internal sealed partial class IslandWindow
    {
        private void UpdateIdleClock(bool visible)
        {
            if (!visible || compactClock.Visibility != Visibility.Visible)
            {
                clockTimer.Stop();
                return;
            }
            if (clockTimer.IsEnabled) return;
            var now = DateTime.Now;
            compactClock.Text = now.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
            System.Windows.Automation.AutomationProperties.SetName(compactClock, "Saat " + compactClock.Text);
            // One update at the next minute boundary, only while the clock is visible.
            clockTimer.Interval = TimeSpan.FromTicks(TimeSpan.TicksPerMinute - now.Ticks % TimeSpan.TicksPerMinute);
            clockTimer.Start();
        }

        private void OnClockTimeChanged(object sender, EventArgs args)
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (closed) return;
                clockTimer.Stop();
                UpdateLiveVisuals();
            }));
        }
    }
}
