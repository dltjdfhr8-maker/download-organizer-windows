using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    public static class SingleFolderSmoke
    {
        [STAThread]
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "SingleFolderSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string root = Path.Combine(temp, "one-folder");
                string data = Path.Combine(temp, "settings");
                Directory.CreateDirectory(Path.Combine(root, "예산"));
                Directory.CreateDirectory(Path.Combine(root, "연수", "자료"));
                Directory.CreateDirectory(data);
                string incoming = Path.Combine(root, "예산_결산.txt");
                string held = Path.Combine(root, "무관한파일.txt");
                string nested = Path.Combine(root, "연수", "자료", "기존자료.txt");
                string temporary = Path.Combine(root, "downloading.crdownload");
                File.WriteAllText(incoming, "budget");
                File.WriteAllText(held, "hold");
                File.WriteAllText(nested, "old");
                File.WriteAllText(temporary, "partial");
                File.SetLastWriteTimeUtc(incoming, DateTime.UtcNow.AddSeconds(-10));
                File.SetLastWriteTimeUtc(held, DateTime.UtcNow.AddSeconds(-10));
                if (Engine.Validate(root, root) != "") throw new Exception("같은 폴더 선택이 차단됨");
                if (Engine.Validate(root, Path.Combine(root, "예산")) == "")
                    throw new Exception("위험한 포함 관계가 허용됨");

                using (StructureEditor editor = new StructureEditor(root, root, data))
                {
                    editor.ShowInTaskbar = false;
                    editor.Opacity = 0;
                    editor.Show();
                    DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!editor.Loaded && DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(20);
                    }
                    if (!editor.Loaded) throw new Exception("구조 편집기가 열리지 않음");
                    TreeView current = (TreeView)typeof(StructureEditor).GetField("current",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
                    TreeView proposed = (TreeView)typeof(StructureEditor).GetField("proposed",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
                    TreeNode planRoot = proposed.Nodes[0], hold = proposed.Nodes[1];
                    TreeNode budget = Find(planRoot, "예산");
                    TreeNode training = Find(planRoot, "연수");
                    TreeNode budgetFile = Find(budget, "예산_결산.txt");
                    TreeNode heldFile = Find(hold, "무관한파일.txt");
                    if (Find(current.Nodes[1], "예산_결산.txt") == null ||
                        Find(current.Nodes[0], "예산_결산.txt") != null ||
                        budgetFile == null || heldFile == null || training == null)
                        throw new Exception("최상위 파일이 누락되거나 중복 표시됨");
                    if (!editor.MovePlannedNode(training, budget))
                        throw new Exception("하위 폴더 재배치 실패");
                    PlanData plan = (PlanData)typeof(StructureEditor).GetMethod("ReadPlan",
                        BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, null);
                    StructureSnapshot snapshot = (StructureSnapshot)typeof(StructureEditor).GetField("snapshot",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
                    if (plan.Files.Count(f => f.Source == incoming) != 1)
                        throw new Exception("최상위 파일이 계획에 중복 포함됨");
                    ApplySummary summary = StructurePlanner.Apply(root, root, snapshot,
                        plan.Files, plan.Directories);
                    if (summary.FilesMoved != 2 ||
                        !File.Exists(Path.Combine(root, "예산", "예산_결산.txt")) ||
                        !File.Exists(Path.Combine(root, "예산", "연수", "자료", "기존자료.txt")) ||
                        !File.Exists(held) || !File.Exists(temporary) ||
                        Directory.Exists(Path.Combine(root, "연수")))
                        throw new Exception("같은 폴더의 파일과 하위 폴더 적용 실패");
                }
                string wrongFolder = Path.Combine(root, "기타");
                Directory.CreateDirectory(wrongFolder);
                string wronglyPlaced = Path.Combine(wrongFolder, "예산_다음해.txt");
                File.WriteAllText(wronglyPlaced, "wrong folder");
                File.SetLastWriteTimeUtc(wronglyPlaced, DateTime.UtcNow.AddSeconds(-10));
                MethodInfo scan = typeof(MainForm).GetMethod("RunScan",
                    BindingFlags.NonPublic | BindingFlags.Static);
                ScanResult batch = (ScanResult)scan.Invoke(null, new object[] {
                    root, root, "batch", null, new HashSet<string>(),
                    new Dictionary<string, string>(), 0L, null
                });
                string relocated = Path.Combine(root, "예산", "예산_다음해.txt");
                if (batch.Moved != 1 || !File.Exists(relocated) || File.Exists(wronglyPlaced) ||
                    File.Exists(Path.Combine(root, "예산", "예산_결산 (2).txt")) ||
                    !Engine.SourceFiles(root, true).Any(f => f.FullName == relocated) ||
                    Engine.SourceFiles(root, true).Any(f => f.FullName == temporary))
                    throw new Exception("하위 폴더 파일의 자동 분류 실패");
                Dictionary<string, string> observations = new Dictionary<string, string>();
                HashSet<string> seen = new HashSet<string>(batch.SeenAdds);
                scan.Invoke(null, new object[] { root, root, "automatic", null, seen,
                    observations, 0L, null });
                ScanResult repeated = (ScanResult)scan.Invoke(null, new object[] { root, root,
                    "automatic", null, seen, observations, 0L, null });
                if (repeated.Moved != 0 || !File.Exists(relocated))
                    throw new Exception("이미 분류한 파일을 다시 이동함");
                Console.WriteLine("PASS: 한 폴더 구조 편집, 하위 파일 자동 분류, 중복 이동 방지");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
            finally
            {
                string safeRoot = Path.GetFullPath(Path.GetTempPath());
                if (temp.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(temp))
                    Directory.Delete(temp, true);
            }
        }

        private static TreeNode Find(TreeNode parent, string name)
        {
            return parent == null ? null : parent.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == name);
        }
    }
}
