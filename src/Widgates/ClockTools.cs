using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Widgates
{
    internal enum CountdownPhase { Ready, Running, Paused, Finished }
    internal enum ClockTool { None, Countdown, Stopwatch }

    internal sealed class ClockTools
    {
        private long countdownDue, countdownPaused;
        private long? stopwatchStarted, stopwatchElapsed;

        internal ClockTools(long? restoredElapsed = null) { stopwatchElapsed = restoredElapsed; }

        internal CountdownPhase Countdown { get; private set; }
        internal bool StopwatchRunning { get { return stopwatchStarted.HasValue; } }
        internal bool StopwatchActive { get { return StopwatchRunning || stopwatchElapsed.HasValue; } }
        internal ClockTool Preview
        {
            get
            {
                if (Countdown != CountdownPhase.Ready) return ClockTool.Countdown;
                return StopwatchActive ? ClockTool.Stopwatch : ClockTool.None;
            }
        }

        internal long Remaining(long now)
        {
            if (Countdown == CountdownPhase.Running) return Math.Max(0, countdownDue - now);
            return Countdown == CountdownPhase.Paused ? countdownPaused : 0;
        }

        internal void StartCountdown(long now, long duration)
        {
            if (StopwatchActive) return;
            countdownDue = now + duration;
            Countdown = CountdownPhase.Running;
        }

        internal void PauseCountdown(long now)
        {
            countdownPaused = Remaining(now);
            Countdown = CountdownPhase.Paused;
        }

        internal void ResumeCountdown(long now) { StartCountdown(now, countdownPaused); }
        internal void ResetCountdown() { Countdown = CountdownPhase.Ready; }

        internal bool CheckCountdown(long now)
        {
            if (Countdown != CountdownPhase.Running || now < countdownDue) return false;
            Countdown = CountdownPhase.Finished;
            return true;
        }

        internal long Elapsed(long now) { return stopwatchElapsed.GetValueOrDefault() + (stopwatchStarted.HasValue ? now - stopwatchStarted.Value : 0); }
        internal void ToggleStopwatch(long now)
        {
            if (Countdown != CountdownPhase.Ready) return;
            if (!StopwatchRunning) { stopwatchStarted = now; return; }
            stopwatchElapsed = Elapsed(now);
            stopwatchStarted = null;
        }

        internal void ResetStopwatch() { stopwatchStarted = stopwatchElapsed = null; }

        internal static string Format(long milliseconds, bool roundUp)
        {
            long seconds = (milliseconds + (roundUp ? 999 : 0)) / 1000;
            var culture = CultureInfo.InvariantCulture;
            string minuteSecond = (seconds / 60 % 60).ToString("00", culture) + ":" + (seconds % 60).ToString("00", culture);
            return seconds < 3600 ? minuteSecond : (seconds / 3600).ToString("00", culture) + ":" + minuteSecond;
        }

        // System uptime includes suspend and is independent of wall-clock adjustments.
        [DllImport("kernel32.dll")]
        internal static extern long GetTickCount64();
    }
}
