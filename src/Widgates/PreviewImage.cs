using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace Widgates
{
    internal static class PreviewImage
    {
        internal const int MaximumBytes = 16 * 1024 * 1024;
        internal const long MaximumPixels = 40000000;

        internal static BitmapSource Decode(Stream stream, int maximumEdge)
        {
            try
            {
                if (!stream.CanSeek || stream.Length == 0 || stream.Length > MaximumBytes)
                    return null;
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.None);
                if (decoder.Frames.Count == 0) return null;
                var frame = decoder.Frames[0];
                if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0
                    || (long)frame.PixelWidth * frame.PixelHeight > MaximumPixels)
                    return null;
                bool landscape = frame.PixelWidth >= frame.PixelHeight;
                stream.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (landscape) bitmap.DecodePixelWidth = Math.Min(maximumEdge, frame.PixelWidth);
                else bitmap.DecodePixelHeight = Math.Min(maximumEdge, frame.PixelHeight);
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (IOException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (ArgumentException) { return null; }
            catch (FormatException) { return null; }
            catch (System.Runtime.InteropServices.ExternalException) { return null; }
        }
    }
}
