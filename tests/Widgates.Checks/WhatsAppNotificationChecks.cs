using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Widgates
{
    internal static class WhatsAppNotificationChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static async Task RunAsync()
        {
            var calls = new List<string>();
            bool opened = WhatsAppNotificationActivation.Open("WhatsApp", id => { calls.Add("shell:" + id); return false; },
                id => { calls.Add("package:" + id); return id == AppMessage.WhatsAppId; });
            Check(opened && calls.Count == 3 && calls[0] == "shell:WhatsApp" && calls[2] == "package:" + AppMessage.WhatsAppId,
                "A legacy WhatsApp notification can open the exact stable package when AppsFolder cannot resolve its alias");
            calls.Clear();
            opened = WhatsAppNotificationActivation.Open(AppMessage.WhatsAppId, id => { calls.Add(id); return true; }, id => { throw new InvalidOperationException("Fallback must not run after acceptance."); });
            Check(opened && calls.Count == 1, "An accepted WhatsApp opening request is never retried through another API");
            calls.Clear();
            opened = WhatsAppNotificationActivation.Open(AppMessage.WhatsAppCurrentBetaId, id => { calls.Add(id); return true; }, _ => false);
            Check(opened && calls.Count == 1 && calls[0] == AppMessage.WhatsAppCurrentBetaId,
                "The current official WhatsApp Beta identity retains its own target");
            calls.Clear();
            opened = WhatsAppNotificationActivation.Open(AppMessage.WhatsAppBetaId, id => { calls.Add(id); return false; }, id => { calls.Add(id); return false; });
            Check(!opened && calls.Count == 4 && !calls.Contains(AppMessage.WhatsAppId),
                "Missing Beta registrations use a bounded Beta-only fallback and never open stable WhatsApp instead");
            calls.Clear();
            Check(!WhatsAppNotificationActivation.Open(AppMessage.WhatsAppId + ".fake", id => { calls.Add(id); return true; }, id => true) && calls.Count == 0,
                "WhatsApp identity lookalikes cannot enter the known-package fallback");
            int packageAttempts = 0;
            var denied = new MediaAppLauncher(notificationLaunch: _ => { throw new UnauthorizedAccessException(); },
                notificationPackageLaunch: _ => { packageAttempts++; return true; });
            Check((await denied.OpenWhatsAppAsync()).Contains("açılamadı") && packageAttempts == 0,
                "Denied WhatsApp access is reported without trying another launch API");
            var cancelled = new MediaAppLauncher(notificationLaunch: _ => { throw new System.ComponentModel.Win32Exception(1223); },
                notificationPackageLaunch: _ => { packageAttempts++; return true; });
            Check((await cancelled.OpenWhatsAppAsync()).Contains("açılamadı") && packageAttempts == 0,
                "A Windows-cancelled WhatsApp opening request is not retried through package activation");
            string target = null;
            var safe = new MediaAppLauncher(notificationLaunch: id => { target = id; return true; }, notificationPackageLaunch: _ => false);
            string result = await safe.OpenNotificationAppAsync(AppMessage.WhatsAppId, "whatsapp://send?phone=000000000&text=untrusted");
            Check(result.Contains("iletildi") && target == AppMessage.WhatsAppId,
                "Phone-like strings and links in a notification label never become WhatsApp launch arguments");
            target = null;
            await safe.OpenNotificationAppAsync("whatsapp://send?phone=000000000", "WhatsApp");
            Check(target == null, "A WhatsApp URI cannot substitute for an application identity");
            packageAttempts = 0;
            var unrelated = new MediaAppLauncher(notificationLaunch: id => { target = id; return false; }, notificationPackageLaunch: _ => { packageAttempts++; return true; });
            await unrelated.OpenNotificationAppAsync("Unrelated.App!App", "WhatsApp");
            Check(target == "Unrelated.App!App" && packageAttempts == 0,
                "An unrelated app named WhatsApp never opens a WhatsApp package");
            Check(!MediaAppLauncher.ValidNotificationAppId("Invalid}.{1BEB6467-F4A7-16AE-72BE-F427253BD6FC}"),
                "Malformed GUID identity prefixes are rejected before shell resolution");
        }
        internal static async Task RunNativeAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; real WhatsApp opening via synthetic Morf notification; no messages, phone links or chat content are read or sent");
            var window = new IslandWindow(new WidgetSettings { WindowsNotifications = true, AutoPauseOthers = false,
                RespectQuietState = false, HideInGames = false, HideWhenIdle = false, Lightweight = LightweightMode.Always });
            try
            {
                window.IsHitTestVisible = false; window.Show(); await Task.Delay(350);
                var messages = Field<AppMessagePeek>(window, "messages");
                var message = AppMessage.Create(70, DateTimeOffset.UtcNow, new[] { "Örnek kişi", "Uygulamayı açma denemesi" }, AppMessage.WhatsAppId, "WhatsApp");
                messages.Offer(message, true);
                Check(messages.Active && Field<TextBlock>(window, "messageSender").Text == "Örnek kişi", "The WhatsApp opening scenario starts with a presented Morf notification");
                await (Task)typeof(IslandWindow).GetMethod("OpenNotificationAsync", Private).Invoke(window, null);
                Check(!messages.Active, "Morf's actual notification-click handler accepts the WhatsApp application opening request");
                await VerifyForegroundAsync("notification-click", output);
                var packaged = new MediaAppLauncher(notificationLaunch: _ => false);
                string result = await packaged.OpenWhatsAppAsync();
                Check(result.Contains("iletildi"), "The exact packaged WhatsApp fallback also accepts a real opening request");
                await VerifyForegroundAsync("package-fallback", output);
            }
            finally { window.Close(); }
        }
        private static async Task VerifyForegroundAsync(string scenario, string output)
        {
            string name = "";
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(300);
                uint processId; GetWindowThreadProcessId(GetForegroundWindow(), out processId);
                if (processId == 0) continue;
                try
                {
                    using (var process = Process.GetProcessById((int)processId)) name = process.ProcessName;
                    if (name.Equals("WhatsApp.Root", StringComparison.OrdinalIgnoreCase) || name.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase)) break;
                }
                catch (ArgumentException) { }
            }
            bool verified = name.Equals("WhatsApp.Root", StringComparison.OrdinalIgnoreCase) || name.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase);
            Check(verified, scenario + " brings the real WhatsApp window to the foreground; process=" + name);
            File.WriteAllText(Path.Combine(output, scenario + ".txt"), "Foreground process: " + name + "; no chat selected or message sent.");
        }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Private).GetValue(owner); }
        private static void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); Console.WriteLine("PASS " + text); }
    }
}
