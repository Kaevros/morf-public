using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Widgates
{
    // One second maximum per output. Audio stays in RAM and is never written to disk.
    internal sealed class AudioFrameQueue
    {
        private readonly byte[] bytes;
        private int start, count;
        internal int Count { get { return count; } }
        internal AudioFrameQueue(int capacity) { if (capacity < 8 || capacity % 8 != 0) throw new ArgumentOutOfRangeException("capacity"); bytes = new byte[capacity]; }
        internal void Write(byte[] data, int length)
        {
            if (length < 0 || length > data.Length || length % 8 != 0) throw new ArgumentOutOfRangeException("length");
            int offset = Math.Max(0, length - bytes.Length); length -= offset;
            int discard = Math.Max(0, count + length - bytes.Length); start = (start + discard) % bytes.Length; count -= discard;
            int tail = (start + count) % bytes.Length, first = Math.Min(length, bytes.Length - tail);
            Buffer.BlockCopy(data, offset, bytes, tail, first); Buffer.BlockCopy(data, offset + first, bytes, 0, length - first); count += length;
        }
        internal int Read(byte[] output, int length)
        {
            if (length < 0 || length > output.Length || length % 8 != 0) throw new ArgumentOutOfRangeException("length");
            int read = Math.Min(length, count), first = Math.Min(read, bytes.Length - start);
            Buffer.BlockCopy(bytes, start, output, 0, first); Buffer.BlockCopy(bytes, 0, output, first, read - first);
            Array.Clear(output, read, length - read); start = (start + read) % bytes.Length; count -= read; return read;
        }
    }

    // Optional shared-mode endpoint loopback -> selected render endpoints. Every wait
    // is driven by WASAPI or Stop. No microphone, idle timer, driver or system routing writes.
    internal sealed class AudioMirror : IDisposable
    {
        internal const int MaximumTargets = 62; // WaitAny: stop + capture + render events, at most 64.
        private readonly object gate = new object();
        private EventWaitHandle stop;
        private Task worker;
        private bool disposed;
        internal bool Active { get; private set; }
        internal string Status { get; private set; }
        internal string SourceId { get; private set; }
        internal string[] TargetIds { get; private set; }
        internal event Action Changed;
        internal AudioMirror() { Status = "Ses çoğaltma kapalı"; TargetIds = new string[0]; }
        internal static bool ValidTargets(string source, string[] targets)
        {
            return !string.IsNullOrEmpty(source) && targets != null && targets.Length > 0 && targets.Length <= MaximumTargets
                && targets.All(x => !string.IsNullOrEmpty(x) && !string.Equals(x, source, StringComparison.OrdinalIgnoreCase)) && targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() == targets.Length;
        }
        internal static Task ProbeAsync(string source, string target)
        {
            return Task.Run(() => {
                object enumerator = AudioDevices.Enumerator();
                try { using (var input = Stream.Open((AudioDevices.DeviceEnumerator)enumerator, source, true)) using (var output = Stream.Open((AudioDevices.DeviceEnumerator)enumerator, target, false)) { } }
                finally { AudioDevices.Release(enumerator); }
            });
        }
        internal Task<string> StartAsync(string source, string[] targets)
        {
            lock (gate)
            {
                if (disposed || (worker != null && !worker.IsCompleted)) return Task.FromResult("Önce çalışan çoğaltmayı durdurun.");
                if (!ValidTargets(source, targets)) return Task.FromResult("Kaynak dışında en az bir farklı ek çıkış seçin.");
                if (stop != null) stop.Dispose(); stop = new EventWaitHandle(false, EventResetMode.ManualReset);
                var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var signal = stop; Status = "Ses çıkışları bağlanıyor…";
                SourceId = source; TargetIds = targets.ToArray();
                worker = Task.Factory.StartNew(() => Run(source, targets.ToArray(), signal, ready), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                worker.ContinueWith(_ => Notify());
                return ready.Task;
            }
        }
        internal void Stop()
        {
            lock (gate) { if (stop != null && !disposed) stop.Set(); }
        }
        internal Task Completion { get { lock (gate) return worker ?? Task.FromResult(0); } }
        private void Notify() { var changed = Changed; if (changed != null) changed(); }
        private void Run(string source, string[] targets, EventWaitHandle signal, TaskCompletionSource<string> ready)
        {
            var streams = new List<Stream>(); object enumerator = null; DeviceWatcher watcher = null; string failure = null;
            try
            {
                enumerator = AudioDevices.Enumerator();
                watcher = new DeviceWatcher(signal, new[] { source }.Concat(targets).ToArray());
                AudioDevices.Check(((AudioDevices.DeviceEnumerator)enumerator).RegisterEndpointNotificationCallback(watcher));
                var input = Stream.Open((AudioDevices.DeviceEnumerator)enumerator, source, true); streams.Add(input);
                foreach (string id in targets) streams.Add(Stream.Open((AudioDevices.DeviceEnumerator)enumerator, id, false));
                if (signal.WaitOne(0)) { ready.TrySetResult("Ses çoğaltma iptal edildi."); return; }
                foreach (var stream in streams) { if (!stream.Input) stream.RenderSilence(); AudioDevices.Check(stream.Client.Start()); }
                Active = true; Status = "Ses çoğaltma açık · " + targets.Length + " ek çıkış"; ready.TrySetResult(Status); Notify();
                var outputs = streams.Where(x => !x.Input).ToArray();
                var handles = new WaitHandle[] { signal }.Concat(streams.Select(x => (WaitHandle)x.Event)).ToArray();
                var scratch = new byte[48000 * 8];
                while (true)
                {
                    int signalled = WaitHandle.WaitAny(handles);
                    if (signalled == 0) break;
                    if (signalled == 1)
                    {
                        uint packet; AudioDevices.Check(input.Capture.GetNextPacketSize(out packet));
                        while (packet > 0 && !signal.WaitOne(0))
                        {
                            IntPtr data; uint frames, flags; ulong position, clock;
                            AudioDevices.Check(input.Capture.GetBuffer(out data, out frames, out flags, out position, out clock));
                            try
                            {
                                int length = checked((int)frames * 8);
                                if (length > scratch.Length) throw new InvalidOperationException("Audio packet limit");
                                if ((flags & 2) != 0) Array.Clear(scratch, 0, length); else Marshal.Copy(data, scratch, 0, length);
                                foreach (var output in outputs) output.Queue.Write(scratch, length);
                            }
                            finally { AudioDevices.Check(input.Capture.ReleaseBuffer(frames)); }
                            AudioDevices.Check(input.Capture.GetNextPacketSize(out packet));
                        }
                    }
                    else streams[signalled - 1].RenderQueued();
                }
                if (watcher.Disconnected) failure = "Ses çıkışı değişti veya bağlantısı kesildi. Çoğaltma durduruldu.";
            }
            catch (Exception error)
            {
                failure = "Ses çoğaltma kullanılamıyor (" + error.HResult.ToString("X8") + "). Çıkışları ve paylaşımlı ses desteğini kontrol edin.";
                ready.TrySetResult(failure);
            }
            finally
            {
                Active = false;
                foreach (var stream in streams) stream.Dispose();
                if (enumerator != null && watcher != null) { try { ((AudioDevices.DeviceEnumerator)enumerator).UnregisterEndpointNotificationCallback(watcher); } catch (COMException) { } }
                AudioDevices.Release(enumerator);
                Status = failure ?? "Ses çoğaltma kapalı"; ready.TrySetResult(Status); Notify();
            }
        }
        public void Dispose()
        {
            Task running; lock (gate) { if (disposed) return; disposed = true; if (stop != null) stop.Set(); running = worker; Changed = null; }
            // The worker owns all native objects and its wait handle until it has exited.
            if (running != null) running.ContinueWith(_ => { lock (gate) { if (stop != null) { stop.Dispose(); stop = null; } } });
            else lock (gate) { if (stop != null) { stop.Dispose(); stop = null; } }
        }
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        private sealed class DeviceWatcher : AudioDevices.DeviceEvents
        {
            private readonly EventWaitHandle signal; private readonly string[] ids;
            internal bool Disconnected;
            internal DeviceWatcher(EventWaitHandle signal, string[] ids) { this.signal = signal; this.ids = ids; }
            private int Change(string id) { if (ids.Contains(id, StringComparer.OrdinalIgnoreCase)) { Disconnected = true; signal.Set(); } return 0; }
            public int OnDeviceStateChanged(string id, uint state) { return state == 1 ? 0 : Change(id); }
            public int OnDeviceAdded(string id) { return 0; }
            public int OnDeviceRemoved(string id) { return Change(id); }
            public int OnDefaultDeviceChanged(int flow, int role, string id) { return 0; }
            public int OnPropertyValueChanged(string id, AudioDevices.PropertyKey key) { return Change(id); }
        }
        private sealed class Stream : IDisposable
        {
            internal AudioClient Client; internal CaptureClient Capture; internal RenderClient Render;
            internal EventWaitHandle Event; internal bool Input; internal uint Frames; internal AudioFrameQueue Queue;
            private object clientObject, service; private byte[] scratch;
            internal static Stream Open(AudioDevices.DeviceEnumerator enumerator, string id, bool input)
            {
                var stream = new Stream { Input = input }; AudioDevices.Device device = null; IntPtr format = IntPtr.Zero;
                try
                {
                    AudioDevices.Check(enumerator.GetDevice(id, out device));
                    stream.clientObject = AudioDevices.Activate(device, "1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"); stream.Client = (AudioClient)stream.clientObject;
                    var wave = new WaveFormat { Tag = 3, Channels = 2, Rate = 48000, Bytes = 384000, Align = 8, Bits = 32 };
                    format = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WaveFormat))); Marshal.StructureToPtr(wave, format, false);
                    AudioDevices.Check(stream.Client.Initialize(0, 0x88040000u | (input ? 0x20000u : 0), 1000000, 0, format, IntPtr.Zero));
                    AudioDevices.Check(stream.Client.GetBufferSize(out stream.Frames));
                    if (stream.Frames == 0 || stream.Frames > 48000) throw new InvalidOperationException("Audio buffer limit");
                    stream.Event = new EventWaitHandle(false, EventResetMode.AutoReset);
                    AudioDevices.Check(stream.Client.SetEventHandle(stream.Event.SafeWaitHandle.DangerousGetHandle()));
                    Guid serviceId = new Guid(input ? "C8ADBD64-E71E-48A0-A4DE-185C395CD317" : "F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
                    AudioDevices.Check(stream.Client.GetService(ref serviceId, out stream.service));
                    if (input) stream.Capture = (CaptureClient)stream.service;
                    else { stream.Render = (RenderClient)stream.service; stream.Queue = new AudioFrameQueue(48000 * 8); stream.scratch = new byte[stream.Frames * 8]; }
                    return stream;
                }
                catch { stream.Dispose(); throw; }
                finally { if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format); AudioDevices.Release(device); }
            }
            internal void RenderSilence() { IntPtr data; AudioDevices.Check(Render.GetBuffer(Frames, out data)); AudioDevices.Check(Render.ReleaseBuffer(Frames, 2)); }
            internal void RenderQueued()
            {
                uint padding; AudioDevices.Check(Client.GetCurrentPadding(out padding));
                if (padding >= Frames) return; uint available = Frames - padding;
                int length = checked((int)available * 8); int read = Queue.Read(scratch, length); IntPtr destination;
                AudioDevices.Check(Render.GetBuffer(available, out destination));
                try { if (read > 0) Marshal.Copy(scratch, 0, destination, length); }
                finally { AudioDevices.Check(Render.ReleaseBuffer(available, read == 0 ? 2u : 0u)); }
            }
            public void Dispose()
            {
                if (Client != null) { try { Client.Stop(); } catch (COMException) { } }
                AudioDevices.Release(service); AudioDevices.Release(clientObject); service = clientObject = null; Client = null;
                if (Event != null) { Event.Dispose(); Event = null; }
            }
        }
        [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat { internal ushort Tag, Channels; internal uint Rate, Bytes; internal ushort Align, Bits, Extra; }
        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface AudioClient
        {
            [PreserveSig] int Initialize(int mode, uint flags, long duration, long period, IntPtr format, IntPtr session);
            [PreserveSig] int GetBufferSize(out uint frames); [PreserveSig] int GetStreamLatency(out long latency); [PreserveSig] int GetCurrentPadding(out uint padding);
            [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest); [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long normal, out long minimum); [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle); [PreserveSig] int GetService(ref Guid id, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }
        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface CaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong clock);
            [PreserveSig] int ReleaseBuffer(uint frames); [PreserveSig] int GetNextPacketSize(out uint frames);
        }
        [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface RenderClient
        { [PreserveSig] int GetBuffer(uint frames, out IntPtr data); [PreserveSig] int ReleaseBuffer(uint frames, uint flags); }
    }
}
