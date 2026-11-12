using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace DownloadOrganizer
{
    internal static class AsyncSmoke
    {
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        private static ScanResult Scan(string source, string root, string mode,
            List<FolderChoice> folders, HashSet<string> seen,
            Dictionary<string, string> observations, long startedAt, Func<bool> canceled = null)
        {
            MethodInfo method = typeof(MainForm).GetMethod("RunScan",
                BindingFlags.NonPublic | BindingFlags.Static);
            return (ScanResult)method.Invoke(null, new object[] {
                source, root, mode, folders, seen, observations, startedAt, canceled
            });
        }

        public static int Main()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "DownloadOrganizerAsync-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(testRoot, "downloads");
                string root = Path.Combine(testRoot, "sorted");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(Path.Combine(root, "예산"));
                Directory.CreateDirectory(Path.Combine(root, "회의록"));
                File.WriteAllText(Path.Combine(root, "예산", "예산_기존.txt"), "sample");
                File.WriteAllText(Path.Combine(root, "회의록", "회의록_기존.txt"), "sample");
                string nested = Path.Combine(source, "새로 받은 묶음");
                Directory.CreateDirectory(nested);
                string[] names = { "예산_첫째.txt", "회의록_둘째.txt", "예산_셋째.txt", "회의록_넷째.txt" };
                foreach (string name in names)
                {
                    string path = Path.Combine(source, name);
                    File.WriteAllText(path, name);
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));
                }
                string nestedFile = Path.Combine(nested, "예산_다섯째.txt");
                File.WriteAllText(nestedFile, "nested");
                File.SetLastWriteTimeUtc(nestedFile, DateTime.UtcNow.AddSeconds(-10));
                ScanResult preview = Scan(source, root, "preview", null, seen: new HashSet<string>(),
                    observations: new Dictionary<string, string>(), startedAt: 0);
                Check(preview.Rows.Any(row => row.Name == Path.Combine("새로 받은 묶음", "예산_다섯째.txt")),
                    "nested download was missing from preview");
                var seen = new HashSet<string>();
                var observations = new Dictionary<string, string>();
                ScanResult first = Scan(source, root, "automatic", null, seen, observations, 0);
                Check(first.Moved == 0 && first.Moves.Count == 0, "first observation moved files");
                ScanResult second = Scan(source, root, "automatic", first.Folders,
                    seen, first.Observations, 0);
                Check(second.Moved == names.Length + 1 && second.Failed == 0 &&
                    second.SeenAdds.Count == 0, "rapid downloads were not all moved");
                Check(Directory.GetFiles(source, "*.txt", SearchOption.AllDirectories).Length == 0 &&
                    Directory.GetFiles(root, "*.txt", SearchOption.AllDirectories).Length == names.Length + 3,
                    "download destinations are incomplete");
                ScanResult third = Scan(source, root, "automatic", second.Folders,
                    new HashSet<string>(), second.Observations, 0);
                Check(third.Moved == 0, "files moved twice");
                string uncertain = Path.Combine(source, "관계없는_항목.txt");
                File.WriteAllText(uncertain, "uncertain");
                File.SetLastWriteTimeUtc(uncertain, DateTime.UtcNow.AddSeconds(-10));
                ScanResult fourth = Scan(source, root, "automatic", second.Folders,
                    new HashSet<string>(), third.Observations, 0);
                ScanResult fifth = Scan(source, root, "automatic", second.Folders,
                    new HashSet<string>(), fourth.Observations, 0);
                Check(fifth.Review == 1 && fifth.RetainedSeen.Count == 1,
                    "uncertain file was not retained for review");
                ScanResult sixth = Scan(source, root, "automatic", second.Folders,
                    fifth.RetainedSeen, fifth.Observations, 0);
                Check(sixth.Review == 0 && sixth.RetainedSeen.Count == 1,
                    "uncertain file was repeatedly processed");
                string canceledFile = Path.Combine(source, "예산_취소.txt");
                File.WriteAllText(canceledFile, "cancel");
                File.SetLastWriteTimeUtc(canceledFile, DateTime.UtcNow.AddSeconds(-10));
                FileInfo canceledInfo = new FileInfo(canceledFile);
                var readyObserved = new Dictionary<string, string>(sixth.Observations);
                readyObserved[Engine.Fingerprint(canceledInfo)] =
                    canceledInfo.Length + "|" + canceledInfo.LastWriteTimeUtc.Ticks;
                ScanResult stopped = Scan(source, root, "automatic", second.Folders,
                    sixth.RetainedSeen, readyObserved, 0, delegate { return true; });
                Check(stopped.Moved == 0 && File.Exists(canceledFile), "canceled scan still moved a file");
                Console.WriteLine("PASS: rapid files, no duplicates, uncertain-file retention, cancellation");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
            finally
            {
                if (testRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }
    }
}
