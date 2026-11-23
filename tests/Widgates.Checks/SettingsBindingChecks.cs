using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Widgates
{
    internal static class SettingsBindingChecks
    {
        internal static void Run()
        {
            var model = new WidgetSettings(); int commits = 0;
            var dialog = new SettingsWindow(model, save => { if (save) commits++; }, _ => true, _ => false, () => { });
            try
            {
                dialog.SelectPage(1);
                ExpandAdvanced(dialog);
                var choices = Toggles(dialog);
                foreach (var choice in choices) Click(choice);
                Check(!model.EdgeOnly && !model.HideWhenIdle && !model.AutoHide && !model.OpenOnHover && !model.ScrollTitles && !model.PeekTrackChanges && !model.RespectQuietState && commits == 5,
                    "Behavior page switches update their own preferences once per click");
                dialog.SelectPage(2);
                foreach (var choice in Toggles(dialog)) Click(choice);
                Check(!model.AutoPauseOthers && !model.ResumeInterrupted && !model.ShowTimeline && !model.ScrollVolume && model.PictureInPicture && commits == 10,
                    "Media and sound switches preserve the playback, timeline and volume bindings");
                dialog.SelectPage(3);
                ExpandAdvanced(dialog);
                choices = Toggles(dialog);
                foreach (var choice in choices.Where(choice => AutomationProperties.GetName(choice) != "Windows ile başlat")) Click(choice);
                Check(!model.ShowTrayIcon && !model.HideInGames && model.HideInFullscreen && !model.FollowDesktops && commits == 14,
                    "System page switches preserve game, fullscreen and desktop preferences");
                var startup = choices.Single(choice => AutomationProperties.GetName(choice) == "Windows ile başlat");
                Click(startup);
                Check(startup.IsChecked == false && !model.StartWithWindows && commits == 14,
                    "Denied startup changes restore the switch without saving an incorrect preference");
                model.StartWithWindows = true; dialog.SelectPage(3);
                startup = Toggles(dialog).Single(choice => AutomationProperties.GetName(choice) == "Windows ile başlat");
                Click(startup);
                Check(startup.IsChecked == true && model.StartWithWindows && commits == 14,
                    "Denied startup removal preserves the previously enabled switch without saving");
            }
            finally { dialog.Close(); }

            var surfaceModel = new WidgetSettings { Opacity = 0.8, LiquidTransparency = 0.4 };
            var surfaceDialog = new SettingsWindow(surfaceModel, _ => { }, _ => true, _ => false, () => { });
            try
            {
                surfaceDialog.SelectPage(0);
                var root = (StackPanel)typeof(SettingsWindow).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(surfaceDialog);
                var controls = Descendants(root).ToArray();
                var liquid = controls.OfType<Slider>().Single(item => AutomationProperties.GetName(item) == "Liquid Glass şeffaflığı");
                var opacity = controls.OfType<Slider>().Single(item => AutomationProperties.GetName(item) == "Yüzey opaklığı");
                var liquidCard = (Border)LogicalTreeHelper.GetParent(LogicalTreeHelper.GetParent(liquid));
                var opacityCard = (Border)LogicalTreeHelper.GetParent(LogicalTreeHelper.GetParent(opacity));
                Check(liquidCard.Visibility == Visibility.Collapsed && opacityCard.Visibility == Visibility.Visible,
                    "Other surfaces show only their own opacity control");
                controls.OfType<RadioButton>().Single(item => AutomationProperties.GetName(item) == "Yüzey · Liquid Glass").IsChecked = true;
                Check(liquidCard.Visibility == Visibility.Visible && opacityCard.Visibility == Visibility.Collapsed,
                    "Selecting Liquid Glass reveals only its own transparency control");
                liquid.Value = 70;
                Check(Math.Abs(surfaceModel.LiquidTransparency - 0.7) < 0.001 && surfaceModel.Opacity == 0.8,
                    "Liquid Glass slider changes its own saved value without changing other surfaces");
                controls.OfType<RadioButton>().Single(item => AutomationProperties.GetName(item) == "Yüzey · Yumuşak cam").IsChecked = true;
                Check(liquidCard.Visibility == Visibility.Collapsed && opacityCard.Visibility == Visibility.Visible && Math.Abs(liquid.Value - 70) < 0.001,
                    "Switching away preserves the independent Liquid Glass setting");
            }
            finally { surfaceDialog.Close(); }
        }
        private static void Click(CheckBox toggle) { toggle.IsChecked = !toggle.IsChecked; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        private static void ExpandAdvanced(SettingsWindow dialog)
        {
            var root = (StackPanel)typeof(SettingsWindow).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
            foreach (var expander in Descendants(root).OfType<Expander>().ToArray()) expander.IsExpanded = true;
        }
        private static CheckBox[] Toggles(SettingsWindow dialog)
        {
            var root = (StackPanel)typeof(SettingsWindow).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
            return Descendants(root).OfType<CheckBox>().ToArray();
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
        private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
    }
}
