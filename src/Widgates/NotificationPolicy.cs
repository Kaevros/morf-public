using System.Runtime.InteropServices;

namespace Widgates
{
    internal static class NotificationPolicy
    {
        // QUNS_ACCEPTS_NOTIFICATIONS only. No polling and no undocumented Focus Assist registry keys.
        internal static bool Allows(int state) { return state == 5; }
        internal static bool AllowsPeek()
        {
            try { int state; return SHQueryUserNotificationState(out state) == 0 && Allows(state); }
            catch { return false; }
        }
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    }
}
