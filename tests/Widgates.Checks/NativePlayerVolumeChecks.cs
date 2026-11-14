using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Widgates
{
    internal static class NativePlayerVolumeChecks
    {
        internal static async Task RunAsync(string output)
        {
            Console.WriteLine("OS " + Environment.OSVersion.VersionString + "; native Core Audio using this test process's silent waveOut stream; no player commands");
            await PlayerVolumeChecks.RunAsync();
            using (var stream = new SilentStream())
            {
                await Task.Delay(200);
                string identity;
                using (var process = Process.GetCurrentProcess()) identity = Path.GetFileName(process.MainModule.FileName);
                var system = new SystemVolume(); var before = await system.ReadAsync();
                var player = new SystemVolume(() => new WindowsPlayerVolumeEndpoint(identity));
                var original = await player.ReadAsync();
                Check(original.Available, "The native session enumerator finds this test process's silent audio session");
                try
                {
                    var state = await player.SetLevelAsync(0.37f);
                    Check(state.Available && Math.Abs(state.Level - 0.37f) < 0.01 && !state.Muted, "Player volume changes only the test process session to 37 percent");
                    state = await player.ToggleMuteAsync();
                    Check(state.Available && state.Muted, "Player mute changes the selected native audio session");
                    var unknown = new SystemVolume(() => new WindowsPlayerVolumeEndpoint("Morf.Does.Not.Exist!Player"));
                    state = await unknown.SetLevelAsync(0.1f);
                    Check(!state.Available, "An unknown native player is unavailable without a system fallback");
                    var after = await system.ReadAsync();
                    Check(before.Available && after.Available && before.Muted == after.Muted && Math.Abs(before.Level - after.Level) < 0.001,
                        "Native player volume and mute leave the master system level unchanged");
                }
                finally
                {
                    // The Inbox compiler predates await-in-finally. Clean up this test's
                    // own session on an MTA worker before releasing the silent stream.
                    Task.Run(() => { using (var endpoint = new WindowsPlayerVolumeEndpoint(identity)) { endpoint.SetLevel(original.Level); endpoint.SetMute(original.Muted); } }).GetAwaiter().GetResult();
                }
            }
        }
        private static void Check(bool success, string text) { if (!success) throw new InvalidOperationException(text); Console.WriteLine("PASS " + text); }
        internal sealed class SilentStream : IDisposable
        {
            private IntPtr handle, data, header;
            private bool prepared;
            internal SilentStream()
            {
                try
                {
                    var format = new WaveFormat { Format = 1, Channels = 1, SampleRate = 48000, BytesPerSecond = 96000, BlockAlign = 2, Bits = 16 };
                    Require(waveOutOpen(out handle, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
                    byte[] silence = new byte[96000]; data = Marshal.AllocHGlobal(silence.Length); Marshal.Copy(silence, 0, data, silence.Length);
                    header = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHeader)));
                    Marshal.StructureToPtr(new WaveHeader { Data = data, Length = (uint)silence.Length, Flags = 4 | 8, Loops = 30 }, header, false);
                    Require(waveOutPrepareHeader(handle, header, (uint)Marshal.SizeOf(typeof(WaveHeader)))); prepared = true;
                    Require(waveOutWrite(handle, header, (uint)Marshal.SizeOf(typeof(WaveHeader))));
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (handle != IntPtr.Zero) { waveOutReset(handle); if (prepared) waveOutUnprepareHeader(handle, header, (uint)Marshal.SizeOf(typeof(WaveHeader))); waveOutClose(handle); handle = IntPtr.Zero; }
                if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
                if (data != IntPtr.Zero) { Marshal.FreeHGlobal(data); data = IntPtr.Zero; }
            }
            private static void Require(uint result) { if (result != 0) throw new InvalidOperationException("Silent test audio stream unavailable: " + result); }
            [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat { public ushort Format, Channels; public uint SampleRate, BytesPerSecond; public ushort BlockAlign, Bits, Extra; }
            [StructLayout(LayoutKind.Sequential)] private struct WaveHeader { public IntPtr Data; public uint Length, Recorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }
            [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
            [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
            [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
            [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr handle);
            [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
            [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr handle);
        }
    }
}
