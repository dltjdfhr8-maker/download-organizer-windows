using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class DesktopInstanceSmoke
    {
        public static int Main(string[] args)
        {
            try
            {
                string key = DesktopInstance.ScopeKey();
                if (args.Length > 0 && args[0] == "--child")
                {
                    Console.WriteLine(key);
                    return 0;
                }
                string desktop = DesktopInstance.DesktopName();
                if (desktop.EndsWith("\\") || key.Length != 16)
                    throw new Exception("desktop identity was not available: " + desktop);
                ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, "--child") {
                    UseShellExecute = false, RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (Process child = Process.Start(start))
                {
                    string childKey = child.StandardOutput.ReadToEnd().Trim();
                    child.WaitForExit();
                    if (child.ExitCode != 0 || childKey != key)
                        throw new Exception("same-desktop processes received different identities");
                }
                Console.WriteLine("PASS: same desktop shares one identity; desktop=" + desktop);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        }
    }
}
