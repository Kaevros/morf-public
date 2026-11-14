using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Widgates
{
    internal static class FeaturePackChecks
    {
        internal static async Task RunAsync(string output)
        {
            var defaults = new WidgetSettings();
            Check(defaults.HoverDelayMs == 250 && defaults.CompactWidth == 180 && defaults.MonitorDevice == "" && defaults.Lightweight == LightweightMode.Automatic
                && defaults.PanelHotkey == "Ctrl+Alt+M" && defaults.PlayHotkey == "" && defaults.AppRules == "", "Feature defaults retain the primary display and panel shortcut with optional controls disabled");
            string path = Path.Combine(output, "feature.settings");
            var preferences = new WidgetSettings { HoverDelayMs = 450, CompactWidth = 205, MonitorDevice = @"\\.\DISPLAY2", AppRules = "cs2.exe = gizle\nvlc.exe = göster",
                PlayHotkey = "Ctrl+Alt+P", Lightweight = LightweightMode.Always };
            preferences.Save(path); preferences.CompactWidth = 220; preferences.Save(path);
            var loaded = WidgetSettings.Load(path);
            Check(loaded.CompactWidth == 220 && loaded.HoverDelayMs == 450 && loaded.AppRules.Contains("vlc") && loaded.PlayHotkey == "Ctrl+Alt+P"
                && loaded.MonitorDevice == preferences.MonitorDevice && loaded.Lightweight == LightweightMode.Always, "All feature preferences round-trip through the bounded local settings file");
            File.WriteAllText(path, "MorfSettings=2\nStartWithWindows=True\n");
            loaded = WidgetSettings.Load(path);
            Check(loaded.CompactWidth == 205 && loaded.PlayHotkey == "Ctrl+Alt+P", "Truncated versioned settings recover the last complete backup");
            string atomic = Path.Combine(output, "atomic.txt"); AtomicTextFile.Write(atomic, "old"); AtomicTextFile.Write(atomic, "new");
            Check(File.ReadAllText(atomic) == "new" && File.ReadAllText(atomic + ".bak") == "old", "Atomic writes retain one previous complete value");
            Check(!AtomicTextFile.Write(output, "not a file") && !Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(output)), Path.GetFileName(output) + ".tmp-*").Any(),
                "A failed atomic replacement leaves no temporary file behind");

            bool cancelled = false; var never = new TaskCompletionSource<bool>(); bool timedOut = false;
            try { await OperationDeadline.Run(never.Task, () => cancelled = true, 20); } catch (TimeoutException) { timedOut = true; }
            Check(timedOut && cancelled && await OperationDeadline.Run(Task.FromResult(true), () => { }, 100), "Hung operations cancel on deadline and the next operation remains usable");
            ApplicationRules rules;
            Check(ApplicationRules.TryParse(preferences.AppRules, out rules) && rules.Hidden("CS2") == true && rules.Hidden("vlc.exe") == false && rules.Hidden("chrome") == null,
                "Application rules match process names and distinguish hide, show and no override");
            Check(!ApplicationRules.TryParse("C:\\app.exe = gizle", out rules) && !ApplicationRules.TryParse("foo=gizle\nFOO.exe=göster", out rules)
                && !ApplicationRules.TryParse(string.Join("\n", Enumerable.Range(0, 33).Select(i => "app" + i + "=gizle")), out rules),
                "Application rules reject paths, duplicates and unbounded lists");
            HotkeyBinding binding;
            Check(HotkeyBinding.TryParse(" Alt + Ctrl + p ", out binding) && binding.Text == "Ctrl+Alt+P" && !HotkeyBinding.TryParse("P", out binding)
                && !HotkeyBinding.TryParse("F12", out binding) && !HotkeyBinding.TryParse("Win+P", out binding), "Hotkey parsing normalizes safe combinations and excludes typing/system reservations");
            var registry = new FakeHotkeys(); var keys = new HotkeySet(registry);
            Check(keys.Apply(new[] { "Ctrl+Alt+M", "Ctrl+Alt+P", "", "" }) && keys.PanelRegistered, "Hotkey registry applies panel and optional media controls");
            Check(keys.Apply(new[] { "Ctrl+Alt+P", "Ctrl+Alt+M", "", "" }) && registry.Values[1] == "Ctrl+Alt+P" && registry.Values[2] == "Ctrl+Alt+M",
                "Hotkey swaps release changed bindings before registering replacements");
            registry.Blocked.Add("Ctrl+Alt+X");
            Check(!keys.Apply(new[] { "Ctrl+Alt+P", "Ctrl+Alt+X", "", "" }) && registry.Values[2] == "Ctrl+Alt+M" && registry.Values[1] == "Ctrl+Alt+P",
                "An occupied shortcut restores previous bindings without disturbing unchanged controls");
            Check(!keys.Apply(new[] { "Ctrl+Alt+M", "Ctrl+Alt+P", "Ctrl+Alt+X", "" }) && registry.Values[1] == "Ctrl+Alt+P" && registry.Values[2] == "Ctrl+Alt+M",
                "A later registration failure rolls back all swapped shortcuts without collisions");
            Check(!keys.Apply(new[] { "Ctrl+Alt+P", "Ctrl+Alt+P", "", "" }), "Duplicate action shortcuts are rejected before registration");
            keys.Dispose(); Check(registry.Values.Count == 0, "Disposal unregisters only this widget's shortcuts");

            var primary = new DisplayMonitor { Device = "primary", Primary = true, Bounds = new Rect(0, 0, 1920, 1080), Work = new Rect(0, 0, 1920, 1040), Scale = 1 };
            var secondary = new DisplayMonitor { Device = "second", Bounds = new Rect(-2560, 0, 2560, 1440), Work = new Rect(-2560, 0, 2560, 1400), Scale = 1.5 };
            Check(MonitorCatalog.Resolve("second", new[] { primary, secondary }) == secondary && MonitorCatalog.Resolve("missing", new[] { primary }) == primary
                && MonitorCatalog.Resolve("", new[] { secondary, primary }) == primary, "Display selection resolves saved monitors and safely falls back after removal");
            var state = new PowerSnapshot();
            Check(state.RenderAllowed && !state.Light(LightweightMode.Automatic) && state.Light(LightweightMode.Always), "Power defaults preserve full behavior on AC and support manual light mode");
            state.OnBattery = true;
            Check(state.Light(LightweightMode.Automatic) && !state.Light(LightweightMode.Never), "Automatic light mode responds to battery without overriding an explicit choice");
            state.Locked = true; Check(!state.RenderAllowed, "Locked sessions prevent visual rendering regardless of light mode");
            state.Locked = false; state.DisplayOn = false; Check(!state.RenderAllowed, "Powered-off displays prevent visual rendering");

            int opened = 0;
            using (var hover = new HoverIntent(Dispatcher.CurrentDispatcher, () => opened++, () => true))
            {
                hover.Request(500); Check(hover.Pending && opened == 0, "Hover intent defers opening instead of reacting to a passing pointer");
                hover.Cancel(); Check(!hover.Pending && opened == 0, "Pointer departure cancels a pending hover without opening");
                hover.Request(0); Check(opened == 1 && !hover.Pending, "Zero-delay preference opens immediately without leaving a clock");
            }
            var now = DateTimeOffset.UtcNow; int pauses = 0;
            using (var sleep = new SleepPauseTimer(Dispatcher.CurrentDispatcher, valid => { if (valid()) pauses++; return Task.FromResult(true); }, () => now))
            {
                Check(!sleep.Active, "Sleep timer starts disabled and stores no persisted deadline");
                sleep.Start(15); now = now.AddMinutes(14); sleep.CheckDue(); Check(sleep.Active && pauses == 0, "Sleep timer never pauses before its selected deadline");
                now = now.AddMinutes(1); sleep.CheckDue(); sleep.CheckDue(); Check(!sleep.Active && pauses == 1, "Sleep expiry pauses once and stops its clock");
                sleep.Start(30); sleep.Cancel(); now = now.AddHours(1); sleep.CheckDue(); Check(pauses == 1 && !sleep.Active, "Cancelled sleep timers cannot pause media later");
            }
            CheckResetAndDiagnostics();
        }

        private static void CheckResetAndDiagnostics()
        {
            var model = new WidgetSettings { CompactWidth = 240, HoverDelayMs = 700 }; int resets = 0, reports = 0, copies = 0;
            var dialog = new SettingsWindow(model, new SettingsActions { Apply = _ => { }, SelectTheme = _ => true, ConfigureStartup = _ => true, Exit = () => { },
                Reset = () => { resets++; model.CopyFrom(new WidgetSettings()); return true; }, Diagnostics = () => { reports++; return "Local report"; }, CopyDiagnostics = _ => copies++ });
            try
            {
                Check(reports == 0, "Diagnostics are not sampled when their page is closed");
                dialog.SelectPage(5); Check(reports == 1, "Opening diagnostics takes one on-demand snapshot");
                Click(FindButton(dialog, "Tanılamayı kopyala")); Check(copies == 1, "Diagnostics copy is explicit and routed through the user's copy action");
                Click(FindButton(dialog, "Varsayılan ayarlara döndür…"));
                Check(resets == 0 && FindButton(dialog, "Vazgeç").IsDefault && !FindButton(dialog, "Evet, varsayılanlara dön").IsDefault,
                    "Reset first opens confirmation and defaults Enter to cancellation");
                Click(FindButton(dialog, "Vazgeç")); Check(resets == 0 && model.CompactWidth == 240, "Cancelling reset leaves every preference untouched");
                Click(FindButton(dialog, "Varsayılan ayarlara döndür…")); Click(FindButton(dialog, "Evet, varsayılanlara dön"));
                Check(resets == 1 && model.CompactWidth == 180 && model.HoverDelayMs == 250, "Only an explicit second confirmation restores defaults");
            }
            finally { dialog.Close(); }
        }
        private static Button FindButton(SettingsWindow dialog, string label)
        {
            var page = (StackPanel)typeof(SettingsWindow).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
            return Descendants(page).OfType<Button>().Single(button => button.Content as string == label);
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var item = VisualTreeHelper.GetChild(root, i); yield return item; foreach (var child in Descendants(item)) yield return child; }
        }
        private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
        private sealed class FakeHotkeys : IHotkeyRegistry
        {
            internal readonly Dictionary<int, string> Values = new Dictionary<int, string>();
            internal readonly HashSet<string> Blocked = new HashSet<string>();
            public bool Register(int id, HotkeyBinding binding) { if (Blocked.Contains(binding.Text) || Values.Values.Contains(binding.Text)) return false; Values[id] = binding.Text; return true; }
            public void Unregister(int id) { Values.Remove(id); }
        }
    }
}
