using System;
using System.Linq;

namespace Widgates
{
    // Match Windows toasts to the actual media source and content, never the display name alone.
    internal static class MediaNotification
    {
        private static int KnownSource(string id)
        {
            if (string.Equals(id, "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(id, "Spotify.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "Spotify", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
        }
        internal static bool SameSource(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second)
                && (string.Equals(first, second, StringComparison.OrdinalIgnoreCase)
                    || (KnownSource(first) != 0 && KnownSource(first) == KnownSource(second)));
        }
        internal static bool Matches(AppMessage message, string source, string title, string artist, string album, string identity)
        {
            if (message == null || string.IsNullOrWhiteSpace(title) || string.IsNullOrEmpty(identity)
                || !SameSource(message.AppId, source) || !string.Equals(message.Sender, title, StringComparison.OrdinalIgnoreCase)
                || (message.MediaTrackIdentity != null && message.MediaTrackIdentity != identity)) return false;
            // Spotify may omit the album in its toast. Match the artist when it is known;
            // the bound track identity prevents a later metadata update from reusing this toast.
            string body = message.Body ?? "";
            return string.IsNullOrWhiteSpace(artist) || body.IndexOf(artist, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        internal static string Detail(string artist, string album)
        {
            return string.Join(" · ", new[] { artist, album }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }
}
