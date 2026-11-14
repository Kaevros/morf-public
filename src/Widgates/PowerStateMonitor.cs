using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Widgates
{
    internal sealed class PowerSnapshot
    {
        internal bool OnBattery, Saving, Locked;
        internal bool DisplayOn = true;
        internal bool RenderAllowed { get { return !Locked && DisplayOn; } }
        internal bool Light(LightweightMode mode) { return mode == LightweightMode.Always || (mode == LightweightMode.Automatic && (OnBattery || Saving)); }
    }
    internal sealed class PowerStateMonitor : IDisposable
    {
        private readonly HwndSource source;
        private readonly List<IntPtr> registrations = new List<IntPtr>();
        private readonly bool sessionRegistered;
        private static readonly Guid Battery = new Guid("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
        private static readonly Guid Saving = new Guid("E00958C0-C213-4ACE-AC77-FECCED2EEEA5");
        private static readonly Guid Display = new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
        internal readonly PowerSnapshot State = new PowerSnapshot();
        internal event Action Changed;
        internal bool NotificationsAvailable { get { return registrations.Count == 3 && sessionRegistered; } }
        internal PowerStateMonitor(HwndSource source)
        {
            this.source = source;
            PowerStatus status; if (GetSystemPowerStatus(out status)) { State.OnBattery = status.AC == 0; State.Saving = status.Saving == 1; }
            foreach (Guid setting in new[] { Battery, Saving, Display })
            {
                Guid id = setting; IntPtr registration = RegisterPowerSettingNotification(source.Handle, ref id, 0);
                if (registration != IntPtr.Zero) registrations.Add(registration);
            }
            sessionRegistered = WTSRegisterSessionNotification(source.Handle, 0);
            source.AddHook(Message);
        }
        private IntPtr Message(IntPtr window, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            bool changed = false;
            if (message == 0x02B1)
            {
                if (wparam.ToInt32() == 7) { State.Locked = true; changed = true; }
                if (wparam.ToInt32() == 8) { State.Locked = false; changed = true; }
            }
            if (message == 0x0218 && wparam.ToInt32() == 0x8013 && lparam != IntPtr.Zero)
            {
                var setting = (PowerSetting)Marshal.PtrToStructure(lparam, typeof(PowerSetting));
                if (setting.Length == 4)
                {
                    int value = Marshal.ReadInt32(lparam, 20);
                    if (setting.Id == Battery) { State.OnBattery = value != 0; changed = true; }
                    if (setting.Id == Saving) { State.Saving = value != 0; changed = true; }
                    if (setting.Id == Display) { State.DisplayOn = value != 0; changed = true; }
                }
            }
            if (message == 0x0218 && (wparam.ToInt32() == 7 || wparam.ToInt32() == 18)) changed = true;
            if (changed && Changed != null) Changed();
            return IntPtr.Zero;
        }
        public void Dispose()
        {
            source.RemoveHook(Message);
            foreach (IntPtr item in registrations) UnregisterPowerSettingNotification(item);
            if (sessionRegistered) WTSUnRegisterSessionNotification(source.Handle);
        }
        [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte AC, Flags, Percent, Saving; public uint Life, FullLife; }
        [StructLayout(LayoutKind.Sequential)] private struct PowerSetting { public Guid Id; public int Length; }
        [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);
        [DllImport("user32.dll")] private static extern IntPtr RegisterPowerSettingNotification(IntPtr window, ref Guid setting, uint flags);
        [DllImport("user32.dll")] private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
        [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
        [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
    }
}
