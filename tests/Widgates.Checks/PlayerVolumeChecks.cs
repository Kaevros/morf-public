using System;
using System.Threading.Tasks;

namespace Widgates
{
    internal static class PlayerVolumeChecks
    {
        internal static async Task RunAsync()
        {
            Check(PlayerVolumeIdentity.Matches("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "", "Spotify.exe"), "Packaged Spotify matches only its explicit process alias");
            Check(PlayerVolumeIdentity.Matches("Some.Package!Player", "Some.Package!Player", "helper.exe"), "An exact Windows application identity matches a helper process");
            Check(PlayerVolumeIdentity.Matches("chrome.exe", "", "chrome.exe") && PlayerVolumeIdentity.Matches("Chrome", "", "chrome.exe"), "Desktop media identities match their exact executable names");
            Check(!PlayerVolumeIdentity.Matches("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "", "cs2.exe")
                && !PlayerVolumeIdentity.Matches("not-spotify", "", "Spotify.exe") && !PlayerVolumeIdentity.Matches("", "", "Spotify.exe"),
                "Missing, unrelated and substring identities never target another application");
            Check(!PlayerVolumeIdentity.Matches("C:\\Spotify.exe", "", "Spotify.exe"), "A source path is not treated as an executable alias");
            var endpoint = new MemoryVolume(); bool valid = false; int opens = 0;
            var volume = new SystemVolume(() => { opens++; return endpoint; }, () => valid);
            await volume.SetLevelAsync(0.2f);
            Check(opens == 0 && endpoint.Writes == 0, "An obsolete volume target is rejected before opening any endpoint");
            valid = true;
            volume = new SystemVolume(() => { valid = false; return endpoint; }, () => valid);
            await volume.ToggleMuteAsync();
            Check(endpoint.Writes == 0, "A source change during endpoint discovery cancels the pending write");
            endpoint.Available = false;
            volume = new SystemVolume(() => endpoint);
            await volume.SetLevelAsync(0.4f); await volume.AdjustAsync(120); await volume.ToggleMuteAsync();
            Check(endpoint.Writes == 0, "An unavailable player never receives writes or falls back to system volume");
            endpoint.Available = true;
            await volume.SetLevelAsync(0.6f);
            Check(Math.Abs(endpoint.Level - 0.6f) < 0.001 && !endpoint.Muted, "An explicit player volume change affects the selected endpoint");
        }
        private static void Check(bool success, string text) { if (!success) throw new InvalidOperationException(text); Console.WriteLine("PASS " + text); }
        private sealed class MemoryVolume : IVolumeEndpoint
        {
            internal bool Available = true, Muted;
            internal int Writes;
            internal float Level = 0.5f;
            public VolumeState Read() { return new VolumeState { Available = Available, Level = Level, Muted = Muted }; }
            public void SetLevel(float value) { Level = value; Writes++; }
            public void SetMute(bool value) { Muted = value; Writes++; }
            public void Dispose() { }
        }
    }
}
