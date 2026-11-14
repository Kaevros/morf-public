using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Widgates
{
    internal static class DesktopAndSurfaceChecks
    {
        internal static async Task RunAsync(string output)
        {
            Check(!PlatformSupport.IsSupported(new Version(10, 0, 10240))
                && PlatformSupport.IsSupported(new Version(10, 0, 17763))
                && PlatformSupport.IsSupported(new Version(10, 0, 19045))
                && PlatformSupport.IsSupported(new Version(10, 0, 22000))
                && PlatformSupport.IsSupported(new Version(10, 0, 26100)),
                "Platform gate accepts Windows 10 1809+ and Windows 11, excluding older media API baselines");
            var preferences = new WidgetSettings { Style = SurfaceStyle.LiquidGlass, GlassStrength = 0.8, LiquidTransparency = 0.75, FollowDesktops = false, Opacity = 0.7 };
            string path = Path.Combine(output, "glass.settings");
            Check(preferences.Save(path), "Glass and desktop preferences can be saved");
            var restored = WidgetSettings.Load(path);
            Check(restored.Style == SurfaceStyle.LiquidGlass && restored.GlassStrength == 0.8 && restored.LiquidTransparency == 0.75 && !restored.FollowDesktops,
                "Independent Liquid Glass transparency and existing preferences survive reload");
            File.WriteAllText(path, "HideWhenIdle=False\nOpacity=0.75");
            restored = WidgetSettings.Load(path);
            Check(restored.Style == SurfaceStyle.SoftGlass && restored.FollowDesktops && !restored.HideWhenIdle && restored.LiquidTransparency == 0.55,
                "Older settings retain their choices and receive safe material/desktop defaults");
            var material = new MaterialSurface();
            material.Update(preferences, false);
            var background = material.Background;
            material.Update(preferences, false);
            Check(ReferenceEquals(background, material.Background) && background.IsFrozen,
                "Unchanged material reuses frozen brushes without rebuilding visual effects");
            Check(material.Edge is LinearGradientBrush && material.Edge.IsFrozen && material.Highlight is LinearGradientBrush && material.Highlight.IsFrozen,
                "Liquid Glass uses cached optical edge and highlight brushes without an animation clock");
            byte previousAlpha = ((SolidColorBrush)material.Background).Color.A;
            preferences.LiquidTransparency = 0.25;
            material.Update(preferences, false);
            Check(((SolidColorBrush)material.Background).Color.A > previousAlpha,
                "Liquid Glass transparency changes the glass fill independently of text");
            byte liquidAlpha = ((SolidColorBrush)material.Background).Color.A;
            preferences.Opacity = 0.45; preferences.GlassStrength = 0;
            material.Update(preferences, false);
            Check(((SolidColorBrush)material.Background).Color.A == liquidAlpha && material.Background.Opacity == 1,
                "Other surface opacity and soft glass strength cannot alter Liquid Glass transparency");
            preferences.Style = SurfaceStyle.Solid;
            material.Update(preferences, false);
            Check(material.Background is SolidColorBrush && material.Background.Opacity == 0.45,
                "Solid material retains the independent surface opacity setting");
            material.Update(preferences, true);
            Check(material.Background.Opacity == 1 && ReferenceEquals(material.Highlight, Brushes.Transparent),
                "High contrast removes glass highlights and forces opaque system colors");

            var owner = new IntPtr(10);
            var foreground = new IntPtr(20);
            var fake = new FakeDesktop();
            int uiThread = Thread.CurrentThread.ManagedThreadId;
            var link = new VirtualDesktopLink(() => fake);
            var result = await link.RefreshAsync(owner, foreground, true);
            Check(result.Available && result.OnCurrentDesktop && fake.QueryThread != uiThread,
                "Desktop queries execute off the UI dispatcher");
            Check(fake.MoveCount == 0, "Current desktop checks do not move a window unnecessarily");
            fake = new FakeDesktop { Current = false };
            result = await new VirtualDesktopLink(() => fake).RefreshAsync(owner, foreground, true);
            Check(result.Transition && result.OnCurrentDesktop && fake.MovedWindow == owner && fake.MoveCount == 1,
                "Desktop following moves only this widget and preserves its window identity");
            fake = new FakeDesktop { Current = false };
            result = await new VirtualDesktopLink(() => fake).RefreshAsync(owner, foreground, false);
            Check(!result.OnCurrentDesktop && fake.MoveCount == 0 && fake.ProbeCount == 0,
                "Disabling desktop following leaves the widget on its existing desktop");
            fake = new FakeDesktop { Current = false, EmptyForegroundId = true };
            result = await new VirtualDesktopLink(() => fake).RefreshAsync(owner, foreground, true);
            Check(result.OnCurrentDesktop && fake.ProbeCount == 1,
                "An empty desktop can use a non-activating current-desktop probe");
            fake = new FakeDesktop { Current = false, DenyMove = true };
            result = await new VirtualDesktopLink(() => fake).RefreshAsync(owner, foreground, true);
            Check(result.Available && !result.OnCurrentDesktop, "A denied desktop move remains a recoverable hidden state");
            result = await new VirtualDesktopLink(() => { throw new COMException("Shell unavailable"); }).RefreshAsync(owner, foreground, true);
            Check(!result.Available, "Unavailable desktop COM access does not crash the widget");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }

        private sealed class FakeDesktop : IDesktopBridge
        {
            internal bool Current = true, DenyMove, EmptyForegroundId;
            internal int MoveCount, ProbeCount, QueryThread;
            internal IntPtr MovedWindow;
            public bool IsCurrent(IntPtr window, out bool current)
            {
                QueryThread = Thread.CurrentThread.ManagedThreadId;
                current = window == new IntPtr(10) ? Current : true;
                return true;
            }
            public bool DesktopId(IntPtr window, out Guid id) { id = EmptyForegroundId ? Guid.Empty : new Guid("00000000-0000-0000-0000-000000000001"); return true; }
            public bool Move(IntPtr window, Guid desktop) { MoveCount++; MovedWindow = window; Current = !DenyMove; return !DenyMove; }
            public Guid ProbeCurrentDesktop() { ProbeCount++; return new Guid("00000000-0000-0000-0000-000000000001"); }
            public void Dispose() { }
        }
    }
}
