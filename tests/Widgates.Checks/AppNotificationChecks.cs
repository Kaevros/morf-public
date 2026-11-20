using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Widgates
{
    internal static class AppNotificationChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static async Task RunAsync(string output)
        {
            Check(AppMessage.IsWhatsApp(AppMessage.WhatsAppId) && AppMessage.IsWhatsApp(AppMessage.WhatsAppBetaId) && !AppMessage.IsWhatsApp("unknown!WhatsApp")
                && !AppMessage.IsWhatsApp(AppMessage.WhatsAppId + ".fake") && !AppMessage.IsWhatsApp("5319275A.WhatsAppDesktopUnrelated!App")
                && !AppMessage.IsWhatsApp("https://example.invalid/WhatsApp"), "Notification filtering accepts explicit WhatsApp identities and rejects prefix lookalikes");
            var moment = DateTimeOffset.UtcNow;
            var first = AppMessage.Create(1, moment, new[] { "Kişi", "İlk mesaj" });
            var second = AppMessage.Create(1, moment, new[] { "Kişi", "İkinci mesaj" });
            var tracker = new AppMessageTracker();
            Check(tracker.Observe(new[] { first }).Length == 0, "Existing Windows notifications establish a quiet startup baseline");
            Check(tracker.Observe(new[] { first }).Length == 0 && tracker.Observe(new[] { second }).Length == 1,
                "Unchanged notification snapshots stay quiet while a replaced message can notify");
            tracker.Reset(); Check(tracker.Observe(new[] { second }).Length == 0, "Reconnecting notification access never replays old messages");
            Check(AppMessage.Create(2, moment, new[] { "\0\u202e" }) == null && AppMessage.Clean(new string('x', 2000), 512).Length == 512,
                "Notification text is bounded and control-only content is ignored");
            using (var peek = new AppMessagePeek(Dispatcher.CurrentDispatcher, () => moment))
            {
                peek.Seconds = 3;
                peek.Offer(first, false); Check(!peek.Active && !peek.ClockRunning, "Suppressed notifications do not create a display or idle timer");
                peek.Offer(first, true); Check(peek.Active && peek.ClockRunning, "A new permitted message starts its one-shot display clock");
                moment += TimeSpan.FromSeconds(2);
                second.Created = moment; peek.Offer(second, true);
                peek.Hold(); Check(peek.Current == second && !peek.ClockRunning, "A reading hover holds the current coalesced message");
                peek.Release(); Check(peek.ClockRunning, "Pointer departure resumes the remaining message deadline");
                Check(Field<DispatcherTimer>(peek, "timer").Interval.TotalSeconds < 1.1, "Replacing a message does not restart the original display duration");
                for (int i = 0; i < 6; i++) peek.Offer(new AppMessage { Key = "queued" + i, Sender = "Kişi " + i, Body = "Örnek", Created = moment }, true);
                Check(peek.Queued == 3, "A message burst retains no more than three pending conversations");
                moment += TimeSpan.FromSeconds(11); peek.Expire();
                Check(!peek.Active && !peek.ClockRunning && peek.Queued == 0, "A sustained burst returns the island to music and drops stale queued messages");
                var fresh = new AppMessage { Key = "fresh", Sender = "Kişi", Created = moment };
                peek.Offer(fresh, true); Check(!peek.Active, "A burst cooldown prevents immediate permanent message replacement");
                moment += TimeSpan.FromSeconds(3); fresh.Created = moment; peek.Offer(fresh, true);
                peek.Retain(new HashSet<string>()); Check(!peek.Active && !peek.ClockRunning, "A notification removed by Windows also disappears from the island");
                peek.Offer(new AppMessage { Key = "old", Sender = "Eski", Created = moment - TimeSpan.FromSeconds(12) }, true);
                Check(!peek.Active, "Delayed notifications cannot show stale messages");
            }
            var clock = DateTimeOffset.UtcNow;
            using (var burst = new AppMessagePeek(Dispatcher.CurrentDispatcher, () => clock))
            {
                burst.Seconds = 8;
                burst.Offer(new AppMessage { Key = "one", Sender = "Bir", Created = clock }, true);
                burst.Offer(new AppMessage { Key = "two", Sender = "İki", Created = clock }, true);
                clock += TimeSpan.FromSeconds(8); burst.Expire();
                Check(burst.Active && Field<DispatcherTimer>(burst, "timer").Interval.TotalSeconds == 2,
                    "Queued notifications share a ten-second continuous-display budget");
                clock += TimeSpan.FromSeconds(2); burst.Expire();
                Check(!burst.Active && !burst.ClockRunning, "The continuous-display budget always returns the island to music");
                clock += TimeSpan.FromSeconds(3);
                burst.Offer(new AppMessage { Key = "three", Sender = "Üç", Created = clock }, true); burst.Expire();
                burst.Offer(new AppMessage { Key = "four", Sender = "Dört", Created = clock }, true);
                Check(!burst.Active, "An ordinary notification expiry also reserves a quiet music interval");
            }
            string path = Path.Combine(output, "notification.settings");
            var settings = new WidgetSettings { WhatsAppNotifications = true, NotificationContent = false, NotificationSeconds = 8 };
            settings.Save(path); var restored = WidgetSettings.Load(path);
            Check(restored.WhatsAppNotifications && !restored.NotificationContent && restored.NotificationSeconds == 8,
                "Application notification preferences round-trip without message text or listening history");
            int applies = 0, previews = 0;
            var dialog = new SettingsWindow(settings, new SettingsActions { Apply = _ => applies++, SelectTheme = _ => true, ConfigureStartup = _ => true,
                NotificationStatus = () => "İzin gerekli", NotificationAvailable = () => false, PreviewNotification = () => previews++ });
            try
            {
                dialog.SelectPage(6);
                var page = Field<StackPanel>(dialog, "page");
                var toggle = Children(page).OfType<CheckBox>().Single(); toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!settings.WhatsAppNotifications && applies == 1, "The Applications switch changes only WhatsApp notification visibility");
                Check(Field<Border>(dialog, "notificationDrawer").Visibility == Visibility.Collapsed && !Field<Border>(dialog, "notificationDrawer").IsEnabled,
                    "Disabled WhatsApp hides and disables its entire settings drawer");
                toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(settings.WhatsAppNotifications && applies == 2 && Field<Border>(dialog, "notificationDrawer").Visibility == Visibility.Visible,
                    "Enabling WhatsApp restores its existing settings drawer");
                var selectors = Children(page).OfType<ComboBox>().ToArray(); selectors[0].SelectedIndex = 0; selectors[1].SelectedIndex = 0;
                Check(settings.NotificationSeconds == 3 && settings.NotificationContent, "Applications duration and content choices update their own preferences");
                Check(!Children(page).OfType<Button>().Single(x => x.Content as string == "Windows iznini bağla").IsEnabled,
                    "An unpackaged Applications view explains unavailable permission instead of prompting incorrectly");
                Children(page).OfType<Button>().Single(x => x.Content as string == "Örnek bildirim göster").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(previews == 1 && !Children(page).OfType<TextBox>().Any(), "Preview is explicit and no unsupported message reply editor is offered");
            }
            finally { dialog.Close(); }
            int permissionRequests = 0;
            var automaticSettings = new WidgetSettings { WindowsNotifications = false };
            var automaticDialog = new SettingsWindow(automaticSettings, new SettingsActions { Apply = _ => { }, SelectTheme = _ => true,
                NotificationStatus = () => "Hazır", NotificationAvailable = () => true,
                ConnectNotifications = () => { permissionRequests++; return Task.FromResult(0); } });
            try
            {
                automaticDialog.SelectPage(6);
                var automaticToggle = Children(Field<StackPanel>(automaticDialog, "page")).OfType<CheckBox>().Single();
                automaticToggle.IsChecked = true; automaticToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(permissionRequests == 1, "The notification master switch starts Windows permission setup directly without a second connect action");
                automaticToggle.IsChecked = false; automaticToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(permissionRequests == 1, "Turning notifications off never prompts for Windows permission");
            }
            finally { automaticDialog.Close(); }
            int statusReads = 0;
            var lazySettings = new WidgetSettings { WhatsAppNotifications = false };
            var lazyDialog = new SettingsWindow(lazySettings, new SettingsActions { Apply = _ => { }, SelectTheme = _ => true,
                NotificationStatus = () => { statusReads++; return "Örnek durum"; }, NotificationAvailable = () => false });
            try
            {
                lazyDialog.SelectPage(6); var lazyPage = Field<StackPanel>(lazyDialog, "page");
                Check(!Children(lazyPage).OfType<Button>().Any(x => x.Content as string == "Windows iznini bağla") && statusReads == 0,
                    "An initially disabled WhatsApp row creates no permission controls or status query");
                var toggle = Children(lazyPage).OfType<CheckBox>().Single(); toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var drawer = Field<Border>(lazyDialog, "notificationDrawer");
                var selector = Children(lazyPage).OfType<ComboBox>().First(); selector.SelectedIndex = 0;
                toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                int before = statusReads; lazyDialog.RefreshRuntimeStatus();
                Check(statusReads == before && drawer.Visibility == Visibility.Collapsed && lazySettings.NotificationSeconds == 3,
                    "Closing the WhatsApp drawer preserves preferences and stops disabled status queries");
                toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(ReferenceEquals(drawer, Field<Border>(lazyDialog, "notificationDrawer")) && selector.SelectedIndex == 0,
                    "Reopening WhatsApp reuses its controls and selected duration");
            }
            finally { lazyDialog.Close(); }
            using (var listener = new AppNotificationListener(Dispatcher.CurrentDispatcher))
            {
                if (!AppNotificationListener.HasIdentity)
                {
                    listener.Configure(true); await listener.RefreshAsync(false);
                    Check(!listener.Connected && listener.Status.Contains("paket"), "The inbox preview recovers gracefully when Windows package identity is absent");
                }
            }
        }
        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; synthetic WhatsApp notification UI; no real notification access, message sends or player commands");
            var settings = new WidgetSettings { AutoPauseOthers = false, RespectQuietState = false, HideWhenIdle = false, NotificationSeconds = 3, Dock = DockPreference.Top, HideInGames = false };
            var widget = new IslandWindow(settings);
            try
            {
                widget.IsHitTestVisible = false; widget.Show(); await Task.Delay(450);
                Call(widget, "TogglePersistent"); await Task.Delay(260);
                var state = Field<PanelState>(widget, "panelState"); var shell = Field<Border>(widget, "shell");
                var messages = Field<AppMessagePeek>(widget, "messages");
                double previousHeight = shell.ActualHeight;
                var notificationPower = Field<PowerStateMonitor>(widget, "power");
                Console.WriteLine("SCENE visible=" + widget.IsVisible + "; messageAllowed=" + typeof(IslandWindow).GetProperty("MessageAllowed", Private).GetValue(widget, null)
                    + "; desktop=" + Field<DesktopPlacement>(widget, "placement").OnCurrentDesktop + "; render=" + notificationPower.State.RenderAllowed
                    + "; display=" + notificationPower.State.DisplayOn + "; locked=" + notificationPower.State.Locked
                    + "; game=" + Field<DesktopPlacement>(widget, "placement").GameActive);
                Call(widget, "PreviewNotification"); await Task.Delay(320);
                Check(messages.Active && shell.ActualHeight >= previousHeight && Field<StackPanel>(widget, "panel").Visibility == Visibility.Visible
                    && Equals(Field<Grid>(widget, "expandedLayout").ReadLocalValue(UIElement.IsHitTestVisibleProperty), true) && state.Expanded && state.Persistent,
                    "A transient message stays inside the expanded pinned hub with the player visible and interactive (active=" + messages.Active
                    + ", height=" + shell.ActualHeight + ", expanded=" + state.Expanded + ", persistent=" + state.Persistent + ")");
                var messageView = Field<Grid>(widget, "messageView"); var messageButton = Field<Button>(widget, "messageOpen");
                Check(messageView.Children.Count == 1 && messageView.Children[0] == messageButton && messageButton.ToolTip == null
                    && !ToolTipService.GetIsEnabled(messageButton) && !VisualChildren(messageButton).OfType<Border>().Any(),
                    "Notification uses one transparent hit target without a nested card, close button or tooltip");
                Point topLeft = shell.TranslatePoint(new Point(), widget);
                Check(Math.Abs(topLeft.Y) < 0.7 && Math.Abs(shell.ActualWidth - 380) < 0.7
                    && Math.Abs(topLeft.X + shell.ActualWidth / 2 - widget.ActualWidth / 2) < 0.7
                    && Grid.GetRow(messageView) == 1 && Grid.GetRowSpan(messageView) == 1,
                    "An inline notification uses the expanded hub width and its own row without a floating capsule inset");
                var connectedMedia = Field<MediaController>(widget, "media");
                bool expectedProgress = connectedMedia.IsPlaying && settings.ShowTimeline && connectedMedia.Timeline != null && connectedMedia.Timeline.Valid;
                Check(Field<DispatcherTimer>(widget, "progress").IsEnabled == expectedProgress && !Field<MarqueeText>(widget, "compactMarquee").Running,
                    "An inline message keeps the visible player's progress clock while the compact marquee stays stopped");
                messages.Hold();
                messages.Current.Body = string.Join(" ", Enumerable.Repeat("Uzun mesaj önizlemesi", 20));
                Call(widget, "UpdateState", false); widget.UpdateLayout();
                var body = Field<TextBlock>(widget, "messageBody");
                var sender = Field<TextBlock>(widget, "messageSender");
                Point bodyTop = body.TranslatePoint(new Point(), shell);
                Check(body.TextWrapping == TextWrapping.Wrap && body.TextTrimming == TextTrimming.None
                    && bodyTop.Y >= sender.TranslatePoint(new Point(0, sender.ActualHeight), shell).Y && Field<ScrollViewer>(widget, "messageScroll").ActualHeight <= 72.5,
                    "A long inline notification wraps in a bounded scroll area without replacing the player");
                Capture(shell, Path.Combine(output, "notification-top.png"));
                Capture(widget, Path.Combine(output, "notification-floating-top.png"));
                var originalTheme = ThemePalette.Preference;
                try
                {
                    typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, ThemePreference.Light);
                    ThemePalette.Refresh(); await Task.Delay(60); Call(widget, "UpdateState", false);
                    Check(SystemParameters.HighContrast || (((SolidColorBrush)Field<TextBlock>(widget, "messageSender").Foreground).Color.R < ((SolidColorBrush)shell.Background).Color.R
                        && ((SolidColorBrush)Field<TextBlock>(widget, "messageBody").Foreground).Color.R < ((SolidColorBrush)shell.Background).Color.R),
                        "Light-theme notification text settles to the matching dark palette colors");
                    Check(SystemParameters.HighContrast || HasDarkText(Field<TextBlock>(widget, "messageSender")), "Light-theme title glyphs render with dark ink after switching palettes");
                    Capture(shell, Path.Combine(output, "notification-light.png"));
                }
                finally
                {
                    typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, originalTheme); ThemePalette.Refresh();
                }
                widget.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(widget), Environment.TickCount, System.Windows.Input.Key.Escape) { RoutedEvent = UIElement.PreviewKeyDownEvent });
                Check(!messages.Active && state.Expanded && state.Persistent,
                    "Escape dismisses the notification while preserving the pinned player state");
                Call(widget, "PreviewNotification"); await Task.Delay(320);
                Field<Button>(widget, "messageOpen").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(300);
                Check(!messages.Active && state.Expanded && state.Persistent && Math.Abs(shell.ActualHeight - previousHeight) < 1,
                    "Clicking a sample restores the previous player without opening WhatsApp");
                Check(shell.Margin == new Thickness(0), "Returning to the player removes only the temporary notification inset");
                settings.Dock = DockPreference.Bottom; settings.NotificationContent = false; Call(widget, "ApplySettings", false);
                Call(widget, "PreviewNotification"); await Task.Delay(300);
                Check(Field<TextBlock>(widget, "messageSender").Text == "Yeni bildirim" && Field<TextBlock>(widget, "messageBody").Text == "İçerik gizli"
                    && Field<Image>(widget, "messageAppIcon").Source == null,
                    "Private notification mode hides sender and body without adding a notification outline");
                Point bottomLeft = shell.TranslatePoint(new Point(), widget);
                Check(Math.Abs(widget.ActualHeight - bottomLeft.Y - shell.ActualHeight) < 0.7 && shell.Margin == new Thickness(0),
                    "Lower-edge inline notifications retain the expanded hub docking and inset");
                Capture(shell, Path.Combine(output, "notification-bottom-private.png"));
                Capture(widget, Path.Combine(output, "notification-floating-bottom.png"));
                messages.Hold(); Check(!messages.ClockRunning, "Native reading hover stops the notification clock");
                messages.Release(); await Task.Delay(3300);
                Check(!messages.Active && !messages.ClockRunning && state.Expanded && state.Persistent,
                    "Real dispatcher expiry restores the pinned player and leaves no idle notification timer");
                await Task.Delay(2100); // Preserve the existing burst cooldown before the next scenario.
                Call(widget, "PreviewNotification"); await Task.Delay(260);
                Check(messages.Active, "The next suppression scenario starts with a visible message after cooldown");
                var placement = Field<DesktopPlacement>(widget, "placement");
                settings.HideInGames = true;
                typeof(DesktopPlacement).GetField("gameActive", Private).SetValue(placement, true); Call(widget, "UpdateVisibility");
                Check(!messages.Active && !widget.IsVisible && Field<TextBlock>(widget, "messageSender").Text == "",
                    "Game suppression cancels the message and clears its visual text immediately");
                settings.HideInGames = false;
                typeof(DesktopPlacement).GetField("gameActive", Private).SetValue(placement, false); Call(widget, "UpdateVisibility");
                Call(widget, "ClosePanel"); await Task.Delay(280);
                Call(widget, "PreviewNotification"); await Task.Delay(100);
                Point expanding = shell.TranslatePoint(new Point(shell.ActualWidth / 2, 0), widget);
                Check(Math.Abs(expanding.X - widget.ActualWidth / 2) < 0.7 && shell.ActualWidth >= 179.5 && shell.ActualWidth <= 380.5
                    && Field<Grid>(widget, "messageView").Visibility == Visibility.Visible,
                    "The content-sized notification stays centered with a bounded width even when it equals the compact width");
                Capture(widget, Path.Combine(output, "notification-expansion.png"));
                messages.Cancel(); await Task.Delay(280);
                Call(widget, "OpenSettings"); await Task.Delay(100);
                var dialog = Field<SettingsWindow>(widget, "settingsWindow"); dialog.SelectPage(6); await Task.Delay(60);
                Capture((FrameworkElement)dialog.Content, Path.Combine(output, "applications.png"));
                Check(dialog.SelectedPage == 6 && !messages.Active, "The real Applications settings page opens without replaying messages");
                dialog.Close();
            }
            finally { widget.Close(); }
        }
        private static void Capture(FrameworkElement element, string path)
        {
            element.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(element)
                { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, element.ActualWidth, element.ActualHeight), Stretch = Stretch.Fill },
                null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
        private static bool HasDarkText(TextBlock label)
        {
            label.UpdateLayout();
            int width = Math.Max(1, (int)Math.Ceiling(label.ActualWidth)), height = Math.Max(1, (int)Math.Ceiling(label.ActualHeight));
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(label), null, new Rect(0, 0, width, height));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
            var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
            for (int index = 0; index < pixels.Length; index += 4)
                if (pixels[index + 3] > 200 && pixels[index] < 128 && pixels[index + 1] < 128 && pixels[index + 2] < 128) return true;
            return false;
        }
        private static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, Private).GetValue(value); }
        private static void Call(object value, string method, params object[] args) { value.GetType().GetMethod(method, Private).Invoke(value, args); }
        private static IEnumerable<DependencyObject> Children(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var nested in Children(child)) yield return nested; }
        }
        private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index); yield return child;
                foreach (var nested in VisualChildren(child)) yield return nested;
            }
        }
        private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
    }
}
