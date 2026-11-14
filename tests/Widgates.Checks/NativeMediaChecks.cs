using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Windows.Foundation;

namespace Widgates
{
    internal static class NativeMediaChecks
    {
        internal static async Task RunSeekAsync(string output)
        {
            var report = new StringBuilder();
            Action<string> log = text => { Console.WriteLine(text); report.AppendLine(text); };
            var window = new IslandWindow(new WidgetSettings { AutoPauseOthers = false, HideWhenIdle = false });
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Windows.Media.Control.GlobalSystemMediaTransportControlsSession source = null;
            MediaController media = (MediaController)typeof(IslandWindow).GetField("media", flags).GetValue(window);
            string originalTitle = null; double originalPosition = 0; DateTimeOffset started = DateTimeOffset.UtcNow;
            bool attempted = false;
            Exception failure = null;
            try
            {
                log("OS " + Environment.OSVersion.VersionString + "; opt-in LIVE Spotify seek using one routed WPF gesture; not a physical mouse test");
                window.IsHitTestVisible = false;
                typeof(IslandWindow).GetField("pinned", flags).SetValue(window, true);
                window.Show(); await Task.Delay(500);
                var candidates = media.Sessions.Where(item => MediaSourceNames.Friendly(item.SourceAppUserModelId) == "Spotify"
                    && item.GetPlaybackInfo().PlaybackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing).ToArray();
                if (candidates.Length != 1) throw new InvalidOperationException("Exactly one playing Spotify session is required.");
                source = candidates[0]; media.Select(source); await Task.Delay(250);
                var loader = (LatestMediaRequest<Windows.Media.Control.GlobalSystemMediaTransportControlsSession>)typeof(MediaController).GetField("metadata", flags).GetValue(media);
                if (await Task.WhenAny(loader.Completion, Task.Delay(6000)) != loader.Completion) throw new TimeoutException("Metadata did not settle.");
                var timeline = media.Timeline;
                if (timeline == null || !timeline.Seekable || !source.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled)
                    throw new InvalidOperationException("Spotify did not enable seek.");
                originalTitle = (await Complete(source.TryGetMediaPropertiesAsync())).Title;
                started = DateTimeOffset.UtcNow;
                originalPosition = timeline.PositionAt(started, true);
                double requested = originalPosition + 12 < timeline.Duration - 8 ? originalPosition + 12 : Math.Max(3, originalPosition - 12);
                requested = TimeSpan.FromTicks(timeline.SeekTicks(requested)).TotalSeconds - timeline.Start.TotalSeconds;
                typeof(IslandWindow).GetMethod("SetExpanded", flags).Invoke(window, new object[] { true });
                typeof(IslandWindow).GetMethod("UpdateState", flags).Invoke(window, new object[] { false });
                var slider = (Slider)typeof(IslandWindow).GetField("seekSlider", flags).GetValue(window);
                if (!slider.IsEnabled) throw new InvalidOperationException("The visible seek control was disabled.");
                log("LIVE title=" + originalTitle + "; cover=" + (media.Thumbnail != null) + "; from=" + originalPosition.ToString("F1") + "; requested=" + requested.ToString("F1"));
                // Emulate the class handler moving the thumb and marking the first click handled.
                slider.Value = requested;
                slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Handled = true });
                attempted = true;
                slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Handled = true });
                bool confirmed = false;
                for (int attempt = 0; attempt < 25; attempt++)
                {
                    await Task.Delay(120);
                    double actual = (source.GetTimelineProperties().Position - timeline.Start).TotalSeconds;
                    if (Math.Abs(actual - requested) < 5) { confirmed = true; log("Confirmed Spotify position=" + actual.ToString("F1")); break; }
                }
                if (!confirmed) throw new InvalidOperationException("Spotify did not confirm the requested seek from the single gesture. " + media.Notice);
                log("PASS A single handled WPF mouse-down/up gesture changes the live Spotify position");
            }
            catch (Exception error) { failure = error; }
            try
            {
                if (attempted && source != null && (await Complete(source.TryGetMediaPropertiesAsync())).Title == originalTitle)
                {
                    await media.CommandAsync("seek", originalPosition + (DateTimeOffset.UtcNow - started).TotalSeconds, source);
                    log("Restored the same track to its original position plus elapsed playback time");
                }
            }
            finally { window.Close(); File.WriteAllText(Path.Combine(output, "live-spotify-seek.txt"), report.ToString()); }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        internal static async Task RunAsync(string output)
        {
            var report = new StringBuilder();
            Action<string> log = text => { Console.WriteLine(text); report.AppendLine(text); };
            using (var media = new MediaController(Dispatcher.CurrentDispatcher) { AutoPauseOthers = false })
            {
                try
                {
                    log("OS " + Environment.OSVersion.VersionString + "; read-only Windows media/session comparison; no playback commands");
                    await media.ConnectAsync(); await Task.Delay(300);
                    log("Ready=" + media.Ready + "; session count=" + media.Sessions.Count);
                    foreach (var source in media.Sessions.ToArray())
                    {
                        media.Select(source); await Task.Delay(150);
                        var metadata = (LatestMediaRequest<Windows.Media.Control.GlobalSystemMediaTransportControlsSession>)typeof(MediaController)
                            .GetField("metadata", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(media);
                        if (await Task.WhenAny(metadata.Completion, Task.Delay(6000)) != metadata.Completion) throw new TimeoutException("Metadata did not settle.");
                        var direct = await Complete(source.TryGetMediaPropertiesAsync());
                        log("SOURCE " + source.SourceAppUserModelId + "; state=" + source.GetPlaybackInfo().PlaybackStatus);
                        log("Windows title=" + direct.Title + "; artist=" + direct.Artist + "; cover reference=" + (direct.Thumbnail != null));
                        log("Morf title=" + media.Title + "; artist=" + media.Artist + "; decoded cover=" + (media.Thumbnail != null));
                        if (direct.Title != media.Title || direct.Artist != media.Artist) throw new InvalidOperationException("Morf metadata differs from Windows.");
                        log("PASS Selected session metadata matches the current Windows snapshot");
                        var controls = source.GetPlaybackInfo().Controls;
                        log("Capabilities shuffle=" + controls.IsShuffleEnabled + "; repeat=" + controls.IsRepeatEnabled + "; seek=" + controls.IsPlaybackPositionEnabled);
                    }
                    if (media.Sessions.Count == 0) log("UNVERIFIED No live media source was published.");
                }
                finally { File.WriteAllText(Path.Combine(output, "native-media.txt"), report.ToString()); }
            }
        }
        private static Task<T> Complete<T>(IAsyncOperation<T> operation)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.Completed = (value, status) => { try { result.TrySetResult(value.GetResults()); } catch (Exception error) { result.TrySetException(error); } };
            return result.Task;
        }
    }
}
