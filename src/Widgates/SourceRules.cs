using System;
using System.Collections.Generic;
using System.Linq;

namespace Widgates
{
    internal sealed class SourceChoice
    {
        internal string Id, Name;
        public override string ToString() { return Name; }
    }

    // Exact application identities only. Serialized values never become commands or paths.
    internal sealed class SourceRules
    {
        internal const int MaximumLength = 2048, MaximumRules = 32;
        private readonly Dictionary<string, int> values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal IEnumerable<string> Ids { get { return values.Keys; } }
        internal int Get(string id) { int value; return id != null && values.TryGetValue(id, out value) ? value : 0; }
        internal static bool ValidId(string id) { return !string.IsNullOrWhiteSpace(id) && id.Length <= 512 && !id.Any(c => char.IsControl(c) || char.IsSurrogate(c)); }
        internal static SourceRules Parse(string text)
        {
            var result = new SourceRules();
            if (string.IsNullOrEmpty(text) || text.Length > MaximumLength) return result;
            try
            {
                foreach (string entry in text.Split(';'))
                {
                    int split = entry.LastIndexOf('='); int mode;
                    if (split <= 0 || !int.TryParse(entry.Substring(split + 1), out mode) || mode < 1 || mode > 2) return new SourceRules();
                    string id = Uri.UnescapeDataString(entry.Substring(0, split));
                    if (!ValidId(id) || result.values.ContainsKey(id) || result.values.Count == MaximumRules) return new SourceRules();
                    result.values.Add(id, mode);
                }
            }
            catch (UriFormatException) { return new SourceRules(); }
            return result;
        }
        internal bool Set(string id, int mode, bool uniqueFirst = false)
        {
            if (!ValidId(id) || mode < 0 || mode > 2) return false;
            var before = new Dictionary<string, int>(values, StringComparer.OrdinalIgnoreCase);
            if (uniqueFirst && mode == 1) foreach (string key in values.Where(x => x.Value == 1).Select(x => x.Key).ToArray()) values.Remove(key);
            if (mode == 0) values.Remove(id); else values[id] = mode;
            if (values.Count <= MaximumRules && Text.Length <= MaximumLength) return true;
            values.Clear(); foreach (var entry in before) values.Add(entry.Key, entry.Value); return false;
        }
        internal string Text { get { return string.Join(";", values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => Uri.EscapeDataString(x.Key) + "=" + x.Value)); } }
    }

    internal static class MediaPreference
    {
        internal static T Choose<T>(IEnumerable<T> sources, T manual, T newest, T current, Func<T, bool> playing, Func<T, int> rule) where T : class
        {
            var all = sources.ToArray();
            if (manual != null && all.Contains(manual)) return manual;
            var eligible = all.Where(x => rule(x) != 2).ToArray();
            return eligible.FirstOrDefault(x => rule(x) == 1 && playing(x))
                ?? MediaSourceSelection<T>.Choose(eligible, null, newest, current, playing);
        }
    }
}
