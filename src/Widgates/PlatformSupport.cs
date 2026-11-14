using System;

namespace Widgates
{
    internal static class PlatformSupport
    {
        internal const int MinimumBuild = 17763;
        internal static bool IsSupported(Version version)
        {
            return version.Major > 10 || (version.Major == 10 && version.Build >= MinimumBuild);
        }
    }
}
