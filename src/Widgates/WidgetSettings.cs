using System;
using System.Globalization;
using System.IO;
using System.Security;

namespace Widgates
{
    internal enum DockPreference { Automatic, Top, Bottom }
    internal enum LightweightMode { Automatic, Always, Never }
    internal enum DisplayMode { Capsule, Line, AutoHide, DynamicIsland }

    internal sealed class WidgetSettings
    {
        internal const int MaximumLength = 16384;
        internal bool HideWhenIdle { get; set; }
        internal bool AutoHide { get; set; }
        internal bool OpenOnHover { get; set; }
        internal bool HideInFullscreen { get; set; }
        internal double Opacity { get; set; }
        internal double LiquidTransparency { get; set; }
        internal DockPreference Dock { get; set; }
        internal SurfaceStyle Style { get; set; }
        internal double GlassStrength { get; set; }
        internal bool FollowDesktops { get; set; }
        internal bool AutoPauseOthers { get; set; }
        internal bool ScrollVolume { get; set; }
        internal bool PlayerVolume { get; set; }
        internal bool PictureInPicture { get; set; }
        internal bool PictureInPictureAdvanced { get; set; }
        internal int PictureInPictureSeekSeconds { get; set; }
        internal bool HideInGames { get; set; }
        internal bool StartWithWindows { get; set; }
        internal bool StartupConfigured { get; set; }
        internal bool EdgeOnly { get; set; }
        internal bool FloatingIsland { get; set; }
        internal bool ScrollTitles { get; set; }
        internal bool ShowTimeline { get; set; }
        internal bool PeekTrackChanges { get; set; }
        internal bool ResumeInterrupted { get; set; }
        internal bool ShowTrayIcon { get; set; }
        internal bool ScrollVolumeOnWholeIsland { get; set; }
        internal bool RespectQuietState { get; set; }
        internal int PeekSeconds { get; set; }
        internal bool WindowsNotifications { get; set; }
        // Read legacy settings and preserve callers while the preview migrates to all Windows toasts.
        internal bool WhatsAppNotifications { get { return WindowsNotifications; } set { WindowsNotifications = value; } }
        internal bool NotificationContent { get; set; }
        internal int NotificationSeconds { get; set; }
        internal string NotificationRules { get; set; }
        internal string MediaRules { get; set; }
        internal DisplayMode DisplayMode
        {
            get { return EdgeOnly ? DisplayMode.Line : AutoHide ? DisplayMode.AutoHide : FloatingIsland ? DisplayMode.DynamicIsland : DisplayMode.Capsule; }
            set { EdgeOnly = value == DisplayMode.Line; AutoHide = value == DisplayMode.AutoHide; FloatingIsland = value == DisplayMode.DynamicIsland; }
        }
        internal int HoverDelayMs { get; set; }
        internal int CompactWidth { get; set; }
        internal string MonitorDevice { get; set; }
        internal string AppRules { get; set; }
        internal string PanelHotkey { get; set; }
        internal string PlayHotkey { get; set; }
        internal string PreviousHotkey { get; set; }
        internal string NextHotkey { get; set; }
        internal LightweightMode Lightweight { get; set; }
        internal string[] Hotkeys { get { return new[] { PanelHotkey, PlayHotkey, PreviousHotkey, NextHotkey }; } }
        internal static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Morf", "widget.settings"); }
        }

        internal WidgetSettings()
        {
            HideWhenIdle = OpenOnHover = true;
            Opacity = 1;
            LiquidTransparency = 0.55;
            Style = SurfaceStyle.SoftGlass;
            GlassStrength = 0.6;
            FollowDesktops = true;
            AutoPauseOthers = ScrollVolume = HideInGames = true;
            StartWithWindows = false;
            ScrollTitles = ShowTimeline = true;
            PeekTrackChanges = ResumeInterrupted = true;
            ShowTrayIcon = ScrollVolumeOnWholeIsland = RespectQuietState = true; PeekSeconds = 4;
            HoverDelayMs = 250; CompactWidth = 180;
            NotificationContent = true; NotificationSeconds = 5;
            NotificationRules = MediaRules = "";
            MonitorDevice = AppRules = PlayHotkey = PreviousHotkey = NextHotkey = "";
            PanelHotkey = "Ctrl+Alt+M";
            PictureInPictureSeekSeconds = 10;
        }

