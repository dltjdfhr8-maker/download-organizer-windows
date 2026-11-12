using System;
using System.IO;
using System.Linq;

namespace DownloadOrganizer
{
    public static class BatchUndoSmoke
    {
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "BatchUndoSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(temp, "source");
                string root = Path.Combine(temp, "sorted");
                string created = Path.Combine(root, "created");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(created);
                string original = Path.Combine(source, "inside.txt");
                string moved = Path.Combine(created, "inside.txt");
                File.WriteAllText(original, "one");
                File.Move(original, moved);
                FileInfo info = new FileInfo(moved);
                ChangeJournal journal = new ChangeJournal();
                ChangeRecord file = journal.AddFile(original, moved, info.Length,
                    info.LastWriteTimeUtc.Ticks, "now", "구조 편집", "group-one");
                ChangeRecord folder = new ChangeRecord {
                    Id = Guid.NewGuid().ToString("N"), GroupId = "group-one",
                    Kind = "폴더 생성", To = created, Current = created, When = "now"
                };
                journal.Records.Add(folder);
                int saves = 0;
                UndoBatchOutcome group = journal.UndoMany(new[] { folder, file }, () => saves++);
                if (group.Undone.Count != 2 || group.Failed.Count != 0 || saves != 2 ||
                    !File.Exists(original) || Directory.Exists(created))
                    throw new Exception("파일과 새 폴더의 의존 순서 되돌리기 실패");

                string first = Path.Combine(source, "first.txt");
                string second = Path.Combine(source, "second.txt");
                string firstMoved = Path.Combine(root, "first.txt");
                string secondMoved = Path.Combine(root, "second.txt");
                File.WriteAllText(first, "first"); File.WriteAllText(second, "second");
                File.Move(first, firstMoved); File.Move(second, secondMoved);
                ChangeRecord firstRecord = Record(journal, first, firstMoved, "");
                ChangeRecord secondRecord = Record(journal, second, secondMoved, "");
                File.WriteAllText(first, "conflict");
                UndoBatchOutcome partial = journal.UndoMany(new[] { firstRecord, secondRecord });
                if (partial.Undone.Count != 1 || partial.Failed.Count != 1 ||
                    !partial.Failed.ContainsKey(firstRecord) || !File.Exists(firstMoved) ||
                    !File.Exists(second) || File.Exists(secondMoved) || File.ReadAllText(first) != "conflict")
                    throw new Exception("충돌 항목 보호 또는 나머지 항목 되돌리기 실패");

                string a = Path.Combine(source, "A.txt");
                string b = Path.Combine(source, "B.txt");
                string stage = Path.Combine(source, "swap.stage");
                File.WriteAllText(a, "A"); File.WriteAllText(b, "B");
                File.Move(a, stage); File.Move(b, a); File.Move(stage, b);
                ChangeRecord renameA = Record(journal, a, b, "rename-group", "파일 이름 변경");
                ChangeRecord renameB = Record(journal, b, a, "rename-group", "파일 이름 변경");
                UndoBatchOutcome swapped = journal.UndoMany(new[] { renameA, renameB });
                if (swapped.Undone.Count != 2 || swapped.Failed.Count != 0 ||
                    File.ReadAllText(a) != "A" || File.ReadAllText(b) != "B")
                    throw new Exception("서로 바뀐 이름의 일괄 되돌리기 실패");
                Console.WriteLine("PASS: 일괄 되돌리기 의존 순서, 충돌 보호, 이름 교환");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
            finally
            {
                if (temp.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(temp)) Directory.Delete(temp, true);
            }
        }

        private static ChangeRecord Record(ChangeJournal journal, string from, string to,
            string groupId, string reason = "test")
        {
            FileInfo info = new FileInfo(to);
            return journal.AddFile(from, to, info.Length, info.LastWriteTimeUtc.Ticks,
                "now", reason, groupId);
        }
    }
}
