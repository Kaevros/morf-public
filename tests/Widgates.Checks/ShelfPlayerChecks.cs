using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Widgates
{
    internal static class ShelfPlayerChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; synthetic shelf/player geometry and notification scrollbars; no media commands, clipboard writes or saved preferences");
            var widget = new IslandWindow(new WidgetSettings { Dock = DockPreference.Top, HideInGames = false, HideWhenIdle = false,
                AutoPauseOthers = false, RespectQuietState = false, Lightweight = LightweightMode.Never });
            var originalTheme = ThemePalette.Preference;
            try
            {
                widget.IsHitTestVisible = false; widget.Show(); await Task.Delay(350);
                var media = Field<MediaController>(widget, "media"); media.Dispose();
                typeof(MediaController).GetProperty("Title", Private).SetValue(media, "Çok İnce", null);
                typeof(MediaController).GetProperty("Artist", Private).SetValue(media, "Nebi Demir", null);
                Call(widget, "UpdateMedia"); Call(widget, "OpenPinned"); await Task.Delay(300);
                var shell = Field<Border>(widget, "shell"); var panel = Field<StackPanel>(widget, "panel");
                var footer = Field<StackPanel>(widget, "playerFooter"); var shelf = Field<FileShelf>(widget, "shelf");
                var section = Field<StackPanel>(widget, "shelfSection");
                var stable = new FrameworkElement[] { Field<ComboBox>(widget, "sessions"), Field<TextBlock>(widget, "mediaTitle"),
                    Field<StackPanel>(widget, "timelinePanel"), Field<Grid>(widget, "playbackControls"), Field<Border>(widget, "volumePanel") };
                var before = stable.Select(x => x.TranslatePoint(new Point(), panel).Y).ToArray();
                double emptyHeight = shell.ActualHeight;
                string path = Path.Combine(output, "Shelf-original.txt"); File.WriteAllText(path, "Original remains unchanged");
                foreach (var theme in new[] { ThemePreference.Dark, ThemePreference.Light })
                {
                    typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, theme); ThemePalette.Refresh();
                    shelf.Add(new[] { path }); Call(widget, "UpdateShelf"); await Task.Delay(45); widget.UpdateLayout();
                    Check(stable.Select((x, i) => Math.Abs(x.TranslatePoint(new Point(), panel).Y - before[i]) < 0.8).All(x => x),
                        theme + " adding a file keeps the source, title, timeline, transport and volume at their existing positions during growth");
                    await Task.Delay(280); widget.UpdateLayout();
                    Check(section.TranslatePoint(new Point(), panel).Y >= footer.TranslatePoint(new Point(0, footer.ActualHeight), panel).Y
                        && shell.ActualHeight > emptyHeight && Field<ScrollViewer>(widget, "panelViewport").ComputedVerticalScrollBarVisibility != Visibility.Visible,
                        theme + " the file shelf extends below the complete player without a native white scrollbar");
                    Capture(shell, Path.Combine(output, "shelf-player-" + theme + ".png"));
                    shelf.Remove(new[] { path }); Call(widget, "UpdateShelf"); await Task.Delay(280); widget.UpdateLayout();
                    double undoHeight = Field<Button>(widget, "shelfRecovery").DesiredSize.Height;
                    Check(Math.Abs(shell.ActualHeight - emptyHeight - undoHeight) < 1 && section.Visibility == Visibility.Collapsed
                        && File.ReadAllText(path) == "Original remains unchanged",
                        theme + " removing the last file leaves only the existing undo action below the unchanged player");
                    Call(widget, "ClosePanel"); await Task.Delay(250); Call(widget, "PreviewNotification");
                    var messages = Field<AppMessagePeek>(widget, "messages"); messages.Hold();
                    messages.Current.Body = string.Join(" ", Enumerable.Repeat("Uzun bildirim metni", 200)); Call(widget, "UpdateState", false); widget.UpdateLayout();
                    var viewer = Field<ScrollViewer>(widget, "messageScroll");
                    var bar = Visuals(viewer).OfType<ScrollBar>().First(x => x.Orientation == Orientation.Vertical);
                    Check(bar.Visibility == Visibility.Visible && bar.OverridesDefaultStyle && bar.Template != null,
                        theme + " overflowing notification content uses the custom scrollbar from its first visible layout");
                    viewer.ScrollToBottom(); widget.UpdateLayout();
                    Check(viewer.VerticalOffset > 0, theme + " themed notification bars retain actual text scrolling");
                    Capture(shell, Path.Combine(output, "notification-scroll-" + theme + ".png"));
                    messages.Cancel(false); Call(widget, "UpdateState", false); Call(widget, "OpenPinned"); await Task.Delay(280);
                }
            }
            finally { typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, originalTheme); ThemePalette.Refresh(); widget.Close(); }
        }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static void Call(object owner, string name, params object[] args) { owner.GetType().GetMethod(name, Private).Invoke(owner, args); }
        private static void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); Console.WriteLine("PASS " + text); }
        private static System.Collections.Generic.IEnumerable<DependencyObject> Visuals(DependencyObject parent)
        { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Visuals(child)) yield return nested; } }
        private static void Capture(FrameworkElement element, string path)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
