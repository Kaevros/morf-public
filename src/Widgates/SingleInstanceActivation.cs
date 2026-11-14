using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Widgates
{
    internal sealed class SingleInstanceActivation : IDisposable
    {
        private readonly EventWaitHandle signal;
        private readonly RegisteredWaitHandle registration;
        private static string Name { get { using (var identity = WindowsIdentity.GetCurrent()) return @"Local\WindowsWidgates.Activate." + identity.User.Value; } }
        internal static string MutexName { get { return Name + ".Mutex"; } }
        internal SingleInstanceActivation(Action activate, string instanceName = null)
        {
            IntPtr descriptor;
            using (var identity = WindowsIdentity.GetCurrent())
                if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;GA;;;" + identity.User.Value + ")", 1, out descriptor, IntPtr.Zero)) throw new Win32Exception();
            IntPtr handle;
            try { var security = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor = descriptor }; handle = CreateEvent(ref security, false, false, instanceName ?? Name); }
            finally { LocalFree(descriptor); }
            if (handle == IntPtr.Zero) throw new Win32Exception();
            signal = new EventWaitHandle(false, EventResetMode.AutoReset);
            signal.SafeWaitHandle.Dispose(); signal.SafeWaitHandle = new SafeWaitHandle(handle, true);
            registration = ThreadPool.RegisterWaitForSingleObject(signal, (_, expired) => { if (!expired) activate(); }, null, Timeout.Infinite, false);
        }
        internal static bool TryActivate(string instanceName = null)
        {
            try
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    IntPtr handle = OpenEvent(2, false, instanceName ?? Name);
                    if (handle != IntPtr.Zero) { using (var owned = new SafeWaitHandle(handle, true)) return SetEvent(owned); }
                    Thread.Sleep(50); // Bounded startup race recovery, never an idle or input poll.
                }
            }
            catch (System.Security.SecurityException) { }
            return false;
        }
        public void Dispose() { registration.Unregister(null); signal.Dispose(); }
        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, IntPtr size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateEventW", SetLastError = true)] private static extern IntPtr CreateEvent(ref SecurityAttributes security, bool manual, bool initial, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenEventW")] private static extern IntPtr OpenEvent(uint access, bool inherit, string name);
        [DllImport("kernel32.dll")] private static extern bool SetEvent(SafeWaitHandle handle);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    }
}
