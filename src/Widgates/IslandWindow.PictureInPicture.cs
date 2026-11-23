using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace Widgates
{
    internal sealed partial class IslandWindow
    {
        private PictureInPictureBridge pictureInPicture;
        private readonly PictureInPicturePlacement pipPlacement = new PictureInPicturePlacement();
        private bool pipRegistered;
        private bool pipPreviouslyConnected;
        private bool pipSetupOffered;
        private string pipSetupFailure;
        private bool CanOpenPictureInPicture
        {
            get { return settings.PictureInPicture && pictureInPicture != null
                && PictureInPictureBridge.Matches(pictureInPicture.Current, media.SourceId, media.Title, media.Artist); }
        }
        private void ConfigurePictureInPicture()
        {
            if (!shellServices) return; // Offscreen previews never register integrations or open IPC.
            if (settings.PictureInPicture && !pipRegistered)
                pipRegistered = PictureInPictureRegistration.Ensure(out pipSetupFailure);
            if (pictureInPicture == null && settings.PictureInPicture && pipRegistered)
            {
                pictureInPicture = new PictureInPictureBridge();
                pictureInPicture.Changed += () =>
                {
                    if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => { if (!closed) UpdatePictureInPictureAction(); }));
                };
            }
            if (pictureInPicture != null) pictureInPicture.Configure(settings.PictureInPicture && pipRegistered, settings.PictureInPictureSeekSeconds, settings.PictureInPictureAdvanced);
            UpdatePictureInPictureAction();
        }
        private void UpdatePictureInPictureAction()
        {
            pipPlacement.Track(settings.PictureInPicture && pictureInPicture != null ? pictureInPicture.Current : null);
            if (pictureInPicture != null && pictureInPicture.Current != null) pipPreviouslyConnected = true;
            bool available = CanOpenPictureInPicture;
            MediaApplication known;
            bool music = MediaAppLauncher.TryIdentify(media.SourceId, out known);
            mediaApplicationButton.Focusable = available || music;
            expandedCoverBorder.Cursor = available ? Cursors.Hand : null;
            expandedCoverBorder.ToolTip = available ? "Videoyu PiP penceresinde göster" : null;
            compactCoverBorder.Cursor = available ? Cursors.Hand : null;
            compactCoverBorder.ToolTip = available ? "Videoyu PiP penceresinde göster" : null;
            AutomationProperties.SetName(mediaApplicationButton, available ? "Videoyu PiP penceresinde göster" : "Müzik uygulamasını aç");
        }
        private async Task OpenPictureInPictureAsync()
        {
            if (!CanOpenPictureInPicture) return;
            CancelTrackSwipe();
            // Opening a video window must not leave an ordinary click latch behind.
            if (!panelState.Persistent) { pinned = false; collapse.Start(); }
            var session = media.Active;
            string track = media.TrackIdentity;
            bool sent = await pictureInPicture.OpenAsync(media.SourceId, media.Title, media.Artist);
            if (!closed && Equals(session, media.Active) && track == media.TrackIdentity && !sent)
            {
                mediaNotice.Text = "Video bağlantısı değişti. Tarayıcıdaki Morf eklentisinden yeniden açabilirsiniz.";
                mediaNotice.Visibility = Visibility.Visible;
            }
        }
        private string PictureInPictureStatus()
        {
            if (!settings.PictureInPicture) return "Kapalı · PiP bağlantısı çalışmıyor.";
            if (!string.IsNullOrEmpty(pipSetupFailure)) return pipSetupFailure;
            if (pictureInPicture != null && !string.IsNullOrEmpty(pictureInPicture.Failure)) return pictureInPicture.Failure;
            if (pictureInPicture != null && pictureInPicture.Current != null) return "Video bağlı";
            return "Video oynatıcısındaki Morf düğmesiyle açın.";
        }
        internal void OfferPictureInPictureSetup()
        {
            if (pipSetupOffered || closed) return;
            pipSetupOffered = true;
            ChoosePictureInPictureBrowser();
        }
        private void SetupPictureInPicture()
        {
            if (settings.PictureInPicture) ChoosePictureInPictureBrowser();
        }
        private void ChoosePictureInPictureBrowser()
        {
            var choice = PictureInPictureSetup.Choose(settingsWindow);
            if (choice == null) return;
            if (!settings.PictureInPicture) { settings.PictureInPicture = true; ApplySettings(true); }
            else ConfigurePictureInPicture();
            if (!pipRegistered) { MessageBox.Show(pipSetupFailure, "Morf · PiP"); return; }
            PictureInPictureRegistration.OpenFolder();
            if (!choice.Open()) MessageBox.Show("Tarayıcınızın uzantılar sayfasından açılan klasörü yükleyin.", "Morf · PiP");
        }
    }
}
