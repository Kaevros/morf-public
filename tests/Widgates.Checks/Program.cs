using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Widgates
{
    // Default checks render offscreen. The explicit --measure mode opens the widget.
    internal static class Checks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static string output;

        [STAThread]
        private static int Main(string[] args)
        {
            bool measure = args.Length > 0 && args[0] == "--measure";
            bool measureArtwork = args.Length > 0 && args[0] == "--measure-artwork";
            bool measurePlayer = args.Length > 0 && args[0] == "--measure-player";
            bool measureExpandedSteady = args.Length > 0 && args[0] == "--measure-expanded-steady";
            bool nativeLayout = args.Length > 0 && args[0] == "--native-layout";
            bool nativeMenu = args.Length > 0 && (args[0] == "--native-menu" || args[0] == "--native-settings");
            bool nativeMedia = args.Length > 0 && args[0] == "--native-media";
            bool nativeLifecycle = args.Length > 0 && args[0] == "--native-lifecycle";
            bool nativeNotifications = args.Length > 0 && args[0] == "--native-notifications";
            bool nativeExpansion = args.Length > 0 && args[0] == "--native-expansion";
            bool nativeFloating = args.Length > 0 && args[0] == "--native-floating";
            bool nativeDrawer = args.Length > 0 && args[0] == "--native-drawer";
            bool nativeClock = args.Length > 0 && args[0] == "--native-clock";
            bool nativeDisplay = args.Length > 0 && args[0] == "--native-display";
            bool nativeRefreshVolume = args.Length > 0 && args[0] == "--native-refresh-volume";
            bool nativeWindowsNotifications = args.Length > 0 && args[0] == "--native-windows-notifications";
            bool nativeProductPack = args.Length > 0 && args[0] == "--native-product-pack";
            bool nativeMixerVisual = args.Length > 0 && args[0] == "--native-mixer-visual";
            bool liveSeek = args.Length > 0 && args[0] == "--live-spotify-seek";
            output = Path.GetFullPath(measure || measureArtwork || measurePlayer || measureExpandedSteady || nativeLayout || nativeMenu || nativeMedia || nativeLifecycle || nativeNotifications || nativeExpansion || nativeFloating || nativeDrawer || nativeClock || nativeDisplay || nativeRefreshVolume || nativeWindowsNotifications || nativeProductPack || nativeMixerVisual || liveSeek ? (args.Length > 1 ? args[1] : "artifacts/window-checks")
                : args.Length == 0 ? "artifacts/ux-checks" : args[0]);
            Directory.CreateDirectory(output);
            int exitCode = 0;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (nativeMixerVisual) await ProductPackChecks.RunNativeAsync(output, true);
                    else if (nativeProductPack) await ProductPackChecks.RunNativeAsync(output);
                    else if (measureArtwork) await PerformanceOptimizationChecks.MeasureArtworkAsync(output);
                    else if (nativeWindowsNotifications) await WindowsNotificationChecks.RunNativeAsync(output);
                    else if (nativeRefreshVolume) { await RefreshExitChecks.RunNativeAsync(output); await NativePlayerVolumeChecks.RunAsync(output); }
                    else if (nativeDisplay) await DisplayRecoveryChecks.RunNativeAsync(output);
                    else if (nativeClock) await IdleClockChecks.RunNativeAsync(output);
                    else if (nativeDrawer) await ApplicationsDrawerChecks.RunNativeAsync(output);
                    else if (nativeFloating) await FloatingIslandChecks.RunNativeAsync(output);
                    else if (nativeExpansion) await ExpansionChecks.RunNativeAsync(output);
                    else if (nativeNotifications) await AppNotificationChecks.RunNativeAsync(output);
                    else if (nativeLifecycle) await LifecycleChecks.RunNativeAsync(output);
                    else if (liveSeek) await NativeMediaChecks.RunSeekAsync(output);
                    else if (nativeMedia) await NativeMediaChecks.RunAsync(output);
                    else if (measureExpandedSteady) await PerformanceChecks.RunExpandedSteadyAsync(output);
                    else if (measurePlayer) await PerformanceChecks.RunPlayerAsync(output);
                    else if (measure) await PerformanceChecks.RunAsync(output);
                    else if (nativeLayout) await NativeLayoutChecks.RunAsync(output);
                    else if (nativeMenu) await NativeMenuChecks.RunAsync(output);
                    else await RunAsync();
                }
                catch (Exception error) { Console.Error.WriteLine(error); exitCode = 1; }
                finally { app.Shutdown(); }
            });
            app.Run();
            return exitCode;
        }

        private static T Field<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, Private);
            return (T)(field == null ? target.GetType().GetProperty(name, Private).GetValue(target, null) : field.GetValue(target));
        }

        private static void Invoke(object target, string name, params object[] args)
        {
            target.GetType().GetMethod(name, Private | BindingFlags.DeclaredOnly).Invoke(target, args);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }

        private static DragEventArgs Drag(IDataObject data, DragDropEffects allowed)
        {
            var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), Private, null,
                new object[] { data, DragDropKeyStates.None, allowed, new Border(), new Point(1, 1) }, null);
            args.RoutedEvent = DragDrop.PreviewDragOverEvent;
            return args;
        }

        private static void SetTheme(ThemePreference preference)
        {
            // Exercise all palettes without changing the owner's saved preference or OS theme.
            typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, preference);
            ThemePalette.Refresh();
        }

        private static void Render(IslandWindow window, string name)
        {
            Invoke(window, "UpdateState", false);
            var shell = Field<Border>(window, "shell");
            shell.Measure(new Size(shell.Width, shell.Height));
            shell.Arrange(new Rect(0, 0, shell.Width, shell.Height));
            shell.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(shell.Width), (int)Math.Ceiling(shell.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(shell);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
        }

        private static async Task RunAsync()
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; media: none (programmatic WPF checks)");
            var window = new IslandWindow(new WidgetSettings());
            try
            {
                var section = Field<StackPanel>(window, "shelfSection");
                var hint = Field<Border>(window, "dropHint");
                var files = Field<ListBox>(window, "files");
                var shelf = Field<FileShelf>(window, "shelf");
                SetTheme(ThemePreference.Light);
                Render(window, "compact-light");
                Invoke(window, "SetExpanded", true);
                Render(window, "empty-light");
                Check(section.Visibility == Visibility.Collapsed && hint.Visibility == Visibility.Collapsed,
                    "Expanded empty panel has no shelf or drop target");
                var unsupported = Drag(new DataObject(DataFormats.Text, "not a file"), DragDropEffects.Copy);
                Invoke(window, "OnDragOver", null, unsupported);
                Check(unsupported.Effects == DragDropEffects.None && hint.Visibility == Visibility.Collapsed,
                    "Text drag does not reveal a file target");

                string photo = Path.Combine(output, "sample.png");
                var pixels = Enumerable.Repeat((byte)180, 32 * 24 * 4).ToArray();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(32, 24, 96, 96, PixelFormats.Bgra32, null, pixels, 32 * 4)));
                using (var stream = File.Create(photo)) encoder.Save(stream);
                string document = Path.Combine(output, "Document.txt");
                File.WriteAllText(document, "Copy-only shelf original");
                string folder = Path.Combine(output, "Folder");
                Directory.CreateDirectory(folder);
                var data = new DataObject(DataFormats.FileDrop, new[] { photo, document, folder });
                var drag = Drag(data, DragDropEffects.Copy);
                Invoke(window, "OnDragOver", null, drag);
                Render(window, "drag-light");
                Check(drag.Effects == DragDropEffects.Copy && hint.Visibility == Visibility.Visible,
                    "File drag reveals the drop hint");
                Invoke(window, "OnDragLeave", null, null);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(hint.Visibility == Visibility.Collapsed, "Leaving or cancelling a drag clears the hint");
                Invoke(window, "OnDragLeave", null, null);
                Invoke(window, "OnDragOver", null, drag);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(hint.Visibility == Visibility.Visible, "Child drag transitions keep the hint visible");
                var drop = Drag(data, DragDropEffects.Copy);
                Invoke(window, "OnDrop", null, drop);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(drop.Effects == DragDropEffects.Copy && shelf.Paths.Count == 3 && hint.Visibility == Visibility.Collapsed,
                    "Dropping stores paths and hides only the drop hint");
                Check(section.Visibility == Visibility.Visible, "Stored files remain accessible without an active drag");
                files.SelectedIndex = 0;
                Invoke(window, "UpdateShelf");
                Check(files.SelectedItems.Count == 1, "Shelf refresh preserves selection");
                var image = await ShelfThumbnail.LoadAsync(photo) as BitmapSource;
                Check(image != null && image.IsFrozen && image.PixelWidth <= 96, "Photo preview decodes to a small frozen bitmap");
                using (File.Open(photo, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                Check(true, "Photo preview releases its source file");
                string corrupt = Path.Combine(output, "corrupt.png");
                File.WriteAllText(corrupt, "invalid image");
                Check(await ShelfThumbnail.LoadAsync(corrupt) == null && await ShelfThumbnail.LoadAsync(photo + ".missing.png") == null,
                    "Corrupt and missing images fall back safely");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Render(window, "shelf-light");
                var containers = files.Items.Cast<ListBoxItem>().ToArray();
                var first = containers[0].TransformToAncestor(files).Transform(new Point());
                var second = containers[1].TransformToAncestor(files).Transform(new Point());
                Check(second.X > first.X && second.Y == first.Y, "File cards occupy one horizontal row");
                double shelfHeight = Field<Border>(window, "shell").Height;
                for (int i = 0; i < 30; i++)
                {
                    string path = Path.Combine(output, "File-" + i + ".txt");
                    File.WriteAllText(path, "sample");
                    shelf.Add(new[] { path });
                }
                Invoke(window, "UpdateShelf");
                Render(window, "full-light");
                Check(shelf.Paths.Count == FileShelf.Capacity && Field<Border>(window, "shell").Height == shelfHeight,
                    "A full 24-item shelf stays the same height");
                Check(FindViewer(files).ScrollableWidth > 0 && FindViewer(files).ScrollableHeight == 0,
                    "Overflow scrolls horizontally only");
                var fullDrag = Drag(data, DragDropEffects.Copy);
                Invoke(window, "OnDragOver", null, fullDrag);
                Check(fullDrag.Effects == DragDropEffects.None && Field<TextBlock>(window, "dropHintText").Text.Contains("dolu"),
                    "A full shelf rejects additions with an explanatory hint");
                Invoke(window, "SetDragTargetVisual", false);
                SetTheme(ThemePreference.Dark);
                Render(window, "full-dark");
                Check(ThemePalette.Text.Color.R > ThemePalette.Surface.Color.R, "Dark palette keeps text lighter than the surface");
                SetTheme(ThemePreference.Light);
                Check(ThemePalette.Text.Color.R < ThemePalette.Surface.Color.R, "Light palette keeps text darker than the surface");
                shelf.Remove(shelf.Paths.ToArray());
                Invoke(window, "UpdateShelf");
                Render(window, "cleared-light");
                Check(section.Visibility == Visibility.Collapsed && Field<Button>(window, "shelfRecovery").Visibility == Visibility.Visible,
                    "Removing the last entry hides the empty shelf and keeps undo accessible");
                Check(File.ReadAllText(document) == "Copy-only shelf original" && File.Exists(photo) && Directory.Exists(folder),
                    "Shelf add and remove preserve original files and folders");
                RunSecurityChecks(photo, document);
                RunDockingChecks(document);
                await DesktopAndSurfaceChecks.RunAsync(output);
                await MediaIntegrationChecks.RunAsync(output);
                await PlayerVolumeChecks.RunAsync();
                await RefreshExitChecks.RunAsync(output);
                await PlayerRefinementChecks.RunAsync(output);
                await MetadataRaceChecks.RunAsync();
                await PerformanceOptimizationChecks.RunAsync();
                SettingsBindingChecks.Run();
                await FeaturePackChecks.RunAsync(output);
                await SettingsRefreshChecks.RunAsync();
                await MediaReturnChecks.RunAsync(output);
                await LifecycleChecks.RunAsync(output);
                await AppNotificationChecks.RunAsync(output);
                WindowsNotificationChecks.Run(output);
                NotificationBannerChecks.Run();
                ProductPackChecks.Run(output);
                SetTheme(ThemePreference.System);
                Invoke(window, "SetExpanded", false);
                Render(window, "compact-system");
                RenderSampleMedia(window);
            }
            finally { window.Close(); }
        }

        private static void RunDockingChecks(string document)
        {
            Check(DockLayout.Resolve(DockPreference.Automatic, TaskbarEdge.Bottom) == DockEdge.Top
                && DockLayout.Resolve(DockPreference.Automatic, TaskbarEdge.Top) == DockEdge.Bottom,
                "Automatic docking chooses the edge opposite the horizontal taskbar");
            Check(DockLayout.Resolve(DockPreference.Top, TaskbarEdge.Top) == DockEdge.Top
                && DockLayout.Resolve(DockPreference.Bottom, TaskbarEdge.Bottom) == DockEdge.Bottom,
                "Explicit upper and lower positions override taskbar synchronization");
            Check(DockLayout.Resolve(DockPreference.Automatic, TaskbarEdge.Left) == DockEdge.Top
                && DockLayout.Resolve(DockPreference.Automatic, TaskbarEdge.Right) == DockEdge.Top,
                "Vertical taskbars use the documented upper-edge fallback");
            var monitor = new Rect(-1920, -1080, 1920, 1080);
            var work = new Rect(-1920, -1040, 1920, 1040);
            foreach (double scale in new[] { 1.0, 1.25, 1.5 })
            {
                var collapsed = new Size(420 * scale, 4 * scale);
                var opened = new Size(420 * scale, 500 * scale);
                var lowerClosed = DockLayout.Position(monitor, work, collapsed, DockEdge.Bottom);
                var lowerOpen = DockLayout.Position(monitor, work, opened, DockEdge.Bottom);
                var upperOpen = DockLayout.Position(monitor, work, opened, DockEdge.Top);
                Check(lowerClosed.Y + collapsed.Height == monitor.Bottom && lowerOpen.Y + opened.Height == monitor.Bottom
                    && upperOpen.Y == monitor.Top && lowerOpen.X + opened.Width / 2 == work.Left + work.Width / 2,
                    "Edge attachment survives height changes and negative monitor coordinates at scale " + scale);
            }
            string preferencesPath = Path.Combine(output, "widget.settings");
            var preferences = new WidgetSettings { OpenOnHover = false, HoverDelayMs = 0 };
            var widget = new IslandWindow(preferences);
            try
            {
                var shell = Field<Border>(widget, "shell");
                Render(widget, "idle-hidden");
                Check(shell.Height == 4 && Field<Grid>(widget, "compact").Visibility == Visibility.Collapsed
                    && Field<TextBlock>(widget, "compactTitle").Text == "" && shell.IsHitTestVisible,
                    "Idle mode removes the name and leaves only the edge recovery area");
                Invoke(widget, "OnShellMouseEnter");
                Render(widget, "hover-disabled");
                Check(Field<StackPanel>(widget, "panel").Visibility == Visibility.Collapsed,
                    "Disabled hover opening does not reveal the panel");
                shell.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                Render(widget, "click-opened");
                Check(Field<StackPanel>(widget, "panel").Visibility == Visibility.Visible,
                    "Clicking the edge opens the panel with hover disabled");
                Invoke(widget, "TogglePinned");
                Render(widget, "shortcut-closed");
                Check(shell.Height == 4, "The panel shortcut also returns to hidden idle mode");
                preferences.OpenOnHover = true;
                Invoke(widget, "ApplySettings", false);
                Invoke(widget, "OnShellMouseEnter");
                Render(widget, "hover-enabled");
                Check(Field<StackPanel>(widget, "panel").Visibility == Visibility.Collapsed,
                    "Hover does not open an invisible window even at zero delay");
                Invoke(widget, "ClosePanel");
                var shelf = Field<FileShelf>(widget, "shelf");
                shelf.Add(new[] { document });
                Invoke(widget, "UpdateShelf");
                Render(widget, "files-compact");
                Check(shell.Height == 36 && Field<TextBlock>(widget, "compactTitle").Text == "1 dosya",
                    "Stored files keep the compact view visible without idle branding");
                preferences.AutoHide = true;
                Invoke(widget, "ApplySettings", false);
                Check(shell.Height == 4 && shelf.Paths.Count == 1, "Optional auto-hide preserves stored shelf items");
                preferences.Dock = DockPreference.Bottom;
                Invoke(widget, "ApplySettings", false);
                Invoke(widget, "SetExpanded", true);
                Render(widget, "lower-dock-open");
                Check(shell.CornerRadius.TopLeft > 0 && shell.CornerRadius.BottomLeft == 0
                    && shell.CornerRadius.BottomRight == 0 && shell.BorderThickness.Bottom == 0 && shell.BorderThickness.Top == 1,
                    "Lower docking rounds only the upper corners and removes the attached outline");
                preferences.Dock = DockPreference.Top;
                Invoke(widget, "ApplySettings", false);
                Render(widget, "upper-dock-open");
                Check(shell.CornerRadius.TopLeft == 0 && shell.CornerRadius.TopRight == 0
                    && shell.CornerRadius.BottomLeft > 0 && shell.BorderThickness.Top == 0 && shell.BorderThickness.Bottom == 1,
                    "Upper docking rounds only the lower corners and removes the attached outline");
                preferences.Opacity = 0.55;
                Invoke(widget, "ApplySettings", false);
                Check(shell.Opacity == 1 && Math.Abs(shell.Background.Opacity - (SystemParameters.HighContrast ? 1 : 0.55)) < 0.001,
                    "Surface opacity changes the background while keeping text and recovery opaque");
                Check(preferences.Save(preferencesPath), "Widget preferences can be saved to a local test file");
                var restored = WidgetSettings.Load(preferencesPath);
                Check(restored.AutoHide && restored.OpenOnHover && restored.Dock == DockPreference.Top && restored.Opacity == 0.55,
                    "Dock, hover, hiding and opacity preferences survive a reload");
                Check(!preferences.Save(Path.Combine(document, "cannot-save.settings")) && preferences.AutoHide,
                    "A denied settings save is recoverable without losing the current choices");
                File.WriteAllText(preferencesPath, "Opacity=NaN\nDock=Unknown\nAutoHide=invalid");
                restored = WidgetSettings.Load(preferencesPath);
                Check(restored.Opacity == 1 && restored.Dock == DockPreference.Automatic && !restored.AutoHide,
                    "Invalid settings fall back to usable defaults");
                File.WriteAllText(preferencesPath, new string('x', WidgetSettings.MaximumLength + 1));
                Check(WidgetSettings.Load(preferencesPath).OpenOnHover, "Oversized settings files fall back safely");
            }
            finally { widget.Close(); }
        }

        private static void RunSecurityChecks(string photo, string document)
        {
            string normalized;
            string[] rejected = { @"\\example.invalid\share\image.png", @"\\?\C:\image.png", @"\\.\pipe\widgates",
                @"C:image.png", @"\image.png", "https://example.invalid/image.png", document + ":hidden", "bad\0path" };
            Check(rejected.All(path => !LocalFileAccess.TryGetPath(path, out normalized)),
                "UNC, device, URL, relative and alternate-stream paths are rejected before file access");
            var isolatedShelf = new FileShelf();
            Check(isolatedShelf.Add(rejected.Concat(new[] { document })) == 1 && isolatedShelf.Paths[0] == document,
                "Rejected paths cannot enter a mixed local shelf drop");
            string junction = Path.Combine(output, "junction-fixture");
            if (Directory.Exists(junction))
                Check(!LocalFileAccess.TryGetPath(junction, out normalized)
                    && !LocalFileAccess.TryGetPath(Path.Combine(junction, "Document.txt"), out normalized),
                    "Junctions and files beneath a junction are rejected");
            else Console.WriteLine("UNVERIFIED Junction fixture absent");
            using (var stream = File.OpenRead(photo))
            {
                var image = PreviewImage.Decode(stream, 192);
                Check(image != null && image.IsFrozen && Math.Max(image.PixelWidth, image.PixelHeight) <= 192,
                    "Media and shelf preview decoder bounds the output dimensions");
            }
            using (var oversized = new MemoryStream(new byte[PreviewImage.MaximumBytes + 1]))
                Check(PreviewImage.Decode(oversized, 96) == null, "Oversized image streams are rejected");
            using (var malformed = new MemoryStream(new byte[] { 1, 2, 3, 4 }))
                Check(PreviewImage.Decode(malformed, 192) == null, "Malformed media covers fall back safely");
        }

        private static void RenderSampleMedia(IslandWindow window)
        {
            // Explicitly synthetic media for visual review; never represents a live session.
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                var background = new LinearGradientBrush(Color.FromRgb(40, 50, 100), Color.FromRgb(206, 128, 133), 65);
                drawing.DrawRectangle(background, null, new Rect(0, 0, 128, 128));
                drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(100, 250, 215, 166)), null, new Point(91, 40), 28, 28);
                drawing.DrawGeometry(new SolidColorBrush(Color.FromRgb(35, 34, 56)), null,
                    Geometry.Parse("M 0 94 L 35 70 L 62 91 L 89 64 L 128 100 L 128 128 L 0 128 Z"));
            }
            var cover = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
            cover.Render(visual);
            cover.Freeze();
            var controller = Field<MediaController>(window, "media");
            foreach (var value in new[] { new { Name = "Title", Value = "Gece Yolculuğu" },
                new { Name = "Artist", Value = "Örnek sanatçı · Örnek albüm" }, new { Name = "Notice", Value = "" } })
                typeof(MediaController).GetProperty(value.Name, Private).SetValue(controller, value.Value, null);
            typeof(MediaController).GetField("<Thumbnail>k__BackingField", Private).SetValue(controller, cover);
            Invoke(window, "UpdateMedia");
            Field<Button>(window, "retry").Visibility = Visibility.Collapsed;
            foreach (string name in new[] { "play", "previous", "next" }) Field<Button>(window, name).IsEnabled = true;
            Invoke(window, "SetExpanded", true);
            SetTheme(ThemePreference.Light);
            Render(window, "sample-media-light");
            SetTheme(ThemePreference.Dark);
            Render(window, "sample-media-dark");
            var preferences = Field<WidgetSettings>(window, "settings");
            foreach (var style in new[] { SurfaceStyle.Solid, SurfaceStyle.SoftGlass, SurfaceStyle.LiquidGlass })
            {
                preferences.Style = style;
                Invoke(window, "ApplySettings", false);
                Render(window, "surface-" + style + "-dark");
            }
            SetTheme(ThemePreference.Light);
            Invoke(window, "ApplySettings", false);
            Render(window, "surface-LiquidGlass-light");
            SetTheme(ThemePreference.Dark);
            preferences.Dock = DockPreference.Bottom;
            Invoke(window, "ApplySettings", false);
            Render(window, "sample-media-bottom-dark");
            Invoke(window, "SetExpanded", false);
            Render(window, "sample-compact-dark");
            Console.WriteLine("RENDER Synthetic media previews; no real media source or commands used");
        }

        private static ScrollViewer FindViewer(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var viewer = child as ScrollViewer ?? FindViewer(child);
                if (viewer != null) return viewer;
            }
            return null;
        }
    }
}
