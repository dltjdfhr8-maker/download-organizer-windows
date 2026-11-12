using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    public static class StructureSmoke
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string temp = Path.Combine(Path.GetTempPath(), "StructureEditorSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(temp, "downloads");
                string root = Path.Combine(temp, "sorted");
                string data = Path.Combine(temp, "settings");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(Path.Combine(root, "기존"));
                Directory.CreateDirectory(Path.Combine(root, "예산"));
                Directory.CreateDirectory(data);
                string nestedSource = Path.Combine(source, "받은 묶음");
                Directory.CreateDirectory(nestedSource);
                Directory.CreateDirectory(Path.Combine(nestedSource, "빈 폴더"));
                string classFolder = Path.Combine(nestedSource, "수강생배포");
                Directory.CreateDirectory(classFolder);
                File.WriteAllText(Path.Combine(classFolder, "수강생자료.txt"), "class");
                File.SetLastWriteTimeUtc(Path.Combine(classFolder, "수강생자료.txt"), DateTime.UtcNow.AddSeconds(-10));
                File.WriteAllText(Path.Combine(data, "topics.txt"), "업무 > 예산\n연수", Encoding.UTF8);
                File.WriteAllText(Path.Combine(root, "기존", "기존자료.txt"), "old");
                File.WriteAllText(Path.Combine(source, "예산_안내.txt"), "incoming");
                File.WriteAllText(Path.Combine(source, "무관한파일.txt"), "incoming");
                File.WriteAllText(Path.Combine(source, "드래그파일.txt"), "drag");
                File.WriteAllText(Path.Combine(nestedSource, "하위드래그파일.txt"), "nested");
                File.WriteAllText(Path.Combine(nestedSource, "하위보류파일.txt"), "hold");
                File.SetLastWriteTimeUtc(Path.Combine(source, "예산_안내.txt"), DateTime.UtcNow.AddSeconds(-10));
                File.SetLastWriteTimeUtc(Path.Combine(source, "드래그파일.txt"), DateTime.UtcNow.AddSeconds(-10));
                File.SetLastWriteTimeUtc(Path.Combine(nestedSource, "하위드래그파일.txt"), DateTime.UtcNow.AddSeconds(-10));
                using (StructureEditor editor = new StructureEditor(source, root, data))
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
                    if (!editor.Loaded) throw new Exception("구조 편집기 비동기 읽기 실패");
                    FieldInfo field = typeof(StructureEditor).GetField("proposed", BindingFlags.NonPublic | BindingFlags.Instance);
                    TreeView tree = (TreeView)field.GetValue(editor);
                    FieldInfo currentField = typeof(StructureEditor).GetField("current", BindingFlags.NonPublic | BindingFlags.Instance);
                    TreeView currentTree = (TreeView)currentField.GetValue(editor);
                    if (tree.ImageList == null || !tree.ImageList.Images.ContainsKey("folder") ||
                        !tree.ImageList.Images.ContainsKey("file") || !tree.ImageList.Images.ContainsKey("file-pdf") ||
                        currentTree.ImageList != tree.ImageList)
                        throw new Exception("폴더·파일 아이콘 표시 실패");
                    TextBox search = (TextBox)typeof(StructureEditor).GetField("searchBox",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
                    Label results = (Label)typeof(StructureEditor).GetField("searchResultLabel",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
                    search.Text = "예산";
                    DateTime searchDeadline = DateTime.UtcNow.AddSeconds(3);
                    while (!results.Text.Contains("찾음") && DateTime.UtcNow < searchDeadline)
                    { Application.DoEvents(); Thread.Sleep(20); }
                    if (!results.Text.Contains("찾음") || tree.SelectedNode == null)
                        throw new Exception("폴더·파일 검색 실패");
                    search.Clear();
                    TreeNode planRoot = tree.Nodes[0], hold = tree.Nodes[1];
                    TreeNode leftNested = Find(currentTree.Nodes[1], "받은 묶음");
                    TreeNode rightNested = Find(hold, "받은 묶음");
                    if (leftNested == null || Find(leftNested, "빈 폴더") == null ||
                        Find(leftNested, "하위드래그파일.txt") == null || rightNested == null ||
                        Find(rightNested, "빈 폴더") == null)
                        throw new Exception("감시 폴더의 하위 구조 표시 실패");
                    TreeNode business = Find(planRoot, "업무");
                    TreeNode budget = Find(business, "예산");
                    TreeNode training = Find(planRoot, "연수");
                    TreeNode existing = Find(planRoot, "기존");
                    TreeNode incoming = Find(budget, "예산_안내.txt");
                    if (business == null || budget == null || training == null || existing == null || incoming == null)
                    {
                        Dump(planRoot, "");
                        Dump(hold, "");
                        throw new Exception("주제 기반 오른쪽 구조 생성 실패");
                    }
                    if (Find(hold, "무관한파일.txt") == null)
                        throw new Exception("분류 보류 파일 표시 실패");
                    TreeNode nestedMoving = FindDeep(tree.Nodes, "하위드래그파일.txt");
                    if (nestedMoving == null || !editor.MovePlannedNode(nestedMoving, training))
                        throw new Exception("감시 하위 파일의 계획 이동 실패");
                    TreeNode oldFile = Find(existing, "기존자료.txt");
                    training.EnsureVisible();
                    Application.DoEvents();
                    Rectangle targetBounds = training.Bounds;
                    Point dropPoint = tree.PointToScreen(new Point(targetBounds.Left + 8,
                        targetBounds.Top + targetBounds.Height / 2));
                    DataObject dragData = new DataObject(new DraggedNode { Node = oldFile, FromLeft = false });
                    DragEventArgs dropArgs = new DragEventArgs(dragData, 0, dropPoint.X, dropPoint.Y,
                        DragDropEffects.Move, DragDropEffects.Move);
                    MethodInfo dragOver = typeof(StructureEditor).GetMethod("DragOverPlan",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    dragOver.Invoke(editor, new object[] { tree, dropArgs });
                    Label dragGuide = (Label)typeof(StructureEditor).GetField("dragGuide",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
                    if (dropArgs.Effect != DragDropEffects.Move ||
                        !dragGuide.Text.Contains("기존자료.txt") || !dragGuide.Text.Contains("연수") ||
                        training.BackColor != Color.FromArgb(199, 239, 209))
                        throw new Exception("드래그 목적지 안내 실패");
                    if (args.Length > 1)
                        using (Bitmap bitmap = new Bitmap(editor.Width, editor.Height))
                        {
                            editor.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            bitmap.Save(args[1]);
                        }
                    MethodInfo drop = typeof(StructureEditor).GetMethod("DropOnPlan", BindingFlags.NonPublic | BindingFlags.Instance);
                    drop.Invoke(editor, new object[] { tree, dropArgs });
                    if (oldFile.Parent != training)
                        throw new Exception("오른쪽 파일 위치 수정 실패");
                    if (training.BackColor != Color.Empty || !dragGuide.Text.Contains("원하는 폴더"))
                        throw new Exception("드래그 안내 정리 실패");
                    Label pathDetails = (Label)typeof(StructureEditor).GetField("selectedPathLabel",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
                    if (!pathDetails.Text.Contains("정리 폴더 > 기존") ||
                        !pathDetails.Text.Contains("정리 폴더 > 연수"))
                        throw new Exception("변경 전후 경로 표시 실패: " + pathDetails.Text);
                    business.Collapse();
                    business.EnsureVisible();
                    Application.DoEvents();
                    Point expandPoint = tree.PointToScreen(new Point(business.Bounds.Left + 8,
                        business.Bounds.Top + business.Bounds.Height / 2));
                    DragEventArgs expandDrop = new DragEventArgs(
                        new DataObject(new DraggedNode { Node = oldFile, FromLeft = false }), 0,
                        expandPoint.X, expandPoint.Y, DragDropEffects.Move, DragDropEffects.Move);
                    dragOver.Invoke(editor, new object[] { tree, expandDrop });
                    DateTime expandDeadline = DateTime.UtcNow.AddSeconds(2);
                    while (!business.IsExpanded && DateTime.UtcNow < expandDeadline)
                    { Application.DoEvents(); Thread.Sleep(20); }
                    if (!business.IsExpanded) throw new Exception("드래그 중 폴더 자동 펼치기 실패");
                    typeof(StructureEditor).GetMethod("ClearDragTarget", BindingFlags.NonPublic |
                        BindingFlags.Instance).Invoke(editor, null);
                    TreeNode leftDownload = Find(currentTree.Nodes[1], "드래그파일.txt");
                    if (leftDownload == null) throw new Exception("왼쪽 다운로드 파일 표시 실패");
                    training.EnsureVisible();
                    Application.DoEvents();
                    Rectangle leftTargetBounds = training.Bounds;
                    Point leftTargetPoint = tree.PointToScreen(new Point(leftTargetBounds.Left + 8,
                        leftTargetBounds.Top + leftTargetBounds.Height / 2));
                    DragEventArgs leftDrop = new DragEventArgs(
                        new DataObject(new DraggedNode { Node = leftDownload, FromLeft = true }), 0,
                        leftTargetPoint.X, leftTargetPoint.Y, DragDropEffects.Move, DragDropEffects.Move);
                    drop.Invoke(editor, new object[] { tree, leftDrop });
                    if (Find(training, "드래그파일.txt") == null)
                        throw new Exception("왼쪽에서 오른쪽으로 드래그 실패");
                    training.EnsureVisible();
                    Application.DoEvents();
                    Point folderSourcePoint = new Point(tree.ClientSize.Width - 12,
                        training.Bounds.Top + training.Bounds.Height / 2);
                    MouseEventArgs folderMouse = new MouseEventArgs(MouseButtons.Left, 1,
                        folderSourcePoint.X, folderSourcePoint.Y, 0);
                    typeof(StructureEditor).GetMethod("HandleFileMouseDown", BindingFlags.NonPublic |
                        BindingFlags.Instance).Invoke(editor, new object[] { tree, folderMouse });
                    TreeNode pendingFolder = (TreeNode)typeof(StructureEditor).GetField("pendingFolderDragNode",
                        BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
                    if (pendingFolder != training)
                        throw new Exception("폴더 행의 빈 부분에서 드래그 준비 실패");
                    typeof(StructureEditor).GetMethod("HandleFileMouseUp", BindingFlags.NonPublic |
                        BindingFlags.Instance).Invoke(editor, new object[] { tree, folderMouse });
                    existing.EnsureVisible();
                    Application.DoEvents();
                    Rectangle folderTargetBounds = existing.Bounds;
                    Point folderTargetPoint = tree.PointToScreen(new Point(tree.ClientSize.Width - 12,
                        folderTargetBounds.Top + folderTargetBounds.Height / 2));
                    DragEventArgs folderDrop = new DragEventArgs(
                        new DataObject(new DraggedNode { Node = training, FromLeft = false }), 0,
                        folderTargetPoint.X, folderTargetPoint.Y, DragDropEffects.Move, DragDropEffects.Move);
                    dragOver.Invoke(editor, new object[] { tree, folderDrop });
                    if (folderDrop.Effect != DragDropEffects.Move ||
                        !dragGuide.Text.Contains("연수") || !dragGuide.Text.Contains("기존"))
                        throw new Exception("오른쪽 폴더 드래그 목적지 안내 실패");
                    drop.Invoke(editor, new object[] { tree, folderDrop });
                    if (training.Parent != existing || Find(training, "드래그파일.txt") == null)
                        throw new Exception("오른쪽 폴더 전체 이동 실패");
                    if (!editor.MovePlannedNode(training, planRoot))
                        throw new Exception("옮긴 폴더를 상위 폴더로 되돌리기 실패");
                    if (editor.MovePlannedNode(business, budget))
                        throw new Exception("폴더 순환 이동 방지 실패");
                    TreeNode classSource = Find(rightNested, "수강생배포");
                    if (classSource == null) throw new Exception("감시 하위 폴더 표시 실패");
                    budget.EnsureVisible();
                    Application.DoEvents();
                    Rectangle classTargetBounds = budget.Bounds;
                    Point classTargetPoint = tree.PointToScreen(new Point(tree.ClientSize.Width - 12,
                        classTargetBounds.Top + classTargetBounds.Height / 2));
                    DragEventArgs classDrop = new DragEventArgs(
                        new DataObject(new DraggedNode { Node = classSource, FromLeft = false }), 0,
                        classTargetPoint.X, classTargetPoint.Y, DragDropEffects.Move, DragDropEffects.Move);
                    dragOver.Invoke(editor, new object[] { tree, classDrop });
                    if (classDrop.Effect != DragDropEffects.Move)
                        throw new Exception("감시 하위 폴더의 중첩 목적지 드래그 실패: " + dragGuide.Text);
                    drop.Invoke(editor, new object[] { tree, classDrop });
                    if (Find(Find(budget, "수강생배포"), "수강생자료.txt") == null)
                        throw new Exception("감시 하위 폴더를 중첩 목적지로 배치 실패");
                    budget.EnsureVisible();
                    Application.DoEvents();
                    Point invalidPoint = tree.PointToScreen(new Point(budget.Bounds.Left + 8,
                        budget.Bounds.Top + budget.Bounds.Height / 2));
                    DragEventArgs invalidDrop = new DragEventArgs(
                        new DataObject(new DraggedNode { Node = business, FromLeft = false }), 0,
                        invalidPoint.X, invalidPoint.Y, DragDropEffects.Move, DragDropEffects.Move);
                    dragOver.Invoke(editor, new object[] { tree, invalidDrop });
                    if (invalidDrop.Effect != DragDropEffects.None ||
                        !dragGuide.Text.Contains("자기 자신") ||
                        budget.BackColor != Color.FromArgb(255, 225, 202))
                        throw new Exception("놓을 수 없는 위치 안내 실패");
                    typeof(StructureEditor).GetMethod("ClearDragTarget", BindingFlags.NonPublic |
                        BindingFlags.Instance).Invoke(editor, null);
                    tree.SelectedNode = existing;
                    MethodInfo removeFolder = typeof(StructureEditor).GetMethod("RemoveFolder", BindingFlags.NonPublic | BindingFlags.Instance);
                    removeFolder.Invoke(editor, new object[] { editor, EventArgs.Empty });
                    DataObject payload = new DataObject(typeof(DraggedNode).FullName, new DraggedNode { Node = incoming });
                    if (!(payload.GetData(typeof(DraggedNode)) is DraggedNode))
                        throw new Exception("드래그 데이터 전달 실패");
                    DataObject automaticPayload = new DataObject(new DraggedNode { Node = incoming });
                    if (!(automaticPayload.GetData(typeof(DraggedNode)) is DraggedNode))
                        throw new Exception("자동 드래그 데이터 전달 실패");
                    if (args.Length > 0)
                    {
                        using (Bitmap bitmap = new Bitmap(editor.Width, editor.Height))
                        {
                            editor.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            bitmap.Save(args[0]);
                        }
                        editor.Hide();
                    }
                    MethodInfo readPlan = typeof(StructureEditor).GetMethod("ReadPlan", BindingFlags.NonPublic | BindingFlags.Instance);
                    PlanData plan = (PlanData)readPlan.Invoke(editor, null);
                    FieldInfo snapshotField = typeof(StructureEditor).GetField("snapshot", BindingFlags.NonPublic | BindingFlags.Instance);
                    StructureSnapshot snapshot = (StructureSnapshot)snapshotField.GetValue(editor);
                    ApplySummary summary = StructurePlanner.Apply(root, source, snapshot, plan.Files, plan.Directories);
                    if (summary.FilesMoved != 5 ||
                        !File.Exists(Path.Combine(root, "업무", "예산", "예산_안내.txt")) ||
                        !File.Exists(Path.Combine(root, "업무", "예산", "수강생배포", "수강생자료.txt")) ||
                        !File.Exists(Path.Combine(root, "연수", "기존자료.txt")) ||
                        !File.Exists(Path.Combine(root, "연수", "드래그파일.txt")) ||
                        !File.Exists(Path.Combine(root, "연수", "하위드래그파일.txt")) ||
                        File.Exists(Path.Combine(source, "드래그파일.txt")) ||
                        !File.Exists(Path.Combine(source, "무관한파일.txt")) ||
                        !File.Exists(Path.Combine(nestedSource, "하위보류파일.txt")) ||
                        Directory.Exists(Path.Combine(root, "기존")))
                        throw new Exception("편집한 구조의 실제 적용 실패");
                }
                Console.WriteLine("PASS: 좌우 구조, 주제 입력, 파일 배치, 드래그 이동, 구조 적용, 순환 방지");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
            finally
            {
                string tempRoot = Path.GetFullPath(Path.GetTempPath());
                if (temp.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(temp))
                    Directory.Delete(temp, true);
            }
        }

        private static TreeNode Find(TreeNode parent, string name)
        {
            if (parent == null) return null;
            foreach (TreeNode child in parent.Nodes)
                if (child.Text == name) return child;
            return null;
        }

        private static TreeNode FindDeep(TreeNodeCollection nodes, string name)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Text == name) return node;
                TreeNode found = FindDeep(node.Nodes, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void Dump(TreeNode node, string indent)
        {
            if (node == null) return;
            Console.WriteLine(indent + node.Text);
            foreach (TreeNode child in node.Nodes) Dump(child, indent + "  ");
        }
    }
}
