using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Widgates
{
    internal sealed class PictureInPictureSetup
    {
        private string name, executable, page;
        public override string ToString() { return name; }

        private static void Find(List<PictureInPictureSetup> choices, string name, string relative, string page)
        {
            var paths = new List<string>();
            string key = @"\Software\Microsoft\Windows\CurrentVersion\App Paths\" + Path.GetFileName(relative);
            foreach (string hive in new[] { "HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE" })
            {
                try { paths.Add((Registry.GetValue(hive + key, "", "") as string ?? "").Trim('"')); }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
            }
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
                paths.Add(Path.Combine(Environment.GetFolderPath(folder), relative));
            foreach (string path in paths)
                if (Path.IsPathRooted(path) && File.Exists(path) && string.Equals(Path.GetFileName(path), Path.GetFileName(relative), StringComparison.OrdinalIgnoreCase))
                { choices.Add(new PictureInPictureSetup { name = name, executable = path, page = page }); break; }
        }

        internal static PictureInPictureSetup Choose(Window owner)
        {
            var choices = new List<PictureInPictureSetup>();
            Find(choices, "Brave", @"BraveSoftware\Brave-Browser\Application\brave.exe", "brave://extensions/");
            Find(choices, "Google Chrome", @"Google\Chrome\Application\chrome.exe", "chrome://extensions/");
            Find(choices, "Microsoft Edge", @"Microsoft\Edge\Application\msedge.exe", "edge://extensions/");
            Find(choices, "Vivaldi", @"Vivaldi\Application\vivaldi.exe", "vivaldi://extensions/");
            Find(choices, "Opera", @"Programs\Opera\opera.exe", "opera://extensions/");
            choices.Add(new PictureInPictureSetup { name = "Diğer Chromium tarayıcısı" });
            var dialog = new Window { Title = "Morf · PiP eklentisi", SizeToContent = SizeToContent.Height, Width = 370,
                ResizeMode = ResizeMode.NoResize, Background = ThemePalette.Surface, Icon = BrandAssets.WindowIcon,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner };
            if (owner != null) dialog.Owner = owner;
            var panel = new StackPanel { Margin = new Thickness(20) };
            var picker = new ComboBox { ItemsSource = choices, SelectedIndex = 0, MinHeight = 34 };
            IslandWindow.StyleSessionPicker(picker);
            AutomationProperties.SetName(picker, "Tarayıcı");
            panel.Children.Add(picker);
            panel.Children.Add(new TextBlock { Text = "Uzantılar sayfasında Geliştirici modu’nu açın. “Paketlenmemiş öğe yükle” ile açılan extension klasörünü seçin.",
                TextWrapping = TextWrapping.Wrap, Foreground = ThemePalette.Text, Margin = new Thickness(0, 12, 0, 12) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(SettingsWindow.Button("Şimdi değil", () => dialog.DialogResult = false));
            buttons.Children.Add(SettingsWindow.Button("Kurulumu aç", () => dialog.DialogResult = true));
            panel.Children.Add(buttons); dialog.Content = panel;
            return dialog.ShowDialog() == true ? (PictureInPictureSetup)picker.SelectedItem : null;
        }

        internal bool Open()
        {
            if (executable == null) return false;
            try { using (Process.Start(new ProcessStartInfo(executable, page) { UseShellExecute = false })) { } return true; }
            catch (Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
        }
    }
}
