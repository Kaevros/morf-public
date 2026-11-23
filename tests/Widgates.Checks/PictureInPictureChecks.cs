using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Widgates
{
    internal static class PictureInPictureChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Token = "0123456789abcdef0123456789abcdef";
        internal static async Task RunAsync(string output)
        {
            var defaults = new WidgetSettings();
            Check(!defaults.PictureInPicture && !defaults.PictureInPictureAdvanced && defaults.PictureInPictureSeekSeconds == 10, "PiP is opt-in with a header-free default and ten-second advanced seeking");
            string path = Path.Combine(output, "pip-settings.txt");
            defaults.PictureInPicture = true; defaults.PictureInPictureAdvanced = true; defaults.PictureInPictureSeekSeconds = 5;
            Check(defaults.Save(path), "PiP settings can be saved");
            var loaded = WidgetSettings.Load(path);
            Check(loaded.PictureInPicture && loaded.PictureInPictureAdvanced && loaded.PictureInPictureSeekSeconds == 5 && loaded.Clone().PictureInPictureAdvanced,
                "PiP settings survive save, load and draft cloning");
            defaults.PictureInPictureSeekSeconds = int.MaxValue; defaults.Save(path);
            Check(WidgetSettings.Load(path).PictureInPictureSeekSeconds == 10, "Invalid seek configuration falls back to ten seconds");

            var status = Status();
            Check(PictureInPictureBrowser.FromExecutable("brave.exe") == "brave" && PictureInPictureBrowser.FromExecutable("msedge.exe") == "edge"
                && PictureInPictureBrowser.FromExecutable("cmd.exe") == null, "Browser identity uses exact native launcher names instead of Chromium's shared user agent");
            uint browserProcess;
            string caller = PictureInPictureBrowser.Caller(out browserProcess);
            Check(caller == null || PictureInPictureProtocol.ValidBrowser(caller), "The one-shot public Windows caller lookup handles a non-browser test launcher safely");
            Check(PictureInPictureBridge.Matches(status, "chrome.exe", "Example video", "Channel"), "PiP artwork matches the selected browser and exact video metadata");
            Check(!PictureInPictureBridge.Matches(status, "Spotify.exe", "Example video", "Channel"), "Spotify artwork never opens browser PiP even with matching titles");
            Check(!PictureInPictureBridge.Matches(status, "msedge.exe", "Example video", "Channel")
                && !PictureInPictureBridge.Matches(status, "chrome.exe", "Another video", "Channel")
                && !PictureInPictureBridge.Matches(status, "chrome.exe", "Example video", "Other channel"), "Different browser, title or channel cannot target another video");
            Check(!PictureInPictureBridge.Matches(status, "chrome.exe", "", ""), "An unknown media identity cannot enable artwork PiP");
            Check(PictureInPictureHost.AllowedOrigin("chrome-extension://" + PictureInPictureHost.ExtensionId + "/")
                && !PictureInPictureHost.AllowedOrigin("https://youtube.com/") && !PictureInPictureHost.AllowedOrigin("chrome-extension://malicious/"),
                "Native host accepts only the packaged extension identity");
            Check(PictureInPictureHost.IsInvocation(new[] { "chrome-extension://" + PictureInPictureHost.ExtensionId + "/", "--parent-window=0" })
                && PictureInPictureHost.IsInvocation(new[] { PictureInPictureHost.Argument })
                && PictureInPictureHost.IsInvocation(new[] { "chrome-extension://untrusted/" })
                && !PictureInPictureHost.IsInvocation(new string[0]),
                "Real Chromium host arguments and rejected extension origins route away from capsule activation");
            Check(PictureInPictureHost.Run(new[] { "chrome-extension://untrusted/", "--parent-window=0" }) == 2
                && PictureInPictureHost.Run(new[] { PictureInPictureHost.Argument }) == 2,
                "Invalid native launches fail closed before any pipe connection or WPF startup");

            await RejectFrame(new byte[] { 0xff, 0xff, 0xff, 0xff }, "Oversize native frames are rejected before allocating their body");
            await RejectFrame(new byte[] { 0, 0, 0, 0 }, "Empty native frames are rejected");
            await RejectFrame(new byte[] { 4, 0, 0, 0, 123 }, "Truncated native frames are rejected");
            Check(Rejected("{\"version\":2,\"type\":\"hello\",\"session\":\"" + Token + "\",\"browser\":\"chrome\"}"), "Protocol versions fail closed");
            Check(Rejected("{\"version\":1,\"type\":\"command\",\"session\":\"" + Token + "\",\"command\":\"execute\"}"), "Native messages cannot execute arbitrary commands");
            status.Site = "open.spotify.com";
            Check(!PictureInPictureProtocol.Valid(status), "Browser Spotify is excluded from PiP as well as the native Spotify application");
            status = Status(); status.Title = new string('x', 513);
            Check(!PictureInPictureProtocol.Valid(status), "Untrusted video metadata is length-bounded");
            Check(!PictureInPictureProtocol.ValidSite("example.com/path") && !PictureInPictureProtocol.ValidSite("example.com@attacker.com")
                && !PictureInPictureProtocol.ValidSite("localhost") && !PictureInPictureProtocol.ValidSession(new string('z', 32)), "Invalid hostnames and session tokens are rejected");
            using (var memory = new MemoryStream())
            {
                await PictureInPictureProtocol.WriteAsync(memory, Status()); memory.Position = 0;
                var roundtrip = await PictureInPictureProtocol.ReadAsync(memory);
                Check(roundtrip.Title == "Example video" && roundtrip.Video && roundtrip.Session == Token, "Bounded native framing preserves video identity");
            }

            string pipeName = "Morf.PiP.Checks." + Guid.NewGuid().ToString("N");
            using (var bridge = new PictureInPictureBridge(pipeName))
            {
                Check(!bridge.Listening && bridge.Completion == null && bridge.Current == null, "Disabled PiP creates no listener, task or video state");
                bridge.Configure(false, 10);
                Check(!bridge.Listening && bridge.Completion == null, "Reapplying disabled PiP still starts no work");
                bridge.Configure(true, 10);
                using (var malformed = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    await malformed.ConnectAsync(2000);
                    await malformed.WriteAsync(new byte[4], 0, 4); await malformed.FlushAsync();
                    bool rejected = false;
                    try { rejected = await Deadline(PictureInPictureProtocol.ReadAsync(malformed)) == null; }
                    catch (IOException) { rejected = true; }
                    Check(rejected, "Malformed browser handshakes are disconnected without stopping the listener");
                }
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    await client.ConnectAsync(2000);
                    await PictureInPictureProtocol.WriteAsync(client, new PipMessage { Type = "hello", Session = Token, Browser = "chrome" });
                    var config = await Deadline(PictureInPictureProtocol.ReadAsync(client));
                    Check(config.Enabled && config.SeekSeconds == 10, "A user-started browser receives Morf's ten-second PiP configuration");
                    var received = new TaskCompletionSource<bool>();
                    bridge.Changed += () => { if (bridge.Current != null) received.TrySetResult(true); };
                    await PictureInPictureProtocol.WriteAsync(client, Status());
                    await Deadline(received.Task);
                    Check(!await bridge.OpenAsync("Spotify", "Example video", "Channel"), "Live IPC refuses artwork commands from Spotify");
                    Check(await bridge.OpenAsync("chrome.exe", "Example video", "Channel"), "Matched artwork sends a command to the explicitly linked video");
                    var command = await Deadline(PictureInPictureProtocol.ReadAsync(client));
                    Check(command.Command == "open" && command.Session == Token, "Artwork commands retain the exact browser session token");
                    var artworkWindow = new IslandWindow(new WidgetSettings { PictureInPicture = true });
                    try
                    {
                        typeof(IslandWindow).GetField("pictureInPicture", Private).SetValue(artworkWindow, bridge);
                        var media = (MediaController)typeof(IslandWindow).GetField("media", Private).GetValue(artworkWindow);
                        typeof(MediaController).GetProperty("SourceId", Private).SetValue(media, "chrome.exe", null);
                        typeof(MediaController).GetProperty("Title", Private).SetValue(media, "Example video", null);
                        typeof(MediaController).GetProperty("Artist", Private).SetValue(media, "Channel", null);
                        typeof(IslandWindow).GetMethod("UpdatePictureInPictureAction", Private).Invoke(artworkWindow, null);
                        var button = (Button)typeof(IslandWindow).GetField("mediaApplicationButton", Private).GetValue(artworkWindow);
                        Check(button.Focusable && AutomationProperties.GetName(button) == "Videoyu PiP penceresinde göster", "A matched video enables the expanded capsule's accessible artwork action");
                        button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        command = await Deadline(PictureInPictureProtocol.ReadAsync(client));
                        Check(command.Command == "open" && command.Session == Token, "The real WPF artwork click sends the linked video's PiP command");
                    }
                    finally
                    {
                        typeof(IslandWindow).GetField("pictureInPicture", Private).SetValue(artworkWindow, null);
                        artworkWindow.Close();
                    }
                    bridge.Configure(true, 15);
                    config = await Deadline(PictureInPictureProtocol.ReadAsync(client));
                    Check(config.SeekSeconds == 15, "Changing the seek setting updates only the active connection");
                    bridge.Configure(false, 10);
                    await Deadline(bridge.Completion);
                    Check(!bridge.Listening && bridge.Current == null, "Turning PiP off releases its listener and clears the selected video");
                }
            }

            var settings = new WidgetSettings { PictureInPicture = true };
            var window = new IslandWindow(settings);
            try
            {
                Check(typeof(IslandWindow).GetField("pictureInPicture", Private).GetValue(window) == null,
                    "Offscreen WPF previews never register browser integration or create a PiP listener");
                var controller = (MediaController)typeof(IslandWindow).GetField("media", Private).GetValue(window);
                typeof(MediaController).GetProperty("SourceId", Private).SetValue(controller, "Spotify.exe", null);
                typeof(IslandWindow).GetMethod("UpdatePictureInPictureAction", Private).Invoke(window, null);
                var artwork = (Button)typeof(IslandWindow).GetField("mediaApplicationButton", Private).GetValue(window);
                Check(artwork.Focusable && AutomationProperties.GetName(artwork) == "Müzik uygulamasını aç", "Spotify keeps its existing accessible artwork action when PiP is enabled");
            }
            finally { window.Close(); }
        }
        private static PipMessage Status() { return new PipMessage { Type = "status", Session = Token, Browser = "chrome", Site = "www.youtube.com", Title = "Example video", Artist = "Channel", Video = true, Open = true }; }
        private static bool Rejected(string json)
        {
            try { PictureInPictureProtocol.Decode(Encoding.UTF8.GetBytes(json)); return false; }
            catch (InvalidDataException) { return true; }
        }
        private static async Task RejectFrame(byte[] bytes, string label)
        {
            bool rejected = false;
            try { using (var memory = new MemoryStream(bytes)) await PictureInPictureProtocol.ReadAsync(memory); }
            catch (IOException) { rejected = true; }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, label);
        }
        private static async Task<T> Deadline<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(4000)) != task) throw new TimeoutException("PiP check exceeded its deadline.");
            return await task;
        }
        private static async Task Deadline(Task task)
        {
            if (await Task.WhenAny(task, Task.Delay(4000)) != task) throw new TimeoutException("PiP check exceeded its deadline.");
            await task;
        }
        private static void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
    }
}
