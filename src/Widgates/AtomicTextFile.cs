using System;
using System.IO;
using System.Text;
using System.Security;

namespace Widgates
{
    internal static class AtomicTextFile
    {
        internal static string Read(string path, int maximum, Func<string, bool> valid)
        {
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    using (var reader = File.OpenText(candidate))
                    {
                        var buffer = new char[maximum + 1]; int count = reader.ReadBlock(buffer, 0, buffer.Length);
                        if (count > 0 && count <= maximum)
                        {
                            string text = new string(buffer, 0, count);
                            if (valid == null || valid(text)) return text;
                        }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (SecurityException) { }
            }
            return null;
        }

        internal static bool Write(string path, string text)
        {
            string temporary = null;
            try
            {
                path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
                temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
                else File.Move(temporary, path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
            finally { if (temporary != null) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (SecurityException) { } } }
        }
    }
}
