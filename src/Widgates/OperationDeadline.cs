using System;
using System.Threading;
using System.Threading.Tasks;

namespace Widgates
{
    internal static class OperationDeadline
    {
        internal static async Task<T> Run<T>(Task<T> operation, Action cancel, int milliseconds)
        {
            using (var timeout = new CancellationTokenSource())
            {
                if (await Task.WhenAny(operation, Task.Delay(milliseconds, timeout.Token)) != operation)
                {
                    try { cancel(); } catch { }
                    Task observed = operation.ContinueWith(faulted => GC.KeepAlive(faulted.Exception), TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException("Medya kaynağı zamanında yanıt vermedi. Yeniden deneyebilirsiniz.");
                }
                timeout.Cancel(); return await operation;
            }
        }
    }
}
