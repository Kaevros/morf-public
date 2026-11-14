using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Widgates
{
    // Opt-in instrumentation of this app's own visible window; no global input injection.
    internal static class PerformanceChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly StringBuilder Report = new StringBuilder();

        internal static async Task RunExpandedSteadyAsync(string output)
        {
            Log("OS: " + Environment.OSVersion.VersionString + "; runtime: " + Environment.Version + "; logical processors: " + Environment.ProcessorCount);
            Log("Expanded WPF test window; ten-second warm-up, 30-second sample; no player commands or saved preference changes; GPU not measured.");
            var window = new IslandWindow(new WidgetSettings { AutoPauseOthers = false, HideWhenIdle = false });
            try
            {
                typeof(IslandWindow).GetProperty("pinned", Private).SetValue(window, true);
                window.IsHitTestVisible = false; window.Show();
                await Task.Delay(1500);
                typeof(IslandWindow).GetMethod("SetExpanded", Private).Invoke(window, new object[] { true });
                await Task.Delay(10000);
                await MeasureAsync("Expanded / settled", window, 30);
            }
            finally { window.Close(); File.WriteAllText(Path.Combine(output, "expanded-steady.txt"), Report.ToString()); }
        }

        internal static async Task RunPlayerAsync(string output)
        {
            Log("OS: " + Environment.OSVersion.VersionString + "; runtime: " + Environment.Version + "; logical processors: " + Environment.ProcessorCount);
            Log("Visible player refinement benchmark; 20 seconds per phase; no real media commands, volume changes or startup writes; GPU not measured.");
            var settings = new WidgetSettings { AutoPauseOthers = false, HideWhenIdle = false };
            var window = new IslandWindow(settings);
            try
            {
                typeof(IslandWindow).GetProperty("pinned", Private).SetValue(window, true);
                window.IsHitTestVisible = false;
                window.Show();
                await Task.Delay(1500);
                var media = (MediaController)typeof(IslandWindow).GetField("media", Private).GetValue(window);
                string original = media.Title;
                var updateMedia = (Action)Delegate.CreateDelegate(typeof(Action), window, typeof(IslandWindow).GetMethod("UpdateMedia", Private));
                // Freeze only this test window's title rendering for a complete animation phase.
                // The controller still receives real source events; no player setting is changed.
                media.Changed -= updateMedia;
                typeof(MediaController).GetProperty("Title", Private).SetValue(media, "Uzun başlık ölçümü · görünür alanda kesintisiz kayan şarkı adı", null);
                typeof(IslandWindow).GetMethod("UpdateMedia", Private).Invoke(window, null);
                await Task.Delay(500);
                var marquee = (MarqueeText)typeof(IslandWindow).GetField("compactMarquee", Private).GetValue(window);
                Log("Compact animation running=" + marquee.Running);
                await MeasureAsync("Compact / long title", window, 20);
                Log("Compact animation at end=" + marquee.Running);
                if (media.Title == "Uzun başlık ölçümü · görünür alanda kesintisiz kayan şarkı adı")
                    typeof(MediaController).GetProperty("Title", Private).SetValue(media, original, null);
                media.Changed += updateMedia;
                typeof(IslandWindow).GetMethod("UpdateMedia", Private).Invoke(window, null);
                typeof(IslandWindow).GetMethod("SetExpanded", Private).Invoke(window, new object[] { true });
                await Task.Delay(500);
                var clock = (System.Windows.Threading.DispatcherTimer)typeof(IslandWindow).GetField("progress", Private).GetValue(window);
                Log("Expanded timeline clock running=" + clock.IsEnabled);
                await MeasureAsync("Expanded / timeline", window, 20);
                settings.EdgeOnly = true;
                typeof(IslandWindow).GetMethod("SetExpanded", Private).Invoke(window, new object[] { false });
                typeof(IslandWindow).GetMethod("ApplySettings", Private).Invoke(window, new object[] { false });
                Log("Line mode clocks: marquee=" + marquee.Running + "; timeline=" + clock.IsEnabled);
                await MeasureAsync("Line only / visual clocks stopped", window, 20);
            }
            finally { window.Close(); File.WriteAllText(Path.Combine(output, "player-performance.txt"), Report.ToString()); }
        }

        internal static async Task RunAsync(string output)
        {
            Log("OS: " + Environment.OSVersion.VersionString + "; runtime: " + Environment.Version
                + "; logical processors: " + Environment.ProcessorCount);
            Log("Real WPF window; no playback commands, clipboard writes or external file copies. GPU not measured.");
            var window = new IslandWindow(new WidgetSettings { HideWhenIdle = false, AutoPauseOthers = false });
            try
            {
                // Keep the benchmark stable without moving the user's pointer or taking focus.
                typeof(IslandWindow).GetProperty("pinned", Private).SetValue(window, true);
                window.IsHitTestVisible = false;
                window.Show();
                await Task.Delay(5000);
                await MeasureAsync("Compact / empty shelf", window);

                string source = Path.Combine(output, "benchmark-source.jpg");
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                    drawing.DrawRectangle(new LinearGradientBrush(Colors.SlateBlue, Colors.Coral, 45), null, new Rect(0, 0, 1920, 1080));
                var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(source)) encoder.Save(stream);
                var paths = new string[FileShelf.Capacity];
                for (int i = 0; i < paths.Length; i++)
                {
                    paths[i] = Path.Combine(output, "Photo-" + i + ".jpg");
                    File.Copy(source, paths[i], true);
                }
                var shelf = (FileShelf)typeof(IslandWindow).GetField("shelf", Private).GetValue(window);
                shelf.Add(paths);
                typeof(IslandWindow).GetMethod("UpdateShelf", Private).Invoke(window, null);
                typeof(IslandWindow).GetMethod("SetExpanded", Private).Invoke(window, new object[] { true });
                await Task.Delay(5000);
                await MeasureAsync("Expanded / 24 local 1920x1080 JPEGs", window);
            }
            finally
            {
                window.Close();
                File.WriteAllText(Path.Combine(output, "performance.txt"), Report.ToString());
            }
        }

        private static async Task MeasureAsync(string name, IslandWindow window, int seconds = 120)
        {
            var media = (MediaController)typeof(IslandWindow).GetField("media", Private).GetValue(window);
            string source = media.Active == null ? "none" : media.Active.SourceAppUserModelId;
            Log("BEGIN " + name + "; media ready=" + media.Ready + "; source=" + source + "; playing=" + media.IsPlaying);
            using (var process = Process.GetCurrentProcess())
            {
                process.Refresh();
                var startCpu = process.TotalProcessorTime;
                long startPrivate = process.PrivateMemorySize64;
                long startWorking = process.WorkingSet64;
                var watch = Stopwatch.StartNew();
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                watch.Stop();
                process.Refresh();
                double cpuSeconds = (process.TotalProcessorTime - startCpu).TotalSeconds;
                double corePercent = 100 * cpuSeconds / watch.Elapsed.TotalSeconds;
                Log(string.Format(CultureInfo.InvariantCulture,
                    "END {0}: seconds={1:F3}; CPU seconds={2:F4}; one-core CPU={3:F4}%; machine CPU={4:F4}%; private MiB={5:F2}->{6:F2}; working set MiB={7:F2}->{8:F2}; process lifetime peak working set MiB={9:F2}",
                    name, watch.Elapsed.TotalSeconds, cpuSeconds, corePercent, corePercent / Environment.ProcessorCount,
                    startPrivate / 1048576.0, process.PrivateMemorySize64 / 1048576.0,
                    startWorking / 1048576.0, process.WorkingSet64 / 1048576.0, process.PeakWorkingSet64 / 1048576.0));
                Log("END media: source=" + (media.Active == null ? "none" : media.Active.SourceAppUserModelId) + "; playing=" + media.IsPlaying);
            }
        }

        private static void Log(string message)
        {
            Console.WriteLine(message);
            Report.AppendLine(message);
        }
    }
}
