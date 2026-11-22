using System;
using System.IO;
using System.Linq;
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
            await RunClockToolsAsync(output);
            await RunStopwatchPersistenceAsync(output);
        }

        private static async Task RunStopwatchPersistenceAsync(string output)
        {
            string path = Path.Combine(output, "stopwatch.state");
            Check(StopwatchState.Save(path, 750) && StopwatchState.Save(path, 800), "Stopwatch snapshots use the local atomic store");
            File.WriteAllText(path, "invalid");
            Check(StopwatchState.Load(path) == 750, "An invalid stopwatch snapshot recovers the previous valid duration");
            Check(StopwatchState.Save(path, null) && !StopwatchState.Load(path).HasValue,
                "A reset snapshot takes precedence over the previous saved duration");

            var settings = new WidgetSettings { DisplayMode = DisplayMode.Line, HideInGames = false, HideInFullscreen = false, AutoPauseOthers = false };
            var first = new IslandWindow(settings, path);
            long shutdownValue;
            try
            {
                first.Show();
                Call(first, "OpenClockTools");
                Click(Field<Button>(first, "stopwatchChoice"));
                Click(Field<Button>(first, "clockToggle"));
                Check(StopwatchState.Load(path) == 0, "Starting a stopwatch stores its initial duration");
                Call(first, "SetExpanded", false);
                var ticker = Field<DispatcherTimer>(first, "clockToolsTick");
                Check(ticker.IsEnabled && ticker.Interval.TotalSeconds > 1,
                    "A hidden running stopwatch wakes for checkpoints at a slower cadence than its display");
                await Task.Delay(160);
                typeof(IslandWindow).GetField("stopwatchSaveDue", Private).SetValue(first, (long?)ClockTools.GetTickCount64());
                Call(first, "UpdateLiveVisuals");
                long checkpoint = StopwatchState.Load(path).Value;
                Check(checkpoint > 0, "The checkpoint stores elapsed time even while the timer is hidden");
                await Task.Delay(160);
                var model = Field<ClockTools>(first, "clocks");
                long beforeShutdown = model.Elapsed(ClockTools.GetTickCount64());
                first.AllowSessionExit();
                long afterShutdown = model.Elapsed(ClockTools.GetTickCount64());
                shutdownValue = StopwatchState.Load(path).Value;
                Check(shutdownValue >= beforeShutdown && shutdownValue <= afterShutdown && shutdownValue > checkpoint,
                    "The Windows session-ending handler saves the current monotonic duration");
            }
            finally { first.Close(); }

            long savedAtExit = StopwatchState.Load(path).Value;
            await Task.Delay(160);
            var reopened = new IslandWindow(settings, path);
            try
            {
                reopened.Show();
                Call(reopened, "SetExpanded", true); Call(reopened, "UpdateState", false);
                var model = Field<ClockTools>(reopened, "clocks");
                var toggle = Field<Button>(reopened, "clockToggle");
                Check(savedAtExit >= shutdownValue && model.StopwatchActive && !model.StopwatchRunning
                    && model.Elapsed(ClockTools.GetTickCount64()) == savedAtExit && (string)toggle.ToolTip == "Sürdür"
                    && !Field<DispatcherTimer>(reopened, "clockToolsTick").IsEnabled,
                    "Reopening restores the stopwatch inline, paused with no elapsed time added while closed");
                Click(toggle);
                await Task.Delay(160);
                Click(toggle);
                Check(!model.StopwatchRunning && StopwatchState.Load(path) > savedAtExit,
                    "Resuming continues from the saved total and pausing persists the new total");
                Click(Field<Button>(reopened, "clockReset"));
                Check(!model.StopwatchActive && !StopwatchState.Load(path).HasValue,
                    "Resetting clears the resumable stopwatch snapshot");
            }
            finally { reopened.Close(); }
        }

        private static async Task RunClockToolsAsync(string output)
        {
            var clocks = new ClockTools();
            clocks.StartCountdown(1000, 3000);
            clocks.ToggleStopwatch(1000);
            clocks.PauseCountdown(2250);
            Check(clocks.Remaining(9000) == 1750 && clocks.Preview == ClockTool.Countdown && !clocks.StopwatchActive,
                "An active or paused countdown excludes the stopwatch and holds its remaining duration");
            clocks.ResumeCountdown(9000);
            Check(clocks.Remaining(10000) == 750 && clocks.Preview == ClockTool.Countdown && !clocks.CheckCountdown(10749)
                && clocks.CheckCountdown(10750) && !clocks.CheckCountdown(99999),
                "A resumed countdown expires once at its deadline");
            clocks.ResetCountdown();
            clocks.ToggleStopwatch(20000);
            clocks.ToggleStopwatch(20000);
            clocks.StartCountdown(20000, 1000);
            Check(clocks.Preview == ClockTool.Stopwatch && clocks.Countdown == CountdownPhase.Ready && clocks.Elapsed(21000) == 0,
                "Even a stopwatch paused at zero excludes a countdown until reset");
            Check(ClockTools.Format(3599999, true) == "01:00:00" && ClockTools.Format(90061000, false) == "25:01:01",
                "Countdown rounds up and stopwatch duration does not wrap at midnight");

            var settings = new WidgetSettings { Dock = DockPreference.Top, DisplayMode = DisplayMode.Capsule, AutoPauseOthers = false, HideInGames = false, HideInFullscreen = false, RespectQuietState = false, CompactWidth = 150, ScrollVolume = true };
            var widget = new IslandWindow(settings);
            var ticker = Field<DispatcherTimer>(widget, "clockToolsTick");
            try
            {
                var media = Field<MediaController>(widget, "media");
                media.Changed -= (Action)Delegate.CreateDelegate(typeof(Action), widget, typeof(IslandWindow).GetMethod("UpdateMedia", Private));
                widget.IsHitTestVisible = false; widget.Show(); await Task.Delay(300);
                typeof(MediaController).GetProperty("Playback", Private).SetValue(media, null, null);
                typeof(MediaController).GetProperty("Title", Private).SetValue(media, "Test parçası", null);
                Call(widget, "UpdateMedia");
                Call(widget, "SetExpanded", true); Call(widget, "UpdateState", false);
                widget.UpdateLayout();
                var shell = Field<Border>(widget, "shell");
                var viewport = Field<ScrollViewer>(widget, "panelViewport");
                var sessions = Field<ComboBox>(widget, "sessions");
                var sourceVisibility = sessions.Visibility;
                double playerHeight = shell.ActualHeight;
                double headerPosition = sessions.TranslatePoint(new Point(), shell).Y;
                var menu = Field<ContextMenu>(widget, "applicationMenu");
                var clockItem = menu.Items.Cast<MenuItem>().Single(item => AutomationProperties.GetName(item) == "Geri sayım ve kronometre");
                Check(menu.Items.Count == 4, "The launch menu keeps three apps and the in-panel clock tools");
                var appItems = menu.Items.Cast<MenuItem>().Where(item => item != clockItem);
                Check(appItems.All(item => ((Grid)item.Header).Children.OfType<Image>().Single().Source != null),
                    "Every app shortcut has a bundled vendor icon before shell icon loading");
                clockItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Task.Delay(250);
                widget.UpdateLayout();
                var clockPanel = Field<Border>(widget, "clockPanel");
                var volumePanel = Field<Border>(widget, "volumePanel");
                var clockChooser = Field<Grid>(widget, "clockChooser");
                var start = Field<Button>(widget, "clockToggle");
                var reset = Field<Button>(widget, "clockReset");
                var clockModel = Field<ClockTools>(widget, "clocks");
                Check(clockPanel.IsVisible && Field<StackPanel>(widget, "playerContent").IsVisible
                    && ((FrameworkElement)sessions.Parent).IsVisible && sessions.Visibility == sourceVisibility && volumePanel.IsVisible
                    && clockPanel.TranslatePoint(new Point(), widget).Y >= volumePanel.TranslatePoint(new Point(0, volumePanel.ActualHeight), widget).Y
                    && clockPanel.TranslatePoint(new Point(0, clockPanel.ActualHeight), widget).Y <= widget.ActualHeight + 1
                    && clockChooser.IsVisible && !ticker.IsEnabled, "The launch menu opens clock controls below volume while keeping the player visible");
                viewport.ScrollToBottom();
                widget.UpdateLayout();
                Check(shell.ActualHeight > playerHeight && Math.Abs(sessions.TranslatePoint(new Point(), shell).Y - headerPosition) < 1
                    && viewport.ScrollableHeight == 0 && viewport.VerticalOffset == 0,
                    "Opening the clock expands the top-docked shell downwards and fits the full player without scrolling");
                if (!SystemParameters.HighContrast)
                {
                    double heightWithTimer = shell.ActualHeight;
                    settings.Style = SurfaceStyle.LiquidGlass; settings.LiquidTransparency = 0.8;
                    Call(widget, "ApplySettings", false);
                    var clearFill = (SolidColorBrush)clockPanel.Background;
                    settings.LiquidTransparency = 0.55;
                    Call(widget, "ApplySettings", false); widget.UpdateLayout();
                    Check(((SolidColorBrush)clockPanel.Background).Color.A > clearFill.Color.A
                        && clockPanel.BorderBrush is LinearGradientBrush && Field<Border>(widget, "clockSheen").Background is LinearGradientBrush
                        && Math.Abs(shell.ActualHeight - heightWithTimer) < 1 && viewport.ScrollableHeight == 0,
                        "Timer glass follows the appearance slider without moving controls or enabling scrolling");
                }
                Capture(widget, Path.Combine(output, "clock-tools-setup.png"));
                var minutes = Field<TextBox>(widget, "clockMinutes");
                var seconds = Field<TextBox>(widget, "clockSeconds");
                minutes.Text = "60";
                Click(start);
                Check(Field<TextBlock>(widget, "clockNotice").IsVisible && !ticker.IsEnabled, "An invalid duration gets inline feedback and starts no timer");
                minutes.Text = "00"; seconds.Text = "50";
                Click(start);
                Check(clockModel.Countdown == CountdownPhase.Running && ticker.Interval.TotalSeconds <= 1 && !minutes.IsVisible
                    && !clockChooser.IsVisible && Field<TextBlock>(widget, "clockValue").IsVisible && reset.IsVisible,
                    "Starting a countdown replaces setup with its inline readout and hides the other tool");
                await Task.Delay(250);
                widget.UpdateLayout();
                Check(viewport.ScrollableHeight == 0 && viewport.VerticalOffset == 0 && shell.ActualHeight > playerHeight
                    && clockPanel.TranslatePoint(new Point(0, clockPanel.ActualHeight), shell).Y <= shell.ActualHeight,
                    "The running timer stays below volume without scrolling");
                Capture(widget, Path.Combine(output, "clock-tools.png"));
                Click(start);
                Check((string)start.ToolTip == "Sürdür" && !ticker.IsEnabled && !clockChooser.IsVisible, "A paused countdown stays inline with its resume control");
                Click(start);
                Call(widget, "SetExpanded", false); Call(widget, "UpdateState", false); widget.UpdateLayout();
                var badge = Field<Button>(widget, "compactClockTools");
                var value = Field<TextBlock>(widget, "compactClockValue");
                Check(badge.IsVisible && value.IsVisible && value.Text.StartsWith("00:") && !Field<TextBlock>(widget, "compactTitle").IsVisible,
                    "A paused media title gives way to the running countdown in the capsule");
                Capture(widget, Path.Combine(output, "clock-tools-compact.png"));
                shell.Width = settings.CompactWidth;
                Call(widget, "RenderCompactContent", true); widget.UpdateLayout();
                var title = Field<TextBlock>(widget, "compactTitle");
                double badgeLeft = badge.TranslatePoint(new Point(0, 0), shell).X;
                Check(badge.IsVisible && !value.IsVisible && title.IsVisible && Grid.GetColumn(badge) == 3
                    && badgeLeft > title.TranslatePoint(new Point(0, 0), shell).X && badgeLeft + badge.ActualWidth <= shell.ActualWidth,
                    "Playing media keeps its title and displays a clock icon to the right at minimum capsule width");
                Capture(widget, Path.Combine(output, "clock-tools-playing.png"));
                Call(widget, "UpdateCompactContent"); Call(widget, "UpdateState", false);
                foreach (var mode in new[] { DisplayMode.Line, DisplayMode.AutoHide })
                {
                    settings.DisplayMode = mode; Call(widget, "ApplySettings", false);
                    Check(!badge.IsVisible && ticker.IsEnabled && ticker.Interval.TotalSeconds > 5, mode + " hides the clock preview and keeps the countdown deadline");
                }
                settings.DisplayMode = DisplayMode.DynamicIsland; Call(widget, "ApplySettings", false);
                Check(badge.IsVisible && value.IsVisible, "The floating capsule also shows the countdown");
                Click(badge);
                Check(clockPanel.IsVisible && Field<StackPanel>(widget, "playerContent").IsVisible && !clockChooser.IsVisible,
                    "Opening the compact indicator shows the active controls in the main player");

                Click(reset);
                Check(clockChooser.IsVisible && minutes.IsVisible && !reset.IsVisible, "Resetting brings back the tool choices and duration input");
                seconds.Text = "01"; Click(start);
                settings.DisplayMode = DisplayMode.Line;
                Call(widget, "SetExpanded", false); Call(widget, "ApplySettings", false);
                await Task.Delay(1400);
                Check(clockModel.Countdown == CountdownPhase.Finished && !ticker.IsEnabled && !badge.IsVisible,
                    "A countdown expires even while the line hides all digits");
                Call(widget, "SetExpanded", true); Call(widget, "UpdateState", false);
                var clockValue = Field<TextBlock>(widget, "clockValue");
                var clockTitle = Field<TextBlock>(widget, "clockTitle");
                Check(clockTitle.Text == "Süre doldu" && clockValue.Text == "00:00" && !clockChooser.IsVisible,
                    "Reopening shows countdown completion in the main panel");
                Click(reset);
                Click(Field<Button>(widget, "stopwatchChoice"));
                Click(start);
                Check(clockModel.StopwatchRunning && !clockChooser.IsVisible && clockTitle.Text == "Kronometre",
                    "Starting the stopwatch also hides the other tool");
                Call(widget, "SetExpanded", false);
                Check(!ticker.IsEnabled, "The hidden stopwatch needs no display updates");
                await Task.Delay(1150);
                Call(widget, "SetExpanded", true); Call(widget, "UpdateState", false);
                Check(clockValue.Text != "00:00", "The stopwatch keeps accurate time while its digits are hidden");
                Click(start);
                Click(reset);
                Check(!ticker.IsEnabled && clockValue.Text == "00:00" && clockChooser.IsVisible,
                    "Resetting the stopwatch releases updates and shows both choices");
            }
            finally { widget.Close(); }
            Check(!ticker.IsEnabled, "Closing Morf stops the clock tools scheduler");
        }

        private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
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
