using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DownloadOrganizer
{
    public static class ClassificationConsistencySmoke
    {
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "ClassificationConsistency-" + Guid.NewGuid().ToString("N"));
            try
            {
                CheckCase(temp, "news-md", "보도자료", "행정안전부_보도자료",
                    "보도자료_130870.md", "보도자료_130871.md");
                CheckCase(temp, "news-date-prefix", "보도자료", "행정안전부_보도자료",
                    "보도자료_130870.md", "2026_보도자료_130871.md");
                CheckCase(temp, "contract-pdf", "계약서", "계약서_검토",
                    "계약서-2025.pdf", "계약서-2026.pdf");
                CheckCase(temp, "training-xlsx", "교육자료", "교육자료_교사용",
                    "교육자료_2025.xlsx", "교육자료_2026.xlsx");
                CheckCase(temp, "report-txt", "보고서", "보고서_초안",
                    "보고서(129).txt", "보고서(130).txt");
                CheckCase(temp, "specific-folder", "행정안전부_보도자료", "보도자료",
                    "행정안전부_보도자료_130870.md", "2026_행정안전부_보도자료_130871.md");
                CheckDuplicateNames(temp);
                CheckStaleCache(temp);
                CheckUnrelatedName(temp);
                CheckAlreadySorted(temp);
                Console.WriteLine("PASS: 확장자·이름 순서·새 폴더 반영, 자동·일괄 일치, 애매한 이름 보류");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
            finally
            {
                if (temp.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(temp)) Directory.Delete(temp, true);
            }
        }

        private static void CheckCase(string temp, string label, string exactName,
            string similarName, string sampleName, string incomingName)
        {
            foreach (string mode in new[] { "automatic", "batch" })
            {
                string caseRoot = Path.Combine(temp, label + "-" + mode);
                string source = Path.Combine(caseRoot, "downloads");
                string root = Path.Combine(caseRoot, "sorted");
                string exact = Path.Combine(root, exactName);
                string similar = Path.Combine(root, similarName);
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(exact);
                Directory.CreateDirectory(similar);
                File.WriteAllText(Path.Combine(similar, sampleName), "older example");
                string incoming = Path.Combine(source, incomingName);
                File.WriteAllText(incoming, "incoming");
                File.SetLastWriteTimeUtc(incoming, DateTime.UtcNow.AddSeconds(-10));
                FileInfo info = new FileInfo(incoming);
                Suggestion suggestion = Engine.Suggest(info, Engine.Folders(root));
                if (!suggestion.Certain || !suggestion.Folder.Equals(exact, StringComparison.OrdinalIgnoreCase))
                    throw new Exception(label + ": 정확한 폴더명을 우선하지 못함 (" + mode + ", " +
                        suggestion.Reason + ", score=" + suggestion.Score.ToString("F2") + ")");
                Dictionary<string, string> observed = new Dictionary<string, string>();
                observed[Engine.Fingerprint(info)] = info.Length + "|" + info.LastWriteTimeUtc.Ticks;
                ScanResult result = MainForm.RunScan(source, root, mode, null, new HashSet<string>(),
                    observed, 0);
                if (result.Moved != 1 || result.Failed != 0 || !File.Exists(Path.Combine(exact, incomingName)) ||
                    File.Exists(incoming))
                    throw new Exception(label + ": " + mode + " 모드의 이동 결과가 다름");
            }
        }

        private static void CheckDuplicateNames(string temp)
        {
            string caseRoot = Path.Combine(temp, "duplicate-leaf");
            string source = Path.Combine(caseRoot, "downloads");
            string root = Path.Combine(caseRoot, "sorted");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(root, "A", "보도자료"));
            Directory.CreateDirectory(Path.Combine(root, "B", "보도자료"));
            string incoming = Path.Combine(source, "보도자료_130871.md");
            File.WriteAllText(incoming, "incoming");
            Suggestion suggestion = Engine.Suggest(new FileInfo(incoming), Engine.Folders(root));
            if (suggestion.Certain) throw new Exception("같은 이름의 폴더가 둘인데 자동 이동 후보로 확정됨");
            if (!suggestion.Reason.Contains("같은 핵심 이름의 폴더가 여러 곳"))
                throw new Exception("중복 폴더명을 보류한 이유가 표시되지 않음");
        }

        private static void CheckStaleCache(string temp)
        {
            string caseRoot = Path.Combine(temp, "stale-cache");
            string source = Path.Combine(caseRoot, "downloads");
            string root = Path.Combine(caseRoot, "sorted");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(root, "행정안전부_보도자료"));
            List<FolderChoice> oldIndex = Engine.Folders(root);
            string exact = Path.Combine(root, "보도자료");
            Directory.CreateDirectory(exact);
            string incoming = Path.Combine(source, "보도자료_130871.md");
            File.WriteAllText(incoming, "incoming");
            File.SetLastWriteTimeUtc(incoming, DateTime.UtcNow.AddSeconds(-10));
            FileInfo info = new FileInfo(incoming);
            Dictionary<string, string> observed = new Dictionary<string, string>();
            observed[Engine.Fingerprint(info)] = info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            ScanResult result = MainForm.RunScan(source, root, "automatic", oldIndex,
                new HashSet<string>(), observed, 0);
            if (result.Moved != 1 || !File.Exists(Path.Combine(exact, info.Name)))
                throw new Exception("자동 이동이 오래된 폴더 목록을 사용함");
        }

        private static void CheckUnrelatedName(string temp)
        {
            string caseRoot = Path.Combine(temp, "unrelated");
            string source = Path.Combine(caseRoot, "downloads");
            string root = Path.Combine(caseRoot, "sorted");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(root, "보도자료"));
            Directory.CreateDirectory(Path.Combine(root, "행정안전부_보도자료"));
            string incoming = Path.Combine(source, "회의안내_2026.pdf");
            File.WriteAllText(incoming, "incoming");
            Suggestion suggestion = Engine.Suggest(new FileInfo(incoming), Engine.Folders(root));
            if (suggestion.Certain) throw new Exception("관련 없는 파일이 자동 이동 후보로 확정됨");
        }

        private static void CheckAlreadySorted(string temp)
        {
            string root = Path.Combine(temp, "already-sorted");
            string exact = Path.Combine(root, "보도자료");
            Directory.CreateDirectory(exact);
            Directory.CreateDirectory(Path.Combine(root, "행정안전부_보도자료"));
            string path = Path.Combine(exact, "보도자료_130871.md");
            File.WriteAllText(path, "existing");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));
            ScanResult result = MainForm.RunScan(root, root, "batch", null,
                new HashSet<string>(), new Dictionary<string, string>(), 0);
            if (result.Moved != 0 || !File.Exists(path))
                throw new Exception("이미 정리된 파일을 다른 폴더로 옮김");
        }
    }
}
