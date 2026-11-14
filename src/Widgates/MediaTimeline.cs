using System;

namespace Widgates
{
    internal sealed class MediaTimeline
    {
        internal TimeSpan Start, End, Position, MinimumSeek, MaximumSeek;
        internal DateTimeOffset Updated;
        internal double Rate;
        internal bool Valid { get { return Start >= TimeSpan.Zero && End > Start; } }
        internal bool Seekable { get { return Valid && MaximumSeek > MinimumSeek && MaximumSeek > Start && MinimumSeek < End; } }
        internal double Duration { get { return Valid ? (End - Start).TotalSeconds : 0; } }
        internal double PositionAt(DateTimeOffset now, bool playing)
        {
            if (!Valid) return 0;
            double elapsed = playing && Rate >= 0 && Rate <= 16 ? Math.Max(0, (now - Updated).TotalSeconds) * Rate : 0;
            return Math.Max(0, Math.Min(Duration, (Position - Start).TotalSeconds + elapsed));
        }
        internal long SeekTicks(double seconds)
        {
            if (!Seekable || double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new InvalidOperationException("Unavailable seek range.");
            double minimum = (MinimumSeek > Start ? MinimumSeek : Start).TotalSeconds;
            double maximum = (MaximumSeek < End ? MaximumSeek : End).TotalSeconds;
            return TimeSpan.FromSeconds(Math.Max(minimum, Math.Min(maximum, Start.TotalSeconds + seconds))).Ticks;
        }
        internal static string Format(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, Math.Min(359999, seconds)));
            return time.TotalHours >= 1 ? ((int)time.TotalHours) + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00")
                : ((int)time.TotalMinutes) + ":" + time.Seconds.ToString("00");
        }
    }
}
