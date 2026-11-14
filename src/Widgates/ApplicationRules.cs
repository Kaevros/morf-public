using System;
using System.Collections.Generic;
using System.Linq;

namespace Widgates
{
    internal sealed class ApplicationRules
    {
        private readonly Dictionary<string, bool> values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        internal string Text { get; private set; }
        internal KeyValuePair<string, bool>[] Entries { get { return values.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToArray(); } }
        internal bool? Hidden(string process) { bool hidden; return process != null && values.TryGetValue(Normalize(process), out hidden) ? (bool?)hidden : null; }
        internal static bool TryParse(string input, out ApplicationRules rules)
        {
            rules = new ApplicationRules(); var lines = new List<string>();
            foreach (string raw in (input ?? "").Split(new[] { '\n', ';' }))
            {
                string line = raw.Trim(); if (line.Length == 0) continue;
                string[] parts = line.Split('=');
                if (parts.Length != 2 || lines.Count >= 32) return false;
                string name = Normalize(parts[0].Trim()); string action = parts[1].Trim().ToLowerInvariant();
                if (name.Length == 0 || name.Length > 80 || name.Any(c => !char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_' && c != ' ')
                    || (action != "gizle" && action != "göster") || rules.values.ContainsKey(name)) return false;
                rules.values[name] = action == "gizle"; lines.Add(name + ".exe = " + action);
            }
            rules.Text = string.Join("\n", lines); return true;
        }
        private static string Normalize(string name) { name = name.ToLowerInvariant(); return name.EndsWith(".exe") ? name.Substring(0, name.Length - 4) : name; }
    }
}
