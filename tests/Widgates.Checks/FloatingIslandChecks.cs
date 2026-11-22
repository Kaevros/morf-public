using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Widgates
{
    internal static class FloatingIslandChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; floating island and native source-popup bounds; synthetic choices, no player commands or saved preferences");
            var settings = new WidgetSettings { HideInGames = false, HideInFullscreen = false, AutoPauseOthers = false,
                RespectQuietState = false, Lightweight = LightweightMode.Never, DisplayMode = DisplayMode.DynamicIsland };
            var widget = new IslandWindow(settings);
            try
            {
                widget.IsHitTestVisible = false; widget.Show(); await Task.Delay(400);
                typeof(IslandWindow).GetProperty("pinned", Private).SetValue(widget, true, null);
                var shell = Field<Border>(widget, "shell");
                var hoverSurface = Field<Grid>(widget, "hoverSurface");
                var topHoverGap = Field<Border>(widget, "topHoverGap");
                foreach (var dock in new[] { DockPreference.Top, DockPreference.Bottom })
                {
                    settings.Dock = dock; Call(widget, "SetExpanded", false); Call(widget, "ApplySettings", false); widget.UpdateLayout();
                    Check(shell.Height == 36 && (dock == DockPreference.Top ? shell.Margin.Top == 8 : shell.Margin.Bottom == 16),
                        dock + " positions the idle floating capsule at the configured edge inset");
                    Check(shell.CornerRadius.TopLeft == 18 && shell.CornerRadius.BottomRight == 18,
                        dock + " rounds all closed capsule corners");
                    Check(topHoverGap.Visibility == Visibility.Collapsed, dock + " closed capsule leaves its outer gap outside the hover area");
                    foreach (var theme in new[] { ThemePreference.Dark, ThemePreference.Light })
                    {
                        typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, theme);
                        ThemePalette.Refresh(); Call(widget, "UpdateState", false); widget.UpdateLayout();
                        Capture(widget, Path.Combine(output, "closed-" + dock + "-" + theme + ".png"));
                    }
                    Call(widget, "SetExpanded", true); Call(widget, "UpdateState", false); widget.UpdateLayout();
                    Check(shell.CornerRadius.TopLeft == 28 && shell.CornerRadius.BottomRight == 28
                        && (dock == DockPreference.Top ? shell.Margin.Top : shell.Margin.Bottom) == 16,
                        dock + " expansion uses the expanded gap and four rounded corners");
                    widget.IsHitTestVisible = true;
                    try
                    {
                        if (dock == DockPreference.Top)
                        {
                            var above = new Point(hoverSurface.ActualWidth / 2, shell.Margin.Top / 2);
                            Check(hoverSurface.InputHitTest(above) == topHoverGap
                                && topHoverGap.ActualWidth == shell.ActualWidth && topHoverGap.ActualHeight == shell.Margin.Top
                                && hoverSurface.InputHitTest(new Point(1, above.Y)) == null,
                                "The space above the open panel joins its hover surface within the panel width");
                        }
                        else Check(topHoverGap.Visibility == Visibility.Collapsed, "Bottom docking leaves the existing hover gap behavior intact");
                    }
                    finally { widget.IsHitTestVisible = false; }
                    if (dock == DockPreference.Top)
                    {
                        var timer = Field<DispatcherTimer>(widget, "collapse");
                        var panelState = Field<PanelState>(widget, "panelState");
                        panelState.Latched = false;
                        timer.Stop();
                        shell.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
                        Check(panelState.Expanded && !timer.IsEnabled, "Leaving the visible shell alone keeps the open panel");
                        hoverSurface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
                        Check(timer.IsEnabled, "Leaving both hover regions schedules the existing collapse delay");
                        await Task.Delay(380);
                        Check(!panelState.Expanded && !timer.IsEnabled, "Leaving the complete hover surface collapses the panel once");
                        panelState.Latched = true;
                        Call(widget, "SetExpanded", true); Call(widget, "UpdateState", false); widget.UpdateLayout();
                    }
                    Capture(widget, Path.Combine(output, "open-" + dock + ".png"));
                    var picker = Field<ComboBox>(widget, "sessions");
                    foreach (double width in new[] { 240.0, 380.0 })
                    {
                        shell.Width = width; Call(widget, "ApplyPlayerWidth", width); widget.UpdateLayout();
                        typeof(IslandWindow).GetField("syncingSessions", Private).SetValue(widget, true);
                        picker.Items.Clear(); picker.Items.Add(new ComboBoxItem { Content = "Otomatik takip" });
                        picker.Items.Add(new ComboBoxItem { Content = "Spotify · çok uzun bir örnek kaynak adı · panel dışına taşmamalı" });
                        picker.SelectedIndex = 0;
                        typeof(IslandWindow).GetField("syncingSessions", Private).SetValue(widget, false);
                        picker.IsDropDownOpen = true; await Task.Delay(70); widget.UpdateLayout();
                        var popup = (Popup)picker.Template.FindName("PART_Popup", picker);
                        var surface = (FrameworkElement)popup.Child;
                        Point a = surface.PointToScreen(new Point()), b = surface.PointToScreen(new Point(surface.ActualWidth, surface.ActualHeight));
                        Point left = shell.PointToScreen(new Point()), right = shell.PointToScreen(new Point(shell.ActualWidth, shell.ActualHeight));
                        Check(popup.IsOpen && a.X >= left.X && b.X <= right.X + 1 && a.Y >= left.Y && b.Y <= right.Y + 1,
                            dock + " source picker stays inside the panel at " + width + " DIP");
                        Capture(surface, Path.Combine(output, "source-" + dock + "-" + width + ".png"));
                        picker.IsDropDownOpen = false;
                    }
                    Call(widget, "UpdateState", false);
                    Call(widget, "PreviewNotification");
                    var messages = Field<AppMessagePeek>(widget, "messages");
                    if (messages.Active)
                    {
                        messages.Cancel(false); Call(widget, "UpdateState", false);
                        Check((dock == DockPreference.Top ? shell.Margin.Top : shell.Margin.Bottom) == 16,
                            dock + " notification dismissal restores floating placement");
                    }
                    else Console.WriteLine("UNVERIFIED " + dock + " notification restoration: presentation policy rejected preview");
                }
                settings.DisplayMode = DisplayMode.Capsule; Call(widget, "SetExpanded", false); Call(widget, "ApplySettings", false);
                Check(shell.Margin == new Thickness(0), "Returning to an attached choice removes the floating inset");
            }
            finally { widget.Close(); }
        }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static void Call(object owner, string name, params object[] args) { owner.GetType().GetMethod(name, Private).Invoke(owner, args); }
        private static void Check(bool success, string text) { if (!success) throw new InvalidOperationException(text); Console.WriteLine("PASS " + text); }
        private static void Capture(FrameworkElement element, string path)
        {
            element.UpdateLayout(); var rect = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            var image = new RenderTargetBitmap((int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = rect, Stretch = Stretch.Fill }, null, rect);
            image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
