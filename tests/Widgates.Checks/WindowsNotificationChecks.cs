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
            Check(first.DisplayTitle(true) == "Başlık" && first.DisplayBody(true) == "İçerik"
                && new AppMessage { AppName = "Takvim", Sender = "Toplantı" }.DisplayBody(true) == "",
                "Visible notification text omits the app prefix and never substitutes an empty body with the app name");
            var music = AppMessage.Create(4, moment, new[] { "NORMAL", "Da Poet & Kayra — NORMAL DELUXE" }, "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "Apple Music");
            Check(MediaNotification.Matches(music, music.AppId, "NORMAL", "Da Poet & Kayra", "NORMAL DELUXE", "track-one")
                && !MediaNotification.Matches(music, "Spotify.exe", "NORMAL", "Da Poet & Kayra", "", "track-one")
                && !MediaNotification.Matches(music, music.AppId, "NORMAL", "Other artist", "", "track-one")
                && !MediaNotification.Matches(music, music.AppId, "Next track", "Da Poet & Kayra", "", "track-two"),
                "A cover requires the same media source, title and artist rather than a matching app display name");
            music.MediaTrackIdentity = "track-one";
            Check(!MediaNotification.Matches(music, music.AppId, "NORMAL", "Da Poet & Kayra", "", "track-two")
                && MediaNotification.SameSource("Spotify.exe", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")
                && !MediaNotification.SameSource("fake.Spotify.exe", "Spotify.exe")
                && !MediaNotification.SameSource("", "")
                && MediaNotification.Detail("Artist", "Album") == "Artist · Album" && MediaNotification.Detail("", "") == "",
                "Track identity rejects stale artwork and only explicit desktop/package aliases share a media source");
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
            using (var tracks = new AppMessagePeek(Dispatcher.CurrentDispatcher, () => moment))
            {
                var songOne = AppMessage.Create(50, moment, new[] { "First song", "Artist" }, "Spotify.exe"); songOne.MediaTrackIdentity = "first";
                var songTwo = AppMessage.Create(51, moment, new[] { "Second song", "Artist" }, "Spotify.exe"); songTwo.MediaTrackIdentity = "second";
                tracks.Offer(songOne, true); tracks.Offer(other, true); tracks.Offer(songTwo, true);
                Check(tracks.Current == songTwo && tracks.Queued == 1,
                    "Rapid music changes update the current media toast without queuing obsolete tracks or dropping another app's notification");
                tracks.Filter(x => x.MediaTrackIdentity == null, false);
                Check(tracks.Current == other && tracks.Queued == 0 && tracks.ClockRunning,
                    "Consuming a media preview when opening the hub preserves the next app's notification without a quiet interval");
            }
            string path = Path.Combine(output, "legacy-notifications.settings");
            File.WriteAllText(path, "WhatsAppNotifications=True\n");
            Check(WidgetSettings.Load(path).WindowsNotifications, "The former WhatsApp opt-in migrates to Windows notification mirroring");
            File.WriteAllText(path, "WindowsNotifications=False\nWhatsAppNotifications=True\n");
            Check(!WidgetSettings.Load(path).WindowsNotifications, "An explicit new master preference wins over the legacy flag");
            var settings = new WidgetSettings { WindowsNotifications = true }; settings.Save(path);
            Check(WidgetSettings.Load(path).WindowsNotifications && !File.ReadAllText(path).Contains("WhatsAppNotifications="),
                "New settings store one notification master switch");
            Check(MediaAppLauncher.ValidNotificationAppId("App.One_123!App") && MediaAppLauncher.ValidNotificationAppId("Spotify")
                && MediaAppLauncher.ValidNotificationAppId("Microsoft.AutoGenerated.App") && !MediaAppLauncher.ValidNotificationAppId("..")
                && MediaAppLauncher.ValidNotificationAppId("Microsoft.AutoGenerated.{1BEB6467-F4A7-16AE-72BE-F427253BD6FC}")
                && !MediaAppLauncher.ValidNotificationAppId("Microsoft.AutoGenerated.{invalid}")
                && !MediaAppLauncher.ValidNotificationAppId("C:\\app.exe")
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
                    Check(shortWidth < 300 && shell.ActualHeight <= 66.5 && shell.Margin.Top == 8, theme + " short toast fits its content without a fixed wide background; active=" + peek.Active + "; size=" + shortWidth + "x" + shell.ActualHeight + "; margin=" + shell.Margin.Top);
                    Check(!shell.HasAnimatedProperties && !Field<Grid>(widget, "messageView").HasAnimatedProperties,
                        theme + " visible toast releases its completed size, inset and opacity clocks");
                    Capture(shell, Path.Combine(output, "compact-" + theme + ".png"));
                    peek.Current.Body = new string('W', 350); Call(widget, "UpdateState", false); widget.UpdateLayout();
                    var body = Field<TextBlock>(widget, "messageBody");
                    Check(shell.ActualWidth <= 380.5 && shell.ActualWidth > shortWidth && body.TextTrimming == TextTrimming.None && body.TextWrapping == TextWrapping.Wrap && shell.ActualHeight > 64,
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
                peek.Cancel(false);
                settings.WindowsNotifications = true; Call(widget, "ApplySettings", false);
                var media = Field<MediaController>(widget, "media");
                media.Dispose(); // Synthetic metadata only; prevent live sources from changing this scene.
                Set(media, "SourceId", "Spotify.exe"); Set(media, "Title", "NORMAL");
                Set(media, "Artist", "Da Poet & Kayra"); Set(media, "Album", "NORMAL DELUXE"); Set(media, "TrackIdentity", "spotify:one");
                var cover = new DrawingImage(new GeometryDrawing(Brushes.Coral, null, new RectangleGeometry(new Rect(0, 0, 40, 40)))); cover.Freeze();
                Set(media, "Thumbnail", cover); Call(widget, "UpdateMedia");
                var song = AppMessage.Create(41, DateTimeOffset.UtcNow, new[] { "NORMAL", "Da Poet & Kayra" }, "Spotify.exe", "Spotify");
                Call(widget, "OfferNotification", song); widget.UpdateLayout();
                Check(peek.Active && ReferenceEquals(Field<Image>(widget, "messageAppIcon").Source, cover)
                    && Field<TextBlock>(widget, "messageSender").Text == "NORMAL"
                    && Field<TextBlock>(widget, "messageBody").Text == "Da Poet & Kayra · NORMAL DELUXE",
                    "A Spotify toast uses matched track artwork, title, artist and album even when the toast omits the album");
                Capture(shell, Path.Combine(output, "music-cover.png"));
                settings.Lightweight = LightweightMode.Never; Call(widget, "ApplySettings", false);
                Set(media, "Title", "Uzun bir şarkı adı ve çok satıra yayılan canlı konser kaydı");
                Set(media, "Album", "Uzun Albüm Adıyla Yayımlanan Özel Kayıtlar — Genişletilmiş Sürüm");
                Call(widget, "UpdateMedia");
                peek.Cancel(false);
                var longTrack = AppMessage.Create(45, DateTimeOffset.UtcNow,
                    new[] { media.Title, media.Artist }, "Spotify.exe", "Spotify");
                // The same media identity has already been shown, so start the visual
                // fixture directly and exercise the live notification rendering path.
                peek.Offer(longTrack, true); widget.UpdateLayout();
                var trackTitle = Field<TextBlock>(widget, "messageSender");
                var trackBody = Field<ScrollViewer>(widget, "messageScroll");
                Check(trackTitle.ActualHeight > 18 && trackBody.ActualHeight > 16 && trackBody.ScrollableHeight == 0
                    && trackBody.TranslatePoint(new Point(0, trackBody.ActualHeight), shell).Y <= shell.ActualHeight
                    && shell.Height == (double)typeof(IslandWindow).GetField("stateHeight", Private).GetValue(widget),
                    "Long track and album text fit at the first frame of the notification");
                await Task.Delay(250);
                Capture(shell, Path.Combine(output, "music-long-track.png"));
                Set(media, "Title", "NORMAL"); Set(media, "Album", "NORMAL DELUXE");
                Call(widget, "UpdateMedia");
                settings.NotificationContent = false; Call(widget, "ApplySettings", false); widget.UpdateLayout();
                Check(Field<Image>(widget, "messageAppIcon").Source == null && Field<TextBlock>(widget, "messageSender").Text == "Yeni bildirim"
                    && Field<TextBlock>(widget, "messageBody").Text == "İçerik gizli",
                    "Privacy mode hides matched music artwork and metadata as well as ordinary message content");
                settings.NotificationContent = true; Call(widget, "ApplySettings", false);
                Set(media, "TrackIdentity", "spotify:two"); Set(media, "Album", "Another album"); Set(media, "Thumbnail", null);
                Call(widget, "UpdateMedia");
                Check(!ReferenceEquals(Field<Image>(widget, "messageAppIcon").Source, cover),
                    "A late same-title track update clears the previous cover instead of rebinding an old notification");
                Call(widget, "OpenPinned"); await Task.Delay(260);
                var state = Field<PanelState>(widget, "panelState");
                Check(!peek.Active && !peek.ClockRunning && state.Expanded && state.Latched && shell.ActualWidth >= 379,
                    "Opening the hub during a music preview cancels that preview and preserves the manually opened player");
                var nextSong = AppMessage.Create(42, DateTimeOffset.UtcNow, new[] { "NORMAL", "Da Poet & Kayra" }, "Spotify.exe", "Spotify");
                Call(widget, "OfferNotification", nextSong);
                Check(!peek.Active && state.Expanded && Field<StackPanel>(widget, "panel").Visibility == Visibility.Visible,
                    "Track notifications from inside or outside the open hub cannot replace or shrink the player");
                var screenshot = AppMessage.Create(43, DateTimeOffset.UtcNow,
                    new[] { "Ekran görüntüsü panoya kopyalandı", "Ekran görüntüleri klasörüne otomatik olarak kaydedildi." }, "Sample.SnippingTool!App", "Ekran Alıntısı Aracı");
                Call(widget, "OfferNotification", screenshot); widget.UpdateLayout();
                Check(peek.Active && Field<TextBlock>(widget, "messageSender").Text == screenshot.Sender
                    && state.Expanded && Equals(Field<Grid>(widget, "expandedLayout").ReadLocalValue(UIElement.IsHitTestVisibleProperty), true) && shell.ActualWidth >= 379,
                    "A screenshot notification uses its content title inside the open hub while player controls stay available; active=" + peek.Active
                    + "; title=" + Field<TextBlock>(widget, "messageSender").Text + "; expanded=" + state.Expanded
                    + "; player hit target=" + Field<Grid>(widget, "expandedLayout").ReadLocalValue(UIElement.IsHitTestVisibleProperty) + "; width=" + shell.ActualWidth);
                Capture(shell, Path.Combine(output, "hub-screenshot.png"));
                peek.Cancel(); await Task.Delay(260);
                Call(widget, "ClosePanel"); await Task.Delay(260);
                var trackPeek = Field<TrackPeek>(widget, "peek");
                trackPeek.Observe("spotify:baseline", true, true);
                trackPeek.Observe("spotify:two", true, true);
                Call(widget, "OfferNotification", AppMessage.Create(44, DateTimeOffset.UtcNow, new[] { "NORMAL", "Da Poet & Kayra" }, "Spotify.exe", "Spotify"));
                Check(trackPeek.Active && !peek.Active, "The Windows toast for an already presented track cannot duplicate Morf's track preview");
                Call(widget, "OpenPinned"); trackPeek.Cancel(); await Task.Delay(260);
                Check(state.Expanded && state.Latched && !trackPeek.ClockRunning && !peek.ClockRunning,
                    "Cancelling old preview clocks after manual opening leaves the hub expanded");
                await Task.Delay(4200);
                Check(state.Expanded && state.Latched && shell.ActualWidth >= 379,
                    "The old track-preview deadline passes without shrinking the manually opened hub");
            }
            finally { widget.Close(); }
        }
        internal static async Task RunActivationAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; registered app resolution; explicit Spotify opening request, no media commands or messages");
            foreach (string id in new[] { "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "Microsoft.ScreenSketch_8wekyb3d8bbwe!App",
                AppMessage.WhatsAppId, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Microsoft.Office.MSACCESS.EXE.15" })
            {
                bool resolved = NotificationAppActivation.CanResolve(id);
                Console.WriteLine((resolved ? "PASS " : "UNVERIFIED Not registered on this device: ") + "AppsFolder identity " + id);
            }
            var launcher = new MediaAppLauncher();
            string result = await launcher.OpenNotificationAppAsync("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify");
            Check(result.Contains("iletildi"), "Windows accepts the real Spotify notification application opening request: " + result);
            await Task.Delay(700);
            uint foregroundProcess; GetWindowThreadProcessId(GetForegroundWindow(), out foregroundProcess);
            using (var process = System.Diagnostics.Process.GetProcessById((int)foregroundProcess))
                Console.WriteLine((process.ProcessName.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ? "PASS " : "UNVERIFIED Foreground may have changed: ")
                    + "After notification activation, foreground process=" + process.ProcessName);
            File.WriteAllText(Path.Combine(output, "activation-result.txt"), result);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static void Set(object owner, string name, object value) { owner.GetType().GetProperty(name, Private).SetValue(owner, value, null); }
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
