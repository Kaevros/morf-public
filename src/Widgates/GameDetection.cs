using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Widgates
{
    internal static class GameDetection
    {
        internal static bool KnownGame(string process)
        {
            string name = (process ?? "").ToLowerInvariant();
            if (name.EndsWith(".exe")) name = name.Substring(0, name.Length - 4);
            return name == "cs2" || name == "csgo" || name == "dota2" || name == "hl2"
                || name == "valorant-win64-shipping" || name == "fortniteclient-win64-shipping"
                || name == "overwatch" || name == "league of legends"
                || name.EndsWith("-win64-shipping") || name.EndsWith("-wingdk-shipping");
        }

        internal static bool IsGame(IntPtr foreground)
        {
            return IsGame(foreground, null);
        }

        internal static bool IsGame(IntPtr foreground, string knownName)
        {
            int state;
            if (SHQueryUserNotificationState(out state) >= 0 && state == 3) return true;
            return KnownGame(knownName ?? ProcessName(foreground));
        }

        internal static string ProcessName(IntPtr window)
        {
            uint id; GetWindowThreadProcessId(window, out id);
            if (id == 0) return "";
            try { using (var process = Process.GetProcessById((int)id)) return process.ProcessName; }
            catch (ArgumentException) { return ""; } catch (InvalidOperationException) { return ""; } catch (System.ComponentModel.Win32Exception) { return ""; }
        }

        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    }
}
