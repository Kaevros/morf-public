using System;

namespace Widgates
{
    internal sealed class SettingsActions
    {
        internal Action<bool> Apply;
        internal Func<ThemePreference, bool> SelectTheme;
        internal Func<bool, bool> ConfigureStartup;
        internal Action Exit;
        internal Func<System.Threading.Tasks.Task<string>> RefreshApplication;
        internal Func<int> ShelfCount;
        internal Action HideTemporarily;
        internal Func<int> SleepMinutes;
        internal Func<bool> SleepActive;
        internal Func<string> MediaStatus;
        internal Func<string> PictureInPictureStatus;
        internal Func<bool> PictureInPictureConnected;
        internal Action SetupPictureInPicture;
        internal Func<System.Threading.Tasks.Task> ResumePrevious;
        internal Func<string[], bool> ConfigureHotkeys;
        internal Func<bool> Reset;
        internal Func<string> Diagnostics;
        internal Action<string> CopyDiagnostics;
        internal Action<int> StartSleep;
        internal Action CancelSleep;
        internal Func<string> SleepStatus;
        internal Func<string> NotificationStatus;
        internal Func<bool> NotificationAvailable;
        internal Func<System.Threading.Tasks.Task> ConnectNotifications;
        internal Func<System.Threading.Tasks.Task> RefreshNotifications;
        internal Action PreviewNotification;
        internal Action OpenNotificationSettings;
        internal Action OpenNotificationAccess;
        internal Func<string, string> SourceStatus;
        internal Func<bool> MotionAllowed;
        internal Func<SourceChoice[]> NotificationSources;
        internal Func<SourceChoice[]> MediaSources;
    }
}
