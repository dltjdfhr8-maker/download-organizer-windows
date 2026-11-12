using System;
using System.IO;
using System.Linq;

namespace DownloadOrganizer
{
    public static class MultiMoveSmoke
    {
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "MultiMoveSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(temp, "source");
                string target = Path.Combine(temp, "target");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(target);
                string first = Path.Combine(source, "보도자료_1.md");
                string second = Path.Combine(source, "보도자료_2.md");
                string existing = Path.Combine(target, "보도자료_1.md");
                File.WriteAllText(first, "first");
                File.WriteAllText(second, "second");
                File.WriteAllText(existing, "existing");
                File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddSeconds(-10));
                File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddSeconds(-10));
                SelectedMoveResult result = MainForm.MoveChosenFiles(new[] { first, second, first }, target);
                if (result.Moves.Count != 2 || result.Failed != 0 || result.Waiting != 0 ||
                    File.Exists(first) || File.Exists(second) || File.ReadAllText(existing) != "existing" ||
                    File.ReadAllText(Path.Combine(target, "보도자료_1 (2).md")) != "first" ||
                    File.ReadAllText(Path.Combine(target, "보도자료_2.md")) != "second" ||
                    result.Moves.Select(move => move.Source).Distinct().Count() != 2)
                    throw new Exception("여러 파일 이동 또는 이름 충돌 보호 실패");
                Console.WriteLine("PASS: 선택한 여러 파일을 한 폴더로 이동, 기존 파일 보호");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
            finally
            {
                if (temp.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(temp)) Directory.Delete(temp, true);
            }
        }
    }
}
