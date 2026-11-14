using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Widgates
{
    internal static class NativeMenuChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly StringBuilder Report = new StringBuilder();

        internal static async Task RunAsync(string output)
        {
            var model = new WidgetSettings { HideWhenIdle = false, AutoPauseOthers = false, HideInGames = false };
            var widget = new IslandWindow(model);
            SettingsWindow dialog = null;
            try
            {
                Log("OS " + Environment.OSVersion.VersionString + "; real WPF settings window; no media commands or saved preference changes");
                widget.IsHitTestVisible = false;
                typeof(IslandWindow).GetProperty("pinned", Private).SetValue(widget, true);
                widget.Show(); await Task.Delay(700);
                Invoke(widget, "SetExpanded", true); await Task.Delay(250);
                var panel = Field<StackPanel>(widget, "panel");
                Check(Field<SettingsWindow>(widget, "settingsWindow") == null, "Settings window is absent until explicitly opened");
                Check(!Descendants(panel).OfType<Button>().Any(button => System.Windows.Automation.AutomationProperties.GetName(button).Contains("Çık")),
                    "Media header has no application exit button");
                Check(!Descendants(panel).OfType<Image>().Any(item => ReferenceEquals(item.Source, BrandAssets.HeaderLogo)), "Branding stays outside the media panel");
                var play = Field<Button>(widget, "play"); var previous = Field<Button>(widget, "previous"); var next = Field<Button>(widget, "next");
                var shell = Field<Border>(widget, "shell");
                double center = play.TranslatePoint(new Point(play.ActualWidth / 2, 0), shell).X;
                double left = previous.TranslatePoint(new Point(previous.ActualWidth / 2, 0), shell).X;
                double right = next.TranslatePoint(new Point(next.ActualWidth / 2, 0), shell).X;
                if (play.IsVisible)
                {
                    Check(Math.Abs(center - shell.ActualWidth / 2) < 0.7, "Play button is centered in the complete media panel");
                    Check(Math.Abs((center - left) - (right - center)) < 0.7, "Previous and next sit symmetrically around play");
                }
                else Log("UNVERIFIED Live playback button geometry: no track is published; controls are correctly hidden");
                Check(!ToolTipService.GetIsEnabled(play) && !ToolTipService.GetIsEnabled(next)
                    && !string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(play)),
                    "Playback controls keep accessible names without visible tooltip chips");
                var media = Field<MediaController>(widget, "media");
                var apple = media.Sessions.FirstOrDefault(source => MediaSourceNames.Friendly(source.SourceAppUserModelId) == "Apple Music");
                if (apple != null) { media.Select(apple); await Task.Delay(200); }
                if (media.Playback != null && !media.Playback.Controls.IsShuffleEnabled && !media.Playback.Controls.IsRepeatEnabled)
                    Check(Field<Button>(widget, "shuffle").Visibility == Visibility.Collapsed && Field<Button>(widget, "repeat").Visibility == Visibility.Collapsed,
                        "Unsupported shuffle and repeat controls are hidden for the live source");
                media.Select(null); await Task.Delay(200);
                var picker = Field<ComboBox>(widget, "sessions");
                if (media.Sessions.Count > 0)
                {
                    Check(picker.Tag is Grid && !Descendants((Grid)picker.Tag).OfType<TextBlock>().Any()
                        && System.Windows.Automation.AutomationProperties.GetName(picker).Contains(MediaSourceNames.Friendly(media.Active.SourceAppUserModelId))
                        && (string)((ComboBoxItem)picker.Items[0]).Content == "Otomatik takip", "Source header uses an icon with an accessible app name and explicit automatic following in the menu");
                    picker.IsDropDownOpen = true; await Task.Delay(50);
                    object firstChoice = picker.Items[0];
                    typeof(MediaController).GetProperty("Sessions", Private).SetValue(media, media.Sessions.ToArray(), null);
                    Invoke(widget, "UpdateMedia");
                    Check(picker.IsDropDownOpen && ReferenceEquals(picker.Items[0], firstChoice), "An incoming source-list update does not rebuild the open picker");
                    var sourcePopup = (Popup)picker.Template.FindName("PART_Popup", picker);
                    Capture((FrameworkElement)sourcePopup.Child, Path.Combine(output, "source-menu.png"));
                    picker.IsDropDownOpen = false; await Task.Delay(50);
                    Check(!ReferenceEquals(picker.Items[0], firstChoice), "Closing the picker applies the deferred source-list update");
                }
                else Log("UNVERIFIED Source picker with a live media app: no published session");
                int saves = 0;
                dialog = new SettingsWindow(model, save => { if (save) saves++; }, preference =>
                {
                    typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, preference);
                    ThemePalette.Refresh(); return true;
                }, value => { model.StartWithWindows = value; return true; }, () => { });
                dialog.ShowActivated = false; dialog.IsHitTestVisible = false; dialog.Show();
                foreach (var preference in new[] { ThemePreference.Dark, ThemePreference.Light })
                {
                    typeof(ThemePalette).GetField("<Preference>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, preference);
                    ThemePalette.Refresh(); Invoke(widget, "ApplySettings", false);
                    dialog.SelectPage(0); await Task.Delay(100);
                    var surface = (Border)dialog.Content;
                    Check(((SolidColorBrush)surface.Background).Color == ThemePalette.Surface.Color, preference + " settings use the matching opaque surface");
                    Check(Descendants(surface).OfType<TextBlock>().Where(text => text.Text.Length > 0).All(text => ((SolidColorBrush)text.Foreground).Color == ThemePalette.Text.Color
                        || ((SolidColorBrush)text.Foreground).Color == ThemePalette.Muted.Color || ((SolidColorBrush)text.Foreground).Color == ThemePalette.DangerInk.Color), preference + " settings labels have readable theme colors");
                    Check(Descendants(surface).OfType<Image>().Any(image => ReferenceEquals(image.Source, BrandAssets.HeaderLogo)), preference + " settings contain the Morf logo");
                    for (int pageIndex = 0; pageIndex < 6; pageIndex++)
                    {
                        dialog.SelectPage(pageIndex); await Task.Delay(80); dialog.UpdateLayout();
                        Capture(surface, Path.Combine(output, "settings-" + preference + "-" + pageIndex + ".png"));
                        Check(dialog.SelectedPage == pageIndex && Field<StackPanel>(dialog, "page").ActualHeight > 0, preference + " settings page " + pageIndex + " is available");
                        if (pageIndex == 1)
                        {
                            var advanced = Descendants(surface).OfType<Expander>().Single();
                            var header = (ToggleButton)advanced.Template.FindName("Header", advanced);
                            Check(advanced.Content == null && !advanced.IsExpanded && header.IsChecked == false,
                                preference + " advanced controls remain uncreated until the themed header opens");
                            int beforeExpansion = saves;
                            header.IsChecked = true; dialog.UpdateLayout();
                            object advancedContent = advanced.Content;
                            Check(advanced.IsExpanded && advancedContent != null && saves == beforeExpansion,
                                preference + " header binding opens advanced settings without saving preferences");
                            Field<ScrollViewer>(dialog, "scroll").ScrollToVerticalOffset(advanced.TranslatePoint(new Point(), Field<StackPanel>(dialog, "page")).Y);
                            await Task.Delay(60); dialog.UpdateLayout();
                            Capture(surface, Path.Combine(output, "advanced-" + preference + ".png"));
                            header.IsChecked = false; advanced.IsExpanded = true; dialog.UpdateLayout();
                            Check(header.IsChecked == true && ReferenceEquals(advanced.Content, advancedContent) && saves == beforeExpansion,
                                preference + " advanced settings reopen with the same controls and synchronized header");
                            advanced.IsExpanded = false;
                            Field<ScrollViewer>(dialog, "scroll").ScrollToTop();
                            double wide = dialog.Width; dialog.Width = 480; dialog.UpdateLayout();
                            var compactNavigation = Field<ComboBox>(dialog, "compactNavigation");
                            Check(compactNavigation.IsVisible && (int)compactNavigation.SelectedValue == pageIndex && !Field<Grid>(dialog, "sidebar").IsVisible,
                                preference + " narrow navigation retains the selected page name");
                            Capture(surface, Path.Combine(output, "narrow-" + preference + ".png"));
                            dialog.Width = wide; dialog.UpdateLayout();
                        }
                        if (pageIndex == 3)
                        {
                            foreach (var expander in Descendants(surface).OfType<Expander>().ToArray()) expander.IsExpanded = true;
                            Field<ScrollViewer>(dialog, "scroll").ScrollToEnd(); await Task.Delay(60); dialog.UpdateLayout();
                            Capture(surface, Path.Combine(output, "rules-" + preference + ".png"));
                        }
                    }
                    dialog.SelectPage(0); await Task.Delay(60);
                    var slider = Descendants(surface).OfType<Slider>().First();
                    VerifySlider(slider, surface, preference + " appearance");
                    int before = saves; Slider.DecreaseSmall.Execute(null, slider);
                    Check(model.Opacity < 1 && saves == before, preference + " appearance slider previews without writing each movement");
                    slider.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), Environment.TickCount, System.Windows.Input.Key.Left)
                        { RoutedEvent = UIElement.PreviewKeyUpEvent });
                    Check(saves == before + 1, preference + " appearance slider commits once when the input gesture finishes");
                    Invoke(widget, "SetExpanded", true); Invoke(widget, "UpdateState", false);
                    VerifySlider(Field<Slider>(widget, "seekSlider"), shell, preference + " playback");
                    Capture(Field<Border>(widget, "shell"), Path.Combine(output, "player-" + preference + ".png"));
                    Invoke(widget, "UpdateVolumeDisplay", new VolumeState { Available = true, Level = 0.58f, Muted = false });
                    Field<Border>(widget, "volumePanel").Visibility = Visibility.Visible; Invoke(widget, "UpdateState", false);
                    VerifySlider(Field<Slider>(widget, "volumeSlider"), shell, preference + " volume");
                    Check(Field<Button>(widget, "muteButton").Content is System.Windows.Shapes.Path, preference + " inline mute uses an icon rather than text");
                    Capture(Field<Border>(widget, "shell"), Path.Combine(output, "volume-" + preference + ".png"));
                    Field<Border>(widget, "volumePanel").Visibility = Visibility.Collapsed;
                    Invoke(widget, "UpdateState", false);
                    double playerHeight = Field<Border>(widget, "shell").Height;
                    var launcher = Descendants(panel).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Medya uygulaması aç");
                    launcher.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(50);
                    var applicationMenu = Field<ContextMenu>(widget, "applicationMenu");
                    Check(applicationMenu.Items.Cast<MenuItem>().All(item => item.Header is Grid && !string.IsNullOrEmpty(item.ToolTip as string))
                        && !Descendants(applicationMenu).OfType<TextBlock>().Any(), preference + " application hub uses named icon actions without text tiles or nested cards");
                    Check(applicationMenu.IsOpen && applicationMenu.Items.Count == 3 && Field<Border>(widget, "shell").Height == playerHeight,
                        preference + " media launcher opens three explicit choices without resizing the player or opening an application");
                    Capture(Field<Border>(widget, "shell"), Path.Combine(output, "launcher-" + preference + ".png"));
                    Capture(applicationMenu, Path.Combine(output, "launcher-menu-" + preference + ".png"));
                    launcher.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Check(!applicationMenu.IsOpen, preference + " launcher toggles closed");
                    launcher.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Invoke(widget, "SetExpanded", false);
                    Check(!applicationMenu.IsOpen, preference + " collapsing the owner also dismisses its application menu");
                }
                dialog.Close(); dialog = null;
                Invoke(widget, "OpenSettings"); await Task.Delay(100);
                var opened = Field<SettingsWindow>(widget, "settingsWindow");
                Check(opened != null && opened.IsVisible, "Settings button opens the on-demand settings window");
                opened.Close();
                Check(Field<SettingsWindow>(widget, "settingsWindow") == null, "Closing settings releases the owner's window reference");
            }
            finally
            {
                if (dialog != null) dialog.Close(); widget.Close();
                File.WriteAllText(Path.Combine(output, "native-settings.txt"), Report.ToString());
            }
        }
        private static void VerifySlider(Slider slider, FrameworkElement owner, string scenario)
        {
            if (!slider.IsVisible) { Log("UNVERIFIED " + scenario + " slider: source has no visible timeline"); return; }
            owner.UpdateLayout(); slider.ApplyTemplate();
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            double saved = slider.Value;
            // Value endpoints must keep the larger drag target inside the slider's layout slot.
            foreach (double value in new[] { slider.Minimum, slider.Maximum })
            {
                slider.Value = value; owner.UpdateLayout();
                var thumb = track.Thumb;
                Point start = thumb.TranslatePoint(new Point(), slider);
                if (thumb.ActualWidth < 28 || thumb.ActualHeight < 28 || start.X < -0.5 || start.Y < -0.5
                    || start.X + thumb.ActualWidth > slider.ActualWidth + 0.5 || start.Y + thumb.ActualHeight > slider.ActualHeight + 0.5)
                    throw new InvalidOperationException(scenario + " slider clips its drag target at an endpoint");
            }
            slider.Value = saved;
            Check(true, scenario + " slider keeps its full drag target inside both endpoints");
        }

        private static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, Private).GetValue(instance); }
        private static void Invoke(object instance, string method, params object[] args)
        {
            instance.GetType().GetMethod(method, Private | BindingFlags.DeclaredOnly).Invoke(instance, args);
        }

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        private static void Capture(FrameworkElement element, string path)
        {
            element.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            image.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Log("PASS " + message);
        }
        private static void Log(string message) { Console.WriteLine(message); Report.AppendLine(message); }
    }
}
