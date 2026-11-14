using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Widgates
{
    internal sealed partial class SettingsWindow
    {
        private TextBlock notificationStatus;
        private TextBlock appleMusicStatus, spotifyStatus;
        private Button notificationConnect;
        private Border notificationDrawer;
        private StackPanel notificationDetails;
        private bool notificationDrawerBuilt;
        private int notificationDrawerRevision;
        private void ApplicationsControls()
        {
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var enabled = new CheckBox { IsChecked = settings.WindowsNotifications, Width = 40, Height = 24, VerticalAlignment = VerticalAlignment.Center, Cursor = System.Windows.Input.Cursors.Hand };
            AutomationProperties.SetName(enabled, "Windows bildirimlerini Morf'ta göster");
            DockPanel.SetDock(enabled, Dock.Right); header.Children.Add(enabled);
            var icon = IslandWindow.SourceIcon(null, "M 4 11 L 4 6 A 4 4 0 0 1 12 6 L 12 11 L 14 13 L 2 13 Z M 6 15 L 10 15", 26); icon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(icon, Dock.Left); header.Children.Add(icon);
            header.Children.Add(Labels("Windows bildirimleri", "Gelen bildirimleri Morf'ta göster.")); page.Children.Add(header);
            notificationDetails = new StackPanel();
            notificationDrawer = new Border { Child = notificationDetails, ClipToBounds = true, Height = 0, Opacity = 0, Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(notificationDrawer, "Bildirim ayarları"); page.Children.Add(notificationDrawer);
            enabled.Click += async (_, __) =>
            {
                settings.WindowsNotifications = enabled.IsChecked == true; apply(true);
                if (!settings.WindowsNotifications && notificationDrawer.IsKeyboardFocusWithin) enabled.Focus();
                UpdateApplicationsDrawer(true); RefreshRuntimeStatus();
                if (settings.WindowsNotifications && actions.ConnectNotifications != null
                    && actions.NotificationAvailable != null && actions.NotificationAvailable())
                {
                    await actions.ConnectNotifications(); RefreshRuntimeStatus();
                }
            };
            UpdateApplicationsDrawer(false);
            page.Children.Add(new Border { Height = 1, Background = ThemePalette.Stroke, Margin = new Thickness(0, 20, 0, 18) });
            page.Children.Add(Labels("Müzik uygulamaları", "Windows medya oturumu yayımlandığında otomatik bağlanır."));
            appleMusicStatus = ApplicationStatus("Apple Music", MediaAppIcon.Identity(MediaApplication.AppleMusic));
            spotifyStatus = ApplicationStatus("Spotify", MediaAppIcon.Identity(MediaApplication.Spotify));
            var media = Button("Medya ve ses ayarları", () => SelectPage(2)); media.HorizontalAlignment = HorizontalAlignment.Left;
            media.Margin = new Thickness(0, 10, 0, 0); page.Children.Add(media);
        }

        private void BuildWhatsAppDetails()
        {
            if (notificationDrawerBuilt) return;
            notificationDrawerBuilt = true;
            var target = notificationDetails;
            notificationStatus = Text("", 11); notificationStatus.Foreground = ThemePalette.Muted;
            notificationStatus.Margin = new Thickness(0, 0, 0, 10); target.Children.Add(notificationStatus);
            var connections = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var connect = notificationConnect = Button("Windows iznini bağla", async () =>
            {
                if (actions.ConnectNotifications != null) await actions.ConnectNotifications();
                RefreshRuntimeStatus();
            });
            connect.IsEnabled = actions.NotificationAvailable != null && actions.NotificationAvailable();
            connections.Children.Add(connect);
            var windows = Button("Windows bildirim ayarları", () => { if (actions.OpenNotificationSettings != null) actions.OpenNotificationSettings(); });
            connections.Children.Add(windows);
            var preview = Button("Örnek bildirim göster", () => { if (actions.PreviewNotification != null) actions.PreviewNotification(); });
            connections.Children.Add(preview);
            target.Children.Add(connections);
            var singleDisplay = Text("Windows başlıkları otomatik yönetilir; kapattığınızda önceki ayarlar geri gelir. Sistem uyarıları Windows'ta kalır.", 11);
            singleDisplay.Foreground = ThemePalette.Muted; singleDisplay.Margin = new Thickness(0, 0, 0, 10); target.Children.Add(singleDisplay);
            FlatChoice(target, "Gösterim süresi", new[] { "3 saniye", "5 saniye", "8 saniye" }, settings.NotificationSeconds == 3 ? 0 : settings.NotificationSeconds == 8 ? 2 : 1,
                index => settings.NotificationSeconds = new[] { 3, 5, 8 }[index]);
            FlatChoice(target, "İçerik", new[] { "Başlık ve içerik", "Yalnızca uygulama" }, settings.NotificationContent ? 0 : 1,
                index => settings.NotificationContent = index == 0);
            SourceRuleControls(true, target);
        }

        private void UpdateApplicationsDrawer(bool animate)
        {
            bool open = settings.WindowsNotifications;
            if (open) BuildWhatsAppDetails();
            if (!open) CloseApplicationsPickers();
            if (!animate || (!IsVisible && Visibility != Visibility.Visible) || !SystemParameters.ClientAreaAnimation || settings.Lightweight == LightweightMode.Always
                || (actions.MotionAllowed != null && !actions.MotionAllowed())) { FinishApplicationsMotion(); return; }
            double from = notificationDrawer.ActualHeight, opacity = notificationDrawer.Opacity;
            notificationDrawer.BeginAnimation(FrameworkElement.HeightProperty, null); notificationDrawer.BeginAnimation(UIElement.OpacityProperty, null);
            notificationDrawer.Visibility = Visibility.Visible;
            notificationDrawer.IsEnabled = notificationDrawer.IsHitTestVisible = open;
            notificationDetails.Measure(new Size(Math.Max(1, page.ActualWidth), double.PositiveInfinity));
            double target = open ? notificationDetails.DesiredSize.Height : 0;
            notificationDrawer.Height = target; notificationDrawer.Opacity = open ? 1 : 0;
            int revision = ++notificationDrawerRevision;
            var height = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(220))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            height.Completed += (_, __) => { if (revision == notificationDrawerRevision) FinishApplicationsMotion(); };
            var fade = new DoubleAnimation(opacity, open ? 1 : 0, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop };
            Timeline.SetDesiredFrameRate(height, 60); Timeline.SetDesiredFrameRate(fade, 60);
            notificationDrawer.BeginAnimation(FrameworkElement.HeightProperty, height); notificationDrawer.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        internal void FinishApplicationsMotion()
        {
            notificationDrawerRevision++;
            if (notificationDrawer == null) return;
            notificationDrawer.BeginAnimation(FrameworkElement.HeightProperty, null); notificationDrawer.BeginAnimation(UIElement.OpacityProperty, null);
            bool open = settings.WhatsAppNotifications;
            notificationDrawer.Height = open ? double.NaN : 0;
            notificationDrawer.Opacity = open ? 1 : 0;
            notificationDrawer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            notificationDrawer.IsEnabled = notificationDrawer.IsHitTestVisible = open;
        }

        private void ResetApplicationsDrawer()
        {
            CloseApplicationsPickers(); FinishApplicationsMotion(); notificationDrawer = null; notificationDetails = null; notificationDrawerBuilt = false;
        }

        private void CloseApplicationsPickers()
        {
            if (notificationDetails == null) return;
            foreach (var row in notificationDetails.Children.OfType<StackPanel>())
                foreach (var picker in row.Children.OfType<ComboBox>()) picker.IsDropDownOpen = false;
        }
        private TextBlock ApplicationStatus(string name, string identity)
        {
            var row = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
            var icon = IslandWindow.SourceIcon(identity, null, 24); icon.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
            var labels = new StackPanel(); labels.Children.Add(Text(name, 12, true));
            var status = Text("", 11); status.Foreground = ThemePalette.Muted; status.Margin = new Thickness(0, 4, 0, 0);
            labels.Children.Add(status); row.Children.Add(labels); page.Children.Add(row); return status;
        }
        private void FlatChoice(StackPanel target, string label, string[] items, int selected, Action<int> change)
        {
            var row = Labels(label, ""); row.Margin = new Thickness(0, 8, 0, 12);
            var picker = new ComboBox { ItemsSource = items, SelectedIndex = selected, MinHeight = 32, MinWidth = 160,
                HorizontalAlignment = HorizontalAlignment.Left, Foreground = ThemePalette.Text, Margin = new Thickness(0, 6, 0, 0) };
            AutomationProperties.SetName(picker, label); IslandWindow.StyleSessionPicker(picker);
            picker.SelectionChanged += (_, __) => { if (picker.SelectedIndex >= 0) { change(picker.SelectedIndex); apply(true); } };
            row.Children.Add(picker); target.Children.Add(row);
        }
    }
}
