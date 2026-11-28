using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace Widgates
{
    internal static class MediaIntegrationChecks
    {
        internal static async Task RunAsync(string output)
        {
            var preferences = new WidgetSettings();
            Check(preferences.AutoPauseOthers && preferences.ScrollVolume && !preferences.StartWithWindows
                && preferences.HideInGames && !preferences.HideInFullscreen, "First-run defaults keep Windows startup opt-in while enabling media focus, wheel volume and game-only hiding");
            string path = Path.Combine(output, "integration.settings");
            File.WriteAllText(path, "HideInFullscreen=True\nOpacity=0.8");
            Check(!WidgetSettings.Load(path).HideInFullscreen, "Legacy fullscreen hiding migrates to the requested game-only default");
            preferences.AutoPauseOthers = preferences.ScrollVolume = preferences.StartWithWindows = preferences.HideInGames = false;
            preferences.Save(path);
            var restored = WidgetSettings.Load(path);
            Check(!restored.AutoPauseOthers && !restored.ScrollVolume && !restored.StartWithWindows && !restored.HideInGames,
                "New integration switches preserve explicit opt-outs");
            Check(MediaSourceNames.Friendly("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App") == "Apple Music"
                && MediaSourceNames.Friendly("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") == "Spotify"
                && MediaSourceNames.Friendly("msedge.exe") == "Microsoft Edge", "Common media providers receive human-readable names");
            Check(GameDetection.KnownGame("cs2.exe") && GameDetection.KnownGame("FortniteClient-Win64-Shipping")
                && !GameDetection.KnownGame("chrome.exe") && !GameDetection.KnownGame("explorer"), "Game-name detection includes CS2 without treating browsers or Explorer as games");

            var policy = new PlaybackPriority<string>();
            policy.Synchronize(new[] { "YouTube", "Apple Music" }, session => session == "YouTube");
            Check(!policy.HasLatest, "Already-running playback at app launch is a non-disruptive baseline");
            Check(policy.Observe("Apple Music", true) && policy.Latest == "Apple Music", "A new playing transition takes media priority");
            var paused = new List<string>();
            bool result = await policy.PauseOthersAsync("Apple Music", _ => true, _ => true,
                session => { paused.Add(session); return Task.FromResult(true); }, () => true);
            Check(result && paused.SequenceEqual(new[] { "YouTube" }), "New Apple Music playback pauses only the older YouTube source");
            Check(!policy.Observe("Apple Music", true), "Repeated playing notifications do not restart arbitration");
            policy.Observe("YouTube", false);
            Check(policy.Observe("YouTube", true) && policy.Latest == "YouTube", "Restarting the older video makes it the newest media source");
            paused.Clear();
            result = await policy.PauseOthersAsync("YouTube", _ => true, _ => false,
                session => { paused.Add(session); return Task.FromResult(true); }, () => true);
            Check(!result && paused.Count == 0, "Unsupported pause never falls back to a toggle that could resume audio");
            result = await policy.PauseOthersAsync("YouTube", _ => true, _ => true, _ => Task.FromResult(true), () => false);
            Check(!result, "A superseded or disabled arbitration request stops before sending commands");
            result = await policy.PauseOthersAsync("YouTube", _ => true, _ => true, _ => Task.FromResult(false), () => true);
            Check(!result, "A player declining pause is reported as a failure");
            policy.Synchronize(new[] { "YouTube" }, _ => true);
            Check(policy.Others("YouTube").Length == 0, "Closed sessions are removed from automatic media control");

            var endpoint = new FakeVolume();
            var volume = new SystemVolume(() => endpoint);
            var state = await volume.AdjustAsync(120);
            Check(Math.Abs(state.Level - 0.52f) < 0.001 && state.Available, "One wheel notch adjusts master volume by two percentage points");
            state = await volume.AdjustAsync(120000);
            Check(state.Level == 1, "Wheel volume is clamped to the device maximum");
            state = await volume.AdjustAsync(-120000);
            Check(state.Level == 0 && state.Muted, "Volume at zero becomes muted");
            state = await volume.AdjustAsync(120);
            Check(state.Level > 0 && !state.Muted, "Raising volume from mute unmutes intentionally");
            state = await volume.ToggleMuteAsync();
            Check(state.Muted && state.Level > 0, "Mute toggle preserves the configured volume level");
            state = await new SystemVolume(() => { throw new System.Runtime.InteropServices.COMException(); }).ReadAsync();
            Check(!state.Available, "Missing or denied output devices are recoverable");

            var store = new FakeStartup();
            var startup = new StartupRegistration(store, @"C:\Morf App\WindowsWidgates.exe");
            preferences = new WidgetSettings();
            bool startupChanged;
            Check(startup.Initialize(preferences, out startupChanged) && startupChanged && store.Value == null && !preferences.StartWithWindows && preferences.StartupConfigured && store.Writes == 0 && store.Removes == 0,
                "First launch reads startup status without creating or removing a registration");
            Check(startup.Initialize(preferences, out startupChanged) && !startupChanged && store.Writes == 0,
                "Unchanged startup status does not require rewriting preferences or the registry");
            string command = "\"C:\\Morf App\\WindowsWidgates.exe\"";
            store.Value = command;
            startup.Initialize(preferences);
            Check(preferences.StartWithWindows && store.Writes == 0 && store.Removes == 0, "An existing matching startup registration is preserved without a write");
            store.Value = "\"C:\\Older Morf\\WindowsWidgates.exe\"";
            startup.Initialize(preferences);
            Check(!preferences.StartWithWindows && store.Value.Contains("Older Morf") && store.Writes == 0 && store.Removes == 0,
                "Launching a different preview never silently retargets the existing startup entry");
            store.Value = null;
            preferences.StartWithWindows = true;
            startup.Initialize(preferences);
            Check(!preferences.StartWithWindows && store.Writes == 0, "Externally removed startup registration is respected without recreating it");
            Check(startup.Configure(true) && store.Value == command && store.Writes == 1, "Only an explicit enable writes the quoted startup command and verifies it");
            Check(startup.Configure(true) && startup.Configure(false) && store.Value == null, "Startup can be enabled and removed through its own setting");
            Check(store.Writes == 1 && store.Removes == 1, "Repeated enable does not repeat a registry write");
            store.Value = command.ToUpperInvariant();
            Check(startup.Configure(true) && store.Writes == 1,
                "A case-only path difference does not rewrite an existing Windows startup registration");
            var ignoredWrite = new FakeStartup { IgnoreWrites = true };
            Check(!new StartupRegistration(ignoredWrite, @"C:\Morf App\WindowsWidgates.exe").Configure(true) && ignoredWrite.Writes == 1 && ignoredWrite.Value == null,
                "A silently blocked startup write is reported as failure without retrying");
            var ignoredRemove = new FakeStartup { IgnoreRemoves = true, Value = command };
            Check(!new StartupRegistration(ignoredRemove, @"C:\Morf App\WindowsWidgates.exe").Configure(false) && ignoredRemove.Removes == 1 && ignoredRemove.Value == command,
                "A silently blocked startup removal is reported as failure");
            Check(!new StartupRegistration(new FakeStartup { DenyRead = true }, @"C:\Morf App\WindowsWidgates.exe").Initialize(new WidgetSettings()),
                "Denied startup status access is recoverable without a registry write");
            Check(StartupRegistration.Command(@"\\server\app.exe") == null && StartupRegistration.Command("C:\\bad\"name.exe") == null,
                "Startup command rejects UNC paths and injected quoting");
            var taskStore = new FakeStartupTask();
            var packagedStartup = new StartupRegistration(taskStore);
            preferences = new WidgetSettings { StartWithWindows = true };
            Check(await packagedStartup.InitializeAsync(preferences) && !preferences.StartWithWindows && taskStore.Enables == 0 && taskStore.Disables == 0,
                "Packaged launch reads the actual Windows startup task without silently enabling it");
            taskStore.State = StartupTaskState.Enabled;
            Check(await packagedStartup.InitializeAsync(preferences) && preferences.StartWithWindows,
                "Packaged startup recognizes the Windows task independently of a package-version path");
            taskStore.State = StartupTaskState.DisabledByUser;
            Check(!await packagedStartup.ConfigureAsync(true) && taskStore.Enables == 0 && packagedStartup.Notice != null,
                "Startup disabled by the user is respected with actionable Windows settings feedback");
            taskStore.State = StartupTaskState.DisabledByPolicy;
            Check(!await packagedStartup.ConfigureAsync(true) && taskStore.Enables == 0,
                "Packaged startup never bypasses a Windows policy block");
            taskStore.State = StartupTaskState.Disabled;
            Check(await packagedStartup.ConfigureAsync(true) && taskStore.State == StartupTaskState.Enabled && taskStore.Enables == 1,
                "Explicit packaged enable activates the native Windows startup task");
            Check(await packagedStartup.ConfigureAsync(true) && taskStore.Enables == 1,
                "Repeated packaged enable does not request another state change");
            Check(await packagedStartup.ConfigureAsync(false) && taskStore.State == StartupTaskState.Disabled && taskStore.Disables == 1,
                "Disabling or resetting packaged startup disables the native task");
            taskStore.DenyRead = true; preferences.StartWithWindows = true;
            Check(!await packagedStartup.InitializeAsync(preferences) && preferences.StartWithWindows,
                "Unavailable packaged startup state preserves the existing preference");
        }

        private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
        private sealed class FakeVolume : IVolumeEndpoint
        {
            private float level = 0.5f;
            private bool muted;
            public VolumeState Read() { return new VolumeState { Available = true, Level = level, Muted = muted }; }
            public void SetLevel(float value) { level = value; }
            public void SetMute(bool value) { muted = value; }
            public void Dispose() { }
        }
        private sealed class FakeStartup : IStartupStore
        {
            internal string Value;
            internal int Writes;
            internal int Removes;
            internal bool IgnoreWrites, IgnoreRemoves, DenyRead;
            public string Read() { if (DenyRead) throw new UnauthorizedAccessException(); return Value; }
            public void Write(string command) { if (!IgnoreWrites) Value = command; Writes++; }
            public void Remove() { if (!IgnoreRemoves) Value = null; Removes++; }
        }
        private sealed class FakeStartupTask : IStartupTaskStore
        {
            internal StartupTaskState State = StartupTaskState.Disabled;
            internal int Enables, Disables;
            internal bool DenyRead;
            public Task<StartupTaskState> ReadAsync() { if (DenyRead) throw new UnauthorizedAccessException(); return Task.FromResult(State); }
            public Task<StartupTaskState> EnableAsync() { Enables++; State = StartupTaskState.Enabled; return Task.FromResult(State); }
            public Task<StartupTaskState> DisableAsync() { Disables++; State = StartupTaskState.Disabled; return Task.FromResult(State); }
        }
    }
}
