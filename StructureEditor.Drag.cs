using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    public partial class StructureEditor
    {
        private readonly Label dragGuide = new Label();
        private readonly Timer dragExpandTimer = new Timer { Interval = 700 };
        private readonly Timer dragScrollTimer = new Timer { Interval = 120 };
        private TreeNode dragSourceNode, dragResolvedNode, dragTarget, dragExpandTarget;
        private List<TreeNode> dragResolvedNodes;
        private bool dragActive, dragTargetAllowed;
        private int dragScrollDirection;
        private string dragTooltip = "";
        private DragGhost dragGhost;

        private Control CreateDragGuide()
        {
            dragGuide.Dock = DockStyle.Fill;
            dragGuide.Margin = new Padding(0, 3, 0, 0);
            dragGuide.Padding = new Padding(8, 0, 8, 0);
            dragGuide.TextAlign = ContentAlignment.MiddleLeft;
            dragGuide.AutoEllipsis = true;
            dragGuide.BorderStyle = BorderStyle.FixedSingle;
            ResetDragGuide();
            dragExpandTimer.Tick += delegate
            {
                dragExpandTimer.Stop();
                if (dragTargetAllowed && dragExpandTarget != null &&
                    dragExpandTarget == dragTarget && dragExpandTarget.TreeView == proposed)
                    dragExpandTarget.Expand();
            };
            dragScrollTimer.Tick += delegate
            {
                if (dragScrollDirection == 0 || proposed.TopNode == null) return;
                TreeNode next = dragScrollDirection < 0 ? proposed.TopNode.PrevVisibleNode :
                    proposed.TopNode.NextVisibleNode;
                if (next != null) proposed.TopNode = next;
            };
            helpTips.SetToolTip(dragGuide, "드래그 중 옮길 항목, 놓을 폴더, 놓을 수 없는 이유를 표시합니다.");
            return dragGuide;
        }

        private void ResetDragGuide()
        {
            dragGuide.Text = "파일이나 폴더를 오른쪽의 원하는 폴더 위에 놓으세요.";
            dragGuide.BackColor = Color.FromArgb(238, 245, 252);
            dragGuide.ForeColor = Color.FromArgb(39, 70, 103);
        }

        private static string DragItemsLabel(List<TreeNode> moving)
        {
            if (moving == null || moving.Count == 0) return "선택한 항목";
            if (moving.Count == 1) return moving[0].Text;
            string names = String.Join(", ", moving.Take(2).Select(n => n.Text).ToArray());
            return moving.Count + "개 파일 (" + names +
                (moving.Count > 2 ? " 외 " + (moving.Count - 2) + "개" : "") + ")";
        }

        private static string DragItemsDetails(List<TreeNode> moving)
        {
            return moving == null ? "" : String.Join("\n", moving.Select(n => n.Text).ToArray());
        }

        private void BeginDragFeedback(TreeNode sourceNode, List<TreeNode> moving)
        {
            dragSourceNode = sourceNode;
            dragResolvedNodes = moving;
            PlanTag sourceTag = NodeTag(sourceNode);
            dragResolvedNode = moving.FirstOrDefault(n => NodeTag(n) != null && sourceTag != null &&
                NodeTag(n).OriginalPath.Equals(sourceTag.OriginalPath, StringComparison.OrdinalIgnoreCase)) ?? moving[0];
            dragActive = true;
            SetDragGuide(DragItemsLabel(moving) + " 이동 중 · 오른쪽 폴더를 가리키세요.",
                Color.FromArgb(238, 245, 252), Color.FromArgb(39, 70, 103),
                DragItemsDetails(moving));
        }

        private Color SearchBackground(TreeNode node)
        {
            string query = searchBox.Text.Trim();
            return query != "" && node.Text.IndexOf(query,
                StringComparison.CurrentCultureIgnoreCase) >= 0 ?
                Color.FromArgb(255, 246, 190) : Color.Empty;
        }

        private string TargetLabel(TreeNode target)
        {
            if (target == proposedHold) return proposedHold.Text;
            string planned;
            return target != null && plannedPaths.TryGetValue(target, out planned) ?
                DisplayLocation(planned) : target == null ? "" : target.Text;
        }

        private string DropBlockReason(TreeNode moving, TreeNode target)
        {
            if (moving == null || NodeTag(moving) == null)
                return "옮길 항목을 찾을 수 없습니다.";
            if (target == null || !IsFolder(target))
                return "오른쪽의 폴더 위에 놓으세요.";
            PlanTag item = NodeTag(moving), destination = NodeTag(target);
            if (item.Kind == PlanKind.SourceFolder)
            {
                if (destination.Kind != PlanKind.Folder && destination.Kind != PlanKind.Root)
                    return "감시 폴더의 하위 폴더는 정리 폴더 안으로만 옮길 수 있습니다.";
                if (FilesUnder(moving) == 0)
                    return "이 폴더에는 아직 옮길 파일이 없습니다.";
                if (target.Nodes.Cast<TreeNode>().Any(child =>
                    child.Text.Equals(moving.Text, StringComparison.OrdinalIgnoreCase)))
                    return "이 폴더에 같은 이름의 항목이 있습니다.";
                return "";
            }
            if (item.Kind != PlanKind.File && item.Kind != PlanKind.Folder)
                return "파일이나 하위 폴더만 옮길 수 있습니다.";
            if (moving.Parent == target)
                return "이미 이 폴더에 있습니다.";
            if (destination.Kind == PlanKind.Hold &&
                (item.Kind != PlanKind.File || !item.FromDownloads))
                return "이 항목은 최상위 보류 영역에 놓을 수 없습니다.";
            if (destination.Kind == PlanKind.SourceFolder &&
                (item.Kind != PlanKind.File || !item.FromDownloads))
                return "감시 폴더의 하위 폴더에는 감시 폴더 파일만 놓을 수 있습니다.";
            for (TreeNode cursor = target; cursor != null; cursor = cursor.Parent)
                if (cursor == moving)
                    return "폴더를 자기 자신이나 그 하위 폴더로 옮길 수 없습니다.";
            if (target.Nodes.Cast<TreeNode>().Any(child => child != moving &&
                child.Text.Equals(moving.Text, StringComparison.OrdinalIgnoreCase)))
                return "이 폴더에 같은 이름의 항목이 있습니다.";
            return "";
        }

        private string DropBlockReasonMany(List<TreeNode> moving, TreeNode target)
        {
            if (moving == null || moving.Count == 0)
                return "옮길 파일을 찾을 수 없습니다.";
            if (moving.Count == 1) return DropBlockReason(moving[0], target);
            if (moving.Any(node => NodeTag(node) == null || NodeTag(node).Kind != PlanKind.File))
                return "여러 항목을 함께 옮길 때는 파일만 선택하세요.";
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TreeNode node in moving)
            {
                if (!names.Add(node.Text))
                    return "선택한 파일에 같은 이름이 있어 한 폴더에 놓을 수 없습니다.";
                string reason = DropBlockReason(node, target);
                if (reason == "이미 이 폴더에 있습니다.")
                    return "선택한 파일 중 이미 이 폴더에 있는 파일이 있습니다.";
                if (reason != "") return reason;
            }
            return "";
        }

        private void UpdateDragFeedback(List<TreeNode> moving, TreeNode target, string reason)
        {
            bool allowed = reason == "";
            if (dragGhost != null)
            {
                dragGhost.SetTarget(allowed ? TargetLabel(target) : "", allowed, reason);
                dragGhost.FollowCursor();
            }
            if (dragTarget != target || dragTargetAllowed != allowed)
            {
                if (dragTarget != null && dragTarget.TreeView == proposed)
                    dragTarget.BackColor = SearchBackground(dragTarget);
                dragTarget = target;
                dragTargetAllowed = allowed;
                if (dragTarget != null && dragTarget.TreeView == proposed)
                    dragTarget.BackColor = allowed ? Color.FromArgb(199, 239, 209) :
                        Color.FromArgb(255, 225, 202);
                dragExpandTimer.Stop();
                dragExpandTarget = allowed && target != null &&
                    target.Nodes.Count > 0 && !target.IsExpanded ? target : null;
                if (dragExpandTarget != null) dragExpandTimer.Start();
            }
            if (allowed)
            {
                SetDragGuide(DragItemsLabel(moving) + "  →  " + TargetLabel(target) + " · 놓으면 배치",
                    Color.FromArgb(224, 246, 230), Color.FromArgb(20, 105, 49),
                    DragItemsDetails(moving) + "\n→ " + TargetLabel(target));
            }
            else
            {
                SetDragGuide(DragItemsLabel(moving) + " · " + reason,
                    Color.FromArgb(255, 237, 220), Color.FromArgb(157, 72, 23),
                    DragItemsDetails(moving) + "\n" + reason);
            }
        }

        private void SetDragGuide(string text, Color background, Color foreground,
            string tooltip = null)
        {
            if (dragGuide.Text != text)
            {
                dragGuide.Text = text;
            }
            string detail = tooltip ?? text;
            if (dragTooltip != detail)
            { dragTooltip = detail; helpTips.SetToolTip(dragGuide, detail); }
            if (dragGuide.BackColor != background) dragGuide.BackColor = background;
            if (dragGuide.ForeColor != foreground) dragGuide.ForeColor = foreground;
        }

        private void UpdateDragAutoScroll(Point point)
        {
            int direction = point.Y < 24 ? -1 :
                point.Y > proposed.ClientSize.Height - 24 ? 1 : 0;
            if (direction == dragScrollDirection) return;
            dragScrollDirection = direction;
            dragScrollTimer.Stop();
            if (direction != 0) dragScrollTimer.Start();
        }

        private void ClearDragTarget()
        {
            if (dragGhost != null) dragGhost.ClearTarget();
            dragExpandTimer.Stop();
            dragScrollTimer.Stop();
            dragExpandTarget = null;
            dragScrollDirection = 0;
            if (dragTarget != null && dragTarget.TreeView == proposed)
                dragTarget.BackColor = SearchBackground(dragTarget);
            dragTarget = null;
            dragTargetAllowed = false;
            if (dragActive && dragResolvedNodes != null)
            {
                SetDragGuide(DragItemsLabel(dragResolvedNodes) +
                    " 이동 중 · 오른쪽 폴더를 가리키세요.",
                    Color.FromArgb(238, 245, 252), Color.FromArgb(39, 70, 103),
                    DragItemsDetails(dragResolvedNodes));
            }
            else ResetDragGuide();
        }

        private void EndDragFeedback()
        {
            dragActive = false;
            dragSourceNode = null;
            dragResolvedNode = null;
            dragResolvedNodes = null;
            ClearDragTarget();
        }

        private void DisposeDragResources()
        {
            dragExpandTimer.Stop(); dragExpandTimer.Dispose();
            dragScrollTimer.Stop(); dragScrollTimer.Dispose();
        }
    }
}
