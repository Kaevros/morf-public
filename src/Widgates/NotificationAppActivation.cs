using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Widgates
{
    internal static class NotificationAppActivation
    {
        // Resolve only a registered AppsFolder identity. Toast text never becomes a path or command.
        internal static bool Open(string appId) { return WithItem(appId, item => {
            var info = new ShellExecuteInfo { Size = Marshal.SizeOf(typeof(ShellExecuteInfo)), Mask = 0x0000050C,
                Verb = "open", IdList = item, Show = 1 };
            bool opened = ShellExecuteEx(ref info);
            int error = opened ? 0 : Marshal.GetLastWin32Error();
            // Respect an explicit Windows denial/cancellation; callers must not retry another API.
            if (error == 5 || error == 1223) throw new Win32Exception(error);
            return opened;
        }); }
        internal static bool CanResolve(string appId) { return WithItem(appId, _ => true); }
        private static bool WithItem(string appId, Func<IntPtr, bool> action)
        {
            if (!MediaAppLauncher.ValidNotificationAppId(appId)) return false;
            int initialized = CoInitializeEx(IntPtr.Zero, 0x6); // STA, disable legacy OLE1 DDE.
            if (initialized < 0) return false;
            IntPtr item = IntPtr.Zero;
            try
            {
                uint attributes;
                if (SHParseDisplayName("shell:AppsFolder\\" + appId, IntPtr.Zero, out item, 0, out attributes) < 0 || item == IntPtr.Zero) return false;
                return action(item);
            }
            finally { if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item); CoUninitialize(); }
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellExecuteInfo
        {
            internal int Size; internal uint Mask; internal IntPtr Window;
            [MarshalAs(UnmanagedType.LPWStr)] internal string Verb;
            [MarshalAs(UnmanagedType.LPWStr)] internal string File;
            [MarshalAs(UnmanagedType.LPWStr)] internal string Parameters;
            [MarshalAs(UnmanagedType.LPWStr)] internal string Directory;
            internal int Show; internal IntPtr Instance, IdList;
            [MarshalAs(UnmanagedType.LPWStr)] internal string Class;
            internal IntPtr ClassKey; internal uint HotKey; internal IntPtr Icon, Process;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item, uint requested, out uint attributes);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
    }
}
