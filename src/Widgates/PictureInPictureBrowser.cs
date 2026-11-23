using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Widgates
{
    internal static class PictureInPictureBrowser
    {
        internal static string FromExecutable(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "chrome.exe": case "chromium.exe": return "chrome";
                case "msedge.exe": return "edge";
                case "brave.exe": return "brave";
                case "opera.exe": return "opera";
                case "vivaldi.exe": return "vivaldi";
                default: return null;
            }
        }
        // One public Toolhelp snapshot, only in the user-started native host. Chromium
        // variants can share a Chrome UA; the actual launcher avoids routing Brave's
        // linked artwork to an unrelated Chrome session. No polling or process hooks.
        internal static string Caller(out uint browserProcess)
        {
            browserProcess = 0;
            using (var snapshot = CreateToolhelp32Snapshot(2, 0))
            {
                if (snapshot.IsInvalid) return null;
                var processes = new Dictionary<uint, Entry>();
                var entry = new Entry { Size = (uint)Marshal.SizeOf(typeof(Entry)) };
                if (!Process32First(snapshot, ref entry)) return null;
                do { processes[entry.Id] = entry; } while (Process32Next(snapshot, ref entry));
                uint id = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                for (int depth = 0; depth < 4; depth++)
                {
                    Entry process;
                    if (!processes.TryGetValue(id, out process) || !processes.TryGetValue(process.ParentId, out process)) return null;
                    string browser = FromExecutable(process.Name);
                    if (browser != null) { browserProcess = process.Id; return browser; }
                    string name = (process.Name ?? "").ToLowerInvariant();
                    if (name != "cmd.exe" && name != "dotnet.exe" && name != "windowswidgates.exe") return null;
                    id = process.Id;
                }
                return null;
            }
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Entry
        {
            public uint Size, Usage, Id;
            public UIntPtr Heap;
            public uint ModuleId, Threads, ParentId;
            public int Priority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint process);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref Entry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref Entry entry);
    }
}
