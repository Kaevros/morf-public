using System;
using System.IO;
using System.ComponentModel;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Win32;

namespace Widgates
{
    internal enum ThemePreference { System, Light, Dark }

    internal static class ThemePalette
    {
        internal static readonly SolidColorBrush Surface = Create();
        internal static readonly SolidColorBrush Text = Create();
        internal static readonly SolidColorBrush Muted = Create();
        internal static readonly SolidColorBrush Accent = Create();
        internal static readonly SolidColorBrush AccentDark = Create();
        internal static readonly SolidColorBrush AccentHover = Create();
        internal static readonly SolidColorBrush Stroke = Create();
        internal static readonly SolidColorBrush StrokeHover = Create();
        internal static readonly SolidColorBrush Card = Create();
        internal static readonly SolidColorBrush Shelf = Create();
        internal static readonly SolidColorBrush Button = Create();
        internal static readonly SolidColorBrush Hover = Create();
        internal static readonly SolidColorBrush Pressed = Create();
        internal static readonly SolidColorBrush PlaySurface = Create();
        internal static readonly SolidColorBrush PlayInk = Create();
        internal static readonly SolidColorBrush Danger = Create();
        internal static readonly SolidColorBrush DangerHover = Create();
        internal static readonly SolidColorBrush DangerInk = Create();
        private static readonly SolidColorBrush GlassTop = Create();
        private static readonly SolidColorBrush GlassBottom = Create();
        private static readonly SolidColorBrush RimTop = Create();
        private static readonly SolidColorBrush RimBottom = Create();
        internal static readonly Brush Glass = Gradient(GlassTop, GlassBottom);
        internal static readonly Brush GlassEdge = Gradient(RimTop, RimBottom);
        internal static ThemePreference Preference { get; private set; }
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Morf", "theme.txt");

        static ThemePalette()
        {
            try
            {
                string stored = AtomicTextFile.Read(SettingsPath, 64, value => { ThemePreference parsed; return Enum.TryParse(value.Trim(), out parsed) && Enum.IsDefined(typeof(ThemePreference), parsed); });
                if (stored != null)
                {
                    using (var reader = new StringReader(stored))
                    {
                        var buffer = new char[65];
                        int count = reader.ReadBlock(buffer, 0, buffer.Length);
                        ThemePreference saved;
                        if (count <= 64 && Enum.TryParse(new string(buffer, 0, count).Trim(), out saved)
                            && Enum.IsDefined(typeof(ThemePreference), saved))
                            Preference = saved;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            Refresh();
        }

        internal static bool Select(ThemePreference preference)
        {
            Preference = preference;
            Refresh();
            try
            {
                return AtomicTextFile.Write(SettingsPath, preference.ToString());
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
        }

        internal static void Refresh()
        {
            if (SystemParameters.HighContrast)
            {
                foreach (var brush in new[] { Surface, Card, Shelf, Button }) Set(brush, SystemColors.WindowColor);
                foreach (var brush in new[] { Text, Muted }) Set(brush, SystemColors.WindowTextColor);
                foreach (var brush in new[] { Accent, StrokeHover, Stroke }) Set(brush, SystemColors.HighlightColor);
                foreach (var brush in new[] { AccentDark, AccentHover, Hover, Pressed }) Set(brush, SystemColors.ControlColor);
                foreach (var brush in new[] { GlassTop, GlassBottom, PlayInk }) Set(brush, SystemColors.WindowColor);
                foreach (var brush in new[] { RimTop, RimBottom, PlaySurface }) Set(brush, SystemColors.WindowTextColor);
                Set(Danger, SystemColors.HighlightColor); Set(DangerHover, SystemColors.ControlColor); Set(DangerInk, SystemColors.HighlightTextColor);
                return;
            }
            bool light = Preference == ThemePreference.Light;
            if (Preference == ThemePreference.System)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                        light = key == null || Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) != 0;
                }
                catch (SecurityException) { light = true; }
                catch (UnauthorizedAccessException) { light = true; }
                catch (IOException) { light = true; }
            }
            Set(Surface, light ? 0xF3F3F5 : 0x242426);
            Set(Text, light ? 0x202024 : 0xF5F5F7);
            Set(Muted, light ? 0x66666D : 0xAEAEB5);
            Set(Accent, light ? 0x5856D6 : 0xA5A3FF);
            Set(AccentDark, light ? 0xEBEAFF : 0x35334F);
            Set(AccentHover, light ? 0x424246 : 0xD7D7DC);
            Set(Stroke, light ? 0xD4D4DA : 0x48484D);
            Set(StrokeHover, light ? 0xA9A9B0 : 0x6E6E76);
            Set(Card, light ? 0xFFFFFF : 0x303034);
            Set(Shelf, light ? 0xE8E8ED : 0x202023);
            Set(Button, light ? 0xE4E4E9 : 0x3A3A40);
            Set(Hover, light ? 0xDADAE2 : 0x46464E);
            Set(Pressed, light ? 0xCACAD4 : 0x51515B);
            Set(PlaySurface, light ? 0x262629 : 0xF5F5F7);
            Set(PlayInk, light ? 0xFFFFFF : 0x232326);
            Set(Danger, light ? 0xB42332 : 0xB93240);
            Set(DangerHover, light ? 0x941D2A : 0x9D2935);
            Set(DangerInk, 0xFFFFFF);
            Set(GlassTop, Color.FromArgb(248, (byte)(light ? 252 : 52), (byte)(light ? 252 : 52), (byte)(light ? 254 : 56)));
            Set(GlassBottom, Color.FromArgb(240, (byte)(light ? 234 : 29), (byte)(light ? 234 : 29), (byte)(light ? 240 : 32)));
            Set(RimTop, Color.FromArgb((byte)(light ? 245 : 100), 255, 255, 255));
            Set(RimBottom, Color.FromArgb((byte)(light ? 45 : 110), 0, 0, 0));
        }

        private static Brush Gradient(SolidColorBrush top, SolidColorBrush bottom)
        {
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.25, 1) };
            var first = new GradientStop { Offset = 0 };
            var last = new GradientStop { Offset = 1 };
            BindingOperations.SetBinding(first, GradientStop.ColorProperty, new Binding("Color") { Source = top });
            BindingOperations.SetBinding(last, GradientStop.ColorProperty, new Binding("Color") { Source = bottom });
            gradient.GradientStops.Add(first);
            gradient.GradientStops.Add(last);
            return gradient;
        }

        private static void Set(SolidColorBrush brush, int rgb)
        {
            Set(brush, Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        }

        private static void Set(SolidColorBrush brush, Color color)
        {
            ((PaletteColor)BindingOperations.GetBinding(brush, SolidColorBrush.ColorProperty).Source).Value = color;
        }

        private static SolidColorBrush Create()
        {
            // A bound brush stays live when WPF seals a control template or style.
            var brush = new SolidColorBrush();
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty,
                new Binding("Value") { Source = new PaletteColor() });
            return brush;
        }

        private sealed class PaletteColor : INotifyPropertyChanged
        {
            private Color value;
            public Color Value
            {
                get { return value; }
                set
                {
                    if (this.value == value) return;
                    this.value = value;
                    if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Value"));
                }
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
