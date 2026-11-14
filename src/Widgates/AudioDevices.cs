using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Widgates
{
    internal sealed class AudioDevice
    {
        internal string Id, Name;
        internal bool Default;
        public override string ToString() { return Name + (Default ? " · Varsayılan" : ""); }
    }

    internal sealed class MixerChannel
    {
        internal string DeviceId, InstanceId, Name, AppId;
        internal uint ProcessId;
        internal VolumeState State;
        internal bool Master;
    }

    internal static class AudioDevices
    {
        internal static object Enumerator() { return Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)); }
        internal static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
        internal static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        internal static object Activate(Device device, string guid)
        {
            object result; Guid id = new Guid(guid); Check(device.Activate(ref id, 23, IntPtr.Zero, out result)); return result;
        }
        internal static AudioDevice[] List()
        {
            object enumerator = null; DeviceCollection collection = null; Device primary = null;
            try
            {
                enumerator = Enumerator(); string defaultId = "";
                if (((DeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out primary) >= 0) Check(primary.GetId(out defaultId));
                Check(((DeviceEnumerator)enumerator).EnumAudioEndpoints(0, 1, out collection));
                uint count; Check(collection.GetCount(out count)); var devices = new List<AudioDevice>();
                for (uint i = 0; i < Math.Min(count, 64); i++)
                {
                    Device device = null;
                    try { Check(collection.Item(i, out device)); string id; Check(device.GetId(out id)); devices.Add(new AudioDevice { Id = id, Name = Name(device, "Ses çıkışı " + (i + 1)), Default = id == defaultId }); }
                    catch (COMException) { }
                    finally { Release(device); }
                }
                return devices.OrderByDescending(x => x.Default).ThenBy(x => x.Name).ToArray();
            }
            finally { Release(primary); Release(collection); Release(enumerator); }
        }
        private static string Name(Device device, string fallback)
        {
            PropertyStore store = null; var value = new Variant();
            try
            {
                Check(device.OpenPropertyStore(0, out store));
                var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                Check(store.GetValue(ref key, out value));
                string name = value.Type == 31 && value.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(value.Pointer) : null;
                return string.IsNullOrWhiteSpace(name) ? fallback : name.Length > 160 ? name.Substring(0, 160) : name;
            }
            catch (COMException) { return fallback; }
            finally { PropVariantClear(ref value); Release(store); }
        }
        internal static MixerChannel[] Channels(string deviceId)
        {
            var result = new List<MixerChannel>();
            object enumerator = null, manager = null; Device device = null; SessionList sessions = null;
            try
            {
                enumerator = Enumerator(); Check(((DeviceEnumerator)enumerator).GetDevice(deviceId, out device));
                manager = Activate(device, "77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
                Check(((SessionManager)manager).GetSessionEnumerator(out sessions)); int count; Check(sessions.GetCount(out count));
                for (int i = 0; i < Math.Min(count, 128); i++)
                {
                    object session = null;
                    try
                    {
                        Check(sessions.GetSession(i, out session)); var control = (SessionControl)session;
                        int state; Check(control.GetState(out state)); if (state == 2) continue;
                        uint pid; string instance, display, appId = "", executable = "";
                        Check(control.GetSessionInstanceIdentifier(out instance)); Check(control.GetProcessId(out pid));
                        bool system = control.IsSystemSoundsSession() == 0;
                        if (!system && !WindowsPlayerVolumeEndpoint.ReadIdentity(pid, out appId, out executable)) continue;
                        control.GetDisplayName(out display);
                        string name = system ? "Windows sesleri" : MediaSourceNames.Friendly(appId.Length > 0 ? appId : executable);
                        if (!string.IsNullOrWhiteSpace(display) && !display.StartsWith("@", StringComparison.Ordinal)) name = display;
                        result.Add(new MixerChannel { DeviceId = deviceId, InstanceId = instance, ProcessId = pid, AppId = appId, Name = name,
                            State = Read((SimpleVolume)session) });
                    }
                    catch (COMException) { }
                    catch (InvalidCastException) { }
                    finally { Release(session); }
                }
                foreach (var group in result.GroupBy(x => x.Name).Where(x => x.Count() > 1)) { int number = 0; foreach (var item in group) item.Name += " · " + (++number); }
                var master = new MixerChannel { DeviceId = deviceId, Master = true, Name = "Ana ses" };
                master.State = Use(master, null, null); result.Insert(0, master); return result.ToArray();
            }
            finally { Release(sessions); Release(manager); Release(device); Release(enumerator); }
        }
        private static VolumeState Read(SimpleVolume volume)
        {
            float level; bool mute; Check(volume.GetMasterVolume(out level)); Check(volume.GetMute(out mute));
            return new VolumeState { Available = true, Level = level, Muted = mute };
        }
        internal static VolumeState Use(MixerChannel channel, float? level, bool? muted)
        {
            if (level.HasValue && (float.IsNaN(level.Value) || float.IsInfinity(level.Value))) return new VolumeState();
            object enumerator = null, manager = null, endpoint = null; Device device = null; SessionList sessions = null;
            try
            {
                enumerator = Enumerator(); Check(((DeviceEnumerator)enumerator).GetDevice(channel.DeviceId, out device));
                Guid context = Guid.Empty;
                if (channel.Master)
                {
                    endpoint = Activate(device, "5CDF2C82-841E-4546-9722-0CF74078229A"); var volume = (EndpointVolume)endpoint;
                    if (level.HasValue) Check(volume.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(1, level.Value)), ref context));
                    if (muted.HasValue) Check(volume.SetMute(muted.Value, ref context));
                    float current; bool silent; Check(volume.GetMasterVolumeLevelScalar(out current)); Check(volume.GetMute(out silent));
                    return new VolumeState { Available = true, Level = current, Muted = silent };
                }
                manager = Activate(device, "77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"); Check(((SessionManager)manager).GetSessionEnumerator(out sessions));
                int count; Check(sessions.GetCount(out count));
                for (int i = 0; i < Math.Min(count, 128); i++)
                {
                    object session = null;
                    try
                    {
                        Check(sessions.GetSession(i, out session)); var control = (SessionControl)session; string instance; uint pid;
                        Check(control.GetSessionInstanceIdentifier(out instance)); Check(control.GetProcessId(out pid));
                        if (instance != channel.InstanceId || pid != channel.ProcessId) continue;
                        var volume = (SimpleVolume)session;
                        if (level.HasValue) { if (float.IsNaN(level.Value) || float.IsInfinity(level.Value)) return Read(volume); Check(volume.SetMasterVolume(Math.Max(0, Math.Min(1, level.Value)), ref context)); }
                        if (muted.HasValue) Check(volume.SetMute(muted.Value, ref context));
                        return Read(volume);
                    }
                    finally { Release(session); }
                }
                return new VolumeState();
            }
            catch (COMException) { return new VolumeState(); }
            catch (UnauthorizedAccessException) { return new VolumeState(); }
            finally { Release(endpoint); Release(sessions); Release(manager); Release(device); Release(enumerator); }
        }

        [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { internal Guid Format; internal uint Id; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] internal struct Variant { [FieldOffset(0)] internal ushort Type; [FieldOffset(8)] internal IntPtr Pointer; }
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface DeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out DeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out Device device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out Device device);
            [PreserveSig] int RegisterEndpointNotificationCallback(DeviceEvents callback);
            [PreserveSig] int UnregisterEndpointNotificationCallback(DeviceEvents callback);
        }
        [ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface DeviceEvents
        {
            [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
            [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
        }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface DeviceCollection
        { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out Device device); }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface Device
        {
            [PreserveSig] int Activate(ref Guid id, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
            [PreserveSig] int OpenPropertyStore(uint access, out PropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out uint state);
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface PropertyStore
        {
            [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out Variant value);
        }
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface SessionManager
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr id, uint flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr id, uint flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out SessionList sessions);
            [PreserveSig] int RegisterSessionNotification(SessionCreated callback);
            [PreserveSig] int UnregisterSessionNotification(SessionCreated callback);
        }
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface SessionList
        { [PreserveSig] int GetCount(out int count); [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control); }
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface SessionControl
        {
            [PreserveSig] int GetState(out int state); [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path); [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
            [PreserveSig] int GetGroupingParam(out Guid group); [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
            [PreserveSig] int RegisterAudioSessionNotification(SessionEvents callback); [PreserveSig] int UnregisterAudioSessionNotification(SessionEvents callback);
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id); [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out uint process); [PreserveSig] int IsSystemSoundsSession();
        }
        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface SimpleVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid context); [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context); [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface EndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(EndpointEvents callback); [PreserveSig] int UnregisterControlChangeNotify(EndpointEvents callback); [PreserveSig] int GetChannelCount(out uint channels);
            [PreserveSig] int SetMasterVolumeLevel(float value, ref Guid context); [PreserveSig] int SetMasterVolumeLevelScalar(float value, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float value); [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float value, ref Guid context); [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float value, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float value); [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float value);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context); [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        }
        [ComVisible(true), Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface SessionEvents
        {
            [PreserveSig] int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
            [PreserveSig] int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
            [PreserveSig] int OnSimpleVolumeChanged(float level, [MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
            [PreserveSig] int OnChannelVolumeChanged(uint count, IntPtr levels, uint changed, ref Guid context);
            [PreserveSig] int OnGroupingParamChanged(ref Guid group, ref Guid context);
            [PreserveSig] int OnStateChanged(int state); [PreserveSig] int OnSessionDisconnected(int reason);
        }
        [ComVisible(true), Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface SessionCreated
        { [PreserveSig] int OnSessionCreated([MarshalAs(UnmanagedType.IUnknown)] object session); }
        [ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface EndpointEvents
        { [PreserveSig] int OnNotify(IntPtr data); }
    }
}
