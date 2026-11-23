using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace Widgates
{
    [DataContract]
    internal sealed class PipMessage
    {
        [DataMember(Name = "version")] public int Version { get; set; }
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "session", EmitDefaultValue = false)] public string Session { get; set; }
        [DataMember(Name = "browser", EmitDefaultValue = false)] public string Browser { get; set; }
        [DataMember(Name = "browserProcess", EmitDefaultValue = false)] public uint BrowserProcess { get; set; }
        [DataMember(Name = "title", EmitDefaultValue = false)] public string Title { get; set; }
        [DataMember(Name = "artist", EmitDefaultValue = false)] public string Artist { get; set; }
        [DataMember(Name = "site", EmitDefaultValue = false)] public string Site { get; set; }
        [DataMember(Name = "video", EmitDefaultValue = false)] public bool Video { get; set; }
        [DataMember(Name = "open", EmitDefaultValue = false)] public bool Open { get; set; }
        [DataMember(Name = "inline", EmitDefaultValue = false)] public bool Inline { get; set; }
        [DataMember(Name = "enabled", EmitDefaultValue = false)] public bool Enabled { get; set; }
        [DataMember(Name = "advanced", EmitDefaultValue = false)] public bool Advanced { get; set; }
        [DataMember(Name = "seekSeconds", EmitDefaultValue = false)] public int SeekSeconds { get; set; }
        [DataMember(Name = "command", EmitDefaultValue = false)] public string Command { get; set; }
        internal PipMessage() { Version = 1; }
    }

    internal static class PictureInPictureProtocol
    {
        internal const int MaximumBytes = 8192;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static int SeekSeconds(int value) { return value == 5 || value == 15 ? value : 10; }
        internal static bool ValidSession(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (char c in value) if (!(c >= 'a' && c <= 'f') && !(c >= '0' && c <= '9')) return false;
            return true;
        }
        internal static bool ValidText(string text, int limit)
        {
            if (text == null) return true;
            if (text.Length > limit) return false;
            foreach (char c in text) if (char.IsControl(c)) return false;
            return true;
        }
        internal static bool ValidBrowser(string value)
        {
            return value == "chrome" || value == "edge" || value == "brave" || value == "firefox" || value == "opera" || value == "vivaldi";
        }
        internal static bool ValidSite(string value)
        {
            Uri uri;
            return !string.IsNullOrEmpty(value) && value.Length <= 253 && Uri.TryCreate("https://" + value, UriKind.Absolute, out uri)
                && uri.Host == value && uri.IsDefaultPort && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.UserInfo)
                && uri.HostNameType == UriHostNameType.Dns && !uri.IsLoopback;
        }
        internal static bool IsMusicSite(string site)
        {
            return site == "spotify.com" || (site ?? "").EndsWith(".spotify.com", StringComparison.Ordinal)
                || site == "music.apple.com" || site == "music.youtube.com";
        }
        internal static bool Valid(PipMessage message)
        {
            if (message == null || message.Version != 1) return false;
            if (message.Type == "hello") return ValidSession(message.Session) && ValidBrowser(message.Browser);
            if (message.Type == "status") return ValidSession(message.Session) && ValidBrowser(message.Browser)
                && ValidSite(message.Site) && !IsMusicSite(message.Site) && message.Video
                && !string.IsNullOrWhiteSpace(message.Title) && ValidText(message.Title, 512) && ValidText(message.Artist, 256);
            if (message.Type == "config") return message.SeekSeconds == 5 || message.SeekSeconds == 10 || message.SeekSeconds == 15;
            if (message.Type == "command") return ValidSession(message.Session) && (message.Command == "open" || message.Command == "close");
            return false;
        }

        internal static byte[] Encode(PipMessage message)
        {
            if (!Valid(message)) throw new InvalidDataException("Invalid PiP message.");
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(PipMessage)).WriteObject(stream, message);
                if (stream.Length > MaximumBytes) throw new InvalidDataException("PiP message too large.");
                return stream.ToArray();
            }
        }
        internal static PipMessage Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("Invalid PiP frame.");
            Utf8.GetString(bytes); // Reject malformed UTF-8 instead of accepting replacement characters.
            using (var stream = new MemoryStream(bytes, false))
            {
                var message = (PipMessage)new DataContractJsonSerializer(typeof(PipMessage)).ReadObject(stream);
                if (!Valid(message)) throw new InvalidDataException("Invalid PiP message.");
                return message;
            }
        }
        internal static async Task<PipMessage> ReadAsync(Stream stream)
        {
            var prefix = new byte[4];
            if (!await ReadExactlyAsync(stream, prefix, true).ConfigureAwait(false)) return null;
            uint length = (uint)(prefix[0] | prefix[1] << 8 | prefix[2] << 16 | prefix[3] << 24);
            if (length == 0 || length > MaximumBytes) throw new InvalidDataException("Invalid PiP frame length.");
            var body = new byte[(int)length];
            await ReadExactlyAsync(stream, body, false).ConfigureAwait(false);
            return Decode(body);
        }
        private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] bytes, bool cleanEof)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes, offset, bytes.Length - offset).ConfigureAwait(false);
                if (read == 0)
                {
                    if (offset == 0 && cleanEof) return false;
                    throw new EndOfStreamException("Truncated PiP frame.");
                }
                offset += read;
            }
            return true;
        }
        internal static async Task WriteAsync(Stream stream, PipMessage message)
        {
            byte[] body = Encode(message);
            int length = body.Length;
            byte[] prefix = { (byte)length, (byte)(length >> 8), (byte)(length >> 16), (byte)(length >> 24) };
            await stream.WriteAsync(prefix, 0, prefix.Length).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
    }
}
