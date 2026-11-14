using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Widgates
{
    internal static class ApplicationCatalog
    {
        // Read only on explicit selection/refresh, never in a recurring timer.
        internal static string[] OpenProcessNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        string name = process.ProcessName; ApplicationRules valid;
                        if (process.MainWindowHandle != IntPtr.Zero && !name.Equals("WindowsWidgates", StringComparison.OrdinalIgnoreCase)
                            && ApplicationRules.TryParse(name + ".exe = gizle", out valid)) names.Add(name);
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (System.NotSupportedException) { }
                }
            }
            return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).Take(128).ToArray();
        }
    }
}
