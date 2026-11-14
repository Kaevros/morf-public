using System;
using System.IO;
using System.Security;

namespace Widgates
{
    internal static class LocalFileAccess
    {
        internal static bool TryGetPath(string path, out string localPath)
        {
            localPath = null;
            // Reject UNC, device paths, drive-relative paths, URLs and alternate streams
            // before calling any filesystem API that might contact a remote provider.
            if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || path.Length > 32767
                || !((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
                || path[1] != ':' || (path[2] != '\\' && path[2] != '/')
                || path.IndexOf(':', 2) >= 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0
                || path.IndexOfAny(new[] { '*', '?' }) >= 0)
                return false;
            try
            {
                string normalized = Path.GetFullPath(path).Replace('/', '\\');
                string root = Path.GetPathRoot(normalized);
                var type = new DriveInfo(root).DriveType;
                if (type != DriveType.Fixed && type != DriveType.Removable
                    && type != DriveType.CDRom && type != DriveType.Ram)
                    return false;
                // Inspect ancestors from the root down, without traversing a junction
                // or symbolic link to find out what is on its other side.
                string current = root;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                foreach (string part in normalized.Substring(root.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, part);
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                }
                localPath = normalized;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }
    }
}
