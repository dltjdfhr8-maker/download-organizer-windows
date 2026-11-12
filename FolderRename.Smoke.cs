using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class FolderRenameSmoke
    {
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void ComboRootAndChildRename(string temp, string source, string data, bool childFirst)
        {
            string label = childFirst ? "하위 먼저" : "상위 먼저";
            string root = Path.Combine(temp, childFirst ? "combo-a" : "combo-b");
            string newRoot = root + "-renamed";
            string[] originalFiles = {
                Path.Combine(root, "기존 자료", "내용.txt"),
                Path.Combine(root, "보관", "보관.txt"),
                Path.Combine(root, "보관", "깊은", "깊은.txt")
            };
            foreach (string file in originalFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, Path.GetFileName(file));
            }
            PlanData historyPlan;
            using (StructureEditor editor = new StructureEditor(source, root, data))
            {
                editor.Opacity = 0;
                editor.Show();
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!editor.Loaded && DateTime.UtcNow < deadline)
                { Application.DoEvents(); Thread.Sleep(20); }
                if (!editor.Loaded) throw new Exception(label + ": 동시 변경 구조를 열지 못했습니다.");
                TreeView proposed = Field<TreeView>(editor, "proposed");
                string error;
                if (!editor.TryRenamePlannedFolder(proposed.Nodes[0], Path.GetFileName(newRoot), out error))
                    throw new Exception(label + ": 상위 폴더 이름 변경 실패: " + error);
                TreeNode child = proposed.Nodes[0].Nodes.Cast<TreeNode>().First(n => n.Text == "기존 자료");
                if (!editor.TryRenamePlannedFolder(child, "새 자료", out error))
                    throw new Exception(label + ": 하위 폴더 이름 변경 실패: " + error);
                TreeNode keep = proposed.Nodes[0].Nodes.Cast<TreeNode>().First(n => n.Text == "보관");
                TreeNode movingFile = keep.Nodes.Cast<TreeNode>().First(n => n.Text == "보관.txt");
                if (!editor.MovePlannedNode(movingFile, child))
                    throw new Exception(label + ": 파일을 다른 폴더로 옮기지 못했습니다.");
                PlanData plan = (PlanData)typeof(StructureEditor)
                    .GetMethod("ReadPlan", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(editor, null);
                StructureSnapshot snapshot = Field<StructureSnapshot>(editor, "snapshot");
                StructurePlanner.ApplyWithRootRename(root, source, newRoot, snapshot,
                    plan.Files, plan.Directories);
                historyPlan = (PlanData)typeof(StructureEditor)
                    .GetMethod("HistoryPlanWithRenamedRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(editor, new object[] { plan, newRoot });
                editor.Close();
            }
            if (Directory.Exists(root) ||
                !File.Exists(Path.Combine(newRoot, "새 자료", "내용.txt")) ||
                !File.Exists(Path.Combine(newRoot, "새 자료", "보관.txt")) ||
                !File.Exists(Path.Combine(newRoot, "보관", "깊은", "깊은.txt")))
                throw new Exception(label + ": 상위+하위 동시 변경 적용 실패");
            ChangeJournal journal = new ChangeJournal();
            journal.AddStructure(historyPlan, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            ChangeRecord rootChange = journal.Records.First(r =>
                r.Kind == "폴더 이동" && r.From == root && r.To == newRoot);
            ChangeRecord childChange = journal.Records.First(r =>
                r.Kind == "폴더 이동" && r.From == Path.Combine(root, "기존 자료"));
            ChangeRecord movedFile = journal.Records.First(r =>
                r.Kind == "파일 이동" && r.From == originalFiles[1]);
            ChangeRecord untouchedFile = journal.Records.First(r =>
                r.Kind == "파일 이동" && r.From == originalFiles[2]);
            bool refused = false;
            try { journal.Undo(untouchedFile); }
            catch (IOException) { refused = true; }
            if (!refused || untouchedFile.Undone || Directory.Exists(root))
                throw new Exception(label + ": 상위 폴더 이름만 바뀐 파일을 따로 되돌렸습니다.");
            if (!childFirst)
            {
                bool blocked = false;
                try { journal.Undo(rootChange); }
                catch (IOException) { blocked = true; }
                if (!blocked || rootChange.Undone || Directory.Exists(root))
                    throw new Exception(label + ": 하위 변경이 남았는데 상위 폴더를 먼저 되돌렸습니다.");
                journal.Undo(movedFile);
                if (Directory.Exists(root) || !File.Exists(Path.Combine(newRoot, "보관", "보관.txt")))
                    throw new Exception(label + ": 옮긴 파일이 현재 상위 폴더 안의 원래 폴더로 돌아오지 않았습니다.");
            }
            journal.Undo(childChange);
            if (Directory.Exists(root))
                throw new Exception(label + ": 하위 폴더를 되돌리면서 옛 상위 폴더를 따로 만들었습니다.");
            if (!File.Exists(Path.Combine(newRoot, "기존 자료", "내용.txt")))
                throw new Exception(label + ": 하위 폴더가 현재 상위 폴더 안에서 원래 이름으로 돌아오지 않았습니다.");
            journal.Undo(rootChange);
            if (childFirst) journal.Undo(movedFile);
            foreach (string file in originalFiles)
                if (!File.Exists(file)) throw new Exception(label + ": 원래 위치로 돌아오지 않은 파일: " + file);
            if (Directory.Exists(newRoot) || Directory.Exists(Path.Combine(root, "새 자료")))
                throw new Exception(label + ": 되돌린 뒤 바뀐 이름의 폴더가 남았습니다.");
            if (journal.Records.Any(r => !r.Undone))
                throw new Exception(label + ": 되돌린 뒤에도 적용됨으로 남은 이력이 있습니다.");
        }

        [STAThread]
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "FolderRenameSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(temp, "downloads");
                string root = Path.Combine(temp, "sorted");
                string original = Path.Combine(root, "기존 자료");
                string renamed = Path.Combine(root, "새 자료");
                string data = Path.Combine(temp, "settings");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(original);
                Directory.CreateDirectory(Path.Combine(root, "충돌"));
                Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(original, "내용.txt"), "sample");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (StructureEditor editor = new StructureEditor(source, root, data))
                {
                    editor.Opacity = 0;
                    editor.Show();
                    DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!editor.Loaded && DateTime.UtcNow < deadline)
                    { Application.DoEvents(); Thread.Sleep(20); }
                    if (!editor.Loaded) throw new Exception("구조 편집기를 열지 못했습니다.");
                    TreeView current = Field<TreeView>(editor, "current");
                    TreeView proposed = Field<TreeView>(editor, "proposed");
                    TreeNode left = current.Nodes[0].Nodes.Cast<TreeNode>().First(n => n.Text == "기존 자료");
                    TreeNode right = proposed.Nodes[0].Nodes.Cast<TreeNode>().First(n => n.Text == "기존 자료");
                    if (right.IsExpanded) throw new Exception("처음부터 모든 파일을 펼쳤습니다.");
                    string error;
                    if (editor.TryRenamePlannedFolder(right, "충돌", out error) || right.Text != "기존 자료")
                        throw new Exception("같은 위치의 이름 충돌을 허용했습니다.");
                    current.SelectedNode = left;
                    current.Focus();
                    Application.DoEvents();
                    using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        timer.Tick += delegate {
                            Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f =>
                                f.Text == "폴더 이름 변경" && f.Visible);
                            if (dialog == null) return;
                            TextBox input = dialog.Controls.OfType<TableLayoutPanel>().First()
                                .Controls.OfType<TextBox>().First();
                            input.Text = "새 자료";
                            ((Button)dialog.AcceptButton).PerformClick();
                            timer.Stop();
                        };
                        timer.Start();
                        MethodInfo processKey = typeof(StructureEditor).GetMethod("ProcessCmdKey",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        bool handled = (bool)processKey.Invoke(editor,
                            new object[] { new Message(), Keys.F2 });
                        timer.Stop();
                        if (!handled || right.Text != "새 자료")
                            throw new Exception("왼쪽에서 선택한 폴더의 F2 이름 변경 실패");
                    }
                    if (!Directory.Exists(original) || Directory.Exists(renamed))
                        throw new Exception("확인 전에 실제 폴더 이름이 변경되었습니다.");
                    MethodInfo readPlan = typeof(StructureEditor).GetMethod("ReadPlan",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    PlanData plan = (PlanData)readPlan.Invoke(editor, null);
                    StructureSnapshot snapshot = Field<StructureSnapshot>(editor, "snapshot");
                    StructurePlanner.Apply(root, source, snapshot, plan.Files, plan.Directories);
                    if (Directory.Exists(original) || !File.Exists(Path.Combine(renamed, "내용.txt")))
                        throw new Exception("확인 후 폴더 이름 변경이 적용되지 않았습니다.");
                    ChangeJournal journal = new ChangeJournal();
                    journal.AddStructure(plan, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    ChangeRecord folderChange = journal.Records.FirstOrDefault(r =>
                        r.Kind == "폴더 이동" && r.From == original && r.To == renamed);
                    if (folderChange == null)
                        throw new Exception("폴더 이름 변경 이력이 기록되지 않았습니다.");
                    journal.Undo(folderChange);
                    if (!File.Exists(Path.Combine(original, "내용.txt")) || Directory.Exists(renamed))
                        throw new Exception("폴더 이름 변경을 되돌리지 못했습니다.");
                    editor.Close();
                }
                string newRoot = Path.Combine(temp, "sorted-renamed");
                using (StructureEditor editor = new StructureEditor(source, root, data))
                {
                    editor.Opacity = 0;
                    editor.Show();
                    DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!editor.Loaded && DateTime.UtcNow < deadline)
                    { Application.DoEvents(); Thread.Sleep(20); }
                    if (!editor.Loaded) throw new Exception("상위 폴더 구조를 열지 못했습니다.");
                    TreeView current = Field<TreeView>(editor, "current");
                    TreeView proposed = Field<TreeView>(editor, "proposed");
                    TreeNode rootNode = current.Nodes[0];
                    current.SelectedNode = current.Nodes[1];
                    current.Focus();
                    rootNode.EnsureVisible();
                    Application.DoEvents();
                    MouseEventArgs clickCount = new MouseEventArgs(MouseButtons.Left, 1,
                        rootNode.Bounds.Right + 12,
                        rootNode.Bounds.Top + rootNode.Bounds.Height / 2, 0);
                    if (current.HitTest(clickCount.Location).Node != rootNode)
                        throw new Exception("폴더 파일 수 영역을 찾지 못했습니다.");
                    typeof(StructureEditor).GetMethod("HandleFileMouseDown",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(editor, new object[] { current, clickCount });
                    if (current.SelectedNode != rootNode)
                        throw new Exception("파일 수 영역 클릭으로 폴더를 선택하지 못했습니다.");
                    using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        timer.Tick += delegate {
                            Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f =>
                                f.Text == "폴더 이름 변경" && f.Visible);
                            if (dialog == null) return;
                            TextBox input = dialog.Controls.OfType<TableLayoutPanel>().First()
                                .Controls.OfType<TextBox>().First();
                            input.Text = "sorted-renamed";
                            ((Button)dialog.AcceptButton).PerformClick();
                            timer.Stop();
                        };
                        timer.Start();
                        Field<Button>(editor, "renameButton").PerformClick();
                        timer.Stop();
                    }
                    if (proposed.Nodes[0].Text != "sorted-renamed")
                        throw new Exception("상위 폴더 이름 변경 버튼이 작동하지 않았습니다.");
                    MethodInfo readPlan = typeof(StructureEditor).GetMethod("ReadPlan",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    PlanData plan = (PlanData)readPlan.Invoke(editor, null);
                    StructureSnapshot snapshot = Field<StructureSnapshot>(editor, "snapshot");
                    StructurePlanner.ApplyWithRootRename(root, source, newRoot, snapshot,
                        plan.Files, plan.Directories);
                    if (Directory.Exists(root) || !File.Exists(Path.Combine(newRoot, "기존 자료", "내용.txt")))
                        throw new Exception("상위 폴더 이름 변경 적용 실패");
                    PlanData historyPlan = (PlanData)typeof(StructureEditor)
                        .GetMethod("HistoryPlanWithRenamedRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(editor, new object[] { plan, newRoot });
                    ChangeJournal journal = new ChangeJournal();
                    journal.AddStructure(historyPlan, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    ChangeRecord rootChange = journal.Records.FirstOrDefault(r =>
                        r.Kind == "폴더 이동" && r.From == root && r.To == newRoot);
                    if (rootChange == null) throw new Exception("상위 폴더 이름 변경 이력이 없습니다.");
                    journal.Undo(rootChange);
                    if (!File.Exists(Path.Combine(root, "기존 자료", "내용.txt")) || Directory.Exists(newRoot))
                        throw new Exception("상위 폴더 이름 변경 되돌리기 실패");
                    editor.Close();
                }
                string sameRootNew = Path.Combine(temp, "sorted-same-folder");
                using (StructureEditor editor = new StructureEditor(root, root, data))
                {
                    editor.Opacity = 0;
                    editor.Show();
                    DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!editor.Loaded && DateTime.UtcNow < deadline)
                    { Application.DoEvents(); Thread.Sleep(20); }
                    if (!editor.Loaded) throw new Exception("같은 폴더 구조를 열지 못했습니다.");
                    TreeView proposed = Field<TreeView>(editor, "proposed");
                    string error;
                    if (!editor.TryRenamePlannedFolder(proposed.Nodes[0], "sorted-same-folder", out error))
                        throw new Exception("같은 폴더 방식의 상위 폴더 이름 변경 실패: " + error);
                    PlanData plan = (PlanData)typeof(StructureEditor)
                        .GetMethod("ReadPlan", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(editor, null);
                    StructureSnapshot snapshot = Field<StructureSnapshot>(editor, "snapshot");
                    StructurePlanner.ApplyWithRootRename(root, root, sameRootNew, snapshot,
                        plan.Files, plan.Directories);
                    if (!File.Exists(Path.Combine(sameRootNew, "기존 자료", "내용.txt")))
                        throw new Exception("같은 폴더 방식의 상위 폴더 적용 실패");
                    PlanData historyPlan = (PlanData)typeof(StructureEditor)
                        .GetMethod("HistoryPlanWithRenamedRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(editor, new object[] { plan, sameRootNew });
                    ChangeJournal journal = new ChangeJournal();
                    journal.AddStructure(historyPlan, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    ChangeRecord rootChange = journal.Records.First(r =>
                        r.Kind == "폴더 이동" && r.From == root && r.To == sameRootNew);
                    journal.Undo(rootChange);
                    if (!File.Exists(Path.Combine(root, "기존 자료", "내용.txt")) ||
                        Directory.Exists(sameRootNew))
                        throw new Exception("같은 폴더 방식의 상위 폴더 되돌리기 실패");
                    editor.Close();
                }
                ComboRootAndChildRename(temp, source, data, true);
                ComboRootAndChildRename(temp, source, data, false);
                Console.WriteLine("PASS: 하위·상위 폴더 이름 변경, 파일 수 클릭, 적용·되돌리기, 같은 폴더 방식, 상위+하위 동시 변경 되돌리기");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
            finally
            {
                if (temp.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(temp)) Directory.Delete(temp, true);
            }
        }
    }
}
