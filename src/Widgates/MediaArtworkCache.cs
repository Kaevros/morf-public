using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace Widgates
{
    // Used only by the serialized metadata worker. Keep one frozen image and its digest,
    // never the encoded bytes or a history of covers. Providers may change art on the same track.
    internal sealed class MediaArtworkCache
    {
        private byte[] digest;
        private BitmapSource image;

        internal BitmapSource Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > PreviewImage.MaximumBytes) return null;
            byte[] next;
            using (var hash = SHA256.Create()) next = hash.ComputeHash(bytes);
            if (image != null && digest.SequenceEqual(next)) return image;
            BitmapSource decoded;
            using (var stream = new MemoryStream(bytes, false)) decoded = PreviewImage.Decode(stream, 192);
            // A malformed or transient result must remain retryable.
            if (decoded != null) { digest = next; image = decoded; }
            return decoded;
        }
    }
}
