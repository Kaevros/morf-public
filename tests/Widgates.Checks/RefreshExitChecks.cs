using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Widgates
{
    internal static class RefreshExitChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static async Task RunAsync(string output)
        {
            int exits = 0, refreshes = 0;
            var model = new WidgetSettings { PlayerVolume = true };
            string preferences = Path.Combine(output, "player-volume.settings"); model.Save(preferences);
            Check(WidgetSettings.Load(preferences).PlayerVolume && model.Clone().PlayerVolume && !new WidgetSettings().PlayerVolume,
                "Player volume preference persists and defaults to system for existing users");
            var pending = new TaskCompletionSource<string>();
            var dialog = new SettingsWindow(model, new SettingsActions { Apply = _ => { }, SelectTheme = _ => true, ConfigureStartup = _ => true,
                Exit = () => exits++, ShelfCount = () => 0, RefreshApplication = () => { refreshes++; return pending.Task; } });
            try
            {
                dialog.RequestExitConfirmation();
                Check(exits == 0 && Field<Border>(dialog, "exitOverlay") != null, "An empty shelf still opens the modal exit popup");
                Check(!Field<Grid>(dialog, "sidebar").IsEnabled && !Field<Grid>(dialog, "body").IsEnabled,
                    "The exit popup disables the settings underneath it");
                var cancel = Nodes(dialog).OfType<Button>().Single(button => button.Content as string == "Vazgeç");
                Check(cancel.IsDefault && cancel.IsCancel, "Enter and Escape use the safe cancellation action"); Click(cancel);
                Check(exits == 0 && Field<Border>(dialog, "exitOverlay") == null && Field<Grid>(dialog, "body").IsEnabled,
                    "Cancelling the popup preserves the settings window and session");
                dialog.RequestExitConfirmation(); dialog.RequestExitConfirmation();
                Check(Nodes(dialog).OfType<Button>().Count(button => button.Content as string == "Evet, Morf'tan çık") == 1,
                    "Repeated exit requests reuse one confirmation popup");
                Click(Nodes(dialog).OfType<Button>().Single(button => button.Content as string == "Evet, Morf'tan çık"));
                Check(exits == 1 && Field<Border>(dialog, "exitOverlay") == null, "Only explicit exit confirmation invokes the exit action");
                var refresh = Field<Button>(dialog, "refreshButton");
                Click(refresh); Click(refresh);
                Check(refreshes == 1 && !refresh.IsEnabled && !Field<Button>(dialog, "compactRefresh").IsEnabled,
                    "The refresh action prevents duplicate operations in both settings layouts");
                pending.SetResult("Yenilendi"); await Task.Delay(20);
                Check(refresh.IsEnabled && refreshes == 1, "Refresh completion restores the action without restarting the process");
            }
            finally { dialog.Close(); }
        }
        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; native popup and volume UI; no real app exit or playback command");
            await RunAsync(output);
            var model = new WidgetSettings();
            var dialog = new SettingsWindow(model, new SettingsActions { Apply = _ => { }, SelectTheme = _ => true, ConfigureStartup = _ => true,
                ShelfCount = () => 2, Exit = () => { } });
            try
            {
                dialog.Show(); await Task.Delay(150);
                foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
                {
                    typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, theme);
                    ThemePalette.Refresh();
                    foreach (double width in new[] { 320.0, 760.0 })
                    {
                        dialog.Width = width; dialog.Height = 640; dialog.RequestExitConfirmation(); dialog.UpdateLayout();
                        var overlay = Field<Border>(dialog, "exitOverlay"); var card = (Border)overlay.Child;
                        Check(card.ActualWidth <= dialog.ActualWidth - 30 && card.ActualHeight <= dialog.ActualHeight,
                            theme + " exit popup fits " + width + " DIP settings");
                        Capture((FrameworkElement)dialog.Content, Path.Combine(output, "exit-" + theme + "-" + width + ".png"));
                        Click(Nodes(dialog).OfType<Button>().Single(button => button.Content as string == "Vazgeç"));
                    }
                }
            }
            finally { dialog.Close(); }

            var settings = new WidgetSettings { AutoPauseOthers = false, HideInGames = false, HideWhenIdle = false, Lightweight = LightweightMode.Always };
            var widget = new IslandWindow(settings);
            try
            {
                widget.IsHitTestVisible = false; widget.Show(); await Task.Delay(300);
                string sample = Path.Combine(output, "refresh-shelf.txt"); File.WriteAllText(sample, "Keep this file.");
                var shelf = Field<FileShelf>(widget, "shelf"); shelf.Add(new[] { sample }); Call(widget, "UpdateShelf");
                var state = Field<PanelState>(widget, "panelState"); state.Persistent = true; Call(widget, "SetExpanded", true);
                var message = (Task<string>)Call(widget, "RefreshApplicationAsync"); await message; await Task.Delay(200);
                Check(shelf.Paths.Count == 1 && File.ReadAllText(sample) == "Keep this file." && state.Persistent,
                    "Application refresh preserves the live shelf, original file and pinned panel");
                widget.UpdateLayout();
                Check(!Nodes(widget).OfType<ComboBox>().Any(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Ses hedefi"),
                    "The player keeps its volume target selection outside the inline panel");
                Capture(Field<Border>(widget, "shell"), Path.Combine(output, "volume-panel.png"));
                settings.PlayerVolume = true; Call(widget, "ApplySettings", false);
                Check(Field<TextBlock>(widget, "volumeTargetLabel").Text.Contains("Oynatıcı"), "The volume slider preserves the target preference from settings");
                // Keep media read-only: only a nonexistent player is exercised for the unavailable UI path.
                Call(widget, "UpdateVolumeDisplay", new VolumeState()); widget.UpdateLayout();
                Check(!Field<Slider>(widget, "volumeSlider").IsEnabled && Field<TextBlock>(widget, "volumeTargetLabel").Text.Contains("Sistem sesi değiştirilmez"),
                    "Unavailable player volume disables the slider and explains that system volume stays unchanged");
                settings.WhatsAppNotifications = true; settings.RespectQuietState = false;
                var settingsView = new SettingsWindow(settings, new SettingsActions { Apply = _ => { }, SelectTheme = _ => true, ConfigureStartup = _ => true });
                try
                {
                    typeof(IslandWindow).GetField("settingsWindow", Private).SetValue(widget, settingsView);
                    Call(widget, "OfferNotification", new AppMessage { Key = "deferred", Sender = "Sample", Body = "Test", Created = DateTimeOffset.UtcNow, Sample = true });
                    Check(Field<AppMessage>(widget, "pendingNotification") != null && !Field<AppMessagePeek>(widget, "messages").Active,
                        "A fresh notification can wait while settings are open");
                    typeof(IslandWindow).GetField("settingsWindow", Private).SetValue(widget, null);
                    Call(widget, "FlushPendingNotification");
                    Check(Field<AppMessage>(widget, "pendingNotification") == null && Field<AppMessagePeek>(widget, "messages").Active,
                        "Closing settings consumes the deferred notification once");
                    Field<AppMessagePeek>(widget, "messages").Cancel(false);
                    typeof(IslandWindow).GetField("settingsWindow", Private).SetValue(widget, settingsView);
                    Call(widget, "OfferNotification", new AppMessage { Key = "stale", Sender = "Sample", Created = DateTimeOffset.UtcNow.AddSeconds(-11), Sample = true });
                    Check(Field<AppMessage>(widget, "pendingNotification") == null, "Old notifications are never retained for a later replay");
                    Field<PowerStateMonitor>(widget, "power").State.Locked = true;
                    Call(widget, "OfferNotification", new AppMessage { Key = "locked", Sender = "Sample", Created = DateTimeOffset.UtcNow, Sample = true });
                    Check(Field<AppMessage>(widget, "pendingNotification") == null, "Session lock does not accumulate deferred messages");
                    Field<PowerStateMonitor>(widget, "power").State.Locked = false;
                }
                finally { typeof(IslandWindow).GetField("settingsWindow", Private).SetValue(widget, null); settingsView.Close(); }
            }
            finally { widget.Close(); }
        }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Private).Invoke(owner, args); }
        private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        private static System.Collections.Generic.IEnumerable<DependencyObject> Nodes(DependencyObject root)
        {
            if (root is Window) root = ((Window)root).Content as DependencyObject;
            if (root == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var node in Nodes(child)) yield return node; }
        }
        private static void Check(bool success, string text) { if (!success) throw new InvalidOperationException(text); Console.WriteLine("PASS " + text); }
        private static void Capture(FrameworkElement element, string path)
        {
            element.UpdateLayout(); var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
