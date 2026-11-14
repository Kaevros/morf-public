using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Widgates
{
    internal static class PlayerVolumeIdentity
    {
        internal static bool Matches(string source, string appId, string executable)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            if (!string.IsNullOrEmpty(appId) && source.Equals(appId, StringComparison.OrdinalIgnoreCase)) return true;
            if (source.IndexOfAny(new[] { '\\', '/', ':' }) >= 0) return false;
            string expected = source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? source : source + ".exe";
            if (expected.Equals(executable, StringComparison.OrdinalIgnoreCase)) return true;
            // Exact aliases only. A display name or a substring must never select an unrelated app.
            if (source.Equals("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", StringComparison.OrdinalIgnoreCase)) expected = "Spotify.exe";
            else if (source.Equals("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", StringComparison.OrdinalIgnoreCase)) expected = "AppleMusic.exe";
            else if (source.Equals("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", StringComparison.OrdinalIgnoreCase)) expected = "Microsoft.Media.Player.exe";
            return expected.Equals(executable, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Open only on an explicit volume interaction. No session notifications or idle polling.
    internal sealed class WindowsPlayerVolumeEndpoint : IVolumeEndpoint
    {
        private readonly List<object> sessions = new List<object>();
        internal int SessionCount { get { return sessions.Count; } }
        internal WindowsPlayerVolumeEndpoint(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            object enumeratorObject = null; IDevices devices = null;
            try
            {
                enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true));
                Marshal.ThrowExceptionForHR(((IDeviceEnumerator)enumeratorObject).EnumAudioEndpoints(0, 1, out devices));
                uint count; Marshal.ThrowExceptionForHR(devices.GetCount(out count));
                for (uint index = 0; index < count; index++)
                {
                    IDevice device = null; object managerObject = null; ISessions entries = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(devices.Item(index, out device));
                        Guid id = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
                        Marshal.ThrowExceptionForHR(device.Activate(ref id, 23, IntPtr.Zero, out managerObject));
                        Marshal.ThrowExceptionForHR(((ISessionManager)managerObject).GetSessionEnumerator(out entries));
                        int length; Marshal.ThrowExceptionForHR(entries.GetCount(out length));
                        for (int item = 0; item < length; item++)
                        {
                            object control = null;
                            try
                            {
                                Marshal.ThrowExceptionForHR(entries.GetSession(item, out control));
                                var session = (ISessionControl)control;
                                int state; uint process;
                                int stateResult = session.GetState(out state), systemResult = session.IsSystemSoundsSession(), processResult = session.GetProcessId(out process);
                                if (stateResult < 0 || state == 2 || systemResult == 0 || processResult != 0 || process == 0) continue;
                                string appId, executable;
                                bool identityRead = ReadIdentity(process, out appId, out executable);
                                if (!identityRead || !PlayerVolumeIdentity.Matches(source, appId, executable)) continue;
                                var volume = (ISimpleVolume)control;
                                float level; if (volume.GetMasterVolume(out level) < 0) continue;
                                sessions.Add(control); control = null;
                            }
                            catch (COMException) { }
                            catch (InvalidCastException) { }
                            finally { Release(control); }
                        }
                    }
                    catch (COMException) { /* A disconnected device must not block the remaining devices. */ }
                    finally { Release(entries); Release(managerObject); Release(device); }
                }
            }
            catch { Dispose(); throw; }
            finally { Release(devices); Release(enumeratorObject); }
        }
        public VolumeState Read()
        {
            if (sessions.Count == 0) return new VolumeState();
            float sum = 0; bool muted = true;
            foreach (object entry in sessions)
            {
                float level; bool silent; var volume = (ISimpleVolume)entry;
                Marshal.ThrowExceptionForHR(volume.GetMasterVolume(out level)); Marshal.ThrowExceptionForHR(volume.GetMute(out silent));
                sum += level; muted &= silent;
            }
            return new VolumeState { Available = true, Level = sum / sessions.Count, Muted = muted };
        }
        public void SetLevel(float value)
        {
            foreach (object entry in sessions) { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(((ISimpleVolume)entry).SetMasterVolume(value, ref context)); }
        }
        public void SetMute(bool muted)
        {
            foreach (object entry in sessions) { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(((ISimpleVolume)entry).SetMute(muted, ref context)); }
        }
        public void Dispose() { foreach (object entry in sessions) Release(entry); sessions.Clear(); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        internal static bool ReadIdentity(uint process, out string appId, out string executable)
        {
            appId = executable = "";
            IntPtr handle = OpenProcess(0x1000, false, process);
            if (handle == IntPtr.Zero) return false;
            try
            {
                var path = new StringBuilder(1024); uint pathLength = (uint)path.Capacity;
                if (QueryFullProcessImageName(handle, 0, path, ref pathLength)) executable = Path.GetFileName(path.ToString());
                uint size = 0;
                if (GetApplicationUserModelId(handle, ref size, null) == 122 && size > 0 && size <= 512)
                {
                    var identity = new StringBuilder((int)size);
                    if (GetApplicationUserModelId(handle, ref size, identity) == 0) appId = identity.ToString();
                }
                return appId.Length > 0 || executable.Length > 0;
            }
            finally { CloseHandle(handle); }
        }
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr process, ref uint size, StringBuilder identity);

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDeviceEnumerator { [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IDevices devices); }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDevices { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IDevice device); }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDevice { [PreserveSig] int Activate(ref Guid id, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint); }
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISessionManager
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr id, uint flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr id, uint flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out ISessions sessions);
        }
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISessions
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
        }
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISessionControl
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
            [PreserveSig] int GetGroupingParam(out Guid group);
            [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr callback);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr callback);
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out uint process);
            [PreserveSig] int IsSystemSoundsSession();
            [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }
        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid context);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        }
    }
}
