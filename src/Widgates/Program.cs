using System;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Windows;

#if INBOX_PREVIEW
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
#endif

namespace Widgates
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (PictureInPictureHost.IsInvocation(args)) { Environment.ExitCode = PictureInPictureHost.Run(args); return; }
            if (args.Length > 0 && args[0] == NotificationBannerGuard.Argument) { Environment.ExitCode = NotificationBannerGuard.Run(args); return; }
            // Unknown/background invocations must never signal the visible capsule.
            if (args.Length != 0) { Environment.ExitCode = 2; return; }
            if (!PlatformSupport.IsSupported(Environment.OSVersion.Version))
            {
                MessageBox.Show("Morf için Windows 10 1809 (17763) veya daha yeni bir Windows sürümü gerekir.",
                    "Morf · Windows sürümü", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RunWidget();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunWidget()
        {
            bool firstInstance;
            using (var instance = new Mutex(false, SingleInstanceActivation.MutexName, out firstInstance))
            {
                if (!firstInstance)
                {
                    if (!SingleInstanceActivation.TryActivate()) MessageBox.Show("Morf zaten çalışıyor. Panel kısayoluyla veya bildirim alanı simgesiyle açabilirsiniz.", "Morf");
                    return;
                }

                bool firstLaunch = !System.IO.File.Exists(WidgetSettings.DefaultPath);
                var settings = WidgetSettings.Load(WidgetSettings.DefaultPath);
                bool startupChanged;
                bool startupReady = new StartupRegistration().Initialize(settings, out startupChanged);
                if (startupReady && startupChanged) settings.Save(WidgetSettings.DefaultPath);
                var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var window = new IslandWindow(settings, StopwatchState.DefaultPath);
                window.EnableShellServices();
                if (firstLaunch) window.ContentRendered += (_, __) => window.OfferPictureInPictureSetup();
                application.SessionEnding += (_, __) => window.AllowSessionExit();
                SingleInstanceActivation activation = null;
                try { activation = new SingleInstanceActivation(() => { if (!application.Dispatcher.HasShutdownStarted) application.Dispatcher.BeginInvoke(new Action(window.OpenFromShell)); }); }
                catch (Exception) { }
                if (!startupReady) window.ShowStartupFailure();
                try { application.Run(window); } finally { if (activation != null) activation.Dispose(); }
            }
        }
    }
}
