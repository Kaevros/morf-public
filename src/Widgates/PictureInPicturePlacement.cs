using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Threading;

namespace Widgates
{
    internal sealed class PictureInPicturePlacement
    {
        private const uint MoveSizeEnd = 0x000b;
        private readonly WinEvent callback;
        private IntPtr hook;
        private string session;
        private uint process;
        private int generation;

        internal PictureInPicturePlacement() { callback = OnDrop; }

        internal static async Task<bool> InvokeInlineAsync(PipMessage message, Func<bool> stillSelected)
        {
            // Only an explicit capsule click reaches this method. Invoke the matching
            // player's accessible button without forwarding keyboard or mouse input.
            string marker = Marker(message.Session) + "-inline";
            for (int attempt = 0; attempt < 3 && stillSelected(); attempt++)
            {
                if (await Task.Run(() => InvokeInline(message.BrowserProcess, marker, stillSelected)).ConfigureAwait(false)) return true;
                // Chromium can populate its accessibility tree after the first query.
                if (attempt < 2) await Task.Delay(150).ConfigureAwait(false);
            }
            return false;
        }

        private static bool InvokeInline(uint browserProcess, string marker, Func<bool> stillSelected)
        {
            bool invoked = false;
            EnumWindows((window, _) =>
            {
                if (!stillSelected()) return false;
                uint owner;
                if (!IsWindowVisible(window) || GetWindowThreadProcessId(window, out owner) == 0 || owner != browserProcess)
                    return true;
                try
                {
                    var button = FindControl(window, marker);
                    object pattern;
                    if (button == null || !button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) return true;
                    if (!stillSelected()) return false;
                    ((InvokePattern)pattern).Invoke();
                    invoked = true;
                    return false;
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
                return true;
            }, IntPtr.Zero);
            return invoked;
        }

        private static string Marker(string token)
        {
            using (var sha = SHA256.Create())
                return "morf-pip-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).Replace("-", "").ToLowerInvariant();
        }

        private static AutomationElement FindControl(IntPtr window, string marker)
        {
            var root = AutomationElement.FromHandle(window);
            return root == null ? null : root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, marker));
        }

        internal void Track(PipMessage message)
        {
            bool active = message != null && message.Open && message.BrowserProcess != 0;
            string token = active ? message.Session : null;
            uint pid = active ? message.BrowserProcess : 0;
            if (token == session && process == pid) return;
            generation++;
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            session = token;
            process = pid;
            hook = active ? SetWinEventHook(MoveSizeEnd, MoveSizeEnd, IntPtr.Zero, callback, pid, 0, 0) : IntPtr.Zero;
        }

        private void OnDrop(IntPtr source, uint type, IntPtr window, int obj, int child, uint thread, uint time)
        {
            if (source != hook || session == null || obj != 0 || child != 0 || !IsCandidate(window, process)) return;
            Bounds dropped;
            if (!GetWindowRect(window, out dropped)) return;
            var dispatcher = Dispatcher.CurrentDispatcher;
            int version = ++generation;
            string token = session;
            // Chromium exposes the PiP play button's HTML id through UI Automation.
            // Verify this particular session off the UI thread before touching any window.
            Task.Run(() =>
            {
                try
                {
                    if (FindControl(window, Marker(token)) == null) return;
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        Bounds now;
                        if (version == generation && IsCandidate(window, process) && GetWindowRect(window, out now) && now.Equals(dropped))
                            Snap(window, now);
                    }));
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
            });
        }

        private static bool IsCandidate(IntPtr window, uint expected)
        {
            uint owner;
            return window != IntPtr.Zero && GetWindowThreadProcessId(window, out owner) != 0 && owner == expected
                && IsWindowVisible(window) && (GetWindowLong(window, -20) & 8) != 0;
        }

        private static void Snap(IntPtr window, Bounds rect)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) return;
            var work = info.Work;
            int width = Math.Min(rect.Right - rect.Left, work.Right - work.Left);
            int height = Math.Min(rect.Bottom - rect.Top, work.Bottom - work.Top);
            if (width <= 0 || height <= 0) return;
            int right = work.Right - width, bottom = work.Bottom - height;
            int x = Math.Min(right, Math.Max(work.Left, rect.Left));
            int y = Math.Min(bottom, Math.Max(work.Top, rect.Top));
            int nearestX = x - work.Left <= right - x ? work.Left : right;
            int nearestY = y - work.Top <= bottom - y ? work.Top : bottom;
            if (Math.Abs(nearestX - x) <= Math.Abs(nearestY - y)) x = nearestX;
            else y = nearestY;
            if (x != rect.Left || y != rect.Top || width != rect.Right - rect.Left || height != rect.Bottom - rect.Top)
                SetWindowPos(window, IntPtr.Zero, x, y, width, height, 0x0014); // Preserve focus and Z order.
        }

        [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Bounds Monitor, Work; public uint Flags; }
        private delegate void WinEvent(IntPtr hook, uint type, IntPtr window, int obj, int child, uint thread, uint time);
        private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Bounds rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint fallback);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