        internal static WidgetSettings Load(string path)
        {
            var settings = new WidgetSettings();
            bool hasGamePreference = false, hasWindowsNotifications = false;
            try
            {
                string text = AtomicTextFile.Read(path, MaximumLength, value => value.Contains("=") && !value.Contains("\0")
                    && (!value.StartsWith("MorfSettings=2\n") || value.EndsWith("End=Morf\n")));
                if (text == null) return settings;
                using (var reader = new StringReader(text))
                {
                    var buffer = new char[MaximumLength + 1];
                    int count = reader.ReadBlock(buffer, 0, buffer.Length);
                    if (count > MaximumLength) return settings;
                    foreach (string line in new string(buffer, 0, count).Split('\n'))
                    {
                        int separator = line.IndexOf('=');
                        if (separator < 0) continue;
                        string key = line.Substring(0, separator).Trim();
                        string value = line.Substring(separator + 1).Trim();
                        bool enabled;
                        double opacity;
                        DockPreference dock;
                        SurfaceStyle style;
                        int integer; HotkeyBinding binding; ApplicationRules rules; LightweightMode mode;
                        if (key == "HideWhenIdle" && bool.TryParse(value, out enabled)) settings.HideWhenIdle = enabled;
                        else if (key == "AutoHide" && bool.TryParse(value, out enabled)) settings.AutoHide = enabled;
                        else if (key == "OpenOnHover" && bool.TryParse(value, out enabled)) settings.OpenOnHover = enabled;
                        else if (key == "HideInFullscreen" && bool.TryParse(value, out enabled)) settings.HideInFullscreen = enabled;
                        else if (key == "FollowDesktops" && bool.TryParse(value, out enabled)) settings.FollowDesktops = enabled;
                        else if (key == "AutoPauseOthers" && bool.TryParse(value, out enabled)) settings.AutoPauseOthers = enabled;
                        else if (key == "ScrollVolume" && bool.TryParse(value, out enabled)) settings.ScrollVolume = enabled;
                        else if (key == "PlayerVolume" && bool.TryParse(value, out enabled)) settings.PlayerVolume = enabled;
                        else if (key == "PictureInPicture" && bool.TryParse(value, out enabled)) settings.PictureInPicture = enabled;
                        else if (key == "PictureInPictureAdvanced" && bool.TryParse(value, out enabled)) settings.PictureInPictureAdvanced = enabled;
                        else if (key == "PictureInPictureSeekSeconds" && int.TryParse(value, out integer)) settings.PictureInPictureSeekSeconds = PictureInPictureProtocol.SeekSeconds(integer);
                        else if (key == "HideInGames" && bool.TryParse(value, out enabled)) { settings.HideInGames = enabled; hasGamePreference = true; }
                        else if (key == "StartWithWindows" && bool.TryParse(value, out enabled)) settings.StartWithWindows = enabled;
                        else if (key == "StartupConfigured" && bool.TryParse(value, out enabled)) settings.StartupConfigured = enabled;
                        else if (key == "EdgeOnly" && bool.TryParse(value, out enabled)) settings.EdgeOnly = enabled;
                        else if (key == "FloatingIsland" && bool.TryParse(value, out enabled)) settings.FloatingIsland = enabled;
                        else if (key == "ScrollTitles" && bool.TryParse(value, out enabled)) settings.ScrollTitles = enabled;
                        else if (key == "ShowTimeline" && bool.TryParse(value, out enabled)) settings.ShowTimeline = enabled;
                        else if (key == "PeekTrackChanges" && bool.TryParse(value, out enabled)) settings.PeekTrackChanges = enabled;
                        else if (key == "ResumeInterrupted" && bool.TryParse(value, out enabled)) settings.ResumeInterrupted = enabled;
                        else if (key == "ShowTrayIcon" && bool.TryParse(value, out enabled)) settings.ShowTrayIcon = enabled;
                        else if (key == "ScrollVolumeOnWholeIsland" && bool.TryParse(value, out enabled)) settings.ScrollVolumeOnWholeIsland = enabled;
                        else if (key == "RespectQuietState" && bool.TryParse(value, out enabled)) settings.RespectQuietState = enabled;
                        else if (key == "PeekSeconds" && int.TryParse(value, out integer)) settings.PeekSeconds = integer == 2 || integer == 6 ? integer : 4;
                        else if (key == "WindowsNotifications" && bool.TryParse(value, out enabled)) { settings.WindowsNotifications = enabled; hasWindowsNotifications = true; }
                        else if (key == "WhatsAppNotifications" && !hasWindowsNotifications && bool.TryParse(value, out enabled)) settings.WindowsNotifications = enabled;
                        else if (key == "NotificationContent" && bool.TryParse(value, out enabled)) settings.NotificationContent = enabled;
                        else if (key == "NotificationSeconds" && int.TryParse(value, out integer)) settings.NotificationSeconds = integer == 3 || integer == 8 ? integer : 5;
                        else if (key == "NotificationRules") settings.NotificationRules = SourceRules.Parse(value).Text;
                        else if (key == "MediaRules") settings.MediaRules = SourceRules.Parse(value).Text;
                        else if (key == "HoverDelayMs" && int.TryParse(value, out integer)) settings.HoverDelayMs = Math.Max(0, Math.Min(1000, integer));
                        else if (key == "CompactWidth" && int.TryParse(value, out integer)) settings.CompactWidth = Math.Max(150, Math.Min(260, integer));
                        else if (key == "MonitorDevice" && value.Length <= 64 && !value.AnyControl()) settings.MonitorDevice = value;
                        else if (key == "AppRules" && ApplicationRules.TryParse(value, out rules)) settings.AppRules = rules.Text;
                        else if (key == "Lightweight" && Enum.TryParse(value, out mode) && Enum.IsDefined(typeof(LightweightMode), mode)) settings.Lightweight = mode;
                        else if (key == "PanelHotkey" && HotkeyBinding.TryParse(value, out binding) && binding.Key != 0) settings.PanelHotkey = binding.Text;
                        else if (key == "PlayHotkey" && HotkeyBinding.TryParse(value, out binding)) settings.PlayHotkey = binding.Text;
                        else if (key == "PreviousHotkey" && HotkeyBinding.TryParse(value, out binding)) settings.PreviousHotkey = binding.Text;
                        else if (key == "NextHotkey" && HotkeyBinding.TryParse(value, out binding)) settings.NextHotkey = binding.Text;
                        else if (key == "Opacity" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity)
                            && !double.IsNaN(opacity) && !double.IsInfinity(opacity)) settings.Opacity = ClampOpacity(opacity);
                        else if (key == "LiquidTransparency" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity)
                            && !double.IsNaN(opacity) && !double.IsInfinity(opacity)) settings.LiquidTransparency = ClampLiquidTransparency(opacity);
                        else if (key == "Dock" && Enum.TryParse(value, out dock) && Enum.IsDefined(typeof(DockPreference), dock)) settings.Dock = dock;
                        else if (key == "Style" && Enum.TryParse(value, out style) && Enum.IsDefined(typeof(SurfaceStyle), style)) settings.Style = style;
                        else if (key == "GlassStrength" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity)
                            && !double.IsNaN(opacity) && !double.IsInfinity(opacity)) settings.GlassStrength = ClampStrength(opacity);
                    }
                    if (!hasGamePreference) settings.HideInFullscreen = false;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            return settings;
        }

        internal bool Save(string path)
        {
            try
            {
                return AtomicTextFile.Write(path, string.Join("\n", new[] {
                    "MorfSettings=2",
                    "HideWhenIdle=" + HideWhenIdle, "AutoHide=" + AutoHide,
                    "OpenOnHover=" + OpenOnHover, "HideInFullscreen=" + HideInFullscreen,
                    "Opacity=" + ClampOpacity(Opacity).ToString("0.00", CultureInfo.InvariantCulture), "Dock=" + Dock,
                    "LiquidTransparency=" + ClampLiquidTransparency(LiquidTransparency).ToString("0.00", CultureInfo.InvariantCulture),
                    "Style=" + Style, "GlassStrength=" + ClampStrength(GlassStrength).ToString("0.00", CultureInfo.InvariantCulture),
                    "FollowDesktops=" + FollowDesktops, "AutoPauseOthers=" + AutoPauseOthers,
                    "ScrollVolume=" + ScrollVolume, "PlayerVolume=" + PlayerVolume, "HideInGames=" + HideInGames,
                    "PictureInPicture=" + PictureInPicture, "PictureInPictureAdvanced=" + PictureInPictureAdvanced,
                    "PictureInPictureSeekSeconds=" + PictureInPictureProtocol.SeekSeconds(PictureInPictureSeekSeconds),
                    "StartWithWindows=" + StartWithWindows, "StartupConfigured=" + StartupConfigured,
                    "EdgeOnly=" + EdgeOnly, "FloatingIsland=" + FloatingIsland, "ScrollTitles=" + ScrollTitles, "ShowTimeline=" + ShowTimeline,
                    "PeekTrackChanges=" + PeekTrackChanges, "ResumeInterrupted=" + ResumeInterrupted,
                    "ShowTrayIcon=" + ShowTrayIcon, "ScrollVolumeOnWholeIsland=" + ScrollVolumeOnWholeIsland,
                    "RespectQuietState=" + RespectQuietState, "PeekSeconds=" + (PeekSeconds == 2 || PeekSeconds == 6 ? PeekSeconds : 4),
                    "WindowsNotifications=" + WindowsNotifications, "NotificationContent=" + NotificationContent,
                    "NotificationSeconds=" + (NotificationSeconds == 3 || NotificationSeconds == 8 ? NotificationSeconds : 5),
                    "NotificationRules=" + SourceRules.Parse(NotificationRules).Text, "MediaRules=" + SourceRules.Parse(MediaRules).Text,
                    "HoverDelayMs=" + Math.Max(0, Math.Min(1000, HoverDelayMs)), "CompactWidth=" + Math.Max(150, Math.Min(260, CompactWidth)),
                    "MonitorDevice=" + MonitorDevice, "AppRules=" + (AppRules ?? "").Replace("\n", ";"), "Lightweight=" + Lightweight,
                    "PanelHotkey=" + PanelHotkey, "PlayHotkey=" + PlayHotkey, "PreviousHotkey=" + PreviousHotkey, "NextHotkey=" + NextHotkey, "End=Morf" }) + "\n");
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
        }

        internal static double ClampOpacity(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 1 : Math.Max(0.45, Math.Min(1, value));
        }

        internal static double ClampLiquidTransparency(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0.55 : Math.Max(0, Math.Min(0.9, value));
        }

        internal WidgetSettings Clone() { var copy = new WidgetSettings(); copy.CopyFrom(this); return copy; }
        internal void CopyFrom(WidgetSettings source)
        {
            foreach (var property in typeof(WidgetSettings).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                if (property.CanWrite && property.Name != "DisplayMode") property.SetValue(this, property.GetValue(source, null), null);
        }

        internal static double ClampStrength(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0.6 : Math.Max(0, Math.Min(1, value));
        }
    }
}
