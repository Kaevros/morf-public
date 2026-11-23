using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Widgates
{
    internal static class ExpansionChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; native hidden-edge expansion with synthetic metadata; no player commands or saved settings");
            var settings = new WidgetSettings { EdgeOnly = true, HideInGames = false, HideInFullscreen = false,
                AutoPauseOthers = false, Lightweight = LightweightMode.Never };
            var widget = new IslandWindow(settings);
            try
            {
                widget.IsHitTestVisible = false; widget.Show(); await Task.Delay(400);
                var media = Field<MediaController>(widget, "media");
                media.Changed -= (Action)Delegate.CreateDelegate(typeof(Action), widget, typeof(IslandWindow).GetMethod("UpdateMedia", Private));
                typeof(MediaController).GetProperty("Title", Private).SetValue(media, "Uzun örnek parça adı · açılış sırasında aynı yerleşimde kalan başlık", null);
                typeof(MediaController).GetProperty("Artist", Private).SetValue(media, "Örnek sanatçı · örnek albüm", null);
                Call(widget, "UpdateMedia");
                var shell = Field<Border>(widget, "shell");
                var layout = Field<Grid>(widget, "expandedLayout");
                var panel = Field<StackPanel>(widget, "panel");
                var viewport = Field<ScrollViewer>(widget, "panelViewport");
                var power = Field<PowerStateMonitor>(widget, "power");
                foreach (var dock in new[] { DockPreference.Top, DockPreference.Bottom })
                {
                    settings.Dock = dock; Call(widget, "SetExpanded", false); Call(widget, "ApplySettings", false);
                    Check(shell.Height <= 4, dock + " begins fully closed at the attached edge");
                    Call(widget, "SetExpanded", true);
                    bool motion = SystemParameters.ClientAreaAnimation && power.State.RenderAllowed;
                    if (motion)
                    {
                        double targetWidth = layout.Width, targetHeight = layout.Height;
                        Check(targetWidth > 300 && targetHeight > 100, dock + " prepares the complete content size before revealing it");
                        await Task.Delay(40); widget.UpdateLayout();
                        Check(Math.Abs(layout.ActualWidth - targetWidth) < 1 && Math.Abs(layout.ActualHeight - targetHeight) < 1,
                            dock + " keeps the final layout size inside the partly open shell");
                        double contentWidth = panel.ActualWidth;
                        Check(viewport.ComputedVerticalScrollBarVisibility != Visibility.Visible,
                            dock + " has no transient scrollbar during opening");
                        Capture(shell, Path.Combine(output, "opening-" + dock + ".png"));
                        await Task.Delay(40); widget.UpdateLayout();
                        Check(Math.Abs(panel.ActualWidth - contentWidth) < 1,
                            dock + " text does not reflow between opening frames");
                    }
                    else Console.WriteLine("UNVERIFIED " + dock + " intermediate motion: Windows motion or rendering is unavailable");
                    await Task.Delay(280); widget.UpdateLayout();
                    Check(double.IsNaN(layout.Width) && double.IsNaN(layout.Height) && panel.Visibility == Visibility.Visible,
                        dock + " returns to responsive layout when opening finishes (layout=" + layout.Width + "x" + layout.Height + ", panel=" + panel.Visibility + ", shell clock=" + shell.HasAnimatedProperties + ")");
                    Check(!shell.HasAnimatedProperties && !panel.HasAnimatedProperties,
                        dock + " releases completed shell and panel animation clocks");
                    Capture(shell, Path.Combine(output, "open-" + dock + ".png"));
                    Call(widget, "SetExpanded", false); await Task.Delay(45);
                    Call(widget, "SetExpanded", true); await Task.Delay(45);
                    Call(widget, "UpdateState", false); await Task.Delay(240);
                    Check(double.IsNaN(layout.Width) && double.IsNaN(layout.Height) && panel.Visibility == Visibility.Visible && panel.Opacity == 1,
                        dock + " recovers after reversal and an immediate layout update");
                    viewport.MaxHeight = 70; shell.Height = 150; widget.UpdateLayout();
                    viewport.ScrollToBottom(); widget.UpdateLayout();
                    Check(viewport.ScrollableHeight > 0 && viewport.VerticalOffset > 0 && viewport.ComputedVerticalScrollBarVisibility != Visibility.Visible,
                        dock + " retains scrolling in a constrained panel without displaying a scrollbar");
                }
                settings.Lightweight = LightweightMode.Always;
                Call(widget, "SetExpanded", false); Call(widget, "ApplySettings", false); Call(widget, "SetExpanded", true);
                Check(double.IsNaN(layout.Width) && double.IsNaN(layout.Height) && !shell.HasAnimatedProperties && panel.Opacity == 1,
                    "Lightweight opening uses immediate responsive layout without a transition clock");

                var placement = Field<DesktopPlacement>(widget, "placement");
                Check(!placement.TrackFullscreen && !placement.Fullscreen,
                    "Disabled fullscreen hiding does not track fullscreen state");
                settings.HideInFullscreen = true; Call(widget, "ApplySettings", false);
                Check(placement.TrackFullscreen, "Enabling fullscreen hiding immediately restores foreground tracking");
                settings.HideInFullscreen = false; Call(widget, "ApplySettings", false);
                Check(!placement.TrackFullscreen && !placement.Fullscreen,
                    "Disabling fullscreen hiding clears its state and stops checks");

                var title = new MarqueeText(60);
                title.SetText("Ölçüm için uzun bir örnek başlık");
                var titleWindow = new Window { Content = title, Width = 100, Height = 45, ShowActivated = false,
                    ShowInTaskbar = false, WindowStyle = WindowStyle.None, AllowsTransparency = true, Opacity = 0, IsHitTestVisible = false };
                try
                {
                    titleWindow.Show(); title.SetActive(true); titleWindow.UpdateLayout();
                    if (SystemParameters.ClientAreaAnimation)
                        Check(title.Running && Field<Canvas>(title, "strip").CacheMode is BitmapCache,
                            "An active visible long title receives its animation cache");
                    else Console.WriteLine("UNVERIFIED animated title cache: Windows animations disabled");
                    title.SetActive(false);
                    Check(!title.Running && Field<Canvas>(title, "strip").CacheMode == null,
                        "Disabling the title animation releases its cache and animation clock");
                }
                finally { titleWindow.Close(); }
            }
            finally { widget.Close(); }
        }

        private static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, Private).GetValue(value); }
        private static void Call(object value, string name, params object[] args) { value.GetType().GetMethod(name, Private).Invoke(value, args); }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
        private static void Capture(FrameworkElement element, string path)
        {
            var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            image.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) png.Save(stream);
        }
    }
}
