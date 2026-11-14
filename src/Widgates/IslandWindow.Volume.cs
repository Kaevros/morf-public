using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Widgates
{
    internal sealed partial class IslandWindow
    {
        private readonly TextBlock volumeTargetLabel;
        private readonly SemaphoreSlim volumeGate = new SemaphoreSlim(1);
        private string observedVolumeTarget, volumeGestureTarget, pendingVolumeTarget;
        private int volumeRevision;
        private readonly AudioMirror audioMirror = new AudioMirror();
        private AudioMixerWindow mixerWindow;
        private void OpenAudioMixer()
        {
            if (closed || Suppressed) return;
            if (mixerWindow != null) { mixerWindow.Activate(); return; }
            collapse.Stop(); messages.Cancel(false); sessions.IsDropDownOpen = false;
            var window = new AudioMixerWindow(audioMirror) { Owner = this, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual };
            window.ContentRendered += (_, __) => MonitorCatalog.PlaceAudioMixer(window, this, settings.MonitorDevice, dockEdge == DockEdge.Top);
            bool repositioning = false;
            window.SizeChanged += (_, __) => {
                if (repositioning) return; repositioning = true;
                Dispatcher.BeginInvoke(new Action(() => { repositioning = false; if (!closed && ReferenceEquals(mixerWindow, window) && window.IsVisible) MonitorCatalog.PlaceAudioMixer(window, this, settings.MonitorDevice, dockEdge == DockEdge.Top); }));
            };
            mixerWindow = window;
            window.Closed += (_, __) => { mixerWindow = null; if (!closed) { UpdateNotificationRouting(); Task refreshVolume = UseVolumeAsync(volume => volume.ReadAsync()); if (!pinned && !shell.IsMouseOver) collapse.Start(); } };
            UpdateNotificationRouting();
            window.Show();
        }
        private string CurrentVolumeTarget { get { return settings.PlayerVolume ? "player:" + (media.Active == null ? "" : media.Active.SourceAppUserModelId) : "system"; } }
        private string PlayerVolumeName { get { return media.Active == null ? "Oynatıcı" : MediaSourceNames.Friendly(media.Active.SourceAppUserModelId); } }
        private void SynchronizeVolumeTarget()
        {
            if (volumeTargetLabel == null || observedVolumeTarget == CurrentVolumeTarget) return;
            observedVolumeTarget = CurrentVolumeTarget; Interlocked.Increment(ref volumeRevision);
            pendingVolumeDelta = 0; volumeGestureTarget = null;
            volumePercent.Text = "—"; volumeSlider.IsEnabled = muteButton.IsEnabled = false;
            volumeTargetLabel.Text = settings.PlayerVolume ? PlayerVolumeName + " · Ses düzeyini okumak için hoparlörü kullanın" : "Windows'un genel ses düzeyi";
            volumeTargetLabel.Visibility = System.Windows.Visibility.Collapsed;
            if (expanded && volumePanel != null && volumePanel.Visibility == System.Windows.Visibility.Visible)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () => { if (!closed && volumePanel.IsVisible) await UseVolumeAsync(volume => volume.ReadAsync()); }));
        }
        private async Task CommitVolumeLevelAsync()
        {
            string target = volumeGestureTarget; volumeGestureTarget = null;
            if (target == null) return;
            float level = (float)volumeSlider.Value / 100;
            await UseVolumeAsync(volume => volume.SetLevelAsync(level), target);
        }
        private async Task UseVolumeAsync(Func<SystemVolume, Task<VolumeState>> operation, string expectedTarget = null)
        {
            SynchronizeVolumeTarget();
            string target = CurrentVolumeTarget; int revision = volumeRevision;
            if (expectedTarget != null && expectedTarget != target) return;
            await volumeGate.WaitAsync();
            try
            {
                Func<bool> valid = () => !closed && Volatile.Read(ref volumeRevision) == revision;
                if (!valid()) return;
                var controller = new SystemVolume(() => target == "system" ? (IVolumeEndpoint)new WindowsVolumeEndpoint()
                    : new WindowsPlayerVolumeEndpoint(target.Substring(7)), valid);
                VolumeState state = await operation(controller);
                if (valid() && volumeGestureTarget == null) UpdateVolumeDisplay(state);
            }
            finally { volumeGate.Release(); }
        }
    }
}
