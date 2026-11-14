using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Widgates
{
    internal sealed class TrayIcon : IDisposable
    {
        private const int Callback = 0x8073;
        private readonly HwndSource source;
        private readonly Action show, settings, hide, exit;
        private readonly Guid identity;
        private readonly uint taskbarCreated;
        private IntPtr icon;
        private bool enabled;
        private ContextMenu menu;
        internal bool Added { get; private set; }
        internal TrayIcon(HwndSource source, Action show, Action settings, Action hide, Action exit, Guid? identity = null)
        {
            this.source = source; this.show = show; this.settings = settings; this.hide = hide; this.exit = exit;
            this.identity = identity ?? new Guid("1CE052A8-F96C-4669-8CB0-DA777EC60321");
            taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            using (var process = Process.GetCurrentProcess())
            {
                IntPtr large; ExtractIconEx(process.MainModule.FileName, 0, out large, out icon, 1);
                if (large != IntPtr.Zero) DestroyIcon(large);
            }
            source.AddHook(Message);
        }
        internal void SetEnabled(bool value)
        {
            enabled = value;
            var data = Data();
            if (!value) { if (Added) Shell_NotifyIcon(2, ref data); Added = false; return; }
            if (Added || icon == IntPtr.Zero) return;
            Added = Shell_NotifyIcon(0, ref data);
            if (Added) { data.Version = 4; Shell_NotifyIcon(4, ref data); }
        }
        private IconData Data()
        {
            return new IconData { Size = (uint)Marshal.SizeOf(typeof(IconData)), Window = source.Handle, Id = 1, Flags = 1 | 2 | 4 | 0x20 | 0x80,
                Message = Callback, Icon = icon, Tip = "Morf · Aç, ayarlar veya çıkış", Identity = identity, Info = "", InfoTitle = "" };
        }
        private IntPtr Message(IntPtr window, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if ((uint)message == taskbarCreated) { Added = false; if (enabled) SetEnabled(true); }
            if (message != Callback || !enabled) return IntPtr.Zero;
            int kind = (int)(lparam.ToInt64() & 0xffff);
            if (kind == 0x400 || kind == 0x401) show();
            if (kind == 0x7b) OpenMenu();
            handled = true; return IntPtr.Zero;
        }
        private void OpenMenu()
        {
            if (menu != null) menu.IsOpen = false;
            menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            PopupTheme.ApplyMenu(menu);
            Add("Morf'u aç", show); Add("Ayarlar", settings); Add("15 dakika gizle", hide); menu.Items.Add(new Separator()); Add("Morf'tan Çık", exit);
            SetForegroundWindow(source.Handle); menu.IsOpen = true;
            menu.Closed += (_, __) => { var data = Data(); Shell_NotifyIcon(3, ref data); PostMessage(source.Handle, 0, IntPtr.Zero, IntPtr.Zero); };
        }
        private void Add(string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, __) => action(); menu.Items.Add(item); }
        public void Dispose()
        {
            SetEnabled(false); if (menu != null) menu.IsOpen = false; source.RemoveHook(Message);
            if (icon != IntPtr.Zero) { DestroyIcon(icon); icon = IntPtr.Zero; }
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct IconData
        {
            public uint Size; public IntPtr Window; public uint Id, Flags, Message; public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
            public uint InfoFlags; public Guid Identity; public IntPtr BalloonIcon;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")] private static extern bool Shell_NotifyIcon(uint operation, ref IconData data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")] private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")] private static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    }
}
