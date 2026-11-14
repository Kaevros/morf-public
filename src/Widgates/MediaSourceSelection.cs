using System;
using System.Collections.Generic;
using System.Linq;

namespace Widgates
{
    internal static class MediaSourceSelection<T> where T : class
    {
        internal static T Choose(IEnumerable<T> sources, T preferred, T newest, T current, Func<T, bool> playing)
        {
            var items = sources.ToArray();
            if (preferred != null && items.Contains(preferred)) return preferred;
            if (newest != null && items.Contains(newest) && playing(newest)) return newest;
            if (current != null && items.Contains(current) && playing(current)) return current;
            return items.FirstOrDefault(playing) ?? (items.Contains(current) ? current : null) ?? items.FirstOrDefault();
        }
    }
}
