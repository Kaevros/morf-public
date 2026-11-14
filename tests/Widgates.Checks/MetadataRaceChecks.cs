using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Widgates
{
    internal static class MetadataRaceChecks
    {
        internal static async Task RunAsync()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = new List<string>(); var published = new List<string>(); int failures = 0;
            var pump = new LatestMediaRequest<string>(async (source, current) =>
            {
                reads.Add(source);
                if (source == "old") await gate.Task;
                if (current()) published.Add(source);
            }, _ => failures++);
            pump.Request("old"); pump.Request("intermediate"); pump.Request("Spotify");
            Check(reads.SequenceEqual(new[] { "old" }), "Metadata reads are serialized while old artwork is pending");
            gate.SetResult(true); await pump.Completion;
            Check(reads.SequenceEqual(new[] { "old", "Spotify" }) && published.SequenceEqual(new[] { "Spotify" }),
                "Metadata bursts coalesce to the newest source and reject obsolete artwork");
            var artwork = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            published.Clear();
            pump = new LatestMediaRequest<string>(async (source, current) =>
            {
                if (current()) published.Add(source + " text");
                if (source == "track A") await artwork.Task;
                if (current()) published.Add(source + " cover");
            }, _ => failures++);
            pump.Request("track A");
            Check(published.SequenceEqual(new[] { "track A text" }), "Track text is available before its artwork stream finishes");
            pump.Request("track B"); artwork.SetResult(true); await pump.Completion;
            Check(!published.Contains("track A cover") && published.Last() == "track B cover", "A late cover cannot overwrite the next track's metadata");
            pump = new LatestMediaRequest<string>((source, current) =>
            {
                if (source == "unavailable") throw new InvalidOperationException();
                if (current()) published.Add(source); return Task.FromResult(true);
            }, _ => failures++);
            pump.Request("unavailable"); await pump.Completion; pump.Request("recovered"); await pump.Completion;
            Check(failures == 1 && published.Last() == "recovered", "A failed metadata read remains recoverable on the next event");
            var closing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool late = false;
            pump = new LatestMediaRequest<string>(async (source, current) => { await closing.Task; late = current(); }, _ => failures++);
            pump.Request("closing"); pump.Dispose(); closing.SetResult(true); await pump.Completion;
            Check(!late, "Closing the controller invalidates outstanding metadata results");
        }
        private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
    }
}
