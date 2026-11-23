using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace Widgates
{
    internal static class PictureInPictureRegistration
    {
        internal const string HostName = "com.morf.pip";
        private static readonly string[] Assets = { "manifest.json", "worker.js", "content.js", "inline.js", "youtube-media.js", "policy.js", "icon128.png" };
        internal static string DirectoryPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Morf", "PiP"); }
        }
        internal static string ExtensionPath { get { return Path.Combine(DirectoryPath, "extension"); } }
        // Registration is opt-in, current-user only, and never installs an extension or
        // changes browser security policy. Store publication/IDs remain owner-controlled.
        internal static bool Ensure(out string failure)
        {
            failure = null;
            try
            {
                string executable = Process.GetCurrentProcess().MainModule.FileName;
                if (!string.Equals(Path.GetFileNameWithoutExtension(executable), "WindowsWidgates", StringComparison.OrdinalIgnoreCase))
                { failure = "PiP kurulumu Morf uygulamasından yapılmalıdır."; return false; }
                Directory.CreateDirectory(ExtensionPath);
                foreach (string asset in Assets)
                {
                    using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Widgates.PiP." + asset))
                    {
                        if (source == null) throw new IOException("Missing PiP asset.");
                        using (var destination = File.Create(Path.Combine(ExtensionPath, asset))) source.CopyTo(destination);
                    }
                }
                string chromium = Path.Combine(DirectoryPath, "chromium-host.json");
                WriteManifest(chromium, new HostManifest { Name = HostName, Description = "Morf video controls", Path = executable, Type = "stdio",
                    Origins = new[] { "chrome-extension://" + PictureInPictureHost.ExtensionId + "/" } });
                foreach (string browser in new[] { @"Google\Chrome", @"Microsoft\Edge", @"Chromium", @"BraveSoftware\Brave-Browser", @"Vivaldi", @"Opera Software" })
                    Register(@"Software\" + browser + @"\NativeMessagingHosts\" + HostName, chromium);
                return true;
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is System.Security.SecurityException)
                    && !(error is System.ComponentModel.Win32Exception)) throw;
                failure = "PiP eklenti bağlantısı hazırlanamadı. Windows erişimini kontrol edip yeniden deneyebilirsiniz.";
                return false;
            }
        }
        private static void Register(string key, string path)
        {
            using (var registration = Registry.CurrentUser.CreateSubKey(key)) registration.SetValue("", path, RegistryValueKind.String);
        }
        private static void WriteManifest(string path, HostManifest manifest)
        {
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(HostManifest)).WriteObject(stream, manifest);
        }
        internal static void OpenFolder()
        {
            try { using (var process = Process.Start(new ProcessStartInfo(ExtensionPath) { UseShellExecute = true })) { } }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is System.ComponentModel.Win32Exception) && !(error is UnauthorizedAccessException)) throw;
            }
        }
        [DataContract]
        private sealed class HostManifest
        {
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "description")] public string Description;
            [DataMember(Name = "path")] public string Path;
            [DataMember(Name = "type")] public string Type;
            [DataMember(Name = "allowed_origins", EmitDefaultValue = false)] public string[] Origins;
        }
    }
}
