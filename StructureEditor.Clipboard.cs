using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    public partial class StructureEditor
    {
        private readonly ContextMenuStrip currentContextMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip proposedContextMenu = new ContextMenuStrip();
        private readonly List<TreeNode> planClipboard = new List<TreeNode>();
        private bool clipboardCut;

        private void HookClipboardMenus()
        {
            ConfigureContextMenu(current, currentContextMenu, false);
            ConfigureContextMenu(proposed, proposedContextMenu, true);
            Disposed += delegate { currentContextMenu.Dispose(); proposedContextMenu.Dispose(); };
        }

        private void ConfigureContextMenu(TreeView tree, ContextMenuStrip menu, bool editable)
        {
            ToolStripMenuItem copy = new ToolStripMenuItem("복사", null, delegate { CopySelection(tree); });
            ToolStripMenuItem cut = new ToolStripMenuItem("잘라내기", null, delegate { CutSelection(tree); });
            ToolStripMenuItem paste = new ToolStripMenuItem("붙여넣기", null, delegate { PasteSelection(); });
            copy.ShortcutKeyDisplayString = "Ctrl+C";
            cut.ShortcutKeyDisplayString = "Ctrl+X";
            paste.ShortcutKeyDisplayString = "Ctrl+V";
            menu.Items.Add(copy); menu.Items.Add(cut); menu.Items.Add(paste);
            menu.Opening += delegate(object sender, CancelEventArgs e)
            {
                TreeNode selected = tree.SelectedNode;
                PlanTag tag = NodeTag(selected);
                bool ready = Loaded && !loading && !working;
                bool item = tag != null && (tag.Kind == PlanKind.File ||
                    tag.Kind == PlanKind.Folder || tag.Kind == PlanKind.SourceFolder);
                copy.Enabled = ready && item;
                cut.Enabled = ready && item;
                paste.Enabled = ready && editable && planClipboard.Count > 0 &&
                    PasteFolder() != null;
                if (!copy.Enabled && !cut.Enabled && !paste.Enabled) e.Cancel = true;
            };
            tree.ContextMenuStrip = menu;
            tree.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right || !Loaded) return;
                TreeNode node = RowNodeAt(tree, e.Location);
                if (node == null) return;
                if (!SelectedFiles(tree).Contains(node)) ClearSelectedFiles();
                tree.SelectedNode = node;
                UpdateSelectionDetails();
            };
            tree.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (!e.Control || !Loaded || loading || working) return;
                if (e.KeyCode == Keys.C) CopySelection(tree);
                else if (e.KeyCode == Keys.X) CutSelection(tree);
                else if (e.KeyCode == Keys.V)
                {
                    if (editable) PasteSelection();
                    else status.Text = "오른쪽 미리보기에서 붙여넣을 폴더를 선택하세요.";
                }
                else return;
                e.Handled = true; e.SuppressKeyPress = true;
            };
        }

        private List<TreeNode> SelectedContextItems(TreeView tree)
        {
            TreeNode selected = tree.SelectedNode;
            if (selected == null) return new List<TreeNode>();
            HashSet<TreeNode> selectedFiles = SelectedFiles(tree);
            if (selectedFiles.Count > 1 && selectedFiles.Contains(selected))
                return AllNodes(tree).Where(node => selectedFiles.Contains(node)).ToList();
            return new List<TreeNode> { selected };
        }

        private static TreeNode CloneForCopy(TreeNode original)
        {
            PlanTag tag = NodeTag(original);
            if (tag == null) return null;
            PlanTag copyTag;
            if (tag.Kind == PlanKind.File)
                copyTag = new PlanTag {
                    Kind = PlanKind.File, OriginalPath = tag.OriginalPath,
                    FromDownloads = tag.FromDownloads, IsCopy = true,
                    Length = tag.Length, LastWriteTicks = tag.LastWriteTicks,
                    CreationTicks = tag.CreationTicks
                };
            else if (tag.Kind == PlanKind.Folder || tag.Kind == PlanKind.SourceFolder)
                copyTag = new PlanTag { Kind = PlanKind.Folder };
            else return null;
            TreeNode result = new TreeNode(original.Text) { Tag = copyTag,
                ImageKey = original.ImageKey, SelectedImageKey = original.ImageKey,
                NodeFont = original.NodeFont, ForeColor = Color.DarkBlue };
            foreach (TreeNode child in original.Nodes)
            {
                TreeNode cloned = CloneForCopy(child);
                if (cloned != null) result.Nodes.Add(cloned);
            }
            return result;
        }

        private void CopySelection(TreeView tree)
        {
            if (!Loaded || loading || working) return;
            List<TreeNode> selected = SelectedContextItems(tree);
            if (selected.Count == 0 || selected.Any(node => CloneForCopy(node) == null))
            { status.Text = "복사할 파일이나 하위 폴더를 선택하세요."; return; }
            planClipboard.Clear();
            foreach (TreeNode node in selected) planClipboard.Add(CloneForCopy(node));
            clipboardCut = false;
            status.Text = selected.Count + "개 항목을 복사할 준비가 됐습니다. 오른쪽 목적지 폴더에서 붙여넣으세요.";
        }

        private void CutSelection(TreeView tree)
        {
            if (!Loaded || loading || working) return;
            List<TreeNode> selected = SelectedContextItems(tree);
            if (selected.Count == 0 || selected.Any(node => NodeTag(node) == null ||
                (NodeTag(node).Kind != PlanKind.File && NodeTag(node).Kind != PlanKind.Folder &&
                 NodeTag(node).Kind != PlanKind.SourceFolder)))
            { status.Text = "잘라낼 파일이나 하위 폴더를 선택하세요."; return; }
            List<TreeNode> resolved = new List<TreeNode>();
            foreach (TreeNode node in selected)
            {
                TreeNode planNode = tree == proposed ? node :
                    Resolve(new DraggedNode { Node = node, FromLeft = true });
                if (planNode == null)
                { status.Text = "오른쪽 미리보기에서 이 항목을 찾지 못했습니다. 구조를 다시 불러오세요."; return; }
                resolved.Add(planNode);
            }
            planClipboard.Clear(); planClipboard.AddRange(resolved);
            clipboardCut = true;
            status.Text = resolved.Count + "개 항목을 잘라냈습니다. 붙여넣기 전에는 위치가 바뀌지 않습니다.";
        }

        private TreeNode PasteFolder()
        {
            TreeNode node = proposed.SelectedNode;
            if (node == null) return null;
            PlanTag tag = NodeTag(node);
            if (tag != null && tag.Kind == PlanKind.File) node = node.Parent;
            tag = NodeTag(node);
            return tag != null && (tag.Kind == PlanKind.Root || tag.Kind == PlanKind.Folder) ? node : null;
        }

        private static string AvailableCopyName(TreeNode target, string requested, bool folder)
        {
            string stem = folder ? requested : Path.GetFileNameWithoutExtension(requested);
            string extension = folder ? "" : Path.GetExtension(requested);
            string name = requested;
            int number = 2;
            while (target.Nodes.Cast<TreeNode>().Any(node =>
                node.Text.Equals(name, StringComparison.OrdinalIgnoreCase)))
                name = stem + " (" + number++ + ")" + extension;
            return name;
        }

        private void PasteSelection()
        {
            if (!Loaded || loading || working || planClipboard.Count == 0) return;
            TreeNode target = PasteFolder();
            if (target == null)
            { status.Text = "오른쪽 정리 폴더 안의 목적지를 선택해 붙여넣으세요."; return; }
            if (clipboardCut)
            {
                if (planClipboard.Any(node => node.TreeView != proposed))
                { planClipboard.Clear(); status.Text = "잘라낸 항목이 바뀌었습니다. 다시 선택해 주세요."; return; }
                string reason = DropBlockReasonMany(planClipboard, target);
                if (reason != "") { status.Text = reason; return; }
                List<TreeNode> moving = planClipboard.ToList();
                planClipboard.Clear();
                MovePlannedNodes(moving, target);
                return;
            }
            List<TreeNode> pasted = new List<TreeNode>();
            foreach (TreeNode template in planClipboard)
            {
                TreeNode item = CloneForCopy(template);
                item.Text = AvailableCopyName(target, item.Text, NodeTag(item).Kind == PlanKind.Folder);
                target.Nodes.Add(item);
                pasted.Add(item);
            }
            target.Expand(); proposed.SelectedNode = pasted[0];
            edited = true; RefreshPlanVisuals();
            status.Text = pasted.Count + "개 항목을 복사 계획에 넣었습니다. 실제 복사는 '변경 내용 확인·적용'에서 진행합니다.";
        }

        private void ClearPlanClipboard()
        {
            planClipboard.Clear(); clipboardCut = false;
        }
    }
}
