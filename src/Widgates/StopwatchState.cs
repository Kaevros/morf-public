using System;
using System.Globalization;
using System.IO;

namespace Widgates
{
    internal static class StopwatchState
    {
        private const string Empty = "-";

        internal static string DefaultPath
        {
            get { return Path.Combine(Path.GetDirectoryName(WidgetSettings.DefaultPath), "stopwatch.state"); }
        }

        internal static long? Load(string path)
        {
            var text = AtomicTextFile.Read(path, 18, value =>
            {
                long milliseconds;
                return value == Empty || TryParse(value, out milliseconds);
            });
            long elapsed;
            return TryParse(text, out elapsed) ? elapsed : (long?)null;
        }

        internal static bool Save(string path, long? elapsed)
        {
            return AtomicTextFile.Write(path, elapsed.HasValue ? elapsed.Value.ToString(CultureInfo.InvariantCulture) : Empty);
        }

        private static bool TryParse(string text, out long milliseconds)
        {
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out milliseconds)
                && milliseconds <= TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond;
        }
    }
}
