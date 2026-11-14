using System;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace Widgates
{
    internal static class BrandAssets
    {
        internal const string ResourcePrefix = "Widgates.Brand.";
        internal static readonly BitmapSource HeaderLogo = Load("morf-64.png");
        internal static readonly BitmapSource WindowIcon = Load("morf-256.png");

        private static BitmapSource Load(string file)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + file))
            {
                if (stream == null) throw new InvalidOperationException("Missing bundled Morf asset: " + file);
                var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                bitmap.Freeze();
                return bitmap;
            }
        }
    }
}
