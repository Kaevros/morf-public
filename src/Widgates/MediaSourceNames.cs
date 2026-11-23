using System;
using System.Linq;

namespace Widgates
{
    internal static class MediaSourceNames
    {
        internal static string Friendly(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "Medya";
            string value = id.ToLowerInvariant();
            if (value.Contains("applemusic") || value.Contains("apple music")) return "Apple Music";
            if (value.Contains("spotify")) return "Spotify";
            if (value.Contains("msedge")) return "Microsoft Edge";
            if (value.Contains("chrome")) return "Google Chrome";
            if (value.Contains("firefox")) return "Firefox";
            if (value.Contains("brave")) return "Brave";
            if (value.Contains("opera")) return "Opera";
            if (value.Contains("vivaldi")) return "Vivaldi";
            if (value.Contains("zunemusic") || value.Contains("mediaplayer")) return "Medya Oynatıcı";
            if (value.Contains("vlc")) return "VLC";
            string clean = new string(id.Where(character => !char.IsControl(character)).Take(120).ToArray());
            clean = clean.Split('!')[0].Split('_')[0];
            if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - 4);
            if (clean.Contains(".")) clean = clean.Split('.').Last();
            return string.IsNullOrWhiteSpace(clean) ? "Medya" : clean;
        }
    }
}
