using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    public partial class StructureEditor
    {
        private readonly HashSet<TreeNode> selectedCurrentFiles = new HashSet<TreeNode>();
        private readonly HashSet<TreeNode> selectedProposedFiles = new HashSet<TreeNode>();
        private TreeNode currentSelectionAnchor, proposedSelectionAnchor;
        private TreeView pendingPlainClickTree;
        private TreeNode pendingPlainClickNode;
        private TreeNode lastChosenNode;
        private TreeView pendingFolderDragTree;
        private TreeNode pendingFolderDragNode;
        private Point pendingFolderDragStart;

        private HashSet<TreeNode> SelectedFiles(TreeView tree)
        {
            return tree == current ? selectedCurrentFiles : selectedProposedFiles;
        }

        private TreeNode SelectionAnchor(TreeView tree)
        {
            return tree == current ? currentSelectionAnchor : proposedSelectionAnchor;
        }

        private void SetSelectionAnchor(TreeView tree, TreeNode node)
        {
            if (tree == current) currentSelectionAnchor = node;
            else proposedSelectionAnchor = node;
        }

        private static IEnumerable<TreeNode> VisibleNodes(TreeView tree)
        {
            Stack<TreeNode> pending = new Stack<TreeNode>();
            for (int i = tree.Nodes.Count - 1; i >= 0; i--) pending.Push(tree.Nodes[i]);
            while (pending.Count > 0)
            {
                TreeNode node = pending.Pop();
                yield return node;
                if (node.IsExpanded)
                    for (int i = node.Nodes.Count - 1; i >= 0; i--) pending.Push(node.Nodes[i]);
            }
        }

        private static TreeNode RowNodeAt(TreeView tree, Point point)
        {
            TreeNode hit = tree.GetNodeAt(point);
            if (hit != null) return hit;
            if (point.X < 0 || point.X >= tree.ClientSize.Width ||
                point.Y < 0 || point.Y >= tree.ClientSize.Height) return null;
            return VisibleNodes(tree).FirstOrDefault(node => !node.Bounds.IsEmpty &&
                point.Y >= node.Bounds.Top && point.Y < node.Bounds.Bottom);
        }

        private void HookFileSelection(TreeView tree)
        {
            tree.MouseDown += HandleFileMouseDown;
            tree.MouseMove += HandleFolderMouseMove;
            tree.MouseUp += HandleFileMouseUp;
            tree.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                { ClearSelectedFiles(); e.Handled = true; }
                else if (e.KeyCode == Keys.F2)
                { RenameSelected(); e.Handled = true; e.SuppressKeyPress = true; }
            };
        }

        private void HandleFileMouseDown(object sender, MouseEventArgs e)
        {
            if (!Loaded || e.Button != MouseButtons.Left) return;
            TreeView tree = (TreeView)sender;
            pendingFolderDragTree = null;
            pendingFolderDragNode = null;
            TreeViewHitTestInfo hit = tree.HitTest(e.Location);
            TreeNode node = hit.Node ?? RowNodeAt(tree, e.Location);
            if (node == null) { ClearSelectedFiles(); return; }
            bool nativeDragArea = (hit.Location & (TreeViewHitTestLocations.Label |
                TreeViewHitTestLocations.Image)) != 0;
            bool rowDragArea = e.X >= node.Bounds.Left - IconSize - 3;
            if (!nativeDragArea && !rowDragArea) return;
            if (NodeTag(node) == null || NodeTag(node).Kind != PlanKind.File)
            {
                ClearSelectedFiles();
                lastChosenNode = node;
                tree.SelectedNode = node;
                UpdateSelectionDetails();
                PlanTag tag = NodeTag(node);
                if (!nativeDragArea && tag != null &&
                    (tag.Kind == PlanKind.Folder || tag.Kind == PlanKind.SourceFolder))
                {
                    pendingFolderDragTree = tree;
                    pendingFolderDragNode = node;
                    pendingFolderDragStart = e.Location;
                }
                return;
            }
            if (!nativeDragArea && (hit.Location & TreeViewHitTestLocations.RightOfLabel) == 0)
                return;
            lastChosenNode = node;
            bool shift = (Control.ModifierKeys & Keys.Shift) != 0;
            bool control = (Control.ModifierKeys & Keys.Control) != 0;
            HashSet<TreeNode> selected = SelectedFiles(tree);
            if (!shift && !control && selected.Count > 1 && selected.Contains(node))
            {
                pendingPlainClickTree = tree;
                pendingPlainClickNode = node;
                tree.SelectedNode = node;
                UpdateSelectionDetails();
                return;
            }
            pendingPlainClickTree = null;
            pendingPlainClickNode = null;
            UpdateFileSelection(tree, node, shift, control);
        }

        private void HandleFileMouseUp(object sender, MouseEventArgs e)
        {
            pendingFolderDragTree = null;
            pendingFolderDragNode = null;
            if (e.Button != MouseButtons.Left || pendingPlainClickTree != sender ||
                pendingPlainClickNode == null) return;
            TreeView tree = (TreeView)sender;
            TreeNode clicked = tree.HitTest(e.Location).Node;
            TreeNode pending = pendingPlainClickNode;
            pendingPlainClickTree = null;
            pendingPlainClickNode = null;
            if (clicked == pending) UpdateFileSelection(tree, pending, false, false);
        }

        private void HandleFolderMouseMove(object sender, MouseEventArgs e)
        {
            if (pendingFolderDragTree != sender || pendingFolderDragNode == null) return;
            if (e.Button != MouseButtons.Left)
            {
                pendingFolderDragTree = null;
                pendingFolderDragNode = null;
                return;
            }
            Size size = SystemInformation.DragSize;
            Rectangle hold = new Rectangle(pendingFolderDragStart.X - size.Width / 2,
                pendingFolderDragStart.Y - size.Height / 2, size.Width, size.Height);
            if (hold.Contains(e.Location)) return;
            TreeView tree = pendingFolderDragTree;
            TreeNode node = pendingFolderDragNode;
            pendingFolderDragTree = null;
            pendingFolderDragNode = null;
            StartDrag(node, tree == current);
        }

        private void UpdateFileSelection(TreeView tree, TreeNode node, bool shift, bool control)
        {
            if (tree == null || node == null || NodeTag(node) == null ||
                NodeTag(node).Kind != PlanKind.File) return;
            HashSet<TreeNode> selected = SelectedFiles(tree);
            if (shift)
            {
                TreeNode anchor = SelectionAnchor(tree);
                List<TreeNode> visibleFiles = VisibleNodes(tree).Where(n =>
                    NodeTag(n) != null && NodeTag(n).Kind == PlanKind.File).ToList();
                int start = anchor == null ? -1 : visibleFiles.IndexOf(anchor);
                int end = visibleFiles.IndexOf(node);
                if (!control) selected.Clear();
                if (start < 0 || end < 0) selected.Add(node);
                else
                    for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++)
                        selected.Add(visibleFiles[i]);
                if (anchor == null) SetSelectionAnchor(tree, node);
            }
            else if (control)
            {
                if (!selected.Add(node)) selected.Remove(node);
                SetSelectionAnchor(tree, selected.Contains(node) ? node : selected.FirstOrDefault());
            }
            else
            {
                selected.Clear(); selected.Add(node);
                SetSelectionAnchor(tree, node);
            }
            MirrorFileSelection(tree);
            TreeNode focus = selected.Contains(node) ? node : selected.FirstOrDefault();
            tree.SelectedNode = focus;
            tree.Invalidate();
            (tree == current ? proposed : current).Invalidate();
            UpdateSelectionDetails();
        }

        private void MirrorFileSelection(TreeView sourceTree)
        {
            TreeView destination = sourceTree == current ? proposed : current;
            HashSet<TreeNode> mirrored = SelectedFiles(destination);
            mirrored.Clear();
            foreach (TreeNode node in SelectedFiles(sourceTree))
            {
                TreeNode counterpart = Counterpart(destination, NodeTag(node));
                if (counterpart != null && NodeTag(counterpart) != null &&
                    NodeTag(counterpart).Kind == PlanKind.File) mirrored.Add(counterpart);
            }
        }

        private void ClearSelectedFiles()
        {
            if (selectedCurrentFiles.Count == 0 && selectedProposedFiles.Count == 0) return;
            selectedCurrentFiles.Clear(); selectedProposedFiles.Clear();
            currentSelectionAnchor = null; proposedSelectionAnchor = null;
            syncingSelection = true;
            try
            {
                if (current.SelectedNode != null && NodeTag(current.SelectedNode) != null &&
                    NodeTag(current.SelectedNode).Kind == PlanKind.File) current.SelectedNode = null;
                if (proposed.SelectedNode != null && NodeTag(proposed.SelectedNode) != null &&
                    NodeTag(proposed.SelectedNode).Kind == PlanKind.File) proposed.SelectedNode = null;
            }
            finally { syncingSelection = false; }
            current.Invalidate(); proposed.Invalidate();
            UpdateSelectionDetails();
        }

        private List<TreeNode> DragSourceNodes(TreeView tree, TreeNode node)
        {
            if (NodeTag(node) == null || NodeTag(node).Kind != PlanKind.File)
                return new List<TreeNode> { node };
            HashSet<TreeNode> selected = SelectedFiles(tree);
            if (!selected.Contains(node)) UpdateFileSelection(tree, node, false, false);
            pendingPlainClickTree = null;
            pendingPlainClickNode = null;
            return AllNodes(tree).Where(n => selected.Contains(n)).ToList();
        }

        private void SetDraggedSelectionAfterMove(List<TreeNode> moved)
        {
            selectedProposedFiles.Clear();
            foreach (TreeNode node in moved)
                if (NodeTag(node) != null && NodeTag(node).Kind == PlanKind.File)
                    selectedProposedFiles.Add(node);
            MirrorFileSelection(proposed);
            proposedSelectionAnchor = moved.FirstOrDefault();
            currentSelectionAnchor = selectedCurrentFiles.FirstOrDefault();
            current.Invalidate(); proposed.Invalidate();
            UpdateSelectionDetails();
        }
    }
}
