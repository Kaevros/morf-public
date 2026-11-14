using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Widgates
{
    internal static class IdleClockChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; idle clock with synthetic media; no playback commands or saved preferences");
            var settings = new WidgetSettings { DisplayMode = DisplayMode.DynamicIsland, AutoPauseOthers = false, HideInGames = false, HideInFullscreen = false, RespectQuietState = false };
            var widget = new IslandWindow(settings);
            var timer = Field<DispatcherTimer>(widget, "clockTimer");
            try
            {
                var media = Field<MediaController>(widget, "media");
                var update = (Action)Delegate.CreateDelegate(typeof(Action), widget, typeof(IslandWindow).GetMethod("UpdateMedia", Private));
                media.Changed -= update;
                widget.IsHitTestVisible = false; widget.Show(); await Task.Delay(700);
                typeof(MediaController).GetProperty("Title", Private).SetValue(media, "", null);
                typeof(MediaController).GetProperty("Playback", Private).SetValue(media, null, null);
                Call(widget, "UpdateMedia"); Call(widget, "UpdateState", false); widget.UpdateLayout();
                var clock = Field<TextBlock>(widget, "compactClock");
                Check(clock.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(clock.Text) && timer.IsEnabled
                    && timer.Interval.TotalSeconds > 0 && timer.Interval.TotalSeconds <= 60,
                    "An idle floating capsule shows the local time and schedules only its next minute");
                Check(AutomationProperties.GetName(clock).StartsWith("Saat ") && Field<Grid>(widget, "compactMarkContainer").Visibility == Visibility.Collapsed,
                    "The idle time has an accessible name and no empty media marker");
                Capture(widget, Path.Combine(output, "idle-clock.png"));
                clock.Text = "stale";
                Call(widget, "OnClockTimeChanged", null, EventArgs.Empty);
                await Task.Delay(100);
                Check(clock.Text == DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture) && timer.IsEnabled,
                    "A Windows clock change refreshes the visible time and reschedules the update");
                Call(widget, "PreviewNotification");
                var messages = Field<AppMessagePeek>(widget, "messages");
                Check(messages.Active && !timer.IsEnabled, "A notification suspends the idle clock");
                messages.Cancel();
                Check(!messages.Active && timer.IsEnabled, "The idle clock resumes after the notification");
                Call(widget, "SetExpanded", true); Call(widget, "UpdateState", false);
                Check(!timer.IsEnabled, "Opening the panel stops the hidden clock update");
                var picker = Field<ComboBox>(widget, "sessions");
                picker.ApplyTemplate(); picker.UpdateLayout();
                Check(!HasText(picker, "\u2304"), "The media source header has no chevron");
                picker.IsDropDownOpen = true;
                Check(picker.IsDropDownOpen, "The media source menu still opens without its chevron");
                picker.IsDropDownOpen = false;
                Call(widget, "SetExpanded", false); Call(widget, "UpdateState", false);
                Check(timer.IsEnabled, "Closing the panel resumes the visible idle clock");
                settings.DisplayMode = DisplayMode.Line; Call(widget, "ApplySettings", false);
                Check(clock.Visibility == Visibility.Collapsed && !timer.IsEnabled, "Line mode hides the idle clock and stops its timer");
                settings.DisplayMode = DisplayMode.AutoHide; Call(widget, "ApplySettings", false);
                Check(clock.Visibility == Visibility.Collapsed && !timer.IsEnabled, "Auto-hide mode hides the idle clock and stops its timer");
                settings.DisplayMode = DisplayMode.Capsule; settings.HideWhenIdle = true; Call(widget, "ApplySettings", false);
                Check(!timer.IsEnabled && Field<Grid>(widget, "compact").Visibility == Visibility.Collapsed,
                    "The explicit hide-when-idle preference still hides the capsule and stops the clock");
                settings.HideWhenIdle = false; Call(widget, "ApplySettings", false);
                Check(clock.Visibility == Visibility.Visible && timer.IsEnabled, "A visible attached idle capsule displays the clock");
                typeof(MediaController).GetProperty("Title", Private).SetValue(media, "Sample track", null);
                Call(widget, "UpdateMedia"); Call(widget, "UpdateState", false);
                Check(clock.Visibility == Visibility.Collapsed && !timer.IsEnabled && Field<TextBlock>(widget, "compactTitle").Text == "Sample track",
                    "A media title replaces the clock and stops its timer");
                typeof(MediaController).GetProperty("Title", Private).SetValue(media, "", null);
                string file = Path.Combine(output, "clock-fixture.txt"); File.WriteAllText(file, "fixture");
                Field<FileShelf>(widget, "shelf").Add(new[] { file }); Call(widget, "UpdateShelf"); Call(widget, "UpdateState", false);
                Check(clock.Visibility == Visibility.Collapsed && !timer.IsEnabled && Field<TextBlock>(widget, "compactTitle").Text == "1 dosya",
                    "A populated shelf retains its file count rather than showing idle time");
            }
            finally { widget.Close(); }
            Check(!timer.IsEnabled, "Closing the widget leaves no running idle-clock timer");
        }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static void Call(object owner, string name, params object[] args) { owner.GetType().GetMethod(name, Private).Invoke(owner, args); }
        private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
        private static bool HasText(DependencyObject root, string text)
        {
            var label = root as TextBlock;
            if (label != null && label.Text == text) return true;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (HasText(VisualTreeHelper.GetChild(root, i), text)) return true;
            return false;
        }
        private static void Capture(FrameworkElement element, string path)
        {
            element.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(element); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) png.Save(stream);
        }
    }
}
