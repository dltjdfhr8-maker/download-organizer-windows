using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class BatchRenameUndoSmoke
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static ChangeJournal RenamedFiles(string folder, params string[] names)
        {
            PlanData plan = new PlanData();
            foreach (string name in names)
            {
                string original = Path.Combine(folder, name + ".txt");
                string renamed = Path.Combine(folder, "new_" + name + ".txt");
                File.WriteAllText(original, name);
                File.Move(original, renamed);
                plan.Files.Add(new PlannedFile { Source = original, Destination = renamed });
            }
            ChangeJournal journal = new ChangeJournal();
            journal.AddStructure(plan, "now");
            return journal;
        }

        [STAThread]
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(),
                "BatchRenameUndo-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                ChangeJournal journal = RenamedFiles(temp, "one", "two", "three");
                Check(journal.Records.Count == 3 && journal.Records.All(r =>
                    r.Reason == "파일 이름 변경"), "일괄 이름 변경 이력이 누락됐습니다.");
                ChangeRecord first = journal.Records[0];
                journal.Undo(first);
                Check(journal.PendingRenameGroup(first).Count == 2,
                    "한 파일만 되돌린 뒤 남은 파일 수가 잘못되었습니다.");
                List<ChangeRecord> undone = journal.UndoRenameGroup(first);
                Check(undone.Count == 2 && journal.Records.All(r => r.Undone) &&
                    new[] { "one", "two", "three" }.All(n =>
                        File.Exists(Path.Combine(temp, n + ".txt"))) &&
                    !Directory.GetFiles(temp, "new_*.txt").Any(),
                    "남은 이름을 한 번에 되돌리지 못했습니다.");
                Application.EnableVisualStyles();
                using (HistoryForm history = new HistoryForm(journal, delegate { }))
                {
                    history.Opacity = 0;
                    history.Show();
                    Application.DoEvents();
                    ListView list = (ListView)typeof(HistoryForm).GetField("list",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(history);
                    if (list.VirtualListSize != 3)
                        throw new Exception("되돌린 파일 이력 수가 잘못되었습니다.");
                    for (int i = 0; i < list.VirtualListSize; i++)
                    {
                        ListViewItem item = list.Items[i];
                        if (item.SubItems[2].Text.StartsWith("new_", StringComparison.Ordinal) ||
                            item.SubItems[5].Text != "되돌림")
                            throw new Exception("이력 목록에 원래 이름과 되돌림 상태가 표시되지 않습니다.");
                    }
                    history.Close();
                }

                journal = RenamedFiles(temp, "alpha", "beta");
                string blocker = Path.Combine(temp, "beta.txt");
                File.WriteAllText(blocker, "another file");
                bool refused = false;
                try { journal.UndoRenameGroup(journal.Records[0]); }
                catch (IOException) { refused = true; }
                Check(refused && journal.Records.All(r => !r.Undone) &&
                    File.Exists(Path.Combine(temp, "new_alpha.txt")) &&
                    File.Exists(Path.Combine(temp, "new_beta.txt")) &&
                    !Directory.GetFiles(temp, ".download-organizer-undo-*.stage").Any(),
                    "이름 충돌 때 일부 파일이 바뀌었습니다.");

                File.Delete(blocker);
                File.AppendAllText(Path.Combine(temp, "new_beta.txt"), "changed");
                refused = false;
                try { journal.UndoRenameGroup(journal.Records[0]); }
                catch (IOException) { refused = true; }
                Check(refused && journal.Records.All(r => !r.Undone),
                    "수정된 파일을 일괄 되돌렸습니다.");

                Console.WriteLine("PASS: 일부 되돌린 일괄 이름의 나머지 복구, 충돌·수정 방지");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
            finally
            {
                if (temp.StartsWith(Path.GetFullPath(Path.GetTempPath()),
                    StringComparison.OrdinalIgnoreCase) && Directory.Exists(temp))
                    Directory.Delete(temp, true);
            }
        }
    }
}
