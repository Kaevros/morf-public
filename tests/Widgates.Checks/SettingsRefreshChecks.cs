using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Widgates
{
    internal static class SettingsRefreshChecks
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        internal static async Task RunAsync()
        {
            var model = new WidgetSettings { AppRules = "example.exe = gizle" }; int saves = 0, theme = -1, started = 0, cancelled = 0, exits = 0;
            var window = new SettingsWindow(model, new SettingsActions { Apply = save => { if (save) saves++; }, SelectTheme = value => { theme = (int)value; return true; },
                ConfigureStartup = _ => true, Exit = () => exits++, StartSleep = value => started = value, CancelSleep = () => cancelled++, SleepStatus = () => "Kapalı" });
            try
            {
                var radios = Children(window.Content as DependencyObject).OfType<RadioButton>().ToArray();
                Check(radios.Length == 6 && !Children(Page(window)).OfType<ComboBox>().Any(), "Theme and surface expose six visible choices without dropdown menus");
                int targetTheme = ThemePalette.Preference == ThemePreference.Light ? 2 : 1;
                Radio(window, targetTheme == 1 ? "Tema · Açık" : "Tema · Koyu").IsChecked = true;
                Check(theme == targetTheme, "Visual theme selection routes to the existing theme preference");
                Radio(window, "Yüzey · Düz").IsChecked = true;
                var glassSlider = Children(Page(window)).OfType<Slider>().Single(slider => AutomationProperties.GetName(slider) == "Cam etkisi");
                var glassCard = (Border)LogicalTreeHelper.GetParent(LogicalTreeHelper.GetParent(glassSlider));
                Check(model.Style == SurfaceStyle.Solid && glassCard.Visibility == Visibility.Collapsed && saves == 1, "Solid surface selection commits once and hides irrelevant glass intensity");
                var exit = Children((DependencyObject)window.Content).OfType<Button>().First(button => button.Content as string == "Morf'tan Çık");
                Check(ReferenceEquals(exit.Background, ThemePalette.Danger) && ReferenceEquals(exit.Foreground, ThemePalette.DangerInk) && exits == 0,
                    "Exit is a dedicated red action without triggering on settings creation");
                window.SelectPage(2);
                Radio(window, "Uyku süresi · 60 dk").IsChecked = true; Click(Button(window, "Başlat")); Click(Button(window, "İptal et"));
                Check(started == 60 && cancelled == 1 && Children(Page(window)).OfType<ComboBox>().All(picker => AutomationProperties.GetName(picker) != "Uyku süresi"), "The themed sleep choices start the selected duration and cancellation remains explicit");
                window.SelectPage(3);
                Children(Page(window)).OfType<Expander>().Single().IsExpanded = true;
                Check(!Children(Page(window)).OfType<TextBox>().Any() && !Children(Page(window)).OfType<Button>().Any(button => (button.Content as string ?? "").Contains(".exe ekle")),
                    "Application rules use selection controls without a text editor or machine-specific recent-app button");
                Radio(window, "example kuralı · Göster").IsChecked = true;
                ApplicationRules parsed; ApplicationRules.TryParse(model.AppRules, out parsed);
                Check(parsed.Hidden("example") == false, "Existing saved rules migrate into actionable hide/show choices");
                Invoke(window, "AddApplicationRule", "other"); ApplicationRules.TryParse(model.AppRules, out parsed);
                Check(parsed.Entries.Length == 2 && parsed.Hidden("other") == true, "Adding an application preserves existing rules and defaults the new rule to hiding");
                Invoke(window, "AddApplicationRule", "OTHER"); ApplicationRules.TryParse(model.AppRules, out parsed);
                Check(parsed.Entries.Length == 2 && parsed.Hidden("example") == false, "A duplicate application cannot overwrite the existing rule list");
                Click(Children(Page(window)).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "other kuralını kaldır"));
                ApplicationRules.TryParse(model.AppRules, out parsed);
                Check(parsed.Entries.Length == 1 && parsed.Hidden("example") == false, "Removing one application preserves the remaining behavior");
            }
            finally { window.Close(); }
            var empty = new IslandWindow(new WidgetSettings());
            try
            {
                var apps = (StackPanel)typeof(IslandWindow).GetField("applicationPanel", Private).GetValue(empty);
                var controls = (Grid)typeof(IslandWindow).GetField("playbackControls", Private).GetValue(empty);
                Check(apps.Visibility == Visibility.Visible && controls.Visibility == Visibility.Collapsed,
                    "An empty player offers application launch choices in place of unusable playback controls");
            }
            finally { empty.Close(); }
            int calls = 0; var gate = new ManualResetEventSlim(false);
            try
            {
                var launcher = new MediaAppLauncher(application => { Interlocked.Increment(ref calls); gate.Wait(2000); return true; });
                var first = launcher.OpenAsync(MediaApplication.Spotify);
                string second = await launcher.OpenAsync(MediaApplication.YouTube); gate.Set(); await first;
                Check(calls == 1 && second.Contains("sürüyor"), "Repeated launch clicks cannot queue multiple applications while an opening is pending");
                await launcher.OpenAsync((MediaApplication)99);
                Check(calls == 1, "Unsupported launch targets are rejected before reaching the operating system");
                int attempts = 0;
                var recovering = new MediaAppLauncher(application => { if (++attempts == 1) throw new System.Runtime.InteropServices.COMException(); return true; });
                string failed = await recovering.OpenAsync(MediaApplication.AppleMusic); string recovered = await recovering.OpenAsync(MediaApplication.AppleMusic);
                Check(failed.Contains("açılamadı") && recovered.Contains("iletildi"), "A missing or denied application remains recoverable on the next explicit launch");
                string notificationTarget = null; bool notificationSta = false;
                var notificationLauncher = new MediaAppLauncher(notificationLaunch: id => { notificationTarget = id; notificationSta = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA; return true; });
                string desktopOpened = await notificationLauncher.OpenNotificationAppAsync("Spotify", "Spotify");
                Check(notificationTarget == "Spotify" && notificationSta && desktopOpened.Contains("iletildi"),
                    "Desktop notification identities reach registered application activation on an STA worker");
                notificationTarget = null;
                await notificationLauncher.OpenNotificationAppAsync("C:\\evil.exe", "Ignored title");
                Check(notificationTarget == null, "Notification text and file paths cannot become launch targets");
                var declined = new MediaAppLauncher(notificationLaunch: _ => false);
                Check((await declined.OpenNotificationAppAsync("Missing.App", "Missing")).Contains("açılamadı"),
                    "An unregistered notification app stays visible as a recoverable opening failure");
            }
            finally { gate.Dispose(); }
        }
        private static StackPanel Page(SettingsWindow window) { return (StackPanel)typeof(SettingsWindow).GetField("page", Private).GetValue(window); }
        private static Button Button(SettingsWindow window, string label) { return Children(Page(window)).OfType<Button>().Single(button => button.Content as string == label); }
        private static RadioButton Radio(SettingsWindow window, string label) { return Children(Page(window)).OfType<RadioButton>().Single(button => AutomationProperties.GetName(button) == label); }
        private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        private static void Invoke(SettingsWindow window, string method, string argument) { typeof(SettingsWindow).GetMethod(method, Private).Invoke(window, new object[] { argument }); }
        private static IEnumerable<DependencyObject> Children(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var descendant in Children(child)) yield return descendant; }
        }
        private static void Check(bool success, string name) { if (!success) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
    }
}
