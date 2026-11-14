using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Widgates
{
    internal static class NativeLayoutChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly StringBuilder Report = new StringBuilder();

        internal static async Task RunAsync(string output)
        {
            var settings = new WidgetSettings { HideWhenIdle = false, HideInFullscreen = false, HideInGames = false, AutoPauseOthers = false };
            var widget = new IslandWindow(settings);
            try
            {
                Log("OS " + Environment.OSVersion.VersionString + "; live WPF geometry checks; no media commands or OS setting changes");
                widget.IsHitTestVisible = false;
                widget.Show();
                await Task.Delay(600);
                var placement = (DesktopPlacement)typeof(IslandWindow).GetField("placement", Private).GetValue(widget);
                var media = (MediaController)typeof(IslandWindow).GetField("media", Private).GetValue(widget);
                Log("Media ready=" + media.Ready + "; source=" + (media.Active == null ? "none" : media.Active.SourceAppUserModelId)
                    + "; playing=" + media.IsPlaying);
                Log("Player capabilities: shuffle=" + (media.Playback != null && media.Playback.Controls.IsShuffleEnabled)
                    + "; repeat=" + (media.Playback != null && media.Playback.Controls.IsRepeatEnabled)
                    + "; seek=" + (media.Playback != null && media.Playback.Controls.IsPlaybackPositionEnabled)
                    + "; timeline=" + (media.Timeline != null && media.Timeline.Valid));
                Log("Return capabilities: type=" + (media.Playback == null ? "none" : media.Playback.PlaybackType.ToString())
                    + "; explicit Play=" + (media.Playback != null && media.Playback.Controls.IsPlayEnabled)
                    + "; explicit Pause=" + (media.Playback != null && media.Playback.Controls.IsPauseEnabled));
                Log("Automatic edge=" + placement.Edge + "; hotkey registered=" + placement.HotkeyRegistered);
                IntPtr ownHandle = ((HwndSource)PresentationSource.FromVisual(widget)).Handle;
                var desktop = await new VirtualDesktopLink().RefreshAsync(ownHandle, ownHandle, false);
                Log("Desktop query: available=" + desktop.Available + "; current=" + desktop.OnCurrentDesktop + "; failure=" + desktop.Failure);
                if (!desktop.Available || !desktop.OnCurrentDesktop) throw new InvalidOperationException("Public virtual-desktop query failed");
                Log("PASS Public desktop manager recognizes the current widget window");
                bool probeWorked = await Task.Run(() =>
                {
                    using (var bridge = new WindowsDesktopBridge())
                    {
                        Guid id = bridge.ProbeCurrentDesktop();
                        int probeStatus = bridge.LastResult;
                        Guid ownerId;
                        bool ownerIdRead = bridge.DesktopId(ownHandle, out ownerId);
                        bool moved = id != Guid.Empty && bridge.Move(ownHandle, id);
                        Log("Desktop probe: valid=" + (id != Guid.Empty) + "; HRESULT=" + probeStatus.ToString("X8")
                            + "; owner ID valid=" + (ownerIdRead && ownerId != Guid.Empty) + "; move=" + moved + "; HRESULT=" + bridge.LastResult.ToString("X8"));
                        return moved;
                    }
                });
                if (!probeWorked) throw new InvalidOperationException("Non-activating desktop probe or own-window move failed");
                Log("PASS Non-activating desktop probe and own-window move to the existing desktop");
                var outputVolume = await new SystemVolume().ReadAsync();
                Log("Read-only Windows volume query: available=" + outputVolume.Available + "; no level or mute change");
                settings.HideInGames = true;
                typeof(DesktopPlacement).GetField("gameActive", Private).SetValue(placement, true);
                typeof(IslandWindow).GetProperty("pinned", Private).SetValue(widget, true);
                typeof(IslandWindow).GetMethod("UpdateVisibility", Private).Invoke(widget, null);
                if (widget.IsVisible) throw new InvalidOperationException("Game suppression failed to hide a pinned widget");
                typeof(IslandWindow).GetMethod("OpenPinned", Private).Invoke(widget, null);
                if (widget.IsVisible) throw new InvalidOperationException("Shortcut path opened the widget over a game");
                Log("PASS Simulated game state hides the widget and blocks its pinned opening");
                settings.HideInGames = false;
                typeof(DesktopPlacement).GetField("gameActive", Private).SetValue(placement, false);
                typeof(IslandWindow).GetMethod("UpdateVisibility", Private).Invoke(widget, null);
                if (!widget.IsVisible) throw new InvalidOperationException("Game suppression did not recover");
                Log("PASS Leaving simulated game state restores the widget");
                placement.EnsureTopmost();
                int extendedStyle = (int)typeof(DesktopPlacement).GetMethod("GetWindowLong", Static).Invoke(null, new object[] { ownHandle, -20 });
                if ((extendedStyle & 8) == 0) throw new InvalidOperationException("Windows did not retain the widget's topmost style");
                Log("PASS Windows reports the visible widget as topmost after game suppression recovers");
                typeof(DesktopPlacement).GetField("<RuleHidden>k__BackingField", Private).SetValue(placement, true);
                typeof(IslandWindow).GetMethod("UpdateVisibility", Private).Invoke(widget, null);
                typeof(IslandWindow).GetMethod("OpenPinned", Private).Invoke(widget, null);
                if (widget.IsVisible) throw new InvalidOperationException("Application hide rule allowed a pinned opening");
                Log("PASS Simulated application hide rule blocks visibility and shortcut opening");
                typeof(DesktopPlacement).GetField("<RuleHidden>k__BackingField", Private).SetValue(placement, false);
                settings.HideInGames = true;
                typeof(DesktopPlacement).GetField("gameActive", Private).SetValue(placement, true);
                typeof(DesktopPlacement).GetField("<RuleShown>k__BackingField", Private).SetValue(placement, true);
                typeof(IslandWindow).GetMethod("UpdateVisibility", Private).Invoke(widget, null);
                if (!widget.IsVisible) throw new InvalidOperationException("Application show rule did not override game suppression");
                Log("PASS Simulated application show rule overrides generic game suppression");
                settings.HideInGames = false;
                typeof(DesktopPlacement).GetField("gameActive", Private).SetValue(placement, false);
                typeof(DesktopPlacement).GetField("<RuleShown>k__BackingField", Private).SetValue(placement, false);
                placement.MonitorDevice = "missing-feature-test-display"; placement.Reposition();
                Verify(widget, placement.Edge, "Missing selected display falls back to primary");
                placement.MonitorDevice = ""; placement.Reposition();
                Verify(widget, placement.Edge, "Automatic taskbar placement");
                foreach (var preference in new[] { DockPreference.Bottom, DockPreference.Top })
                {
                    settings.Dock = preference;
                    typeof(IslandWindow).GetMethod("ApplySettings", Private).Invoke(widget, new object[] { false });
                    await Task.Delay(400);
                    Verify(widget, placement.Edge, "Manual " + preference + " collapsed");
                    typeof(IslandWindow).GetMethod("SetExpanded", Private).Invoke(widget, new object[] { true });
                    await Task.Delay(400);
                    Verify(widget, placement.Edge, "Manual " + preference + " expanded");
                    var launcher = (Button)typeof(IslandWindow).GetField("launchButton", Private).GetValue(widget);
                    launcher.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    await Task.Delay(80);
                    Verify(widget, placement.Edge, "Manual " + preference + " with application launcher");
                    typeof(IslandWindow).GetMethod("SetExpanded", Private).Invoke(widget, new object[] { false });
                    settings.AutoHide = true;
                    typeof(IslandWindow).GetMethod("ApplySettings", Private).Invoke(widget, new object[] { false });
                    await Task.Delay(400);
                    Verify(widget, placement.Edge, "Manual " + preference + " hidden");
                    settings.AutoHide = false;
                }
                settings.EdgeOnly = true;
                typeof(IslandWindow).GetMethod("ApplySettings", Private).Invoke(widget, new object[] { false });
                var clock = (System.Windows.Threading.DispatcherTimer)typeof(IslandWindow).GetField("progress", Private).GetValue(widget);
                var marquee = (MarqueeText)typeof(IslandWindow).GetField("compactMarquee", Private).GetValue(widget);
                if (clock.IsEnabled || marquee.Running) throw new InvalidOperationException("Line mode left a visual clock running");
                Log("PASS Line mode stops progress updates and title animation with live media connected");
                settings.EdgeOnly = false;
                // Pin only this test window's model so a pending pointer-leave event cannot close this scenario.
                typeof(IslandWindow).GetProperty("pinned", Private).SetValue(widget, true);
                typeof(IslandWindow).GetMethod("SetExpanded", Private).Invoke(widget, new object[] { true });
                await Task.Delay(300);
                if (media.IsPlaying && media.Timeline != null && media.Timeline.Valid)
                {
                    if (!clock.IsEnabled) throw new InvalidOperationException("Visible playing timeline did not start");
                    Log("PASS Visible playing timeline starts the local progress display clock");
                }
                settings.ShowTimeline = false;
                typeof(IslandWindow).GetMethod("ApplySettings", Private).Invoke(widget, new object[] { false });
                if (clock.IsEnabled) throw new InvalidOperationException("Disabled timeline left its clock running");
                Log("PASS Disabling timeline stops its display clock");
                var power = (PowerStateMonitor)typeof(IslandWindow).GetField("power", Private).GetValue(widget);
                Log("Power notification registration available=" + power.NotificationsAvailable);
                power.State.Locked = true;
                typeof(IslandWindow).GetMethod("UpdateState", Private).Invoke(widget, new object[] { false });
                if (clock.IsEnabled || marquee.Running) throw new InvalidOperationException("Locked power state left visual clocks running");
                Log("PASS Simulated session lock stops visual clocks with native media connected");
                power.State.Locked = false; power.State.DisplayOn = false;
                typeof(IslandWindow).GetMethod("UpdateState", Private).Invoke(widget, new object[] { false });
                if (clock.IsEnabled || marquee.Running) throw new InvalidOperationException("Display-off power state left visual clocks running");
                Log("PASS Simulated display off stops visual clocks");
                power.State.DisplayOn = true;
                // Synthetic track data only; no real Play/Pause is sent by these native peek checks.
                media.Changed -= (Action)Delegate.CreateDelegate(typeof(Action), widget, typeof(IslandWindow).GetMethod("UpdateMedia", Private));
                typeof(MediaController).GetProperty("Title", Private).SetValue(media, "Yeni parça · Geceye bir şarkı", null);
                typeof(MediaController).GetProperty("Artist", Private).SetValue(media, "Örnek sanatçı", null);
                settings.EdgeOnly = true;
                typeof(IslandWindow).GetProperty("pinned", Private).SetValue(widget, false);
                typeof(IslandWindow).GetMethod("SetExpanded", Private).Invoke(widget, new object[] { false });
                var peek = (TrackPeek)typeof(IslandWindow).GetField("peek", Private).GetValue(widget);
                foreach (var preference in new[] { DockPreference.Top, DockPreference.Bottom })
                {
                    peek.Cancel(false); settings.Dock = preference;
                    typeof(IslandWindow).GetMethod("ApplySettings", Private).Invoke(widget, new object[] { false });
                    peek.Observe("native-baseline-" + preference, false, false); peek.Observe("native-track-" + preference, true, true);
                    await Task.Delay(300);
                    var shell = (Border)typeof(IslandWindow).GetField("shell", Private).GetValue(widget);
                    if (!peek.Active || shell.ActualHeight < 47 || shell.ActualWidth < 239) throw new InvalidOperationException("Peek did not reveal its mini capsule");
                    var artist = (TextBlock)typeof(IslandWindow).GetField("compactArtist", Private).GetValue(widget);
                    if (artist.Visibility != Visibility.Visible || artist.Text != "Örnek sanatçı") throw new InvalidOperationException("Peek artist is missing");
                    Verify(widget, placement.Edge, "Synthetic track peek " + preference);
                    foreach (var theme in new[] { ThemePreference.Dark, ThemePreference.Light })
                    {
                        typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, theme);
                        ThemePalette.Refresh(); typeof(IslandWindow).GetMethod("UpdateState", Private).Invoke(widget, new object[] { false });
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(shell.ActualWidth), (int)Math.Ceiling(shell.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        var drawing = new DrawingVisual(); using (var canvas = drawing.RenderOpen()) canvas.DrawRectangle(new VisualBrush(shell), null, new Rect(0, 0, shell.ActualWidth, shell.ActualHeight));
                        bitmap.Render(drawing); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var stream = File.Create(Path.Combine(output, "peek-" + theme + "-" + preference + ".png"))) png.Save(stream);
                    }
                }
                await Task.Delay(4250);
                if (peek.Active || peek.ClockRunning) throw new InvalidOperationException("Native peek expiry did not stop");
                Verify(widget, placement.Edge, "Expired peek returns to the attached line");
                peek.Observe("native-blocked-track", true, true);
                settings.HideInGames = true;
                typeof(DesktopPlacement).GetField("gameActive", Private).SetValue(placement, true);
                typeof(IslandWindow).GetMethod("UpdateVisibility", Private).Invoke(widget, null);
                if (peek.Active || peek.ClockRunning || widget.IsVisible) throw new InvalidOperationException("Game hiding failed during peek");
                Log("PASS Simulated game state cancels an active peek and its clock");
            }
            finally
            {
                widget.Close();
                File.WriteAllText(Path.Combine(output, "native-layout.txt"), Report.ToString());
            }
        }

        private static void Verify(IslandWindow widget, DockEdge edge, string label)
        {
            if (!widget.IsVisible) throw new InvalidOperationException(label + ": test window was not visible");
            var monitorArgs = new object[] { null };
            if (!(bool)typeof(DesktopPlacement).GetMethod("TryGetPrimaryMonitor", Static).Invoke(null, monitorArgs))
                throw new InvalidOperationException("Cannot read primary monitor bounds");
            var monitor = monitorArgs[0];
            var bounds = monitor.GetType().GetField("Bounds").GetValue(monitor);
            var source = (HwndSource)PresentationSource.FromVisual(widget);
            var windowArgs = new object[] { source.Handle, null };
            if (!(bool)typeof(DesktopPlacement).GetMethod("GetWindowRect", Static).Invoke(null, windowArgs))
                throw new InvalidOperationException("Cannot read the test window bounds");
            var window = windowArgs[1];
            int top = (int)window.GetType().GetField("Top").GetValue(window);
            int bottom = (int)window.GetType().GetField("Bottom").GetValue(window);
            int expected = (int)bounds.GetType().GetField(edge == DockEdge.Top ? "Top" : "Bottom").GetValue(bounds);
            int actual = edge == DockEdge.Top ? top : bottom;
            if (actual != expected) throw new InvalidOperationException(label + ": edge gap " + (actual - expected));
            var shell = (Border)typeof(IslandWindow).GetField("shell", Private).GetValue(widget);
            if ((edge == DockEdge.Top ? shell.BorderThickness.Top : shell.BorderThickness.Bottom) != 0)
                throw new InvalidOperationException(label + ": attached edge retained an outline");
            Log("PASS " + label + "; edge=" + edge + "; physical top=" + top + "; bottom=" + bottom
                + "; shell height DIP=" + shell.ActualHeight);
        }

        private static void Log(string message)
        {
            Console.WriteLine(message);
            Report.AppendLine(message);
        }
    }
}
