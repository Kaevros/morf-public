using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Widgates
{
    internal static class PlayerRefinementChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static async Task RunAsync(string output)
        {
            var settings = new WidgetSettings { EdgeOnly = true, ScrollTitles = false, ShowTimeline = false, OpenOnHover = false, AutoPauseOthers = false, HoverDelayMs = 0 };
            string path = Path.Combine(output, "refinement.settings");
            settings.Save(path);
            var loaded = WidgetSettings.Load(path);
            Check(loaded.EdgeOnly && !loaded.ScrollTitles && !loaded.ShowTimeline, "Line mode, title motion and timeline choices survive reload");
            Check(!new WidgetSettings().EdgeOnly && new WidgetSettings().ShowTimeline && new WidgetSettings().ScrollTitles,
                "Line mode is optional while timeline and long-title motion start enabled");
            var timeline = new MediaTimeline { Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(210), Position = TimeSpan.FromSeconds(40),
                MinimumSeek = TimeSpan.FromSeconds(20), MaximumSeek = TimeSpan.FromSeconds(200), Updated = DateTimeOffset.UtcNow, Rate = 2 };
            var now = timeline.Updated.AddSeconds(5);
            Check(timeline.Valid && timeline.Seekable && timeline.Duration == 200 && timeline.PositionAt(now, true) == 40,
                "Timeline accounts for nonzero start and playback speed from one event snapshot");
            Check(timeline.PositionAt(now, false) == 30, "Paused position does not advance from elapsed wall time");
            Check(timeline.PositionAt(now.AddHours(1), true) == 200, "Estimated progress never exceeds the content duration");
            Check(timeline.SeekTicks(-1000) == TimeSpan.FromSeconds(20).Ticks && timeline.SeekTicks(1000) == TimeSpan.FromSeconds(200).Ticks,
                "Seek commands clamp to both reported seek boundaries");
            bool rejected = false;
            try { timeline.SeekTicks(double.NaN); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !new MediaTimeline().Valid && !new MediaTimeline().Seekable, "Unknown durations and invalid seeks cannot become actionable");
            Check(MediaTimeline.Format(75) == "1:15" && MediaTimeline.Format(3675) == "1:01:15", "Elapsed and duration labels handle minutes and long content");
            var volume = new MemoryVolume();
            var state = await new SystemVolume(() => volume).SetLevelAsync(0.75f);
            Check(state.Level == 0.75f && !state.Muted, "Volume slider sets its chosen level without losing it through mute");

            var widget = new IslandWindow(settings);
            try
            {
                var controller = Field<MediaController>(widget, "media");
                typeof(MediaController).GetProperty("Title", Private).SetValue(controller, "Çok uzun bir şarkı adı; kompakt panelin genişliğini artırmamalı", null);
                Invoke(widget, "UpdateMedia");
                Invoke(widget, "UpdateState", false);
                Check(Field<TextBlock>(widget, "mediaNotice").Visibility == Visibility.Collapsed
                    && Field<TextBlock>(widget, "mediaTitle").ToolTip == null
                    && !ToolTipService.GetIsEnabled(Field<Button>(widget, "mediaApplicationButton"))
                    && !ToolTipService.GetIsEnabled(Field<Button>(widget, "next")),
                    "The player has no small status captions, song-title tooltip or transport tooltip chips");
                var shell = Field<Border>(widget, "shell");
                Check(shell.Width == 72 && shell.Height == 3 && Field<Grid>(widget, "compact").Visibility == Visibility.Collapsed,
                    "Line mode conceals active content in a 72 by 3 DIP recovery strip");
                Invoke(widget, "OnShellMouseEnter");
                Invoke(widget, "UpdateState", false);
                Check(Field<StackPanel>(widget, "panel").Visibility == Visibility.Collapsed,
                    "Invisible line-mode windows cannot expand from synthetic hover");
                Invoke(widget, "SetExpanded", true);
                Invoke(widget, "ClosePanel");
                Check(shell.Width == 72 && shell.Height == 3, "Closing the line-mode panel restores its minimal strip");
                settings.EdgeOnly = false;
                Invoke(widget, "ApplySettings", false);
                var marquee = Field<MarqueeText>(widget, "compactMarquee");
                Check(shell.Width == 180 && marquee.Width == 108 && marquee.ClipToBounds,
                    "Long titles keep the compact panel at 180 DIP inside a clipped text viewport");
                Check(!marquee.Running && !Field<System.Windows.Threading.DispatcherTimer>(widget, "progress").IsEnabled,
                    "Offscreen controls have no marquee or progress clock running");
                Check(Field<Canvas>(marquee, "strip").CacheMode == null,
                    "An inactive title marquee does not retain a bitmap rendering cache");
                foreach (SurfaceStyle style in Enum.GetValues(typeof(SurfaceStyle)))
                {
                    settings.Style = style;
                    var material = new MaterialSurface(); material.Update(settings, false);
                    Check(material.Background is SolidColorBrush && (style == SurfaceStyle.LiquidGlass
                        ? material.Highlight is LinearGradientBrush && material.Highlight.IsFrozen
                        : ReferenceEquals(material.Highlight, Brushes.Transparent)),
                        style + " keeps its intended material without animated visual effects");
                }
                Invoke(widget, "UpdateVolumeDisplay", new VolumeState { Available = true, Level = 0.42f, Muted = false });
                Check(Field<Slider>(widget, "volumeSlider").Value > 41 && Field<Slider>(widget, "volumeSlider").Value < 43
                    && Field<Button>(widget, "volumeButton").ToolTip == null,
                    "Volume feedback uses the inline themed control instead of a covering text tooltip");
                typeof(MediaController).GetProperty("Title", Private).SetValue(controller, "", null);
                Field<FileShelf>(widget, "shelf").Add(new[] { path });
                Invoke(widget, "UpdateShelf");
                Invoke(widget, "UpdateState", false);
                Check(shell.Width == 112 && marquee.Width == 62, "Files-only compact view keeps its count and cover inside the smaller capsule");
                var seek = Field<Slider>(widget, "seekSlider");
                seek.IsEnabled = true;
                seek.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Handled = true });
                Check(Field<bool>(widget, "scrubbing"), "The first seek click starts a gesture even after WPF class handling marks it handled");
                seek.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Handled = true });
                Check(!Field<bool>(widget, "scrubbing"), "A single seek release finishes the gesture without requiring a second click");
            }
            finally { widget.Close(); }
        }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static void Invoke(object owner, string name, params object[] args) { owner.GetType().GetMethod(name, Private | BindingFlags.DeclaredOnly).Invoke(owner, args); }
        private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
        private sealed class MemoryVolume : IVolumeEndpoint
        {
            private VolumeState state = new VolumeState { Available = true, Level = 0.5f, Muted = true };
            public VolumeState Read() { return state; }
            public void SetLevel(float level) { state.Level = level; }
            public void SetMute(bool muted) { state.Muted = muted; }
            public void Dispose() { }
        }
    }
}
