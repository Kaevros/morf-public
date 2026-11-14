using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Widgates
{
    internal static class ShelfThumbnail
    {
        private static readonly SemaphoreSlim DecodeSlots = new SemaphoreSlim(2);

        internal static async Task<ImageSource> LoadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png"
                && extension != ".bmp" && extension != ".gif" && extension != ".tif" && extension != ".tiff")
                return null;
            await DecodeSlots.WaitAsync();
            try
            {
                return await Task.Run<ImageSource>(() =>
                {
                    try
                    {
                        string localPath;
                        if (!LocalFileAccess.TryGetPath(path, out localPath)) return null;
                        // Do not hydrate cloud placeholders just to draw a shelf card.
                        const FileAttributes unavailable = FileAttributes.Offline | FileAttributes.ReparsePoint
                            | (FileAttributes)0x40000 | (FileAttributes)0x400000;
                        if ((File.GetAttributes(localPath) & unavailable) != 0)
                            return null;
                        using (var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete))
                        {
                            return PreviewImage.Decode(stream, 96);
                        }
                    }
                    catch (IOException) { return null; }
                    catch (UnauthorizedAccessException) { return null; }
                    catch (System.Security.SecurityException) { return null; }
                    catch (NotSupportedException) { return null; }
                    catch (ArgumentException) { return null; }
                    catch (FormatException) { return null; }
                    catch (System.Runtime.InteropServices.ExternalException) { return null; }
                });
            }
            finally { DecodeSlots.Release(); }
        }
    }
}
