using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Widgates
{
    internal static class MediaAppIcon
    {
        private static readonly Dictionary<string, Task<BitmapSource>> cache = new Dictionary<string, Task<BitmapSource>>(StringComparer.Ordinal);
        internal static string Identity(MediaApplication application)
        {
            return application == MediaApplication.AppleMusic ? "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App"
                : application == MediaApplication.Spotify ? "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" : "";
        }

        internal static Task<BitmapSource> GetAsync(string identity)
        {
            // Read only an AppsFolder identity, never an arbitrary path, URL or executable.
            if (string.IsNullOrEmpty(identity) || identity.Length > 256
                || !identity.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == '!'))
                return Task.FromResult<BitmapSource>(null);
            lock (cache)
            {
                Task<BitmapSource> pending;
                if (cache.TryGetValue(identity, out pending)) return pending;
                if (cache.Count >= 32) return Task.FromResult<BitmapSource>(null);
                pending = Task.Run(() => Read(identity));
                cache.Add(identity, pending);
                return pending;
            }
        }

        private static BitmapSource Read(string identity)
        {
            IShellItemImageFactory factory = null;
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName("shell:AppsFolder\\" + identity, IntPtr.Zero, ref iid, out factory) < 0 || factory == null) return null;
                // ICONONLY; extraction stays off the UI thread and the result is reused.
                if (factory.GetImage(new NativeSize { Width = 32, Height = 32 }, 4, out bitmap) < 0 || bitmap == IntPtr.Zero) return null;
                var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            catch (Exception error)
            {
                if (!(error is COMException) && !(error is ArgumentException) && !(error is InvalidOperationException)
                    && !(error is UnauthorizedAccessException) && !(error is System.Security.SecurityException)) throw;
                return null;
            }
            finally
            {
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (factory != null && Marshal.IsComObject(factory)) Marshal.FinalReleaseComObject(factory);
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { internal int Width, Height; }
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string name, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr item);
    }
}
