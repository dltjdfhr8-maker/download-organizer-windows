using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    public partial class StructureEditor
    {
        private readonly ImageList visualImages = new ImageList {
            ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit
        };
        private Bitmap folderGlyph, fileGlyph;
        private readonly Font detailFont = new Font("Malgun Gothic", 8F);
        private readonly TextBox searchBox = new TextBox();
        private readonly Label searchResultLabel = new Label();
        private readonly Label selectedPathLabel = new Label();
        private readonly Label changeSummaryLabel = new Label();
        private readonly Button nextMatchButton = new SoftButton();
        private readonly Timer searchTimer = new Timer { Interval = 220 };
        private readonly Dictionary<TreeNode, int> fileCounts = new Dictionary<TreeNode, int>();
        private readonly Dictionary<TreeNode, string> changeBadges = new Dictionary<TreeNode, string>();
        private readonly Dictionary<TreeNode, string> plannedPaths = new Dictionary<TreeNode, string>();
        private readonly List<TreeNode> searchMatches = new List<TreeNode>();
        private bool syncingSelection;
        private int searchIndex = -1;

        private static string VisualIconKey(PlanKind kind)
        {
            return kind == PlanKind.File ? "file" : "folder";
        }

        private static Bitmap FolderImage()
        {
            Bitmap image = new Bitmap(16, 16);
            using (Graphics graphics = Graphics.FromImage(image))
            using (SolidBrush tab = new SolidBrush(Color.FromArgb(167, 201, 231)))
            using (SolidBrush body = new SolidBrush(Color.FromArgb(104, 158, 212)))
            using (Pen outline = new Pen(Color.FromArgb(74, 126, 184)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillRectangle(tab, 1, 3, 7, 3);
                graphics.FillRectangle(body, 1, 5, 14, 9);
                graphics.DrawRectangle(outline, 1, 5, 13, 8);
            }
            return image;
        }

        private static Bitmap FileImage()
        {
            Bitmap image = new Bitmap(16, 16);
            using (Graphics graphics = Graphics.FromImage(image))
            using (SolidBrush paper = new SolidBrush(Color.White))
            using (Pen outline = new Pen(Color.FromArgb(77, 104, 133)))
            using (Pen line = new Pen(Color.FromArgb(111, 164, 205)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Point[] shape = { new Point(3, 1), new Point(10, 1), new Point(13, 4),
                    new Point(13, 14), new Point(3, 14) };
                graphics.FillPolygon(paper, shape);
                graphics.DrawPolygon(outline, shape);
                graphics.DrawLine(line, 5, 7, 11, 7);
                graphics.DrawLine(line, 5, 10, 11, 10);
            }
            return image;
        }

        private void InitializeVisualTree(TreeView tree)
        {
            if (visualImages.Images.Count == 0)
            {
                folderGlyph = FolderImage();
                fileGlyph = FileImage();
                visualImages.Images.Add("folder", folderGlyph);
                visualImages.Images.Add("file", fileGlyph);
            }
            tree.ImageList = visualImages;
            tree.ItemHeight = 26;
            tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            tree.DrawNode += DrawVisualNode;
            tree.AfterSelect += TreeSelectionChanged;
            HookFileSelection(tree);
        }

        private void DrawVisualNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null || e.Bounds.Width <= 0) return;
            PlanTag tag = NodeTag(e.Node);
            if (tag == null) return;
            TreeView tree = (TreeView)sender;
            bool dropTarget = e.Node == dragTarget;
            bool grouped = SelectedFiles(tree).Count > 1 && SelectedFiles(tree).Contains(e.Node);
            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            bool searchMatch = e.Node.BackColor != Color.Empty;
            if (dropTarget || grouped || selected || searchMatch)
            {
                Rectangle highlight = new Rectangle(e.Bounds.Left - 2, e.Bounds.Top,
                    Math.Max(10, tree.ClientSize.Width - e.Bounds.Left), e.Bounds.Height);
                Color fill = dropTarget ? (dragTargetAllowed ? Color.FromArgb(199, 239, 209) :
                    Color.FromArgb(255, 225, 202)) : grouped || selected ?
                    Color.FromArgb(225, 238, 252) : e.Node.BackColor;
                using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, highlight);
                if (dropTarget)
                {
                    Color edge = dragTargetAllowed ? Color.FromArgb(39, 150, 80) :
                        Color.FromArgb(211, 114, 49);
                    using (Pen pen = new Pen(edge)) e.Graphics.DrawRectangle(pen, highlight);
                }
            }
            Color labelColor = dropTarget ? (dragTargetAllowed ? Color.FromArgb(20, 87, 43) :
                Color.FromArgb(136, 59, 18)) : grouped || selected ?
                Color.FromArgb(25, 74, 130) : e.Node.ForeColor.IsEmpty ? tree.ForeColor : e.Node.ForeColor;
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(e.Graphics, e.Node.Text, tree.Font, e.Bounds, labelColor, flags);
            int x = e.Bounds.Right + 6;
            int count;
            if (tag.Kind != PlanKind.File && fileCounts.TryGetValue(e.Node, out count))
            {
                string text = "  " + count + "개";
                Color color = dropTarget ? (dragTargetAllowed ? Color.FromArgb(20, 87, 43) :
                    Color.FromArgb(136, 59, 18)) : grouped ? Color.FromArgb(25, 74, 130) :
                    selected ? Color.FromArgb(25, 74, 130) : Color.DimGray;
                TextRenderer.DrawText(e.Graphics, text, detailFont,
                    new Rectangle(x, e.Bounds.Top, 120, e.Bounds.Height), color, flags);
                x += TextRenderer.MeasureText(e.Graphics, text, detailFont,
                    new Size(1000, e.Bounds.Height), TextFormatFlags.NoPadding).Width + 8;
            }
            string badge;
            if (changeBadges.TryGetValue(e.Node, out badge) && badge != "")
            {
                Color color = dropTarget ? (dragTargetAllowed ? Color.FromArgb(20, 87, 43) :
                    Color.FromArgb(136, 59, 18)) : grouped ? Color.FromArgb(25, 74, 130) :
                    selected ? Color.FromArgb(25, 74, 130) : badge == "새 폴더" ?
                    Color.FromArgb(27, 125, 58) : Color.FromArgb(0, 91, 173);
                TextRenderer.DrawText(e.Graphics, "• " + badge, detailFont,
                    new Rectangle(x, e.Bounds.Top, 100, e.Bounds.Height), color, flags);
            }
        }

        private Control CreateSearchBar()
        {
            TableLayoutPanel bar = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0)
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 183));
            bar.Controls.Add(new Label {
                Text = "파일·폴더 찾기", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            searchBox.Dock = DockStyle.Fill;
            searchBox.Margin = new Padding(4, 9, 7, 8);
            searchBox.TextChanged += delegate {
                searchTimer.Stop();
                if (String.IsNullOrWhiteSpace(searchBox.Text)) RebuildSearchMatches(false);
                else searchTimer.Start();
            };
            searchBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                { GoToNextMatch(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape)
                { searchBox.Clear(); e.SuppressKeyPress = true; }
            };
            searchTimer.Tick += delegate { searchTimer.Stop(); RebuildSearchMatches(true); };
            bar.Controls.Add(searchBox, 1, 0);
            nextMatchButton.Text = "다음 결과";
            nextMatchButton.Dock = DockStyle.Fill;
            nextMatchButton.Enabled = false;
            nextMatchButton.Click += delegate { GoToNextMatch(); };
            bar.Controls.Add(nextMatchButton, 2, 0);
            searchResultLabel.Text = "";
            searchResultLabel.Dock = DockStyle.Fill;
            searchResultLabel.TextAlign = ContentAlignment.MiddleCenter;
            bar.Controls.Add(searchResultLabel, 3, 0);
            bar.Controls.Add(new Label {
                Text = "파랑: 이동  ·  초록: 새 폴더",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
                ForeColor = SoftUi.Muted
            }, 4, 0);
            helpTips.SetToolTip(searchBox, "파일이나 폴더 이름을 입력합니다. Enter를 누르면 다음 결과로 이동합니다.");
            helpTips.SetToolTip(nextMatchButton, "검색 결과를 차례대로 선택합니다.");
            return bar;
        }

        private Control CreateSelectionBar()
        {
            TableLayoutPanel bar = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0)
            };
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            selectedPathLabel.Text = "항목을 선택하면 현재 위치와 변경할 위치가 표시됩니다.";
            selectedPathLabel.Dock = DockStyle.Fill;
            selectedPathLabel.AutoEllipsis = true;
            selectedPathLabel.TextAlign = ContentAlignment.MiddleLeft;
            changeSummaryLabel.Text = "변경 예정 항목을 계산하는 중입니다.";
            changeSummaryLabel.Dock = DockStyle.Fill;
            changeSummaryLabel.AutoEllipsis = true;
            changeSummaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            changeSummaryLabel.ForeColor = SoftUi.Accent;
            bar.Controls.Add(selectedPathLabel, 0, 0);
            bar.Controls.Add(changeSummaryLabel, 0, 1);
            helpTips.SetToolTip(selectedPathLabel, "선택한 항목의 현재 위치와 변경 후 위치입니다.");
            helpTips.SetToolTip(changeSummaryLabel, "오른쪽 계획에 포함된 파일 및 폴더 변경 수입니다. 아직 실제 파일은 바뀌지 않았습니다.");
            return bar;
        }

        private int CountCurrentFiles(TreeNode node)
        {
            PlanTag tag = NodeTag(node);
            int count = tag != null && tag.Kind == PlanKind.File ? 1 : 0;
            foreach (TreeNode child in node.Nodes) count += CountCurrentFiles(child);
            fileCounts[node] = count;
            return count;
        }

        private void DecoratePlan(TreeNode node, string parentPath, bool inHold,
            HashSet<string> represented, int[] changes)
        {
            PlanTag tag = NodeTag(node);
            if (tag == null) return;
            string planned = tag.Kind == PlanKind.Root ? ProposedRootPath() :
                tag.Kind == PlanKind.Hold ? source :
                Path.Combine(parentPath, node.Text);
            plannedPaths[node] = planned;
            string badge = "";
            if (tag.Kind == PlanKind.Root && !planned.Equals(root, StringComparison.OrdinalIgnoreCase))
            { badge = "이름 변경"; changes[1]++; }
            else if (tag.Kind == PlanKind.Folder)
            {
                if (tag.OriginalPath == "") { badge = "새 폴더"; changes[2]++; }
                else
                {
                    represented.Add(tag.OriginalPath);
                    if (!tag.OriginalPath.Equals(planned, StringComparison.OrdinalIgnoreCase))
                    { badge = "이동"; changes[1]++; }
                }
            }
            else if (tag.Kind == PlanKind.File &&
                !tag.OriginalPath.Equals(planned, StringComparison.OrdinalIgnoreCase))
            {
                badge = Path.GetDirectoryName(tag.OriginalPath).Equals(
                    Path.GetDirectoryName(planned), StringComparison.OrdinalIgnoreCase) ?
                    "이름 변경" : "이동";
                changes[0]++;
            }
            changeBadges[node] = badge;
            Color color = badge == "새 폴더" ? Color.FromArgb(27, 125, 58) :
                (badge == "이동" || badge == "이름 변경") ? Color.FromArgb(0, 91, 173) :
                tag.Kind == PlanKind.Hold ? Color.DimGray : Color.Black;
            if (node.ForeColor != color) node.ForeColor = color;
            node.ToolTipText = tag.OriginalPath == "" ? planned :
                tag.OriginalPath.Equals(planned, StringComparison.OrdinalIgnoreCase) ?
                planned : tag.OriginalPath + "  →  " + planned;
            int fileCount = tag.Kind == PlanKind.File ? 1 : 0;
            foreach (TreeNode child in node.Nodes)
            {
                DecoratePlan(child, planned, tag.Kind == PlanKind.Hold, represented, changes);
                fileCount += fileCounts[child];
            }
            fileCounts[node] = fileCount;
        }

        private void RefreshPlanVisuals()
        {
            if (!Loaded || currentRoot == null || proposedRoot == null) return;
            fileCounts.Clear(); changeBadges.Clear(); plannedPaths.Clear();
            CountCurrentFiles(currentRoot);
            CountCurrentFiles(currentDownloads);
            HashSet<string> represented = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int[] changes = new int[3];
            DecoratePlan(proposedRoot, root, false, represented, changes);
            DecoratePlan(proposedHold, source, true, represented, changes);
            int removed = snapshot == null ? 0 : snapshot.TargetDirectories.Count - represented.Count;
            changeSummaryLabel.Text = "변경 예정: 파일 " + changes[0] + "개 변경 · 폴더 " +
                changes[1] + "개 이동 · 새 폴더 " + changes[2] + "개 · 빈 폴더 " + removed + "개 정리";
            current.Invalidate(); proposed.Invalidate();
            if (!String.IsNullOrWhiteSpace(searchBox.Text)) RebuildSearchMatches(false);
            UpdateSelectionDetails();
        }

        private static IEnumerable<TreeNode> AllNodes(TreeView tree)
        {
            Stack<TreeNode> pending = new Stack<TreeNode>();
            for (int i = tree.Nodes.Count - 1; i >= 0; i--) pending.Push(tree.Nodes[i]);
            while (pending.Count > 0)
            {
                TreeNode node = pending.Pop();
                yield return node;
                for (int i = node.Nodes.Count - 1; i >= 0; i--) pending.Push(node.Nodes[i]);
            }
        }

        private void RebuildSearchMatches(bool selectFirst = false)
        {
            searchMatches.Clear(); searchIndex = -1;
            string query = searchBox.Text.Trim();
            foreach (TreeNode node in AllNodes(current))
            {
                Color color = query != "" && node.Text.IndexOf(query,
                    StringComparison.CurrentCultureIgnoreCase) >= 0 ? Color.FromArgb(255, 246, 190) : Color.Empty;
                if (node.BackColor != color) node.BackColor = color;
            }
            foreach (TreeNode node in AllNodes(proposed))
            {
                bool match = query != "" && node.Text.IndexOf(query,
                    StringComparison.CurrentCultureIgnoreCase) >= 0;
                Color color = match ? Color.FromArgb(255, 246, 190) : Color.Empty;
                if (node.BackColor != color) node.BackColor = color;
                if (match) searchMatches.Add(node);
            }
            nextMatchButton.Enabled = searchMatches.Count > 0;
            searchResultLabel.Text = query == "" ? "" : searchMatches.Count + "개 찾음";
            if (selectFirst && searchMatches.Count > 0) GoToNextMatch();
        }

        private void GoToNextMatch()
        {
            if (searchTimer.Enabled)
            { searchTimer.Stop(); RebuildSearchMatches(false); }
            if (searchMatches.Count == 0) return;
            searchIndex = (searchIndex + 1) % searchMatches.Count;
            TreeNode node = searchMatches[searchIndex];
            for (TreeNode parent = node.Parent; parent != null; parent = parent.Parent) parent.Expand();
            if (NodeTag(node) != null && NodeTag(node).Kind == PlanKind.File)
                UpdateFileSelection(proposed, node, false, false);
            else ClearSelectedFiles();
            proposed.SelectedNode = node;
            node.EnsureVisible();
            searchResultLabel.Text = (searchIndex + 1) + "/" + searchMatches.Count + " 찾음";
        }

        private TreeNode Counterpart(TreeView destination, PlanTag tag)
        {
            if (tag == null) return null;
            if (tag.Kind == PlanKind.Root) return destination.Nodes.Count > 0 ? destination.Nodes[0] : null;
            if (tag.Kind == PlanKind.Hold) return destination.Nodes.Count > 1 ? destination.Nodes[1] : null;
            if (tag.OriginalPath == "") return null;
            foreach (TreeNode top in destination.Nodes)
            {
                TreeNode found = FindRightNode(top, tag);
                if (found != null) return found;
            }
            return null;
        }

        private void TreeSelectionChanged(object sender, TreeViewEventArgs e)
        {
            if (syncingSelection || !Loaded) return;
            lastChosenNode = e.Node;
            syncingSelection = true;
            try
            {
                TreeView other = sender == current ? proposed : current;
                TreeNode counterpart = Counterpart(other, NodeTag(e.Node));
                if (counterpart != null)
                {
                    for (TreeNode parent = counterpart.Parent; parent != null; parent = parent.Parent)
                        parent.Expand();
                    other.SelectedNode = counterpart;
                    counterpart.EnsureVisible();
                }
                else if (sender == proposed) current.SelectedNode = null;
                else proposed.SelectedNode = null;
            }
            finally { syncingSelection = false; }
            UpdateSelectionDetails();
        }

        private string DisplayLocation(string path)
        {
            if (String.IsNullOrEmpty(path)) return "없음";
            string full = Engine.Full(path);
            string newRoot = ProposedRootPath();
            string anchor = Engine.SameOrInside(full, newRoot) ? newRoot :
                Engine.SameOrInside(full, root) ? root :
                Engine.SameOrInside(full, source) ? source : "";
            if (anchor == "") return full;
            string label = anchor.Equals(root, StringComparison.OrdinalIgnoreCase) ?
                "정리 폴더" : anchor.Equals(newRoot, StringComparison.OrdinalIgnoreCase) ?
                "정리 폴더(변경 후)" : "감시 폴더";
            string relative = full.Substring(anchor.Length).TrimStart(Path.DirectorySeparatorChar);
            return relative == "" ? label : label + " > " +
                relative.Replace(Path.DirectorySeparatorChar.ToString(), " > ");
        }

        private void UpdateSelectionDetails()
        {
            HashSet<TreeNode> grouped = selectedProposedFiles.Count > 1 ? selectedProposedFiles :
                selectedCurrentFiles.Count > 1 ? selectedCurrentFiles : null;
            if (grouped != null)
            {
                TreeView tree = grouped == selectedProposedFiles ? proposed : current;
                List<TreeNode> ordered = AllNodes(tree).Where(n => grouped.Contains(n)).ToList();
                selectedPathLabel.Text = DragItemsLabel(ordered) +
                    " 선택됨 · 끌어 옮기거나 일괄 이름 변경을 누르세요.";
                helpTips.SetToolTip(selectedPathLabel, DragItemsDetails(ordered));
                return;
            }
            TreeNode node = proposed.SelectedNode ?? current.SelectedNode;
            if (node == null)
            {
                selectedPathLabel.Text = "항목을 선택하면 현재 위치와 변경할 위치가 표시됩니다.";
                return;
            }
            PlanTag tag = NodeTag(node);
            string planned;
            if (tag == null || !plannedPaths.TryGetValue(node, out planned)) planned = tag == null ? "" : tag.OriginalPath;
            string original = tag == null ? "" : tag.OriginalPath;
            if (node.TreeView == current && tag != null && tag.Kind == PlanKind.Folder &&
                Counterpart(proposed, tag) == null)
                selectedPathLabel.Text = "현재: " + DisplayLocation(original) + "    →    변경 후: 빈 폴더 정리";
            else if (original == "")
                selectedPathLabel.Text = "새 항목: " + DisplayLocation(planned);
            else if (original.Equals(planned, StringComparison.OrdinalIgnoreCase))
                selectedPathLabel.Text = "위치: " + DisplayLocation(planned);
            else
                selectedPathLabel.Text = "현재: " + DisplayLocation(original) +
                    "    →    변경 후: " + DisplayLocation(planned);
            helpTips.SetToolTip(selectedPathLabel, "현재: " + original + "\n변경 후: " + planned);
        }

        private void DisposeVisualResources()
        {
            searchTimer.Stop(); searchTimer.Dispose();
            visualImages.Dispose();
            if (folderGlyph != null) folderGlyph.Dispose();
            if (fileGlyph != null) fileGlyph.Dispose();
            detailFont.Dispose();
        }
    }
}
