using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;

namespace Widgates
{
    internal static class LifecycleChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; temporary own WPF windows and isolated tray identity; no media commands");
            var preferences = new WidgetSettings { AutoPauseOthers = false, HoverDelayMs = 0, HideInGames = false };
            var widget = new IslandWindow(preferences);
            try
            {
                widget.Show(); await Task.Delay(500);
                var source = (HwndSource)PresentationSource.FromVisual(widget);
                using (var tray = new TrayIcon(source, () => { }, () => { }, () => { }, () => { }, Guid.NewGuid()))
                {
                    tray.SetEnabled(true); Check(tray.Added, "Native shell accepts the optional isolated tray icon");
                    tray.SetEnabled(false); Check(!tray.Added, "Disabling the tray removes its icon without an idle service");
                }
                Call(widget, "TogglePersistent"); Call(widget, "OpenSettings");
                var settings = (SettingsWindow)typeof(IslandWindow).GetField("settingsWindow", Private).GetValue(widget);
                Call(widget, "OnShellMouseEnter");
                var state = (PanelState)typeof(IslandWindow).GetField("panelState", Private).GetValue(widget);
                Check(settings.IsVisible && !state.Expanded, "Zero-delay hover cannot reopen the native panel over settings");
                settings.Close();
                Check(state.Persistent && state.Expanded && state.Latched, "Closing native settings restores an explicitly pinned panel");
                var placement = (DesktopPlacement)typeof(IslandWindow).GetField("placement", Private).GetValue(widget);
                preferences.HideInGames = true;
                typeof(DesktopPlacement).GetField("gameActive", Private).SetValue(placement, true); Call(widget, "UpdateVisibility");
                Check(!widget.IsVisible, "Game suppression still hides a persistently pinned panel");
                preferences.HideInGames = false;
                typeof(DesktopPlacement).GetField("gameActive", Private).SetValue(placement, false); Call(widget, "UpdateVisibility");
                Check(widget.IsVisible && state.Persistent && state.Expanded, "Leaving a game restores an explicitly pinned panel");
                Call(widget, "HideTemporarily"); Check(!widget.IsVisible, "Temporary hide removes the native main window");
                widget.OpenFromShell(); Check(widget.IsVisible && state.Expanded, "Explicit shell recovery cancels hiding and opens the same window");
                Call(widget, "ClosePanel"); Check(!state.Persistent && !state.Expanded, "Explicit dismissal clears persistent pinning");
            }
            finally { widget.Close(); }
        }
        private static void Call(object target, string method) { target.GetType().GetMethod(method, Private).Invoke(target, null); }
        internal static async Task RunAsync(string output)
        {
            var swipe = new TrackSwipe();
            swipe.Begin(new Point(100, 40)); swipe.Move(new Point(40, 42));
            Check(swipe.Command == "next" && swipe.Moved, "A left drag requests the next track");
            swipe.Begin(new Point(100, 40)); swipe.Move(new Point(160, 42));
            Check(swipe.Command == "previous", "A right drag requests the previous track");
            swipe.Begin(new Point(100, 40)); swipe.Move(new Point(103, 41));
            Check(!swipe.Moved && swipe.Command == null, "Small pointer jitter preserves an ordinary cover click");
            swipe.Move(new Point(125, 41));
            Check(swipe.Moved && swipe.Command == null, "A short drag consumes the click without skipping a track");
            swipe.Move(new Point(100, 40));
            Check(swipe.Moved && swipe.Command == null, "Dragging back to the origin cancels the skip without opening the application");
            swipe.Begin(new Point(100, 40)); swipe.Move(new Point(102, 62)); swipe.Move(new Point(165, 62));
            Check(swipe.Command == null, "A vertical drag cannot later become a track skip");
            swipe.Cancel(); swipe.Move(new Point(200, 40));
            Check(!swipe.Active && !swipe.Moved && swipe.Command == null, "Cancellation leaves no pending gesture or input polling");
            var state = new PanelState { Expanded = true, Latched = true, Persistent = true };
            state.SuspendForSettings();
            Check(!state.Expanded && !state.CanHover(false, false), "Settings suspend expansion and both hover paths");
            state.RestoreAfterSettings(true);
            Check(state.Expanded && state.Latched && state.Persistent, "Settings restore the previous persistent panel state");
            state.SuspendForSettings(); state.RestoreAfterSettings(false);
            Check(!state.Expanded && !state.Latched, "Suppression prevents restoration over a game or another desktop");
            var items = new[] { "paused", "playing", "newest" };
            Check(MediaSourceSelection<string>.Choose(items, null, null, "paused", x => x == "playing") == "playing", "Automatic selection prefers playback over stale Windows current without auto-pause");
            Check(MediaSourceSelection<string>.Choose(items, "paused", "newest", "playing", x => x != "paused") == "paused", "An explicit source remains selected when another starts");
            Check(MediaSourceSelection<string>.Choose(items, null, "newest", "playing", x => x != "paused") == "newest", "Automatic selection follows the most recently started playing source");
            var returning = new PlaybackReturn<string>();
            var pause = returning.Begin("music", "video", true, "track");
            returning.Complete(pause, true, new ReturnSnapshot { State = ReturnPlaybackState.Paused, Position = 5, CanPlay = true, Music = true });
            var sessions = new Dictionary<string, ReturnSnapshot> { { "music", new ReturnSnapshot { State = ReturnPlaybackState.Paused, Position = 5, CanPlay = true, Music = true } }, { "video", new ReturnSnapshot { State = ReturnPlaybackState.Paused } } };
            Check(returning.TakePause(sessions) == null && returning.TakePause(sessions, true) != null && returning.Count == 0, "Explicit return can consume owned music while a quiet video session remains open");
            Check(Enumerable.Range(1, 7).Where(x => x != 5).All(x => !NotificationPolicy.Allows(x)) && NotificationPolicy.Allows(5), "Mini notification permits only the public accepts-notifications state");
            using (var peek = new TrackPeek(Dispatcher.CurrentDispatcher))
            {
                peek.DurationSeconds = 2; peek.Observe("old", true, true); peek.Observe("new", true, true); peek.Hold();
                Check(peek.Active && !peek.ClockRunning, "Reading a mini notification pauses its clock");
                peek.Release(); Check(peek.Active && peek.ClockRunning, "Leaving a mini notification resumes its remaining clock");
                peek.Cancel(); Check(!peek.Active && !peek.ClockRunning, "Cancelled mini notifications leave no idle timer");
            }
            using (var sleep = new SleepPauseTimer(Dispatcher.CurrentDispatcher, _ => Task.FromResult(0)))
            { sleep.Start(15); sleep.Cancel(); Check(sleep.SelectedMinutes == 15 && !sleep.Active, "Sleep duration survives cancellation and settings reconstruction"); }
            MediaApplication app;
            Check(MediaAppLauncher.TryIdentify("Spotify.exe", out app) && app == MediaApplication.Spotify && !MediaAppLauncher.TryIdentify("https://malicious.invalid", out app), "Cover opening accepts known music identities rather than arbitrary metadata targets");
            var preferences = new WidgetSettings { EdgeOnly = true, AutoHide = true, PeekSeconds = 6, ShowTrayIcon = false, ScrollVolumeOnWholeIsland = false, RespectQuietState = false };
            var copy = preferences.Clone();
            Check(copy.EdgeOnly && copy.AutoHide && copy.DisplayMode == DisplayMode.Line, "Legacy overlapping preferences survive copying with a deterministic displayed mode");
            copy.DisplayMode = DisplayMode.Capsule; Check(!copy.EdgeOnly && !copy.AutoHide, "Choosing a display mode clears conflicting legacy flags");
            copy.DisplayMode = DisplayMode.DynamicIsland;
            Check(copy.FloatingIsland && !copy.EdgeOnly && !copy.AutoHide && copy.Clone().DisplayMode == DisplayMode.DynamicIsland,
                "Dynamic island is an independent copied display choice without legacy hide flags");
            string floatingPath = Path.Combine(output, "floating.settings"); copy.Save(floatingPath);
            Check(WidgetSettings.Load(floatingPath).DisplayMode == DisplayMode.DynamicIsland,
                "The floating island choice round-trips in versioned local settings");
            copy.DisplayMode = DisplayMode.Line;
            Check(!copy.FloatingIsland && copy.EdgeOnly, "Returning to an attached mode clears the floating choice");
            string path = Path.Combine(output, "lifecycle.settings"); preferences.Save(path); var restored = WidgetSettings.Load(path);
            Check(restored.PeekSeconds == 6 && !restored.ShowTrayIcon && !restored.ScrollVolumeOnWholeIsland && !restored.RespectQuietState, "New preferences round-trip with the local versioned settings");
            int exits = 0;
            var dialog = new SettingsWindow(new WidgetSettings(), new SettingsActions { Apply = _ => { }, SelectTheme = _ => true, ConfigureStartup = _ => true, ShelfCount = () => 1, Exit = () => exits++ });
            try
            {
                dialog.RequestExitConfirmation();
                Check(exits == 0 && Nodes((DependencyObject)dialog.Content).OfType<Button>().Any(x => x.Content as string == "Evet, Morf'tan çık"), "Nonempty shelf exit presents an explicit confirmation before exit");
                var cancel = Nodes((DependencyObject)dialog.Content).OfType<Button>().Single(x => x.Content as string == "Vazgeç");
                Check(cancel.IsDefault, "Exit confirmation defaults Enter to cancellation"); cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(exits == 0, "Cancelling shelf exit preserves the session"); dialog.RequestExitConfirmation();
                Nodes((DependencyObject)dialog.Content).OfType<Button>().Single(x => x.Content as string == "Evet, Morf'tan çık").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(exits == 1, "Only explicit confirmation invokes application exit");
                dialog.SelectPage(1); var page = (StackPanel)typeof(SettingsWindow).GetField("page", Private).GetValue(dialog);
                var advanced = Nodes(page).OfType<Expander>().Single();
                Check(advanced.Content == null, "Advanced controls are not created until expanded");
                advanced.IsExpanded = true; Check(advanced.Content != null, "Advanced controls are created on demand");
                foreach (int width in new[] { 240, 320, 440, 760 })
                {
                    dialog.SelectPage(0); dialog.ApplyResponsive(width);
                    var root = (FrameworkElement)dialog.Content; root.Width = width; root.Height = 640;
                    root.Measure(new Size(width, 640)); root.Arrange(new Rect(0, 0, width, 640)); root.UpdateLayout();
                    Check(Nodes(root).OfType<Viewbox>().All(box => box.ActualWidth <= ((FrameworkElement)box.Parent).ActualWidth + 1), "Settings preview fits its choice at " + width + " DIP");
                    var bitmap = new RenderTargetBitmap(width, 640, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var file = File.Create(Path.Combine(output, "responsive-" + width + ".png"))) encoder.Save(file);
                }
            }
            finally { dialog.Close(); }
            var small = new IslandWindow(new WidgetSettings { AutoPauseOthers = false });
            try
            {
                var media = (MediaController)typeof(IslandWindow).GetField("media", Private).GetValue(small);
                typeof(MediaController).GetProperty("Title", Private).SetValue(media, "Örnek parça", null); Call(small, "UpdateMedia");
                var shelf = (FileShelf)typeof(IslandWindow).GetField("shelf", Private).GetValue(small); shelf.Add(new[] { path }); Call(small, "UpdateShelf");
                typeof(IslandWindow).GetMethod("SetExpanded", Private).Invoke(small, new object[] { true });
                // This fixture checks the settled responsive panel, not an opening frame.
                typeof(IslandWindow).GetMethod("UpdateState", Private).Invoke(small, new object[] { false });
                foreach (double width in new[] { 240.0, 380.0 })
                {
                    typeof(IslandWindow).GetMethod("ApplyPlayerWidth", Private).Invoke(small, new object[] { width });
                    var viewport = (ScrollViewer)typeof(IslandWindow).GetField("panelViewport", Private).GetValue(small); viewport.MaxHeight = 190;
                    var root = (Border)typeof(IslandWindow).GetField("shell", Private).GetValue(small); root.Width = width; root.Height = 280;
                    root.Measure(new Size(width, 280)); root.Arrange(new Rect(0, 0, width, 280)); root.UpdateLayout();
                    var play = (Button)typeof(IslandWindow).GetField("play", Private).GetValue(small);
                    var playerContent = (StackPanel)typeof(IslandWindow).GetField("panel", Private).GetValue(small);
                    viewport.ScrollToVerticalOffset(play.TranslatePoint(new Point(), playerContent).Y); root.UpdateLayout();
                    var point = play.TranslatePoint(new Point(play.ActualWidth / 2, play.ActualHeight), root);
                    Check(Math.Abs(point.X - width / 2) < 1 && point.Y >= 0 && point.Y <= 280 && viewport.ScrollableHeight > 0,
                        "Constrained shelf panel keeps playback centered and reachable through native scrolling at " + width + " DIP");
                }
            }
            finally { small.Close(); }
            using (var hide = new TemporaryHide(Dispatcher.CurrentDispatcher))
            { hide.Start(); Check(hide.Active, "Temporary hide starts only on request"); hide.Cancel(); Check(!hide.Active, "Manual recovery cancels temporary hiding"); }
            var activated = new TaskCompletionSource<bool>(); string eventName = @"Local\WindowsWidgates.Checks.Activate." + Guid.NewGuid().ToString("N");
            using (var activation = new SingleInstanceActivation(() => activated.TrySetResult(true), eventName))
            {
                Check(SingleInstanceActivation.TryActivate(eventName) && await Task.WhenAny(activated.Task, Task.Delay(1500)) == activated.Task,
                    "Current-user activation event wakes an existing isolated instance without polling");
            }
        }
        private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
        { foreach (var node in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return node; foreach (var child in Nodes(node)) yield return child; } }
        private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
    }
}
