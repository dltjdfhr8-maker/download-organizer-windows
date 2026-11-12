using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class MultiSelectSmoke
    {
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
        }

        private static TreeNode Find(TreeNode parent, string name)
        {
            return parent.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == name);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            string temp = Path.Combine(Path.GetTempPath(), "MultiSelectSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(temp, "downloads");
                string root = Path.Combine(temp, "sorted");
                string target = Path.Combine(root, "분류");
                string data = Path.Combine(temp, "settings");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(target);
                Directory.CreateDirectory(Path.Combine(root, "A"));
                Directory.CreateDirectory(Path.Combine(root, "B"));
                Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(root, "A", "중복.txt"), "A");
                File.WriteAllText(Path.Combine(root, "B", "중복.txt"), "B");
                foreach (string name in new string[] { "새파일_1.txt", "새파일_2.txt", "새파일_3.txt" })
                {
                    string path = Path.Combine(source, name);
                    File.WriteAllText(path, name);
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (StructureEditor editor = new StructureEditor(source, root, data))
                {
                    editor.ShowInTaskbar = false;
                    editor.Opacity = 0;
                    editor.Show();
                    DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!editor.Loaded && DateTime.UtcNow < deadline)
                    { Application.DoEvents(); Thread.Sleep(20); }
                    if (!editor.Loaded) throw new Exception("편집기 로드 실패");
                    TreeView current = Field<TreeView>(editor, "current");
                    TreeView proposed = Field<TreeView>(editor, "proposed");
                    TreeNode planRoot = proposed.Nodes[0];
                    TreeNode targetNode = Find(planRoot, "분류");
                    TreeNode duplicateA = Find(Find(planRoot, "A"), "중복.txt");
                    TreeNode duplicateB = Find(Find(planRoot, "B"), "중복.txt");
                    MethodInfo select = typeof(StructureEditor).GetMethod("UpdateFileSelection",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    select.Invoke(editor, new object[] { proposed, duplicateA, false, false });
                    select.Invoke(editor, new object[] { proposed, duplicateB, false, true });
                    if (Field<HashSet<TreeNode>>(editor, "selectedProposedFiles").Count != 2)
                        throw new Exception("Ctrl 복수 선택 실패");
                    targetNode.EnsureVisible();
                    Application.DoEvents();
                    Point point = proposed.PointToScreen(new Point(targetNode.Bounds.Left + 8,
                        targetNode.Bounds.Top + targetNode.Bounds.Height / 2));
                    MethodInfo dragOver = typeof(StructureEditor).GetMethod("DragOverPlan",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    DragEventArgs collision = new DragEventArgs(new DataObject(new DraggedNode {
                        Node = duplicateA, Nodes = new List<TreeNode> { duplicateA, duplicateB }
                    }), 0, point.X, point.Y, DragDropEffects.Move, DragDropEffects.Move);
                    dragOver.Invoke(editor, new object[] { proposed, collision });
                    if (collision.Effect != DragDropEffects.None ||
                        !Field<Label>(editor, "dragGuide").Text.Contains("같은 이름"))
                        throw new Exception("같은 이름 파일 충돌 방지 실패");
                    typeof(StructureEditor).GetMethod("ClearDragTarget", BindingFlags.NonPublic |
                        BindingFlags.Instance).Invoke(editor, null);

                    TreeNode leftDownloads = current.Nodes[1];
                    List<TreeNode> files = leftDownloads.Nodes.Cast<TreeNode>().ToList();
                    if (files.Count != 3) throw new Exception("다운로드 파일 준비 실패");
                    select.Invoke(editor, new object[] { current, files[0], false, false });
                    select.Invoke(editor, new object[] { current, files[2], true, false });
                    if (Field<HashSet<TreeNode>>(editor, "selectedCurrentFiles").Count != 3)
                        throw new Exception("Shift 범위 선택 실패");
                    select.Invoke(editor, new object[] { current, files[1], false, true });
                    if (Field<HashSet<TreeNode>>(editor, "selectedCurrentFiles").Count != 2)
                        throw new Exception("Ctrl 선택 해제 실패");
                    select.Invoke(editor, new object[] { current, files[1], false, true });
                    if (Field<HashSet<TreeNode>>(editor, "selectedCurrentFiles").Count != 3 ||
                        Field<HashSet<TreeNode>>(editor, "selectedProposedFiles").Count != 3)
                        throw new Exception("Ctrl 선택 추가 실패");
                    Label details = Field<Label>(editor, "selectedPathLabel");
                    if (!details.Text.Contains("3개 파일"))
                        throw new Exception("선택한 파일 수 표시 실패");
                    DraggedNode payload = new DraggedNode { Node = files[0], FromLeft = true,
                        Nodes = new List<TreeNode>(files) };
                    DragEventArgs batch = new DragEventArgs(new DataObject(payload), 0,
                        point.X, point.Y, DragDropEffects.Move, DragDropEffects.Move);
                    dragOver.Invoke(editor, new object[] { proposed, batch });
                    Label guide = Field<Label>(editor, "dragGuide");
                    if (batch.Effect != DragDropEffects.Move || !guide.Text.Contains("3개 파일") ||
                        !guide.Text.Contains(files[0].Text) || !guide.Text.Contains("분류"))
                        throw new Exception("복수 드래그 안내 실패");
                    using (DragGhost ghost = new DragGhost(files))
                    {
                        ghost.SetTarget("정리 폴더 > 분류", true, "");
                        ghost.FollowCursor();
                        ghost.Show(editor);
                        Application.DoEvents();
                        if (!ghost.Visible) throw new Exception("커서 옆 드래그 미리보기 실패");
                        if (args.Length > 1)
                            using (Bitmap bitmap = new Bitmap(ghost.Width, ghost.Height))
                            {
                                ghost.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                                bitmap.Save(args[1]);
                            }
                        ghost.Close();
                    }
                    if (args.Length > 0)
                        using (Bitmap bitmap = new Bitmap(editor.Width, editor.Height))
                        {
                            editor.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            bitmap.Save(args[0]);
                        }
                    typeof(StructureEditor).GetMethod("DropOnPlan", BindingFlags.NonPublic |
                        BindingFlags.Instance).Invoke(editor, new object[] { proposed, batch });
                    if (targetNode.Nodes.Cast<TreeNode>().Count(n => n.Text.StartsWith("새파일_")) != 3 ||
                        Field<HashSet<TreeNode>>(editor, "selectedProposedFiles").Count != 3)
                        throw new Exception("여러 파일 계획상 이동 실패");
                    PlanData plan = (PlanData)typeof(StructureEditor).GetMethod("ReadPlan",
                        BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, null);
                    StructureSnapshot snapshot = Field<StructureSnapshot>(editor, "snapshot");
                    List<string> progress = new List<string>();
                    ApplySummary summary = StructurePlanner.Apply(root, source, snapshot,
                        plan.Files, plan.Directories, delegate(int completed, int total,
                            string filePath, string phase)
                        {
                            progress.Add(completed + "/" + total + ":" + phase + ":" + Path.GetFileName(filePath));
                        });
                    if (summary.FilesMoved != 3 || Directory.GetFiles(source).Length != 0 ||
                        Directory.GetFiles(target, "새파일_*.txt").Length != 3 ||
                        !File.Exists(Path.Combine(root, "A", "중복.txt")) ||
                        !File.Exists(Path.Combine(root, "B", "중복.txt")))
                        throw new Exception("여러 파일 실제 적용 실패");
                    if (!progress.Any(x => x.Contains("이동 준비 중:새파일_1.txt")) ||
                        !progress.Any(x => x.Contains("이동 중:새파일_2.txt")) ||
                        !progress.Any(x => x.StartsWith("3/3:이동 완료:")))
                        throw new Exception("파일별 이동 진행 표시 실패");
                }
                Console.WriteLine("PASS: Ctrl/Shift 복수 선택, 파일 목록 안내, 충돌 방지, 일괄 적용과 진행 표시");
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
