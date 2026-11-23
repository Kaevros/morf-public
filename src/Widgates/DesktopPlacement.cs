using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Widgates
{
    internal sealed class DesktopPlacement : IDisposable
    {
        private const uint ForegroundEvent = 0x0003;
        private const uint LocationEvent = 0x800B;
        private readonly Window window;
        private readonly HwndSource source;
        private readonly WinEventCallback desktopEvent;
        private readonly DispatcherTimer refresh;
        private readonly IntPtr foregroundHook;
        private IntPtr locationHook;
        private IntPtr watchedForeground;
        private IntPtr taskbar;
        private IntPtr taskbarHook;
        private readonly uint taskbarCreated;
        private bool fullscreen;
        private bool gameActive;
        private readonly VirtualDesktopLink desktops = new VirtualDesktopLink();
        private readonly IntPtr desktopVisibilityHook;
        private Task desktopRefresh;
        private bool desktopPending, disposed, desktopSyncNeeded;
        private bool onCurrentDesktop = true;
        private readonly uint ownProcess;
        private readonly HotkeySet hotkeys;
        private ApplicationRules rules;
        private DisplayMonitor[] monitorCache;
        private bool displayGeometryPending;

        internal event Action HotkeyPressed;
        internal event Action FullscreenChanged;
        internal event Action DockChanged;
        internal event Action DisplayGeometryChanged;
        internal event Action DesktopChanged;
        internal event Action<string> MediaHotkeyPressed;
        internal bool FollowDesktops { get; set; }
        internal bool TrackFullscreen { get; set; }
        internal bool OnCurrentDesktop { get { return onCurrentDesktop; } }
        internal DockPreference Preference { get; set; }
        internal DockEdge Edge { get; private set; }
        internal bool Fullscreen { get { return fullscreen; } }
        internal bool GameActive { get { return gameActive; } }
        internal bool HotkeyRegistered { get { return hotkeys.PanelRegistered; } }
        internal string HotkeyStatus { get { return hotkeys.Status; } }
        internal string HotkeyFailure { get { return hotkeys.Failure; } }
        internal string MonitorDevice { get; set; }
        internal string MonitorStatus { get; private set; }
        internal Size AvailableSize
        {
            get
            {
                MonitorInfo info;
                if (!TryGetTargetMonitor(out info)) return new Size(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
                double scale = GetDpiForWindow(source.Handle) / 96.0; if (scale <= 0) scale = 1;
                return new Size((info.Work.Right - info.Work.Left) / scale, (info.Work.Bottom - info.Work.Top) / scale);
            }
        }
        internal bool RuleHidden { get; private set; }
        internal bool RuleShown { get; private set; }
        internal bool OwnForeground { get; private set; }

        internal DesktopPlacement(Window window, HwndSource source, bool trackFullscreen)
        {
            this.window = window;
            this.source = source;
            gameActive = GameDetection.IsGame(GetForegroundWindow());
            TrackFullscreen = trackFullscreen;
            fullscreen = TrackFullscreen && IsFullscreen(GetForegroundWindow());
            FollowDesktops = true;
            taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            source.AddHook(WindowMessage);
            desktopEvent = OnDesktopEvent;
            refresh = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
            refresh.Tick += RefreshTick;
            foregroundHook = SetWinEventHook(ForegroundEvent, ForegroundEvent, IntPtr.Zero, desktopEvent, 0, 0, 0);
            GetWindowThreadProcessId(source.Handle, out ownProcess);
            desktopVisibilityHook = SetWinEventHook(0x8017, 0x8018, IntPtr.Zero, desktopEvent, ownProcess, 0, 0); // Cloak/uncloak.
            ApplicationRules.TryParse("", out rules);
            hotkeys = new HotkeySet(new WindowsHotkeyRegistry(source.Handle));
            hotkeys.Apply(new[] { "Ctrl+Alt+M", "", "", "" });
            QueueRefresh();
        }

        internal void Reposition()
        {
            Reposition(false);
        }

        private void Reposition(bool restoreSize)
        {
            MonitorInfo monitor;
            if (!TryGetTargetMonitor(out monitor))
                return;

            WatchTaskbar();
            var edge = DockLayout.Resolve(Preference, GetTaskbarEdge(monitor));
            if (edge != Edge)
            {
                Edge = edge;
                if (DockChanged != null) DockChanged();
            }

            double scale = GetDpiForWindow(source.Handle) / 96.0;
            if (scale == 0)
                scale = 1;
            int width = (int)Math.Ceiling(window.ActualWidth * scale);
            int height = (int)Math.Ceiling(window.ActualHeight * scale);
            if (restoreSize)
            {
                // Windows may constrain the HWND during a game's display-mode switch.
                // Its current size is then no longer the intended WPF content size.
                var content = window.Content as FrameworkElement;
                width = (int)Math.Ceiling(window.Width * scale);
                if (content != null) height = (int)Math.Ceiling(content.DesiredSize.Height * scale);
            }
            var position = DockLayout.Position(
                new System.Windows.Rect(monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Right - monitor.Bounds.Left, monitor.Bounds.Bottom - monitor.Bounds.Top),
                new System.Windows.Rect(monitor.Work.Left, monitor.Work.Top, monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top),
                new Size(width, height), Edge);
            Rect current;
            int x = (int)position.X, y = (int)position.Y;
            if (!GetWindowRect(source.Handle, out current) || current.Left != x || current.Top != y
                || (restoreSize && (current.Right - current.Left != width || current.Bottom - current.Top != height)))
                SetWindowPos(source.Handle, IntPtr.Zero, x, y, width, height, restoreSize ? 0x0014u : 0x0015u); // Retain z-order and activation; normally retain size too.
        }

        private static TaskbarEdge GetTaskbarEdge(MonitorInfo monitor)
        {
            var data = new AppBarData { Size = (uint)Marshal.SizeOf(typeof(AppBarData)) };
            if (SHAppBarMessage(5, ref data) != UIntPtr.Zero)
            {
                var rect = data.Bounds;
                if (rect.Right > monitor.Bounds.Left && rect.Left < monitor.Bounds.Right
                    && rect.Bottom > monitor.Bounds.Top - 4 && rect.Top < monitor.Bounds.Bottom + 4)
                {
                    if (rect.Right - rect.Left >= rect.Bottom - rect.Top)
                        return Math.Abs(rect.Top - monitor.Bounds.Top) < Math.Abs(rect.Bottom - monitor.Bounds.Bottom)
                            ? TaskbarEdge.Top : TaskbarEdge.Bottom;
                    return Math.Abs(rect.Left - monitor.Bounds.Left) < Math.Abs(rect.Right - monitor.Bounds.Right)
                        ? TaskbarEdge.Left : TaskbarEdge.Right;
                }
            }
            return monitor.Work.Top > monitor.Bounds.Top ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        }

        private void WatchTaskbar()
        {
            IntPtr current = FindWindow("Shell_TrayWnd", null);
            if (current == taskbar) return;
            if (taskbarHook != IntPtr.Zero) UnhookWinEvent(taskbarHook);
            taskbar = current;
            taskbarHook = IntPtr.Zero;
            if (taskbar == IntPtr.Zero) return;
            uint process;
            GetWindowThreadProcessId(taskbar, out process);
            if (process != 0)
                taskbarHook = SetWinEventHook(LocationEvent, LocationEvent, IntPtr.Zero, desktopEvent, process, 0, 0);
        }

        private void RefreshTick(object sender, EventArgs args)
        {
            refresh.Stop();
            IntPtr foreground = GetForegroundWindow();
            if (foreground != watchedForeground)
            {
                if (locationHook != IntPtr.Zero)
                    UnhookWinEvent(locationHook);
                watchedForeground = foreground;
                locationHook = IntPtr.Zero;
                if (foreground != IntPtr.Zero && foreground != source.Handle)
                {
                    uint process;
                    GetWindowThreadProcessId(foreground, out process);
                    if (process != 0)
                        locationHook = SetWinEventHook(LocationEvent, LocationEvent, IntPtr.Zero, desktopEvent, process, 0, 0);
                }
            }

            bool restoreSize = displayGeometryPending;
            displayGeometryPending = false;
            if (restoreSize && DisplayGeometryChanged != null) DisplayGeometryChanged();
            Reposition(restoreSize);
            // Keep the state of the last external window while the keyboard shortcut gives us focus.
            RefreshForeground(foreground);
            if (!gameActive && onCurrentDesktop && window.IsVisible) EnsureTopmost();
            if (desktopSyncNeeded)
            {
                desktopSyncNeeded = false;
                if (desktopRefresh == null || desktopRefresh.IsCompleted)
                    desktopRefresh = RefreshDesktopAsync(FollowDesktops);
                else desktopPending = true;
            }
        }

        internal void EnsureTopmost()
        {
            if (!disposed && (!gameActive || RuleShown) && !RuleHidden && onCurrentDesktop && window.IsVisible)
                SetWindowPos(source.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // Topmost, retain position/size/focus.
        }

        private async Task RefreshDesktopAsync(bool follow)
        {
            try
            {
                var result = await desktops.RefreshAsync(source.Handle, GetForegroundWindow(), follow);
                // Explorer's cloak notification can precede desktop registration.
                // Retry only this observed transition, with a strict bound and no idle timer.
                for (int attempt = 0; follow && result.Available && result.Transition && !result.OnCurrentDesktop && attempt < 2; attempt++)
                {
                    await Task.Delay(attempt == 0 ? 120 : 300);
                    if (disposed) return;
                    result = await desktops.RefreshAsync(source.Handle, GetForegroundWindow(), follow);
                }
                if (disposed) return;
                bool current = result.Available ? result.OnCurrentDesktop : !IsShellCloaked(source.Handle);
                if (current != onCurrentDesktop || result.Transition)
                {
                    onCurrentDesktop = current;
                    if (DesktopChanged != null) DesktopChanged();
                    if (current) Reposition();
                }
            }
            finally
            {
                if (desktopPending && !disposed) { desktopPending = false; desktopSyncNeeded = true; QueueRefresh(); }
            }
        }

        internal async Task<bool> EnsureCurrentDesktopAsync()
        {
            if (desktopRefresh != null) await desktopRefresh;
            if (disposed) return false;
            await RefreshDesktopAsync(true);
            return !disposed && onCurrentDesktop;
        }

        private static bool IsShellCloaked(IntPtr window)
        {
            uint cloaked;
            return DwmGetCloaked(window, 14, out cloaked, sizeof(uint)) == 0 && (cloaked & 2) != 0;
        }

        private bool IsFullscreen(IntPtr foreground)
        {
            MonitorInfo monitor;
            Rect bounds;
            if (foreground == IntPtr.Zero || !TryGetTargetMonitor(out monitor) || !GetWindowRect(foreground, out bounds))
                return false;

            var name = new StringBuilder(64);
            GetClassName(foreground, name, name.Capacity);
            switch (name.ToString())
            {
                case "Progman":
                case "WorkerW":
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                    return false;
            }

            const int caption = 0x00C00000;
            if (IsZoomed(foreground) && (GetWindowLong(foreground, -16) & caption) == caption)
                return false;
            Rect frame;
            if (DwmGetWindowAttribute(foreground, 9, out frame, Marshal.SizeOf(typeof(Rect))) == 0)
                bounds = frame;

            return bounds.Left <= monitor.Bounds.Left + 1 && bounds.Top <= monitor.Bounds.Top + 1
                && bounds.Right >= monitor.Bounds.Right - 1 && bounds.Bottom >= monitor.Bounds.Bottom - 1;
        }

        private void OnDesktopEvent(IntPtr hook, uint kind, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
        {
            if (disposed) return;
            if ((hwnd == source.Handle && (kind == 0x8017 || kind == 0x8018))
                || (kind == ForegroundEvent && (!onCurrentDesktop || IsShellCloaked(source.Handle))))
                desktopSyncNeeded = true;
            if (!disposed && kind == ForegroundEvent) RefreshForeground(hwnd);
            if (kind == ForegroundEvent || (hwnd == source.Handle && (kind == 0x8017 || kind == 0x8018))
                || ((hwnd == watchedForeground || hwnd == taskbar) && objectId == 0 && childId == 0))
                QueueRefresh();
        }

        private void QueueRefresh() { if (!disposed) refresh.Start(); }

        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (taskbarCreated != 0 && (uint)message == taskbarCreated) QueueRefresh();
            switch (message)
            {
                case 0x001A: // WM_SETTINGCHANGE (including taskbar work area)
                case 0x007E: // WM_DISPLAYCHANGE
                case 0x02E0: // WM_DPICHANGED
                    InvalidateDisplayGeometry();
                    break;
                case 0x0312: // WM_HOTKEY
                    if (wparam.ToInt32() >= 1 && wparam.ToInt32() <= 4)
                    {
                        handled = true;
                        if (wparam.ToInt32() == 1 && HotkeyPressed != null)
                            HotkeyPressed();
                        else if (wparam.ToInt32() != 1 && MediaHotkeyPressed != null)
                            MediaHotkeyPressed(new[] { "", "play", "previous", "next" }[wparam.ToInt32() - 1]);
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        private static bool TryGetPrimaryMonitor(out MonitorInfo info)
        {
            info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            return GetMonitorInfo(MonitorFromPoint(new Point(), 1), ref info);
        }

        internal bool ConfigureHotkeys(string[] values) { return hotkeys.Apply(values); }
        internal void ConfigureRules(string value)
        {
            ApplicationRules parsed;
            if (ApplicationRules.TryParse(value, out parsed)) rules = parsed;
            RefreshForeground(GetForegroundWindow());
        }
        private void RefreshForeground(IntPtr foreground)
        {
            uint process; GetWindowThreadProcessId(foreground, out process);
            OwnForeground = process != 0 && process == ownProcess;
            if (OwnForeground) return;
            string name = GameDetection.ProcessName(foreground);
            bool? rule = rules == null ? null : rules.Hidden(name);
            bool hidden = rule == true, shown = rule == false;
            bool nowGame = GameDetection.IsGame(foreground, name), nowFullscreen = TrackFullscreen && IsFullscreen(foreground);
            if (gameActive && !nowGame) InvalidateDisplayGeometry();
            bool changed = gameActive != nowGame || fullscreen != nowFullscreen || hidden != RuleHidden || shown != RuleShown;
            gameActive = nowGame; fullscreen = nowFullscreen; RuleHidden = hidden; RuleShown = shown;
            if (changed && FullscreenChanged != null) FullscreenChanged();
        }
        internal void RefreshDisplayGeometry() { InvalidateDisplayGeometry(); }
        private void InvalidateDisplayGeometry()
        {
            monitorCache = null;
            displayGeometryPending = true;
            QueueRefresh();
        }
        private bool TryGetTargetMonitor(out MonitorInfo info)
        {
            if (monitorCache == null) monitorCache = MonitorCatalog.All();
            var selected = MonitorCatalog.Resolve(MonitorDevice, monitorCache);
            if (selected == null) return TryGetPrimaryMonitor(out info);
            MonitorStatus = selected.ToString() + (!string.IsNullOrEmpty(MonitorDevice) && !selected.Device.Equals(MonitorDevice, StringComparison.OrdinalIgnoreCase) ? " · Seçili ekran yok, ana ekrana dönüldü" : "");
            info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)),
                Bounds = new Rect { Left = (int)selected.Bounds.Left, Top = (int)selected.Bounds.Top, Right = (int)selected.Bounds.Right, Bottom = (int)selected.Bounds.Bottom },
                Work = new Rect { Left = (int)selected.Work.Left, Top = (int)selected.Work.Top, Right = (int)selected.Work.Right, Bottom = (int)selected.Work.Bottom } };
            return true;
        }

        public void Dispose()
        {
            disposed = true;
            refresh.Stop();
            if (foregroundHook != IntPtr.Zero)
                UnhookWinEvent(foregroundHook);
            if (locationHook != IntPtr.Zero)
                UnhookWinEvent(locationHook);
            if (taskbarHook != IntPtr.Zero)
                UnhookWinEvent(taskbarHook);
            if (desktopVisibilityHook != IntPtr.Zero) UnhookWinEvent(desktopVisibilityHook);
            hotkeys.Dispose();
            source.RemoveHook(WindowMessage);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public Rect Bounds, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public uint Size;
            public IntPtr Window;
            public uint CallbackMessage;
            public uint Edge;
            public Rect Bounds;
            public IntPtr Parameter;
        }
        private delegate void WinEventCallback(IntPtr hook, uint kind, IntPtr hwnd, int objectId, int childId, uint thread, uint time);

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out Rect rect, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetCloaked(IntPtr hwnd, uint attribute, out uint value, int size);
    }
}
