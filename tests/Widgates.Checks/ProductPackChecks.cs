using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Widgates
{
    internal static class ProductPackChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Check(bool okay, string text) { if (!okay) throw new InvalidOperationException(text); Console.WriteLine("PASS " + text); }
        internal static void Run(string output)
        {
            var rules = new SourceRules(); Check(rules.Set("Chat.Test!App", 1) && rules.Set("Browser.exe", 2), "Application preferences accept exact identities");
            var restored = SourceRules.Parse(rules.Text); Check(restored.Get("chat.test!app") == 1 && restored.Get("browser.exe") == 2, "Application preferences round-trip with case-insensitive identities");
            Check(!rules.Set("bad\nsetting", 2) && SourceRules.Parse("Bad=9").Ids.Count() == 0 && SourceRules.Parse(new string('x', 2049)).Ids.Count() == 0, "Malformed and oversized source rules cannot become active preferences");
            rules.Set("Music.One", 1, true); rules.Set("Music.Two", 1, true); Check(rules.Get("Music.One") == 0 && rules.Get("Music.Two") == 1, "Choosing another favorite replaces the previous favorite");
            string a = "Music", b = "Video", excluded = "Excluded";
            Func<string, bool> playing = x => true; Func<string, int> rule = x => x == a ? 1 : x == excluded ? 2 : 0;
            Check(MediaPreference.Choose(new[] { a, b, excluded }, null, b, b, playing, rule) == a, "A playing favorite wins automatic source selection");
            Check(MediaPreference.Choose(new[] { a, b, excluded }, b, a, a, playing, rule) == b, "Explicit source selection wins over a favorite");
            Check(MediaPreference.Choose(new[] { excluded }, null, excluded, excluded, playing, rule) == null, "An excluded source is not automatically selected");
            Check(MediaPreference.Choose(new[] { excluded }, excluded, null, null, playing, rule) == excluded, "An excluded source remains available to explicit selection");
            var prefs = new WidgetSettings { NotificationRules = restored.Text, MediaRules = rules.Text }; string path = Path.Combine(output, "product-pack.settings");
            Check(prefs.Save(path) && WidgetSettings.Load(path).NotificationRules == restored.Text && WidgetSettings.Load(path).MediaRules == rules.Text, "Notification and media rules persist independently");
            var store = new Store(); var banner = new NotificationBannerSession(store); banner.Enable();
            banner.Selected = id => id != "Chat.Test!App"; banner.Reconcile();
            Check(store.Values["Chat.Test!App"] == BannerValue.On && store.Values["Other.Test!App"] == BannerValue.Off, "Excluding an application restores its Windows banner without disabling other sources");
            banner.Selected = id => true; banner.Reconcile(); Check(store.Values["Chat.Test!App"] == BannerValue.Off, "An explicitly re-included application can be routed again");
            banner.Restore(); Check(store.Values.All(x => x.Value == BannerValue.On) && store.Journal.Length == 0, "Stopping selected routing restores all original Windows banner values");
            string folder = Path.Combine(output, "product-shelf"); Directory.CreateDirectory(folder);
            var files = Enumerable.Range(0, 4).Select(i => Path.Combine(folder, i + ".txt")).ToArray(); foreach (string file in files) File.WriteAllText(file, "original");
            var shelf = new FileShelf(); shelf.Add(files); shelf.Move(new[] { files[1], files[2] }, -1);
            Check(shelf.Paths.SequenceEqual(new[] { files[1], files[2], files[0], files[3] }), "Multiple selected shelf files move together without reversing order");
            shelf.Assign(new[] { files[1] }, "Grup 2"); shelf.Remove(new[] { files[1], files[0] }); shelf.UndoRemove();
            Check(shelf.Paths.SequenceEqual(new[] { files[1], files[2], files[0], files[3] }) && shelf.Group(files[1]) == "Grup 2", "Undo restores removed shelf references in their order and group");
            Check(!shelf.CanUndo && files.All(file => File.ReadAllText(file) == "original"), "Shelf organization never renames, moves or changes original files");
            shelf.Assign(new[] { files[1], files[3] }, "Grup 2"); shelf.Move(new[] { files[1] }, 1, "Grup 2");
            Check(shelf.Paths.SequenceEqual(new[] { files[3], files[2], files[0], files[1] }), "Reordering inside a filtered shelf group leaves hidden files in their slots");
            shelf.Add(new[] { files[0], "https://example.test/file" });
            Check(shelf.LastAddNotice.Contains("zaten rafta") && shelf.LastAddNotice.Contains("desteklenmeyen"), "Shelf feedback distinguishes duplicate files and rejected locations");
            var queue = new AudioFrameQueue(32); byte[] data = Enumerable.Range(0, 48).Select(x => (byte)x).ToArray(), read = new byte[32];
            queue.Write(data, 48); Check(queue.Count == 32 && queue.Read(read, 32) == 32 && read.SequenceEqual(data.Skip(16)), "Audio queues bound latency by retaining only the newest whole stereo frames");
            queue.Read(read, 32); Check(read.All(x => x == 0) && queue.Count == 0, "Audio underruns return silence instead of repeating stale samples");
            Check(!AudioMirror.ValidTargets("a", new[] { "A" }) && !AudioMirror.ValidTargets("a", new[] { "b", "b" }) && AudioMirror.ValidTargets("a", new[] { "b", "c" }), "Audio mirroring rejects feedback paths and duplicate targets");
            using (var peek = new AppMessagePeek(System.Windows.Threading.Dispatcher.CurrentDispatcher))
            {
                var now = DateTimeOffset.UtcNow;
                peek.Offer(new AppMessage { Key = "1", AppId = "Chat", Sender = "A", Created = now }, true);
                peek.Offer(new AppMessage { Key = "2", AppId = "Other", Sender = "B", Created = now }, true);
                peek.Filter(x => x.AppId != "Other"); Check(peek.Queued == 0 && peek.Current.AppId == "Chat", "Changing notification selection removes queued excluded apps without dismissing allowed content");
            }
            var message = new AppMessage { AppName = "Chat", Sender = "Private sender", Body = "Private body" };
            Check(message.DisplayTitle(false) == "Chat" && message.DisplayBody(false) == "Yeni bildirim"
                && message.DisplayTitle(true).Contains("Private sender"), "Notification privacy hides both sender title and message content");
        }
        internal static async Task RunNativeAsync(string output, bool visualOnly = false)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + (visualOnly
                ? "; synthetic mixer and player UI; no audio capture or volume writes"
                : "; synthetic mixer UI plus this test process's silent Windows audio session; no user audio volume writes"));
            var mirror = new AudioMirror(); var device = new AudioDevice { Id = "test-device", Name = "Hoparlör / Kulaklık", Default = true };
            var entries = Enumerable.Range(0, 7).Select(i => new MixerChannel { DeviceId = device.Id, Master = i == 0, InstanceId = "test-" + i,
                Name = new[] { "Ana ses", "Spotify", "Apple Music", "Chrome", "Discord", "Windows sesleri", "Uzun uygulama adı" }[i], State = new VolumeState { Available = true, Level = 0.35f + i * 0.06f } }).ToArray();
            var window = new AudioMixerWindow(mirror, () => new[] { device, new AudioDevice { Id = "second", Name = "Bluetooth kulaklık" } }, id => entries,
                (channel, level, muted) => new VolumeState { Available = true, Level = level ?? channel.State.Level, Muted = muted ?? channel.State.Muted });
            try
            {
                window.Show(); await window.RefreshAsync(true); await Task.Delay(150);
                var stack = (StackPanel)typeof(AudioMixerWindow).GetField("columns", Private).GetValue(window);
                var sliders = Descendants(stack).OfType<Slider>().ToArray();
                Check(sliders.Length == 7 && sliders.All(x => x.Orientation == Orientation.Vertical), "The mixer renders separate vertical application volume sliders");
                var track = (Track)sliders[0].Template.FindName("PART_Track", sliders[0]);
                sliders[0].Value = 0; window.UpdateLayout(); double bottom = track.Thumb.TransformToAncestor(sliders[0]).Transform(new Point()).Y;
                sliders[0].Value = 100; window.UpdateLayout(); double top = track.Thumb.TransformToAncestor(sliders[0]).Transform(new Point()).Y;
                Check(top < bottom && track.Thumb.ActualWidth >= 28, "Vertical mixer sliders increase upwards with usable thumb targets");
                sliders[0].Value = 35;
                foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
                {
                    typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, theme); ThemePalette.Refresh();
                    await Task.Delay(80);
                    window.Width = 600; window.UpdateLayout(); Render(window, Path.Combine(output, "mixer-" + theme + ".png"));
                    window.Width = 320; window.UpdateLayout(); Render(window, Path.Combine(output, "mixer-narrow-" + theme + ".png"));
                    Check(window.ActualWidth <= 321 && ((ScrollViewer)stack.Parent).ScrollableWidth > 0, "Narrow " + theme + " mixer keeps columns horizontal and scrollable");
                }
                Check(Descendants(window).OfType<Button>().Any(x => AutomationProperties.GetName(x).Contains("Windows'ta")), "Application output assignment has an explicit Windows mixer action");
                var notice = (TextBlock)typeof(AudioMixerWindow).GetField("notice", Private).GetValue(window);
                Check(notice.Visibility == Visibility.Collapsed, "A populated mixer hides routine explanatory status text");
                var scrollBar = Descendants((ScrollViewer)stack.Parent).OfType<ScrollBar>().Single(x => x.Orientation == Orientation.Horizontal);
                var indicator = Descendants(scrollBar).OfType<Border>().Single(x => x.Name == "Indicator");
                Check(indicator.ActualHeight <= 3 && indicator.Opacity < 0.5 && scrollBar.ActualHeight >= 12,
                    "The translucent three-DIP scroll indicator retains its larger drag target");
                var oldSlider = sliders[1]; oldSlider.Focus(); var scroller = (ScrollViewer)stack.Parent; scroller.ScrollToHorizontalOffset(120); window.UpdateLayout();
                double offset = scroller.HorizontalOffset; await window.RefreshAsync(true);
                Check(Descendants(stack).OfType<Slider>().Contains(oldSlider) && oldSlider.IsKeyboardFocusWithin && Math.Abs(scroller.HorizontalOffset - offset) < 1,
                    "Mixer refresh preserves slider identity, keyboard focus and horizontal position");
            }
            finally { window.Close(); mirror.Dispose(); }
            if (visualOnly) { await PlayerVisualAsync(output); await SettingsVisualAsync(output); return; }
            var prefs = new WidgetSettings { WindowsNotifications = true }; int saves = 0;
            var choices = new[] { new SourceChoice { Id = "Chat.Test!App", Name = "Sohbet" }, new SourceChoice { Id = "Music.Test!App", Name = "Müzik" } };
            var dialog = new SettingsWindow(prefs, new SettingsActions { Apply = _ => saves++, NotificationSources = () => choices, MediaSources = () => choices,
                NotificationAvailable = () => false, MediaStatus = () => "Otomatik kontrol bekliyor." });
            try
            {
                dialog.Show(); dialog.SelectPage(6); dialog.UpdateLayout();
                var source = Descendants(dialog).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "Bildirim uygulaması");
                var mode = Descendants(dialog).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "Uygulama tercihi");
                source.SelectedIndex = 0; mode.SelectedIndex = 2;
                string selectedApp = ((SourceChoice)source.SelectedItem).Id;
                Descendants(dialog).OfType<Button>().Single(x => x.Content as string == "Uygula").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(SourceRules.Parse(prefs.NotificationRules).Get(selectedApp) == 2,
                    "The notification settings UI applies the chosen app's Windows fallback rule");
                Render(dialog, Path.Combine(output, "notification-rules-Dark.png"));
                dialog.SelectPage(2); dialog.UpdateLayout();
                source = Descendants(dialog).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "Medya uygulaması");
                mode = Descendants(dialog).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "Uygulama tercihi");
                source.SelectedIndex = 0; mode.SelectedIndex = 1;
                Descendants(dialog).OfType<Button>().Single(x => x.Content as string == "Uygula").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(SourceRules.Parse(prefs.MediaRules).Ids.Count() == 1 && saves == 2, "The media settings UI saves its favorite independently of notification rules");
                Render(dialog, Path.Combine(output, "media-rules-Dark.png"));
            }
            finally { dialog.Close(); }
            var devices = await Task.Run(() => AudioDevices.List()); Check(devices.Length > 0 && devices.All(x => !string.IsNullOrEmpty(x.Id)), "Native Windows output devices expose stable endpoint identities");
            using (var silent = new NativePlayerVolumeChecks.SilentStream())
            {
                await Task.Delay(180); string id = devices.First(x => x.Default).Id;
                var channels = await Task.Run(() => AudioDevices.Channels(id));
                var own = channels.FirstOrDefault(x => !x.Master && x.ProcessId == (uint)Process.GetCurrentProcess().Id);
                var masterBefore = channels.First(x => x.Master).State;
                Check(own != null && own.State.Available, "The new mixer enumerates this test process's real silent Windows audio session");
                int changes = 0; AudioMixerWatch watch = null; VolumeState original = own.State;
                try
                {
                    watch = await Task.Run(() => new AudioMixerWatch(id, () => System.Threading.Interlocked.Increment(ref changes)));
                    var state = await Task.Run(() => AudioDevices.Use(own, 0.41f, false));
                    Check(state.Available && Math.Abs(state.Level - 0.41f) < 0.005, "Mixer volume writes target the exact native test session");
                    await Task.Delay(120); Check(changes > 0, "Core Audio volume callbacks notify the mixer without polling");
                    string appId, executable; WindowsPlayerVolumeEndpoint.ReadIdentity(own.ProcessId, out appId, out executable);
                    await Task.Run(() => { using (var endpoint = new WindowsPlayerVolumeEndpoint(string.IsNullOrEmpty(appId) ? executable : appId)) endpoint.Read(); });
                    Check((await Task.Run(() => AudioDevices.Use(channels.First(x => x.Master), null, null))).Level == masterBefore.Level,
                        "Legacy player controls can coexist with the mixer without changing the master volume");
                    var stale = new MixerChannel { DeviceId = id, InstanceId = "gone-session", ProcessId = own.ProcessId };
                    Check(!(await Task.Run(() => AudioDevices.Use(stale, 0.2f, null))).Available, "An expired session does not redirect a volume write to another app");
                }
                finally
                {
                    if (watch != null) Task.Run(() => watch.Dispose()).GetAwaiter().GetResult();
                    Task.Run(() => AudioDevices.Use(own, original.Level, original.Muted)).GetAwaiter().GetResult();
                }
            }
            if (devices.Length > 1) { await AudioMirror.ProbeAsync(devices[0].Id, devices[1].Id); Check(true, "WASAPI source and second-output streams initialize without starting or capturing audio"); }
            else Console.WriteLine("UNVERIFIED Two-output native format probe: only one output device is connected");
            Console.WriteLine("UNVERIFIED Physical Bluetooth plus speaker playback, latency and long-duration drift");
        }
        private static async Task PlayerVisualAsync(string output)
        {
            var settings = new WidgetSettings { AutoPauseOthers = false, HideInGames = false, HideWhenIdle = false, Lightweight = LightweightMode.Always };
            var widget = new IslandWindow(settings);
            Func<string, object> field = name => typeof(IslandWindow).GetField(name, Private).GetValue(widget);
            Action<string, object[]> call = (name, args) => typeof(IslandWindow).GetMethod(name, Private).Invoke(widget, args);
            try
            {
                widget.IsHitTestVisible = false; widget.Show(); await Task.Delay(300);
                ((MediaController)field("media")).Dispose();
                ((PanelState)field("panelState")).Persistent = true;
                call("SetExpanded", new object[] { true });
                ((TextBlock)field("mediaTitle")).Text = "Evening focus";
                ((TextBlock)field("mediaArtist")).Text = "Morf demo";
                ((StackPanel)field("applicationPanel")).Visibility = Visibility.Collapsed;
                var controls = (Grid)field("playbackControls"); controls.Visibility = Visibility.Visible;
                ((StackPanel)field("timelinePanel")).Visibility = Visibility.Visible;
                ((TextBlock)field("elapsedLabel")).Text = "0:48";
                ((TextBlock)field("durationLabel")).Text = "2:39";
                ((Slider)field("seekSlider")).Value = 0.3;
                foreach (string name in new[] { "play", "previous", "next" }) ((Button)field(name)).IsEnabled = true;
                ((Slider)field("seekSlider")).IsEnabled = true;
                call("UpdateVolumeDisplay", new object[] { new VolumeState { Available = true, Level = 0.65f } });
                var shell = (Border)field("shell"); var volume = (Border)field("volumePanel");
                foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
                {
                    typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, theme); ThemePalette.Refresh();
                    await Task.Delay(80);
                    call("UpdateState", new object[] { false }); widget.UpdateLayout();
                    double volumeTop = volume.TranslatePoint(new Point(), shell).Y;
                    double controlsBottom = controls.TranslatePoint(new Point(0, controls.ActualHeight), shell).Y;
                    Check(volumeTop >= controlsBottom && volumeTop + volume.ActualHeight <= shell.ActualHeight,
                        theme + " player keeps volume below transport controls within the panel");
                    Check(!Descendants(shell).OfType<ComboBox>().Any(x => AutomationProperties.GetName(x) == "Ses hedefi")
                        && !Descendants(shell).OfType<Button>().Any(x => x.Content as string == "Mikser"),
                        theme + " player removes the inline target picker and duplicate mixer button");
                    Render(shell, Path.Combine(output, "player-volume-" + theme + ".png"));
                }
                settings.ScrollVolume = false; call("ApplySettings", new object[] { false });
                Check(((Button)field("volumeButton")).Visibility == Visibility.Visible && volume.Visibility == Visibility.Collapsed,
                    "Mixer access remains available when inline volume adjustment is disabled");
            }
            finally { widget.Close(); }
        }
        private static async Task SettingsVisualAsync(string output)
        {
            var settings = new WidgetSettings();
            var window = new SettingsWindow(settings, new SettingsActions { Apply = _ => { }, SelectTheme = _ => true, ConfigureStartup = _ => true });
            try
            {
                window.Show(); window.Width = 840; window.Height = 620; window.SelectPage(0);
                foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
                {
                    typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, theme);
                    ThemePalette.Refresh(); window.SelectPage(0); await Task.Delay(80);
                    Render(window, Path.Combine(output, "settings-" + theme + ".png"));
                }
            }
            finally { window.Close(); }
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        private static void Render(FrameworkElement window, string path)
        {
            window.UpdateLayout();
            var root = Window.GetWindow(window) ?? window;
            var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(root);
            BitmapSource capture = image;
            if (!ReferenceEquals(root, window))
            {
                var origin = window.TranslatePoint(new Point(), root);
                capture = new CroppedBitmap(image, new Int32Rect((int)Math.Round(origin.X), (int)Math.Round(origin.Y),
                    (int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight)));
            }
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(capture)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
        private sealed class Store : INotificationBannerStore
        {
            internal readonly Dictionary<string, BannerValue> Values = new Dictionary<string, BannerValue> { { "Chat.Test!App", BannerValue.On }, { "Other.Test!App", BannerValue.On } };
            internal byte[] Journal = new byte[0];
            public IEnumerable<string> Senders() { return Values.Keys.ToArray(); }
            public bool CanRoute(string id) { return Values.ContainsKey(id); }
            public BannerValue Read(string id) { return Values[id]; }
            public void Write(string id, BannerValue value) { if (Journal.Length == 0) throw new InvalidOperationException("Write before journal"); Values[id] = value; }
            public byte[] LoadJournal() { return Journal; }
            public void SaveJournal(byte[] data) { Journal = data; }
        }
    }
}
