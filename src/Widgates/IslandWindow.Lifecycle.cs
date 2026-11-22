using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Widgates
{
    internal sealed partial class IslandWindow
    {
        private TrayIcon tray;
        private TemporaryHide temporaryHide;
        private bool shellServices, exitConfirmed;
        internal void EnableShellServices()
        {
            shellServices = true;
            bannerRouting = new NotificationBannerRouting(Dispatcher);
            bannerRouting.ConfigureRules(settings.NotificationRules);
            bannerRouting.Changed += () => { if (!closed && settingsWindow != null) settingsWindow.RefreshRuntimeStatus(); };
            UpdateNotificationRouting();
            SourceInitialized += (_, __) =>
            {
                var source = PresentationSource.FromVisual(this) as HwndSource;
                if (source == null) return;
                tray = new TrayIcon(source, OpenFromShell, OpenSettings, HideTemporarily, RequestExit);
                tray.SetEnabled(settings.ShowTrayIcon);
            };
        }
        internal void OpenFromShell()
        {
            if (closed) return;
            temporaryHide.Cancel();
            if (SettingsOpen) { settingsWindow.Activate(); return; }
            if (Suppressed) return;
            OpenPinned();
        }
        private void HideTemporarily()
        {
            CloseSettings(); peek.Cancel(false); SetExpanded(false); temporaryHide.Start();
        }
        private void RequestExit()
        {
            OpenSettings(); if (settingsWindow != null) settingsWindow.RequestExitConfirmation();
        }
        private async System.Threading.Tasks.Task<string> RefreshApplicationAsync()
        {
            if (closed) return "Morf kapanıyor.";
            hover.Cancel(); collapse.Stop();
            OnDisplayGeometryChanged(); placement.RefreshDisplayGeometry();
            await media.RefreshConnectionAsync();
            if (closed) return "Morf kapandı.";
            await ConnectNotificationsAsync(false);
            UpdateMedia(); UpdateShelf();
            return media.Ready ? "Görünüm ve bağlantılar yenilendi. Raf ve ayarlar korundu." : "Görünüm yenilendi; Windows medya bağlantısı kurulamadı. Raf ve ayarlar korundu.";
        }
        internal void AllowSessionExit()
        {
            SaveStopwatch(ClockTools.GetTickCount64());
            exitConfirmed = true;
        }
        private void ApplyPlayerWidth(double width)
        {
            bool narrow = width < 320;
            Grid.SetColumn(primaryControls, narrow ? 0 : 1); Grid.SetColumnSpan(primaryControls, narrow ? 3 : 1);
            Grid.SetRow(shuffle, narrow ? 1 : 0); Grid.SetRow(extraControls, narrow ? 1 : 0);
        }
    }
}
