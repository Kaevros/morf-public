using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;

namespace Widgates
{
    internal sealed partial class IslandWindow
    {
        private AppNotificationListener appNotifications;
        private NotificationBannerRouting bannerRouting;
        private bool routingMasterPreference;
        private AppMessagePeek messages;
        private AppMessage pendingNotification;
        private Grid messageView;
        private Button messageOpen;
        private TextBlock messageSender, messageBody;
        private Image messageAppIcon;
        private FrameworkElement messageIconFallback;
        private string renderedIconApp;
        private int notificationIconRevision;
        private bool notificationInline;
        private string lastMediaPreviewIdentity;
        private string notificationActionNotice;
        private int notificationSizeRevision, notificationInsetRevision;
        private string renderedMessage;
        private bool openingMessage;
        private bool hidingMessage;
        private Thickness notificationInset;
        private ScrollViewer messageScroll;
        private Point? notificationDragStart;
        private string notificationDragKey;
        private bool notificationDragging;
        private readonly TranslateTransform notificationTranslation = new TranslateTransform();
        private bool MessageAllowed { get { return !SettingsOpen && MessageContextAllowed; } }
        private bool MessageContextAllowed
        {
            get { return !closed && mixerWindow == null && IsVisible && !temporaryHide.Active && !Suppressed && !draggingOut && !isDragOver
                && !scrubbing && !trackSwipe.Active && !sessions.IsDropDownOpen && !applicationMenu.IsOpen
                && (placement == null || (placement.OnCurrentDesktop && !(settings.HideInFullscreen && !placement.RuleShown && placement.Fullscreen)))
                && (power == null || power.State.RenderAllowed) && (!settings.RespectQuietState || NotificationPolicy.AllowsPeek()); }
        }
        private void InitializeNotifications()
        {
            messages = new AppMessagePeek(Dispatcher);
            appNotifications = new AppNotificationListener(Dispatcher);
            appNotifications.Changed += () => { UpdateNotificationRouting(); if (settingsWindow != null) settingsWindow.RefreshRuntimeStatus(); };
            appNotifications.Received += OfferNotification;
            appNotifications.Snapshot += keys => { messages.Retain(keys); if (pendingNotification != null && !keys.Contains(pendingNotification.Key)) pendingNotification = null; };
            messages.Changed += () =>
            {
                if (closed) return;
                if (messages.Active) peek.Cancel(false);
                UpdateState(true);
                if (messages.Active && shell.IsMouseOver) messages.Hold();
                if (!messages.Active && !pinned && !shell.IsMouseOver) collapse.Start();
            };
            messageView = new Grid { Visibility = Visibility.Collapsed, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
            var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new ColumnDefinition());
            content.RenderTransform = notificationTranslation;
            var icon = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            messageIconFallback = SourceIcon(null, "M 4 11 L 4 6 A 4 4 0 0 1 12 6 L 12 11 L 14 13 L 2 13 Z M 6 15 L 10 15", 24);
            messageAppIcon = new Image { Width = 40, Height = 40, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
            RenderOptions.SetBitmapScalingMode(messageAppIcon, BitmapScalingMode.HighQuality); RoundImage(messageAppIcon, 8);
            icon.Children.Add(messageIconFallback); icon.Children.Add(messageAppIcon); content.Children.Add(icon);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            messageSender = Label("", 13, ThemePalette.Text, true); messageSender.TextWrapping = TextWrapping.Wrap; messageSender.TextTrimming = TextTrimming.None;
            messageBody = Label("", 11, Muted); messageBody.TextWrapping = TextWrapping.Wrap; messageBody.TextTrimming = TextTrimming.None;
            // Give text runs a new brush on palette changes so their cached glyphs also refresh.
            messageSender.SetBinding(TextBlock.ForegroundProperty, new Binding("Color") { Source = ThemePalette.Text, Converter = NotificationTextBrush.Instance });
            messageBody.SetBinding(TextBlock.ForegroundProperty, new Binding("Color") { Source = Muted, Converter = NotificationTextBrush.Instance });
            messageBody.Margin = new Thickness(0, 2, 0, 0); labels.Children.Add(messageSender); labels.Children.Add(messageBody);
            messageScroll = new ScrollViewer { Content = labels, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(messageScroll, 1); content.Children.Add(messageScroll);
            // A single interactive surface: no nested button background, hover card or tooltip.
            messageOpen = new Button { Content = content, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 12, 16, 12), Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, FocusVisualStyle = null };
            var buttonTemplate = new ControlTemplate(typeof(Button));
            var hitArea = new FrameworkElementFactory(typeof(Grid)); hitArea.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            hitArea.AppendChild(presenter); buttonTemplate.VisualTree = hitArea; messageOpen.Template = buttonTemplate;
            System.Windows.Controls.ToolTipService.SetIsEnabled(messageOpen, false);
            messageOpen.Click += async (_, __) => await OpenNotificationAsync();
            messageOpen.PreviewMouseLeftButtonDown += (_, e) => { if (!messages.Active) return; notificationDragStart = e.GetPosition(this); notificationDragKey = messages.Current.Key; notificationDragging = false; };
            messageOpen.PreviewMouseMove += (_, e) =>
            {
                if (!notificationDragStart.HasValue || e.LeftButton != MouseButtonState.Pressed || !messages.Active || messages.Current.Key != notificationDragKey) return;
                var delta = e.GetPosition(this) - notificationDragStart.Value;
                if (!notificationDragging && Math.Abs(delta.X) >= 8 && Math.Abs(delta.X) > Math.Abs(delta.Y)) { notificationDragging = true; messages.Hold(); messageOpen.CaptureMouse(); }
                if (notificationDragging) { notificationTranslation.X = delta.X; e.Handled = true; }
            };
            messageOpen.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (!notificationDragStart.HasValue) return;
                bool dragged = notificationDragging; double distance = notificationTranslation.X;
                notificationDragStart = null; notificationDragging = false;
                if (!dragged) return;
                e.Handled = true; messageOpen.ReleaseMouseCapture();
                if (messages.Active && messages.Current.Key == notificationDragKey && Math.Abs(distance) >= 52) DismissNotificationSwipe(distance);
                else { notificationTranslation.X = 0; if (messages.Active && !shell.IsMouseOver) messages.Release(); }
            };
            messageOpen.LostMouseCapture += (_, __) => { if (notificationDragging) { notificationDragging = false; notificationDragStart = null; notificationTranslation.X = 0; if (messages.Active && !shell.IsMouseOver) messages.Release(); } };
            messageOpen.GotKeyboardFocus += (_, __) => { if (messages.Active && !expanded) { shell.BorderBrush = Accent; shell.BorderThickness = new Thickness(1); } };
            messageOpen.LostKeyboardFocus += (_, __) => { if (messages.Active && !expanded) { shell.BorderBrush = material.Edge; shell.BorderThickness = new Thickness(SystemParameters.HighContrast ? 1 : 0); } };
            messageView.Children.Add(messageOpen);
        }
        private bool RenderNotificationContent()
        {
            hidingMessage = false;
            bool fresh = renderedMessage != messages.Current.Key;
            if (fresh) notificationActionNotice = null;
            renderedMessage = messages.Current.Key;
            if (fresh) messageScroll.ScrollToTop();
            string appName = string.IsNullOrWhiteSpace(messages.Current.AppName) ? "Bildirim" : messages.Current.AppName;
            bool showContent = settings.NotificationContent && SourceRules.Parse(settings.NotificationRules).Get(messages.Current.AppId) != 1;
            messageSender.Text = messages.Current.DisplayTitle(showContent);
            messageBody.Text = messages.Current.DisplayBody(showContent);
            bool matchedMedia = MatchesCurrentMedia(messages.Current);
            if (matchedMedia)
            {
                messages.Current.MediaTrackIdentity = media.TrackIdentity;
                lastMediaPreviewIdentity = media.TrackIdentity;
            }
            bool mediaContent = showContent && matchedMedia;
            if (mediaContent)
            {
                messageSender.Text = media.Title;
                messageBody.Text = MediaNotification.Detail(media.Artist, media.Album);
            }
            if (!string.IsNullOrEmpty(notificationActionNotice)) messageBody.Text = notificationActionNotice;
            messageBody.Visibility = string.IsNullOrWhiteSpace(messageBody.Text) ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetName(messageOpen, messages.Current.Sample ? "Örnek bildirimi kapat" : appName + " · " + messageSender.Text);
            AutomationProperties.SetHelpText(messageOpen, messageBody.Text + (AppMessage.IsWhatsApp(messages.Current.AppId)
                ? "\nWhatsApp'ı açar; sohbet uygulama içinden seçilir." : ""));
            Task iconUpdate = UpdateNotificationIconAsync(messages.Current.AppId, mediaContent ? media.Thumbnail : null, showContent);
            messageOpen.IsHitTestVisible = true;
            messageView.Visibility = Visibility.Visible; messageView.IsHitTestVisible = true;
            return fresh;
        }
        private double RenderInlineNotification()
        {
            if (!notificationInline) { notificationSizeRevision++; notificationInline = true; }
            RenderNotificationContent();
            expandedLayout.IsHitTestVisible = true;
            SetNotificationInset(dockEdge == DockEdge.Top ? new Thickness(0, NormalIslandGap, 0, 0) : new Thickness(0, 0, 0, NormalIslandGap), false);
            Grid.SetRow(messageView, 1); Grid.SetRowSpan(messageView, 1);
            messageView.Margin = new Thickness(8, 0, 8, 8);
            messageView.BeginAnimation(UIElement.OpacityProperty, null); messageView.Opacity = 1;
            messageView.RenderTransform = Transform.Identity;
            double availableHeight = placement == null ? SystemParameters.WorkArea.Height : placement.AvailableSize.Height;
            messageScroll.MaxHeight = Math.Max(1, Math.Min(96, availableHeight / 4) - 24);
            double width = PanelSizing.Fit(new Size(380, 1), placement == null ? new Size(SystemParameters.WorkArea.Width, availableHeight) : placement.AvailableSize).Width;
            messageView.Measure(new Size(Math.Max(1, width - 2), double.PositiveInfinity));
            return messageView.DesiredSize.Height;
        }
        private void RenderNotification(bool animate)
        {
            notificationInline = false;
            bool fresh = RenderNotificationContent();
            Grid.SetRow(messageView, 0); Grid.SetRowSpan(messageView, 2);
            messageView.Margin = new Thickness(0);
            bool motion = animate && SystemParameters.ClientAreaAnimation && !LightMode && (power == null || power.State.RenderAllowed);
            Size available = placement == null ? new Size(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height) : placement.AvailableSize;
            messageSender.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            messageBody.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double contentWidth = Math.Max(messageSender.DesiredSize.Width, messageBody.DesiredSize.Width);
            double width = Math.Min(Math.Min(available.Width, HostWidth), Math.Max(180, Math.Min(380, Math.Ceiling(contentWidth + 84))));
            double gap = Math.Min(8, Math.Max(0, available.Height - 64));
            double maxHeight = Math.Max(1, Math.Min(320, available.Height - gap));
            messageScroll.MaxHeight = Math.Max(1, maxHeight - 24);
            messageScroll.Measure(new Size(Math.Max(1, width - 84), double.PositiveInfinity));
            double height = Math.Min(maxHeight, Math.Max(64, Math.Ceiling(messageScroll.DesiredSize.Height + 24)));
            SetNotificationInset(dockEdge == DockEdge.Top ? new Thickness(0, gap, 0, 0) : new Thickness(0, 0, 0, gap), motion);
            shell.Background = material.Background; shell.BorderBrush = material.Edge; shell.Opacity = 1;
            shell.CornerRadius = new CornerRadius(height <= 76 ? height / 2 : 24); sheen.CornerRadius = shell.CornerRadius;
            shell.BorderThickness = new Thickness(SystemParameters.HighContrast || messageOpen.IsKeyboardFocusWithin ? 1 : 0);
            if (messageOpen.IsKeyboardFocusWithin) shell.BorderBrush = Accent;
            sheen.Background = Brushes.Transparent; sheen.Visibility = Visibility.Collapsed;
            shell.ToolTip = null; AutomationProperties.SetName(shell, (messages.Current.AppName ?? "Uygulama") + " bildirimi");
            compact.BeginAnimation(UIElement.OpacityProperty, null); panel.BeginAnimation(UIElement.OpacityProperty, null);
            compact.Opacity = panel.Opacity = 0; compact.IsHitTestVisible = expandedLayout.IsHitTestVisible = false;
            compact.Visibility = panel.Visibility = Visibility.Collapsed;
            messageView.Visibility = Visibility.Visible; messageView.IsHitTestVisible = true;
            bool resized = stateWidth != width || stateHeight != height;
            stateWidth = width; stateHeight = height; stateExpanded = expanded;
            if (motion && resized)
            {
                var duration = TimeSpan.FromMilliseconds(220);
                var w = new DoubleAnimation(width, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var h = new DoubleAnimation(height, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Timeline.SetDesiredFrameRate(w, 30); Timeline.SetDesiredFrameRate(h, 30);
                int revision = ++notificationSizeRevision;
                h.Completed += (_, __) =>
                {
                    if (closed || !messages.Active || revision != notificationSizeRevision) return;
                    shell.Width = width; shell.Height = height;
                    shell.BeginAnimation(FrameworkElement.WidthProperty, null); shell.BeginAnimation(FrameworkElement.HeightProperty, null);
                };
                shell.BeginAnimation(FrameworkElement.WidthProperty, w); shell.BeginAnimation(FrameworkElement.HeightProperty, h);
            }
            else if (!motion)
            {
                notificationSizeRevision++;
                shell.BeginAnimation(FrameworkElement.WidthProperty, null); shell.BeginAnimation(FrameworkElement.HeightProperty, null);
                shell.Width = width; shell.Height = height;
                messageView.BeginAnimation(UIElement.OpacityProperty, null); messageView.Opacity = 1;
                var previousScale = messageView.RenderTransform as ScaleTransform;
                if (previousScale != null) { previousScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); previousScale.BeginAnimation(ScaleTransform.ScaleYProperty, null); previousScale.ScaleX = previousScale.ScaleY = 1; }
            }
            if (fresh)
            {
                messageView.BeginAnimation(UIElement.OpacityProperty, null); messageView.Opacity = 1;
                var scale = new ScaleTransform(1, 1); messageView.RenderTransform = scale;
                if (motion)
                {
                    var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop }; Timeline.SetDesiredFrameRate(fade, 30);
                    var grow = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
                    string key = renderedMessage;
                    fade.Completed += (_, __) => { if (messages.Active && renderedMessage == key) messageView.BeginAnimation(UIElement.OpacityProperty, null); };
                    grow.Completed += (_, __) => { if (ReferenceEquals(messageView.RenderTransform, scale)) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); } };
                    Timeline.SetDesiredFrameRate(grow, 30); messageView.BeginAnimation(UIElement.OpacityProperty, fade);
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow); scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                }
            }
            UpdateLiveVisuals();
        }
        private void HideNotificationView(bool animate)
        {
            double gap = NormalIslandGap;
            SetNotificationInset(dockEdge == DockEdge.Top ? new Thickness(0, gap, 0, 0) : new Thickness(0, 0, 0, gap),
                animate && SystemParameters.ClientAreaAnimation && !LightMode && (power == null || power.State.RenderAllowed));
            compact.IsHitTestVisible = expandedLayout.IsHitTestVisible = true;
            if (messageView.Visibility != Visibility.Visible) return;
            messageView.IsHitTestVisible = false;
            if (animate && !notificationInline && !expanded && SystemParameters.ClientAreaAnimation && !LightMode)
            {
                if (hidingMessage) return;
                hidingMessage = true;
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)); Timeline.SetDesiredFrameRate(fade, 30);
                fade.Completed += (_, __) => { if (!messages.Active) { hidingMessage = false; messageView.Visibility = Visibility.Collapsed; ClearNotificationContent(); messageView.BeginAnimation(UIElement.OpacityProperty, null); } };
                messageView.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else { hidingMessage = false; messageView.BeginAnimation(UIElement.OpacityProperty, null); messageView.Opacity = 0; messageView.Visibility = Visibility.Collapsed; ClearNotificationContent(); }
            notificationInline = false;
        }
        private void ClearNotificationContent()
        {
            notificationDragStart = null; notificationDragging = false; notificationTranslation.BeginAnimation(TranslateTransform.XProperty, null); notificationTranslation.X = 0;
            renderedMessage = renderedIconApp = notificationActionNotice = null;
            notificationIconRevision++;
            messageSender.Text = messageBody.Text = "";
            AutomationProperties.SetHelpText(messageOpen, "");
            messageAppIcon.Source = null; messageAppIcon.Visibility = Visibility.Collapsed; messageIconFallback.Visibility = Visibility.Visible;
        }
        private void DismissNotificationSwipe(double distance)
        {
            string key = notificationDragKey;
            bool motion = SystemParameters.ClientAreaAnimation && !LightMode && (power == null || power.State.RenderAllowed);
            if (!motion) { notificationTranslation.X = 0; if (messages.Active && messages.Current.Key == key) messages.Expire(); return; }
            messageOpen.IsHitTestVisible = false;
            var slide = new DoubleAnimation(distance, Math.Sign(distance) * Math.Max(shell.ActualWidth, 80), TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var fade = new DoubleAnimation(messageView.Opacity, 0, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop };
            slide.Completed += (_, __) => {
                notificationTranslation.BeginAnimation(TranslateTransform.XProperty, null); notificationTranslation.X = 0;
                messageView.BeginAnimation(UIElement.OpacityProperty, null); messageView.Opacity = 1; messageOpen.IsHitTestVisible = true;
                if (messages.Active && messages.Current.Key == key) messages.Expire();
            };
            Timeline.SetDesiredFrameRate(slide, 30); Timeline.SetDesiredFrameRate(fade, 30);
            notificationTranslation.BeginAnimation(TranslateTransform.XProperty, slide); messageView.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        private void SetNotificationInset(Thickness target, bool animate)
        {
            if (notificationInset == target && animate) return;
            Thickness current = shell.Margin;
            notificationInset = target;
            int revision = ++notificationInsetRevision;
            shell.BeginAnimation(FrameworkElement.MarginProperty, null); shell.Margin = target;
            if (!animate || current == target) return;
            var inset = new ThicknessAnimation(current, target, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            inset.Completed += (_, __) => { if (revision == notificationInsetRevision) shell.BeginAnimation(FrameworkElement.MarginProperty, null); };
            Timeline.SetDesiredFrameRate(inset, 30); shell.BeginAnimation(FrameworkElement.MarginProperty, inset);
        }
        private void PreviewNotification()
        {
            CloseSettings();
            if (closed || !MessageAllowed) { ShowSettingsNotice("Bildirim şu an gizleme tercihleriniz nedeniyle gösterilemiyor."); return; }
            messages.Cancel(false);
            messages.Offer(new AppMessage { Key = "sample:" + Guid.NewGuid(), AppName = "Morf", Sender = "Yeni bildirim", Body = "Bildirimler burada görünür.", Created = DateTimeOffset.UtcNow, Sample = true }, true);
        }
        private async Task OpenNotificationAsync()
        {
            if (!messages.Active || openingMessage) return;
            if (messages.Current.Sample) { messages.Cancel(); return; }
            string selected = messages.Current.Key;
            openingMessage = true;
            try
            {
                messages.Hold();
                string result = await applicationLauncher.OpenNotificationAppAsync(messages.Current.AppId, messages.Current.AppName);
                if (!closed)
                {
                    ShowSettingsNotice(result);
                    if (messages.Active && messages.Current.Key == selected)
                    {
                        if (result.Contains("iletildi")) messages.Cancel();
                        else { notificationActionNotice = "Windows bildirim merkezinden açabilirsiniz."; UpdateState(false); }
                    }
                }
            }
            finally { openingMessage = false; if (!closed && messages.Active && !shell.IsMouseOver) messages.Release(); }
        }
        private async Task UpdateNotificationIconAsync(string appId, ImageSource cover, bool showContent)
        {
            appId = appId ?? "";
            if (!showContent || cover != null)
            {
                notificationIconRevision++; renderedIconApp = null;
                messageAppIcon.Source = cover;
                messageAppIcon.Visibility = cover == null ? Visibility.Collapsed : Visibility.Visible;
                messageIconFallback.Visibility = cover == null ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            if (renderedIconApp == appId) return;
            int revision = ++notificationIconRevision;
            renderedIconApp = appId; messageAppIcon.Source = null; messageAppIcon.Visibility = Visibility.Collapsed; messageIconFallback.Visibility = Visibility.Visible;
            try
            {
                var bitmap = await MediaAppIcon.GetAsync(appId);
                if (closed || revision != notificationIconRevision || renderedIconApp != appId || !messages.Active || (messages.Current.AppId ?? "") != appId || bitmap == null) return;
                messageAppIcon.Source = bitmap; messageAppIcon.Visibility = Visibility.Visible; messageIconFallback.Visibility = Visibility.Collapsed;
            }
            catch { /* An unavailable app icon retains the static bell. */ }
        }
        private async Task ConnectNotificationsAsync(bool request)
        {
            if (request && bannerRouting != null) bannerRouting.Retry();
            await appNotifications.RefreshAsync(request);
            if (settingsWindow != null) settingsWindow.RefreshRuntimeStatus();
        }
        private void OpenWindowsNotificationSettings() { OpenWindowsNotificationSettings(false); }
        private void OpenWindowsNotificationSettings(bool access)
        {
            try { using (var process = Process.Start(new ProcessStartInfo(access ? "ms-settings:privacy-notifications" : "ms-settings:notifications") { UseShellExecute = true })) { } }
            catch { ShowSettingsNotice("Windows bildirim ayarları açılamadı."); }
        }
        private void OfferNotification(AppMessage message)
        {
            pendingNotification = null;
            if (!settings.WindowsNotifications || message == null || !MessageContextAllowed) return;
            if (SourceRules.Parse(settings.NotificationRules).Get(message.AppId) == 2) return;
            if (bannerRouting != null && !bannerRouting.CanPresent(message.AppId)) return;
            if (MatchesCurrentMedia(message))
            {
                if (expanded || lastMediaPreviewIdentity == media.TrackIdentity) return;
                message.MediaTrackIdentity = media.TrackIdentity;
            }
            if (MessageAllowed) messages.Offer(message, true);
            else if (SettingsOpen && DateTimeOffset.UtcNow - message.Created <= TimeSpan.FromSeconds(10)) pendingNotification = message;
        }
        internal void FlushPendingNotification()
        {
            var pending = pendingNotification; pendingNotification = null;
            if (settings.WindowsNotifications && MessageAllowed && pending != null && SourceRules.Parse(settings.NotificationRules).Get(pending.AppId) != 2
                && (bannerRouting == null || bannerRouting.CanPresent(pending.AppId))
                && !(MatchesCurrentMedia(pending) && (expanded || lastMediaPreviewIdentity == media.TrackIdentity))) messages.Offer(pending, true);
        }
        private bool MatchesCurrentMedia(AppMessage message)
        {
            return MediaNotification.Matches(message, media.SourceId,
                media.Title, media.Artist, media.Album, media.TrackIdentity);
        }
        private bool IsMediaNotification(AppMessage message)
        {
            return message != null && (message.MediaTrackIdentity != null || MatchesCurrentMedia(message));
        }
        private void UpdateNotificationRouting()
        {
            if (bannerRouting == null || appNotifications == null) return;
            if (routingMasterPreference != settings.WindowsNotifications) { routingMasterPreference = settings.WindowsNotifications; bannerRouting.Retry(); }
            bool allowed = settings.WindowsNotifications && appNotifications.ReadingReady && !closed && mixerWindow == null && IsVisible && !temporaryHide.Active && !Suppressed
                && (placement == null || (placement.OnCurrentDesktop && !(settings.HideInFullscreen && !placement.RuleShown && placement.Fullscreen)))
                && (power == null || power.State.RenderAllowed) && (!settings.RespectQuietState || NotificationPolicy.AllowsPeek());
            bannerRouting.SetEnabled(allowed);
        }
        private string NotificationRuntimeStatus()
        {
            return settings.WindowsNotifications && appNotifications.ReadingReady && bannerRouting != null ? bannerRouting.Status : appNotifications.Status;
        }
        private sealed class NotificationTextBrush : IValueConverter
        {
            internal static readonly NotificationTextBrush Instance = new NotificationTextBrush();
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (!(value is Color)) return DependencyProperty.UnsetValue;
                var brush = new SolidColorBrush((Color)value); brush.Freeze(); return brush;
            }
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return Binding.DoNothing; }
        }
    }
}
