using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading.Tasks;

namespace Widgates
{
    internal static class PictureInPictureHost
    {
        internal const string Argument = "--pip-native-host";
        internal const string ExtensionId = "kobpckacdnbgcaedocbpbplmkfdmfigd";
        internal static bool IsInvocation(string[] args)
        {
            return args != null && args.Length > 0 && (args[0] == Argument
                || args[0].StartsWith("chrome-extension:", StringComparison.OrdinalIgnoreCase));
        }
        internal static bool AllowedOrigin(string origin)
        {
            return origin == "chrome-extension://" + ExtensionId + "/";
        }
        internal static int Run(string[] args)
        {
            // Never enter WPF, activate the capsule or create another widget instance here.
            // Chromium invokes the registered executable with the extension origin
            // as argv[0], optionally followed by --parent-window. The manifest has
            // no facility for adding our diagnostic --pip-native-host switch.
            int originIndex = args.Length > 0 && args[0] == Argument ? 1 : 0;
            if (args.Length <= originIndex || !AllowedOrigin(args[originIndex])) return 2;
            try { RunAsync().GetAwaiter().GetResult(); return 0; }
            catch (Exception error)
            {
                if (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is TimeoutException
                    || error is ObjectDisposedException || error is System.Runtime.Serialization.SerializationException
                    || error is System.Xml.XmlException || error is System.Text.DecoderFallbackException) return 3;
                return 4;
            }
        }
        private static async Task RunAsync()
        {
            using (var input = Console.OpenStandardInput())
            using (var output = Console.OpenStandardOutput())
            using (var pipe = new NamedPipeClientStream(".", PictureInPictureBridge.DefaultPipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
            {
                // Only the extension starts this host. A missing/disabled Morf is recoverable.
                await pipe.ConnectAsync(1500).ConfigureAwait(false);
                uint browserProcess;
                string caller = PictureInPictureBrowser.Caller(out browserProcess);
                Task inbound = CopyAsync(input, pipe, true, caller, browserProcess);
                Task outbound = CopyAsync(pipe, output, false);
                Task finished = await Task.WhenAny(inbound, outbound).ConfigureAwait(false);
                pipe.Dispose();
                await finished.ConfigureAwait(false);
            }
        }
        private static async Task CopyAsync(Stream input, Stream output, bool browser, string caller = null, uint browserProcess = 0)
        {
            bool first = true;
            while (true)
            {
                PipMessage message = await PictureInPictureProtocol.ReadAsync(input).ConfigureAwait(false);
                if (message == null) return;
                if (browser ? (first ? message.Type != "hello" : message.Type != "status")
                    : (message.Type != "config" && message.Type != "command"))
                    throw new InvalidDataException("PiP message direction rejected.");
                first = false;
                if (browser && caller != null) message.Browser = caller;
                if (browser) message.BrowserProcess = browserProcess;
                await PictureInPictureProtocol.WriteAsync(output, message).ConfigureAwait(false);
            }
        }
    }
}
