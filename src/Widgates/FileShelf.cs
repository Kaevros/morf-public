using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Widgates
{
    internal sealed class FileShelf
    {
        internal const int Capacity = 24;
        internal const int MaximumIncomingItems = 256;
        private readonly List<string> paths = new List<string>();
        private readonly Dictionary<string, string> groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private List<string> undoPaths;
        private Dictionary<string, string> undoGroups;
        internal IReadOnlyList<string> Paths { get { return paths; } }
        internal string LastAddNotice { get; private set; }
        internal bool CanUndo { get { return undoPaths != null; } }
        internal string Group(string path) { string value; return groups.TryGetValue(path, out value) ? value : "Genel"; }
        internal void Assign(IEnumerable<string> selected, string group)
        {
            if (group != "Genel" && group != "Grup 1" && group != "Grup 2" && group != "Grup 3") return;
            foreach (string path in selected.Where(x => paths.Contains(x, StringComparer.OrdinalIgnoreCase))) groups[path] = group;
        }
        internal void Move(IEnumerable<string> selected, int direction, string group = null)
        {
            var set = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            var slots = Enumerable.Range(0, paths.Count).Where(i => group == null || Group(paths[i]) == group).ToArray();
            if (direction < 0) for (int i = 1; i < slots.Length; i++) { if (set.Contains(paths[slots[i]]) && !set.Contains(paths[slots[i - 1]])) { string value = paths[slots[i - 1]]; paths[slots[i - 1]] = paths[slots[i]]; paths[slots[i]] = value; } }
            else for (int i = slots.Length - 2; i >= 0; i--) { if (set.Contains(paths[slots[i]]) && !set.Contains(paths[slots[i + 1]])) { string value = paths[slots[i + 1]]; paths[slots[i + 1]] = paths[slots[i]]; paths[slots[i]] = value; } }
        }
        internal bool UndoRemove()
        {
            if (undoPaths == null) return false;
            var removed = undoPaths.Where(x => !paths.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (paths.Count + removed.Length > Capacity) return false;
            foreach (string path in removed)
            {
                int index = undoPaths.IndexOf(path);
                paths.Insert(Math.Min(index, paths.Count), path);
                string group; if (undoGroups.TryGetValue(path, out group)) groups[path] = group;
            }
            undoPaths = null; undoGroups = null; return true;
        }

        internal int Add(IEnumerable<string> incoming)
        {
            int added = 0;
            int examined = 0;
            int duplicate = 0, rejected = 0, full = 0;
            if (incoming == null) return 0;
            foreach (string path in incoming)
            {
                if (examined++ == MaximumIncomingItems)
                    break;
                string localPath;
                if (!LocalFileAccess.TryGetPath(path, out localPath)) { rejected++; continue; }
                if (paths.Contains(localPath, StringComparer.OrdinalIgnoreCase)) { duplicate++; continue; }
                if (paths.Count == Capacity) { full++; continue; }
                paths.Add(localPath);
                added++;
            }
            LastAddNotice = added + " öğe eklendi" + (duplicate > 0 ? " · " + duplicate + " zaten rafta" : "")
                + (rejected > 0 ? " · " + rejected + " erişilemeyen veya desteklenmeyen konum" : "")
                + (full > 0 ? " · Raf dolu, " + full + " öğe eklenemedi" : "");
            return added;
        }

        internal void Remove(IEnumerable<string> selected)
        {
            var removing = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            if (!paths.Any(removing.Contains)) return;
            undoPaths = new List<string>(paths); undoGroups = new Dictionary<string, string>(groups, StringComparer.OrdinalIgnoreCase);
            paths.RemoveAll(removing.Contains);
            foreach (string path in removing) groups.Remove(path);
        }

        internal static bool Exists(string path)
        {
            string localPath;
            return LocalFileAccess.TryGetPath(path, out localPath);
        }
    }
}
