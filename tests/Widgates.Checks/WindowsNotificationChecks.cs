using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Widgates
{
    internal static class WindowsNotificationChecks
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        internal static void Run(string output)
        {
            var moment = DateTimeOffset.UtcNow;
            var first = AppMessage.Create(1, moment, new[] { "Başlık", "İçerik" }, "App.One!App", "Birinci uygulama");
            var other = AppMessage.Create(1, moment, new[] { "Başlık", "İçerik" }, "App.Two!App", "İkinci uygulama");
            Check(first.AppId == "App.One!App" && first.AppName == "Birinci uygulama" && first.Key != other.Key,
                "Windows toasts retain their originating app and do not collide across sources");
            var tracker = new AppMessageTracker();
            tracker.Reset(moment);
            var old = AppMessage.Create(3, moment.AddMinutes(-1), new[] { "Eski", "Merkezde bekleyen" }, "App.One!App");
            var delayed = tracker.Observe(new[] { old, first });
            Check(delayed.Length == 1 && delayed[0] == first,
                "A delayed first read preserves notifications received since connection without replaying history");
            Check(tracker.Observe(new[] { old, first }).Length == 0,
                "Recovered snapshots do not display the same notification twice");
            using (var peek = new AppMessagePeek(Dispatcher.CurrentDispatcher, () => moment))
            {
                peek.Offer(first, true); peek.Offer(other, true);
                Check(peek.Current == first && peek.Queued == 1, "Equal titles from different apps are not merged as one conversation");
                var update = AppMessage.Create(2, moment, new[] { "Başlık", "Güncelleme" }, first.AppId, first.AppName);
                peek.Offer(update, true); Check(peek.Current == update && peek.Queued == 1, "Only the same app and title coalesce without growing the queue");
            }
            string path = Path.Combine(output, "legacy-notifications.settings");
            File.WriteAllText(path, "WhatsAppNotifications=True\n");
            Check(WidgetSettings.Load(path).WindowsNotifications, "The former WhatsApp opt-in migrates to Windows notification mirroring");
            File.WriteAllText(path, "WindowsNotifications=False\nWhatsAppNotifications=True\n");
            Check(!WidgetSettings.Load(path).WindowsNotifications, "An explicit new master preference wins over the legacy flag");
            var settings = new WidgetSettings { WindowsNotifications = true }; settings.Save(path);
            Check(WidgetSettings.Load(path).WindowsNotifications && !File.ReadAllText(path).Contains("WhatsAppNotifications="),
                "New settings store one notification master switch");
            Check(MediaAppLauncher.ValidNotificationAppId("App.One_123!App") && !MediaAppLauncher.ValidNotificationAppId("C:\\app.exe")
                && !MediaAppLauncher.ValidNotificationAppId("https://example.test") && !MediaAppLauncher.ValidNotificationAppId("App!App --argument"),
                "Notification activation accepts a registered app identity and rejects paths, links and arguments");
        }
        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; synthetic Windows toasts from multiple sources; no real Windows notification removal or permission request");
            var settings = new WidgetSettings { HideWhenIdle = false, HideInGames = false, AutoPauseOthers = false, RespectQuietState = false, NotificationSeconds = 8 };
            var widget = new IslandWindow(settings);
            try
            {
                widget.IsHitTestVisible = false; widget.Show(); await Task.Delay(350);
                var shell = Field<Border>(widget, "shell"); var peek = Field<AppMessagePeek>(widget, "messages");
                foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
                {
                    typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, theme); ThemePalette.Refresh();
                    peek.Cancel(false);
                    peek.Offer(new AppMessage { Key = "short:" + theme, AppId = "Sample.App!App", AppName = "Takvim", Sender = "Toplantı", Body = "5 dakika kaldı", Created = DateTimeOffset.UtcNow, Sample = true }, true);
                    await Task.Delay(320); widget.UpdateLayout(); peek.Hold();
                    double shortWidth = shell.ActualWidth;
                    Check(shortWidth < 300 && shell.ActualHeight <= 56.5 && shell.Margin.Top == 8, theme + " short toast fits its content without a fixed wide background; active=" + peek.Active + "; size=" + shortWidth + "x" + shell.ActualHeight + "; margin=" + shell.Margin.Top);
                    Check(!shell.HasAnimatedProperties && !Field<Grid>(widget, "messageView").HasAnimatedProperties,
                        theme + " visible toast releases its completed size, inset and opacity clocks");
                    Capture(shell, Path.Combine(output, "compact-" + theme + ".png"));
                    peek.Current.Body = new string('W', 350); Call(widget, "UpdateState", false); widget.UpdateLayout();
                    var body = Field<TextBlock>(widget, "messageBody");
                    Check(shell.ActualWidth <= 360.5 && shell.ActualWidth > shortWidth && body.TextTrimming == TextTrimming.None && body.TextWrapping == TextWrapping.Wrap && shell.ActualHeight > 56,
                        theme + " long toast wraps and expands within screen bounds");
                    Capture(shell, Path.Combine(output, "long-" + theme + ".png"));
                    peek.Current.Sample = false;
                    settings.WindowsNotifications = true; Call(widget, "ApplySettings", false);
                    settings.WindowsNotifications = false; Call(widget, "ApplySettings", false); await Task.Delay(250);
                    Check(!peek.Active && !peek.ClockRunning && Field<TextBlock>(widget, "messageBody").Text == "",
                        theme + " disabling mirroring clears Morf presentation and stops its clock");
                }
                settings.Lightweight = LightweightMode.Always; Call(widget, "ApplySettings", false);
                peek.Offer(new AppMessage { Key = "swipe-one", AppName = "Bir", Sender = "Bir", Body = "İlk", Created = DateTimeOffset.UtcNow, Sample = true }, true);
                peek.Offer(new AppMessage { Key = "swipe-two", AppName = "İki", Sender = "İki", Body = "Sonraki", Created = DateTimeOffset.UtcNow, Sample = true }, true);
                typeof(IslandWindow).GetField("notificationDragKey", Private).SetValue(widget, "swipe-one");
                Call(widget, "DismissNotificationSwipe", -80d);
                Check(peek.Active && peek.Current.Key == "swipe-two", "Swipe dismisses only the current notification and retains the queued one with reduced motion");
            }
            finally { widget.Close(); }
        }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static void Call(object owner, string name, params object[] args) { owner.GetType().GetMethod(name, Private).Invoke(owner, args); }
        private static void Check(bool success, string text) { if (!success) throw new InvalidOperationException(text); Console.WriteLine("PASS " + text); }
        private static void Capture(FrameworkElement element, string path)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight)); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
