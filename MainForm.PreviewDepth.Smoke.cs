using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DownloadOrganizer
{
    public static class PreviewDepthSmoke
    {
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "PreviewDepthSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(temp, "watch");
                string root = Path.Combine(temp, "sorted");
                string child = Path.Combine(source, "첫 폴더");
                string grandchild = Path.Combine(child, "다음 폴더");
                Directory.CreateDirectory(grandchild);
                Directory.CreateDirectory(Path.Combine(root, "자료"));
                File.WriteAllText(Path.Combine(source, "바로 안.txt"), "root");
                File.WriteAllText(Path.Combine(child, "첫 단계.txt"), "child");
                File.WriteAllText(Path.Combine(grandchild, "더 깊은 파일.txt"), "grandchild");
                ScanResult top = Preview(source, root, source);
                if (top.Rows.Count != 2 || top.PreviewFiles != 1 || top.PreviewDirectories != 1 ||
                    !top.Rows.Any(row => row.DirectoryPath == child) ||
                    !top.Rows.Any(row => row.Name == "바로 안.txt") ||
                    top.Rows.Any(row => row.Name == "첫 단계.txt" || row.Name == "더 깊은 파일.txt"))
                    throw new Exception("최상위 한 단계 미리보기 실패");
                ScanResult next = Preview(source, root, child);
                if (next.Rows.Count != 2 || next.PreviewFiles != 1 || next.PreviewDirectories != 1 ||
                    !next.Rows.Any(row => row.DirectoryPath == grandchild) ||
                    !next.Rows.Any(row => row.Name == "첫 단계.txt") ||
                    next.Rows.Any(row => row.Name == "더 깊은 파일.txt"))
                    throw new Exception("하위 폴더 한 단계 미리보기 실패");
                Console.WriteLine("PASS: 미리보기는 현재 폴더 파일과 바로 아래 폴더만 표시");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
            finally
            {
                if (temp.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(temp)) Directory.Delete(temp, true);
            }
        }

        private static ScanResult Preview(string source, string root, string folder)
        {
            return MainForm.RunScan(source, root, "preview", null, new HashSet<string>(),
                new Dictionary<string, string>(), 0, null, folder);
        }
    }
}
