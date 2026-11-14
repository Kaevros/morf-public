using System;
using System.Threading.Tasks;

namespace Widgates
{
    // Serialize metadata reads, coalesce bursts and let both text and artwork reject obsolete results.
    internal sealed class LatestMediaRequest<T> : IDisposable
    {
        private readonly Func<T, Func<bool>, Task> read;
        private readonly Action<Exception> failed;
        private T latest;
        private int version;
        private bool pending, running, disposed;
        internal Task Completion { get; private set; }
        internal LatestMediaRequest(Func<T, Func<bool>, Task> read, Action<Exception> failed)
        {
            this.read = read; this.failed = failed; Completion = Task.FromResult(true);
        }
        internal void Request(T source)
        {
            if (disposed) return;
            latest = source; version++; pending = true;
            if (running) return;
            running = true; Completion = RunAsync();
        }
        internal void Invalidate() { version++; pending = false; }
        private async Task RunAsync()
        {
            try
            {
                while (pending && !disposed)
                {
                    pending = false;
                    T source = latest; int revision = version;
                    Func<bool> current = () => !disposed && version == revision;
                    try { await read(source, current); }
                    catch (Exception error) { if (current()) failed(error); }
                }
            }
            finally { running = false; }
        }
        public void Dispose() { disposed = true; Invalidate(); }
    }
}
