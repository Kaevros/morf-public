using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Widgates
{
    internal static class DisplayRecoveryChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; synthetic display events and native window resizing; no OS display changes or media commands");
            var settings = new WidgetSettings { HideWhenIdle = false, HideInGames = false, HideInFullscreen = false,
                AutoPauseOthers = false, Lightweight = LightweightMode.Always };
            var widget = new IslandWindow(settings);
            try
            {
                widget.IsHitTestVisible = false; widget.Show(); await Task.Delay(400);
                var placement = Field<DesktopPlacement>(widget, "placement");
                var shell = Field<Border>(widget, "shell");
                var layout = Field<Grid>(widget, "expandedLayout");
                var state = Field<PanelState>(widget, "panelState");
                var media = Field<MediaController>(widget, "media");
                Console.WriteLine("Media connection ready=" + media.Ready + "; active=" + (media.Active == null ? "none" : media.Active.SourceAppUserModelId) + "; no commands sent");
                media.Changed -= (Action)Delegate.CreateDelegate(typeof(Action), widget, typeof(IslandWindow).GetMethod("UpdateMedia", Private));
                string sample = Path.Combine(output, "shelf-original.txt"); File.WriteAllText(sample, "Display recovery preserves the original.");
                var shelf = Field<FileShelf>(widget, "shelf");
                Check(shelf.Add(new[] { sample }) == 1, "Adds a local shelf fixture without moving its original");
                Call(widget, "UpdateShelf");
                state.Persistent = true;
                Call(widget, "SetExpanded", true); widget.UpdateLayout();
                IntPtr hwnd = new WindowInteropHelper(widget).Handle;
                foreach (var dock in new[] { DockPreference.Top, DockPreference.Bottom })
                {
                    settings.Dock = dock; Call(widget, "ApplySettings", false); widget.UpdateLayout();
                    foreach (int message in new[] { 0x007E, 0x02E0, 0x001A })
                    {
                        // Reproduce Windows constraining the native host while the panel retains its DIP size.
                        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 220, 80, 0x0016);
                        widget.UpdateLayout();
                        Check(widget.ActualWidth < shell.Width, dock + " reproduces a host narrower than its panel");
                        Message(placement, hwnd, message);
                        await Task.Delay(260); widget.UpdateLayout();
                        Check(widget.ActualWidth >= shell.ActualWidth && widget.ActualHeight >= shell.ActualHeight + shell.Margin.Top + shell.Margin.Bottom - 1,
                            dock + " recovers the complete native host after " + message.ToString("X4"));
                        Check(Math.Abs(shell.Width - Math.Min(380, placement.AvailableSize.Width)) < 1 && double.IsNaN(layout.Width) && double.IsNaN(layout.Height),
                            dock + " restores responsive panel layout after " + message.ToString("X4"));
                        Check(state.Persistent && state.Expanded, dock + " preserves the pinned panel after " + message.ToString("X4"));
                        var monitorBounds = MonitorCatalog.Resolve("", MonitorCatalog.All()).Bounds;
                        NativeRect bounds; GetWindowRect(hwnd, out bounds);
                        Check(dock == DockPreference.Top ? bounds.Top == (int)monitorBounds.Top : bounds.Bottom == (int)monitorBounds.Bottom,
                            dock + " preserves the native docked edge after " + message.ToString("X4"));
                        Check(!Field<DispatcherTimer>(placement, "refresh").IsEnabled,
                            dock + " display recovery stops its event timer after " + message.ToString("X4"));
                    }
                }

                // A low-resolution work area must resize the content, then restore it when the desktop returns.
                Message(placement, hwnd, 0x007E);
                var monitor = MonitorCatalog.Resolve("", MonitorCatalog.All());
                typeof(DesktopPlacement).GetField("monitorCache", Private).SetValue(placement, new[] {
                    new DisplayMonitor { Device = monitor.Device, Primary = true, Handle = monitor.Handle,
                        Bounds = new Rect(0, 0, 300, 200), Work = new Rect(0, 0, 300, 180), Scale = monitor.Scale } });
                await Task.Delay(260); widget.UpdateLayout();
                Check(shell.Width <= placement.AvailableSize.Width && shell.Height + shell.Margin.Top + shell.Margin.Bottom <= placement.AvailableSize.Height + 1,
                    "A reduced work area recomputes panel width and height");
                Message(placement, hwnd, 0x007E); await Task.Delay(260); widget.UpdateLayout();
                Check(Math.Abs(shell.Width - Math.Min(380, placement.AvailableSize.Width)) < 1,
                    "Restoring the desktop restores the panel width without reopening it");

                // Recover even if the game transition is the final notification received.
                typeof(DesktopPlacement).GetField("gameActive", Private).SetValue(placement, true);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 220, 80, 0x0016);
                Call(placement, "RefreshForeground", IntPtr.Zero);
                await Task.Delay(260); widget.UpdateLayout();
                Check(widget.ActualWidth >= shell.ActualWidth && !placement.GameActive,
                    "Leaving a game refreshes geometry even without a final display message");

                settings.Lightweight = LightweightMode.Never; state.Persistent = false;
                Call(widget, "SetExpanded", false); await Task.Delay(280);
                Call(widget, "SetExpanded", true);
                Message(placement, hwnd, 0x007E); await Task.Delay(360); widget.UpdateLayout();
                Check(double.IsNaN(layout.Width) && double.IsNaN(layout.Height) && !shell.HasAnimatedProperties,
                    "A display event during opening releases frozen layout and obsolete animation clocks");
                Check(!Field<DispatcherTimer>(placement, "refresh").IsEnabled,
                    "Recovery leaves no recurring display timer");
                Check(shelf.Paths.Count == 1 && shelf.Paths[0] == sample && File.ReadAllText(sample) == "Display recovery preserves the original.",
                    "Display recovery preserves shelf paths and original file contents");
                Capture(shell, Path.Combine(output, "recovered-panel.png"));

                settings.Lightweight = LightweightMode.Always;
                foreach (var mode in new[] { DisplayMode.DynamicIsland, DisplayMode.Line, DisplayMode.AutoHide })
                {
                    settings.DisplayMode = mode; Call(widget, "SetExpanded", false); Call(widget, "ApplySettings", false);
                    Message(placement, hwnd, 0x007E); await Task.Delay(260); widget.UpdateLayout();
                    Check(mode == DisplayMode.DynamicIsland ? shell.Height == 36 && shell.Margin.Bottom == 16 : shell.Height <= 4,
                        mode + " retains its collapsed geometry after display recovery");
                }
            }
            finally { widget.Close(); }
        }

        private static void Message(DesktopPlacement placement, IntPtr hwnd, int message)
        {
            // Invoke only our hook: a fabricated WM_DPICHANGED must not change WPF's real DPI.
            object[] args = { hwnd, message, IntPtr.Zero, IntPtr.Zero, false };
            typeof(DesktopPlacement).GetMethod("WindowMessage", Private).Invoke(placement, args);
            Check(!(bool)args[4], "Display messages remain available to WPF");
        }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static void Call(object owner, string name, params object[] args) { owner.GetType().GetMethod(name, Private).Invoke(owner, args); }
        private static void Check(bool success, string text) { if (!success) throw new InvalidOperationException(text); Console.WriteLine("PASS " + text); }
        private static void Capture(FrameworkElement element, string path)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
