using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Widgates
{
    internal sealed class DisplayMonitor
    {
        internal string Device;
        internal IntPtr Handle;
        internal Rect Bounds, Work;
        internal bool Primary;
        internal double Scale;
        public override string ToString() { return Device.Replace("\\\\.\\", "") + (Primary ? " · Ana ekran" : "") + " · " + (int)Bounds.Width + "×" + (int)Bounds.Height; }
    }
    internal static class MonitorCatalog
    {
        internal static DisplayMonitor[] All()
        {
            var result = new List<DisplayMonitor>();
            MonitorCallback callback = (handle, dc, rectangle, data) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (GetMonitorInfo(handle, ref info))
                {
                    uint x, y; double scale = GetDpiForMonitor(handle, 0, out x, out y) >= 0 && x > 0 ? x / 96.0 : 1;
                    result.Add(new DisplayMonitor { Device = info.Device, Handle = handle, Bounds = Rect(info.Bounds), Work = Rect(info.Work), Primary = (info.Flags & 1) != 0, Scale = scale });
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return result.OrderByDescending(item => item.Primary).ThenBy(item => item.Device).ToArray();
        }
        internal static DisplayMonitor Resolve(string device, DisplayMonitor[] monitors)
        {
            return monitors.FirstOrDefault(item => !string.IsNullOrEmpty(device) && item.Device.Equals(device, StringComparison.OrdinalIgnoreCase))
                ?? monitors.FirstOrDefault(item => item.Primary) ?? monitors.FirstOrDefault();
        }
        internal static void PlaceSettings(Window window, string device)
        {
            var monitor = Resolve(device, All()); if (monitor == null) return;
            double scale = monitor.Scale > 0 ? monitor.Scale : 1;
            window.Width = Math.Min(760, Math.Max(240, monitor.Work.Width / scale - 24));
            window.Height = Math.Min(640, Math.Max(240, monitor.Work.Height / scale - 24));
            var handle = new WindowInteropHelper(window).Handle; NativeRect rect;
            if (handle != IntPtr.Zero && GetWindowRect(handle, out rect))
                SetWindowPos(handle, IntPtr.Zero, (int)(monitor.Work.Left + Math.Max(0, (monitor.Work.Width - (rect.Right - rect.Left)) / 2)),
                    (int)(monitor.Work.Top + Math.Max(0, (monitor.Work.Height - (rect.Bottom - rect.Top)) / 2)), 0, 0, 0x0015);
        }
        internal static void PlaceAudioMixer(Window window, Window owner, string device, bool below)
        {
            var monitor = Resolve(device, All()); if (monitor == null) return;
            double scale = monitor.Scale > 0 ? monitor.Scale : 1;
            window.Width = Math.Min(600, Math.Max(240, monitor.Work.Width / scale - 24));
            window.MaxHeight = Math.Max(180, monitor.Work.Height / scale - 24); window.UpdateLayout();
            var handle = new WindowInteropHelper(window).Handle; NativeRect popup, parent;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out popup) || !GetWindowRect(new WindowInteropHelper(owner).Handle, out parent)) return;
            int width = popup.Right - popup.Left, height = popup.Bottom - popup.Top;
            int x = (int)Math.Max(monitor.Work.Left, Math.Min(parent.Left + (parent.Right - parent.Left - width) / 2, monitor.Work.Right - width));
            int y = (int)Math.Max(monitor.Work.Top, Math.Min(below ? parent.Bottom + 8 * scale : parent.Top - height - 8 * scale, monitor.Work.Bottom - height));
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
        }
        private static Rect Rect(NativeRect value) { return new Rect(value.Left, value.Top, value.Right - value.Left, value.Bottom - value.Top); }
        private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
        { public int Size; public NativeRect Bounds, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
