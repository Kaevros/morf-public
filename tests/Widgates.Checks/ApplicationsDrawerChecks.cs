using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Widgates
{
    internal static class ApplicationsDrawerChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; native WhatsApp settings drawer; no notification permissions, registry writes or player commands");
            var model = new WidgetSettings { WhatsAppNotifications = false, Lightweight = LightweightMode.Never };
            int applies = 0, permissionCalls = 0; bool motionAllowed = true;
            var dialog = new SettingsWindow(model, new SettingsActions { Apply = _ => applies++, SelectTheme = _ => true,
                MotionAllowed = () => motionAllowed, NotificationAvailable = () => false, NotificationStatus = () => "İzin gerekli",
                ConnectNotifications = () => { permissionCalls++; return Task.FromResult(0); } });
            try
            {
                dialog.ShowActivated = false; dialog.IsHitTestVisible = false; dialog.Show(); dialog.SelectPage(6); await Task.Delay(100);
                var drawer = Field<Border>(dialog, "notificationDrawer"); var page = Field<StackPanel>(dialog, "page");
                var toggle = FindToggle(page);
                Check(drawer.Visibility == Visibility.Collapsed && drawer.ActualHeight == 0 && !Field<bool>(dialog, "notificationDrawerBuilt"),
                    "Disabled WhatsApp initially has no settings controls or empty drawer height");
                Capture((FrameworkElement)dialog.Content, Path.Combine(output, "applications-off.png"));
                Click(toggle, true); await Task.Delay(40); dialog.UpdateLayout();
                Check(model.WhatsAppNotifications && drawer.Visibility == Visibility.Visible && drawer.IsEnabled && permissionCalls == 0,
                    "Enabling WhatsApp reveals its settings without requesting permission");
                if (SystemParameters.ClientAreaAnimation)
                    Check(drawer.Height > 0 && !double.IsNaN(drawer.Height) && drawer.HasAnimatedProperties,
                        "The visible drawer uses a bounded opening transition");
                else Console.WriteLine("UNVERIFIED Intermediate drawer animation: Windows animations are disabled");
                await Task.Delay(280); dialog.UpdateLayout();
                Check(double.IsNaN(drawer.Height) && drawer.Opacity == 1 && !drawer.HasAnimatedProperties,
                    "An open drawer returns to responsive height without a remaining animation clock");
                Capture((FrameworkElement)dialog.Content, Path.Combine(output, "applications-on.png"));
                var disclosure = Field<Button>(dialog, "notificationDisclosure");
                disclosure.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(280);
                Check(model.WindowsNotifications && drawer.Visibility == Visibility.Collapsed && !drawer.IsHitTestVisible,
                    "The disclosure hides notification options without disabling notifications");
                disclosure.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(280);
                Check(drawer.Visibility == Visibility.Visible && !drawer.HasAnimatedProperties && applies == 1,
                    "Reopening notification details reuses controls without changing or saving preferences");
                Click(toggle, false); await Task.Delay(40); Click(toggle, true); await Task.Delay(280);
                Check(drawer.Visibility == Visibility.Visible && double.IsNaN(drawer.Height) && drawer.Opacity == 1,
                    "Rapid close and reopen cannot leave the drawer clipped or hidden");
                Click(toggle, false); await Task.Delay(280); dialog.UpdateLayout();
                Check(drawer.Visibility == Visibility.Collapsed && !drawer.IsEnabled && !drawer.IsHitTestVisible && drawer.ActualHeight == 0,
                    "A closed drawer consumes no space or input");
                motionAllowed = false; Click(toggle, true);
                Check(double.IsNaN(drawer.Height) && !drawer.HasAnimatedProperties && drawer.Opacity == 1,
                    "Reduced-power motion policy opens the drawer immediately");
                motionAllowed = true; Click(toggle, false); dialog.SelectPage(0); await Task.Delay(260);
                Check(!drawer.HasAnimatedProperties && Field<Border>(dialog, "notificationDrawer") == null && dialog.SelectedPage == 0,
                    "Page navigation cancels drawer clocks and discards the old view");
                dialog.SelectPage(6); toggle = FindToggle(Field<StackPanel>(dialog, "page")); Click(toggle, true);
                drawer = Field<Border>(dialog, "notificationDrawer"); dialog.Close();
                Check(!drawer.HasAnimatedProperties && permissionCalls == 0 && applies == 7,
                    "Closing settings stops motion and never invokes a connection action implicitly");
            }
            finally { dialog.Close(); }
        }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static CheckBox FindToggle(DependencyObject root)
        {
            foreach (object item in LogicalTreeHelper.GetChildren(root))
            {
                var toggle = item as CheckBox; if (toggle != null) return toggle;
                var child = item as DependencyObject; if (child != null) { var result = FindToggle(child); if (result != null) return result; }
            }
            return null;
        }
        private static void Click(CheckBox toggle, bool enabled) { toggle.IsChecked = enabled; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
        private static void Capture(FrameworkElement element, string path)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
