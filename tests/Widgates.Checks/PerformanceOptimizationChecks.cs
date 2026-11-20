using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Widgates
{
    internal static class PerformanceOptimizationChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int RepeatedCovers = 2000;

        internal static async Task RunAsync()
        {
            var cache = new MediaArtworkCache();
            byte[] first = Cover(Colors.Coral), second = Cover(Colors.Indigo);
            var image = await Task.Run(() => cache.Decode(first));
            Check(image != null && image.IsFrozen && Math.Max(image.PixelWidth, image.PixelHeight) <= 192,
                "Cached artwork remains frozen, bounded and usable across threads");
            Check(ReferenceEquals(image, await Task.Run(() => cache.Decode((byte[])first.Clone()))),
                "Republished identical cover bytes reuse the decoded image");
            var changed = await Task.Run(() => cache.Decode(second));
            Check(changed != null && !ReferenceEquals(image, changed),
                "Changed artwork is refreshed even when the track text stays the same");
            Check(cache.Decode(new byte[] { 1, 2, 3 }) == null && cache.Decode(null) == null
                && cache.Decode(new byte[PreviewImage.MaximumBytes + 1]) == null,
                "Malformed, absent and oversized cover data never reuse unrelated artwork");
            Check(ReferenceEquals(changed, cache.Decode(second)) && !ReferenceEquals(image, cache.Decode(first)),
                "Artwork cache retains only the last successful cover and recovers after invalid data");

            var dispatcher = Dispatcher.CurrentDispatcher;
            var controller = new MediaController(dispatcher);
            var publishMetadata = (Action<string, string, string, string>)Delegate.CreateDelegate(typeof(Action<string, string, string, string>), controller,
                typeof(MediaController).GetMethod("PublishMetadataText", Private));
            Action<string, string, string> publish = (title, artist, identity) => publishMetadata(title, artist, identity, "");
            int notifications = 0;
            controller.Changed += () => notifications++;
            publish("Track", "Artist", "identity A");
            typeof(MediaController).GetProperty("Thumbnail", Private).SetValue(controller, image, null);
            publish("Track", "Artist", "identity A");
            Check(notifications == 1 && ReferenceEquals(controller.Thumbnail, image),
                "Identical metadata preserves artwork without another full UI update");
            publish("Track", "Artist", "identity B");
            Check(notifications == 2 && controller.TrackIdentity == "identity B" && controller.Thumbnail == null,
                "A new track identity clears old artwork even when its title and artist match");
            publish("Next track", "Artist", "identity C");
            Check(notifications == 3 && controller.Title == "Next track" && controller.Thumbnail == null,
                "The next track publishes text and clears old artwork before its cover arrives");
            typeof(MediaController).GetProperty("Notice", Private).SetValue(controller, "Temporary failure", null);
            publish("Next track", "Artist", "identity C");
            Check(notifications == 4 && controller.Notice == "", "An unchanged successful metadata read still clears a previous error");
            typeof(MediaController).GetProperty("Thumbnail", Private).SetValue(controller, image, null);
            publishMetadata("Next track", "Artist", "identity C", "New album");
            Check(notifications == 5 && controller.Album == "New album" && controller.Thumbnail == null,
                "An album-only update publishes the new detail and invalidates the previous cover");
            var queue = (Action)Delegate.CreateDelegate(typeof(Action), controller, typeof(MediaController).GetMethod("QueueRefresh", Private));
            int posted = 0;
            DispatcherHookEventHandler hook = (_, e) => { if (e.Operation.Priority == DispatcherPriority.Background) Interlocked.Increment(ref posted); };
            dispatcher.Hooks.OperationPosted += hook;
            try
            {
                // Block dispatcher execution until the producer burst is complete.
                Task.Run(() => Parallel.For(0, 2000, _ => queue())).GetAwaiter().GetResult();
                Check(posted == 1, "Two thousand cross-thread refresh requests post one UI callback");
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                var timer = (DispatcherTimer)typeof(MediaController).GetField("refresh", Private).GetValue(controller);
                Check(timer.IsEnabled, "A coalesced media event still schedules the session refresh");
                await Task.Delay(160);
                Check(!timer.IsEnabled, "The media refresh timer stops after its event instead of polling at idle");
                posted = 0; queue();
                Check(posted == 1, "A later media event can schedule another refresh");
                controller.Dispose();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                Check(!timer.IsEnabled, "A queued media event cannot restart the disposed controller");
            }
            finally { dispatcher.Hooks.OperationPosted -= hook; controller.Dispose(); }

            var settings = new WidgetSettings { Lightweight = LightweightMode.Never, AutoPauseOthers = false };
            var window = new IslandWindow(settings);
            var progress = (DispatcherTimer)typeof(IslandWindow).GetField("progress", Private).GetValue(window);
            var update = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), window, typeof(IslandWindow).GetMethod("UpdateProgressClock", Private));
            int ticks = 0;
            progress.Tick += (_, __) => ticks++;
            try
            {
                // Isolate scheduling from player availability and the display's visibility.
                // The product Tick handler stops offscreen visuals; restart only this test clock.
                update(true);
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 1600) { update(true); await Task.Delay(60); }
                Check(ticks > 0, "Frequent media refreshes cannot starve the one-second local progress tick");
                settings.Lightweight = LightweightMode.Always; update(true);
                Check(progress.Interval == TimeSpan.FromSeconds(2), "Power-saving progress cadence still switches to two seconds");
                update(false);
                Check(!progress.IsEnabled, "Stopping progress releases its timer after the scheduling optimization");
            }
            finally { window.Close(); }
        }

        internal static Task MeasureArtworkAsync(string output)
        {
            return Task.Run(() =>
            {
                byte[] bytes = Cover(Colors.Coral);
                var report = new System.Text.StringBuilder();
                report.AppendLine("OS " + Environment.OSVersion.VersionString + "; runtime " + Environment.Version);
                report.AppendLine("Synthetic 1920x1080 JPEG; " + RepeatedCovers + " repeated covers per path, 3 trials after warm-up. Decode/hash only; no WinRT, UI, GPU or installed-app measurement.");
                Func<BitmapSource> original = () => { using (var stream = new MemoryStream(bytes, false)) return PreviewImage.Decode(stream, 192); };
                var cache = new MediaArtworkCache();
                Func<BitmapSource> optimized = () => cache.Decode(bytes);
                for (int i = 0; i < 5; i++) { original(); optimized(); }
                for (int trial = 1; trial <= 3; trial++)
                {
                    // Alternate order to reduce a fixed warm-cache ordering bias.
                    if (trial % 2 == 1) { Measure(report, original, "original", trial); Measure(report, optimized, "cached", trial); }
                    else { Measure(report, optimized, "cached", trial); Measure(report, original, "original", trial); }
                }
                File.WriteAllText(Path.Combine(output, "artwork-performance.txt"), report.ToString());
                Console.Write(report);
            });
        }

        private static void Measure(System.Text.StringBuilder report, Func<BitmapSource> decode, string label, int trial)
        {
            using (var process = Process.GetCurrentProcess())
            {
                var cpu = process.TotalProcessorTime;
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < RepeatedCovers; i++) { var image = decode(); if (image == null) throw new InvalidOperationException("Benchmark decode failed"); }
                watch.Stop(); process.Refresh();
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "Trial {0} {1}: wall_ms={2:F3}; cpu_ms={3:F3}",
                    trial, label, watch.Elapsed.TotalMilliseconds, (process.TotalProcessorTime - cpu).TotalMilliseconds));
            }
        }

        private static byte[] Cover(Color color)
        {
            int width = 1920, height = 1080;
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = (byte)(color.R * (i / 4 % width) / width); pixels[i + 3] = 255; }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream()) { encoder.Save(stream); return stream.ToArray(); }
        }
        private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
    }
}
