using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Widgates
{
    internal struct DesktopSyncResult
    {
        internal bool Available, OnCurrentDesktop, Transition;
        internal string Failure;
    }

    internal interface IDesktopBridge : IDisposable
    {
        bool IsCurrent(IntPtr window, out bool current);
        bool DesktopId(IntPtr window, out Guid id);
        bool Move(IntPtr window, Guid desktop);
        Guid ProbeCurrentDesktop();
    }

    internal sealed class VirtualDesktopLink
    {
        private readonly Func<IDesktopBridge> create;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1);

        internal VirtualDesktopLink() : this(() => new WindowsDesktopBridge()) { }
        internal VirtualDesktopLink(Func<IDesktopBridge> create) { this.create = create; }

        internal async Task<DesktopSyncResult> RefreshAsync(IntPtr owner, IntPtr foreground, bool follow)
        {
            await gate.WaitAsync();
            try { return await Task.Run(() => Refresh(owner, foreground, follow)); }
            finally { gate.Release(); }
        }

        private DesktopSyncResult Refresh(IntPtr owner, IntPtr foreground, bool follow)
        {
            try
            {
                using (var bridge = create())
                {
                    bool current;
                    if (!bridge.IsCurrent(owner, out current)) return new DesktopSyncResult { Failure = "Desktop status query rejected" };
                    var result = new DesktopSyncResult { Available = true, OnCurrentDesktop = current, Transition = !current };
                    if (current || !follow) return result;
                    Guid target = Guid.Empty;
                    bool candidateCurrent;
                    if (foreground != IntPtr.Zero && foreground != owner
                        && bridge.IsCurrent(foreground, out candidateCurrent) && candidateCurrent)
                        bridge.DesktopId(foreground, out target);
                    if (target == Guid.Empty) target = bridge.ProbeCurrentDesktop();
                    // Never activate another window or switch the user's desktop.
                    // This process's own top-level window is the only move target.
                    if (target != Guid.Empty && bridge.Move(owner, target))
                        result.OnCurrentDesktop = bridge.IsCurrent(owner, out current) && current;
                    return result;
                }
            }
            catch (COMException error) { return Unavailable(error); }
            catch (InvalidCastException error) { return Unavailable(error); }
            catch (PlatformNotSupportedException error) { return Unavailable(error); }
            catch (UnauthorizedAccessException error) { return Unavailable(error); }
            catch (OperationCanceledException error) { return Unavailable(error); }
            catch (InvalidOperationException error) { return Unavailable(error); }
            catch (System.Security.SecurityException error) { return Unavailable(error); }
        }

        private static DesktopSyncResult Unavailable(Exception error)
        {
            return new DesktopSyncResult { Failure = error.GetType().Name + " " + error.HResult.ToString("X8") };
        }
    }

    internal sealed class WindowsDesktopBridge : IDesktopBridge
    {
        private readonly IVirtualDesktopManager manager;
        internal int LastResult { get; private set; }
        internal WindowsDesktopBridge()
        {
            manager = (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), true));
        }
        public bool IsCurrent(IntPtr window, out bool current) { return manager.IsWindowOnCurrentVirtualDesktop(window, out current) >= 0; }
        public bool DesktopId(IntPtr window, out Guid id) { LastResult = manager.GetWindowDesktopId(window, out id); return LastResult >= 0; }
        public bool Move(IntPtr window, Guid desktop) { LastResult = manager.MoveWindowToDesktop(window, ref desktop); return LastResult >= 0; }

        public Guid ProbeCurrentDesktop()
        {
            // A fully transparent, non-activating tool window supplies the current
            // desktop ID when the foreground is the pinned Explorer desktop/taskbar.
            // Created only for a transition, without content or a rendering loop.
            var application = System.Windows.Application.Current;
            if (application == null || application.Dispatcher.HasShutdownStarted) return Guid.Empty;
            var dispatcher = application.Dispatcher;
            IntPtr probe = IntPtr.Zero;
            System.Windows.Window probeWindow = null;
            dispatcher.Invoke(new Action(() =>
            {
                probeWindow = new System.Windows.Window
                {
                    Width = 1, Height = 1, Left = -32000, Top = -32000,
                    WindowStyle = System.Windows.WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, AllowsTransparency = true,
                    Opacity = 0, Background = System.Windows.Media.Brushes.Transparent
                };
                probeWindow.Show();
                probe = new System.Windows.Interop.WindowInteropHelper(probeWindow).Handle;
            }));
            if (probe == IntPtr.Zero) return Guid.Empty;
            try
            {
                Guid id;
                bool current;
                if (IsCurrent(probe, out current) && current && DesktopId(probe, out id) && id != Guid.Empty) return id;
                // Shell registration is asynchronous. One bounded retry on this
                // worker thread; never a recurring timer or a wait on the UI thread.
                Thread.Sleep(100);
                return IsCurrent(probe, out current) && current && DesktopId(probe, out id) ? id : Guid.Empty;
            }
            finally
            {
                if (!dispatcher.HasShutdownStarted) dispatcher.Invoke(new Action(() => probeWindow.Close()));
            }
        }

        public void Dispose() { if (Marshal.IsComObject(manager)) Marshal.FinalReleaseComObject(manager); }

        [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVirtualDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool current);
            [PreserveSig] int GetWindowDesktopId(IntPtr window, out Guid id);
            [PreserveSig] int MoveWindowToDesktop(IntPtr window, ref Guid desktop);
        }

    }
}
