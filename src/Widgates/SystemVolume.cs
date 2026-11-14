using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Widgates
{
    internal struct VolumeState
    {
        internal bool Available, Muted;
        internal float Level;
    }

    internal interface IVolumeEndpoint : IDisposable
    {
        VolumeState Read();
        void SetLevel(float value);
        void SetMute(bool muted);
    }

    internal sealed class SystemVolume
    {
        private readonly Func<IVolumeEndpoint> open;
        private readonly Func<bool> valid;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1);
        internal SystemVolume() : this(() => new WindowsVolumeEndpoint()) { }
        internal SystemVolume(Func<IVolumeEndpoint> open, Func<bool> valid = null) { this.open = open; this.valid = valid ?? (() => true); }
        internal Task<VolumeState> ReadAsync() { return UseAsync(endpoint => endpoint.Read()); }
        internal Task<VolumeState> SetLevelAsync(float level)
        {
            if (float.IsNaN(level) || float.IsInfinity(level)) return ReadAsync();
            return UseAsync(endpoint => { if (!endpoint.Read().Available) return new VolumeState(); float value = Math.Max(0, Math.Min(1, level)); endpoint.SetLevel(value); endpoint.SetMute(value == 0); return endpoint.Read(); });
        }
        internal Task<VolumeState> ToggleMuteAsync()
        {
            return UseAsync(endpoint => { var state = endpoint.Read(); if (!state.Available) return state; endpoint.SetMute(!state.Muted); return endpoint.Read(); });
        }
        internal Task<VolumeState> AdjustAsync(int wheelDelta)
        {
            return UseAsync(endpoint =>
            {
                var state = endpoint.Read();
                if (!state.Available) return state;
                float next = Math.Max(0, Math.Min(1, state.Level + 0.02f * wheelDelta / 120f));
                endpoint.SetLevel(next);
                if (next == 0) endpoint.SetMute(true);
                else if (wheelDelta > 0 && state.Muted) endpoint.SetMute(false);
                return endpoint.Read();
            });
        }

        private async Task<VolumeState> UseAsync(Func<IVolumeEndpoint, VolumeState> action)
        {
            await gate.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    try { if (!valid()) return new VolumeState(); using (var endpoint = open()) return valid() ? action(endpoint) : new VolumeState(); }
                    catch (COMException) { return new VolumeState(); }
                    catch (InvalidCastException) { return new VolumeState(); }
                    catch (UnauthorizedAccessException) { return new VolumeState(); }
                    catch (System.Security.SecurityException) { return new VolumeState(); }
                });
            }
            finally { gate.Release(); }
        }
    }

    internal sealed class WindowsVolumeEndpoint : IVolumeEndpoint
    {
        private object enumeratorObject;
        private IDevice device;
        private IEndpointVolume volume;
        internal WindowsVolumeEndpoint()
        {
            try
            {
                enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true));
                var enumerator = (IDeviceEnumerator)enumeratorObject;
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
                Guid id = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
                object endpoint;
                Marshal.ThrowExceptionForHR(device.Activate(ref id, 23, IntPtr.Zero, out endpoint));
                volume = (IEndpointVolume)endpoint;
            }
            catch { Dispose(); throw; }
        }
        public VolumeState Read()
        {
            float level;
            bool muted;
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out level));
            Marshal.ThrowExceptionForHR(volume.GetMute(out muted));
            return new VolumeState { Available = true, Level = level, Muted = muted };
        }
        public void SetLevel(float value) { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(value, ref context)); }
        public void SetMute(bool muted) { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(volume.SetMute(muted, ref context)); }
        public void Dispose()
        {
            Release(volume); Release(device); Release(enumeratorObject);
            volume = null; device = null; enumeratorObject = null;
        }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDevice
        {
            [PreserveSig] int Activate(ref Guid id, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
        }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int GetChannelCount(out uint channels);
            [PreserveSig] int SetMasterVolumeLevel(float value, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float value, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float value);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float value, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float value, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float value);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float value);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        }
    }
}
