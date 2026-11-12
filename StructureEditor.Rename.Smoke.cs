using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class RenameSmoke
    {
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static TreeNode Find(TreeNode parent, string name)
        {
            foreach (TreeNode child in parent.Nodes)
            {
                if (child.Text == name) return child;
                TreeNode found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }

        [STAThread]
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "RenameSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(temp, "downloads");
                string root = Path.Combine(temp, "sorted");
                string reports = Path.Combine(root, "reports");
                string data = Path.Combine(temp, "settings");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(reports);
                Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(reports, "first.txt"), "first");
                File.WriteAllText(Path.Combine(reports, "second.txt"), "second");
                File.WriteAllText(Path.Combine(source, "IMG_001.png"), "image");
                File.SetLastWriteTimeUtc(Path.Combine(source, "IMG_001.png"),
                    DateTime.UtcNow.AddSeconds(-10));
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
                    TreeView proposed = Field<TreeView>(editor, "proposed");
                    TreeNode folder = Find(proposed.Nodes[0], "reports");
                    TreeNode first = Find(folder, "first.txt");
                    TreeNode second = Find(folder, "second.txt");
                    TreeNode image = Find(proposed.Nodes[1], "IMG_001.png");
                    if (first == null || second == null || image == null)
                        throw new Exception("이름 변경 대상이 미리보기에 없습니다.");
                    Button batch = Field<Button>(editor, "batchRenameButton");
                    if (batch.Text != "일괄 이름 변경" || !batch.Enabled)
                        throw new Exception("일괄 이름 변경 버튼이 표시되지 않습니다.");

                    proposed.SelectedNode = first;
                    proposed.Focus();
                    Application.DoEvents();
                    using (System.Windows.Forms.Timer renameTimer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        renameTimer.Tick += delegate {
                            Form renameDialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f =>
                                f.Text == "파일 이름 변경" && f.Visible);
                            if (renameDialog == null) return;
                            TextBox input = renameDialog.Controls.OfType<TableLayoutPanel>().First()
                                .Controls.OfType<TextBox>().First();
                            input.Text = "renamed";
                            ((Button)renameDialog.AcceptButton).PerformClick();
                            renameTimer.Stop();
                        };
                        renameTimer.Start();
                        MethodInfo processKey = typeof(StructureEditor).GetMethod("ProcessCmdKey",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        bool handled = (bool)processKey.Invoke(editor,
                            new object[] { new Message(), Keys.F2 });
                        renameTimer.Stop();
                        if (!handled || first.Text != "renamed.txt")
                            throw new Exception("F2로 한 파일 이름 변경 실패");
                    }
                    string error;
                    if (editor.TryRenamePlannedFiles(new Dictionary<TreeNode, string> {
                        { first, "second.txt" } }, out error) || first.Text != "renamed.txt")
                        throw new Exception("기존 파일과 이름 충돌을 허용했습니다.");
                    if (editor.TryRenamePlannedFiles(new Dictionary<TreeNode, string> {
                        { first, "CON.txt" } }, out error) || first.Text != "renamed.txt")
                        throw new Exception("Windows 예약 이름을 허용했습니다.");
                    if (editor.TryRenamePlannedFiles(new Dictionary<TreeNode, string> {
                        { first, "same.txt" }, { second, "same.txt" } }, out error) ||
                        first.Text != "renamed.txt" || second.Text != "second.txt")
                        throw new Exception("일괄 이름 중복을 허용하거나 일부만 변경했습니다.");
                    if (editor.TryRenamePlannedFiles(new Dictionary<TreeNode, string> {
                        { first, "renamed.pdf" } }, out error) || first.Text != "renamed.txt")
                        throw new Exception("일괄 변경에서 확장자가 바뀌었습니다.");

                    MethodInfo select = typeof(StructureEditor).GetMethod("UpdateFileSelection",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    select.Invoke(editor, new object[] { proposed, first, false, false });
                    select.Invoke(editor, new object[] { proposed, second, false, true });
                    if (Field<HashSet<TreeNode>>(editor, "selectedProposedFiles").Count != 2)
                        throw new Exception("일괄 이름 변경 대상 선택 실패");
                    using (System.Windows.Forms.Timer batchTimer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        batchTimer.Tick += delegate {
                            Form batchDialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f =>
                                f.Text == "파일 이름 일괄 변경" && f.Visible);
                            if (batchDialog == null) return;
                            TableLayoutPanel panel = batchDialog.Controls.OfType<TableLayoutPanel>().First();
                            TextBox[] inputs = panel.Controls.OfType<TextBox>().ToArray();
                            inputs[0].Text = "Q1_";
                            inputs[1].Text = "_done";
                            ListBox preview = panel.Controls.OfType<ListBox>().First();
                            if (!preview.Items.Cast<object>().Any(x => x.ToString().Contains("Q1_renamed_done.txt")))
                                throw new Exception("일괄 이름 변경 미리보기 실패");
                            ((Button)batchDialog.AcceptButton).PerformClick();
                            batchTimer.Stop();
                        };
                        batchTimer.Start();
                        batch.PerformClick();
                        batchTimer.Stop();
                    }
                    if (first.Text != "Q1_renamed_done.txt" || second.Text != "Q1_second_done.txt")
                        throw new Exception("앞·뒤 일괄 이름 변경 실패");
                    if (!editor.TryRenamePlannedFiles(new Dictionary<TreeNode, string> {
                        { image, "IMG_001_review.png" } }, out error))
                        throw new Exception("다운로드 보류 파일 이름 변경 실패: " + error);
                    if (File.Exists(Path.Combine(reports, "Q1_renamed_done.txt")))
                        throw new Exception("미리보기에서 실제 파일을 먼저 변경했습니다.");

                    MethodInfo readPlan = typeof(StructureEditor).GetMethod("ReadPlan",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    PlanData plan = (PlanData)readPlan.Invoke(editor, null);
                    StructureSnapshot snapshot = Field<StructureSnapshot>(editor, "snapshot");
                    ApplySummary summary = StructurePlanner.Apply(root, source, snapshot,
                        plan.Files, plan.Directories);
                    if (summary.FilesMoved != 3 ||
                        !File.Exists(Path.Combine(reports, "Q1_renamed_done.txt")) ||
                        !File.Exists(Path.Combine(reports, "Q1_second_done.txt")) ||
                        !File.Exists(Path.Combine(source, "IMG_001_review.png")))
                        throw new Exception("계획한 파일 이름이 실제 위치에 반영되지 않았습니다.");

                    ChangeJournal journal = new ChangeJournal();
                    journal.AddStructure(plan, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    ChangeRecord record = journal.Records.FirstOrDefault(r =>
                        r.Kind == "파일 이동" && r.From.EndsWith("first.txt"));
                    if (record == null || record.Reason != "파일 이름 변경")
                        throw new Exception("파일 이름 변경 이력이 기록되지 않았습니다.");
                    MethodInfo displayKind = typeof(HistoryForm).GetMethod("DisplayKind",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    if ((string)displayKind.Invoke(null, new object[] { record }) != "이름 변경")
                        throw new Exception("이력 화면에서 이름 변경으로 표시되지 않습니다.");
                    journal.Undo(record);
                    if (!File.Exists(Path.Combine(reports, "first.txt")) ||
                        File.Exists(Path.Combine(reports, "Q1_renamed_done.txt")))
                        throw new Exception("선택한 파일 이름 변경을 되돌리지 못했습니다.");
                    editor.Close();
                }
                Console.WriteLine("PASS: F2 대상·일괄 이름 계획, 충돌 방지, 실제 적용, 개별 되돌리기");
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
