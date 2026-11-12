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
        private const int IconSize = 20;
        private static readonly Font FolderNodeFont = new Font("Malgun Gothic", 10F, FontStyle.Bold);
        private readonly Font treeFont = new Font("Malgun Gothic", 10F);
        private readonly Font sectionFont = new Font("Malgun Gothic", 9F, FontStyle.Bold);
        private readonly Font countFont = new Font("Malgun Gothic", 9F);
        private readonly Font badgeFont = new Font("Malgun Gothic", 8.5F, FontStyle.Bold);
        private readonly ImageList visualImages = new ImageList {
            ImageSize = new Size(IconSize, IconSize), ColorDepth = ColorDepth.Depth32Bit
        };
        private readonly LocationCard sourceCard = new LocationCard(false), targetCard = new LocationCard(true);
        private readonly TextBox searchBox = new TextBox();
        private readonly Label searchResultLabel = new Label();
        private readonly Label selectedPathLabel = new Label();
        private readonly Label changeSummaryLabel = new Label();
        private Label selectionCaption, selectedNameLabel, currentLocationLabel, plannedLocationLabel;
        private readonly PictureBox selectedIcon = new PictureBox();
        private readonly Button nextMatchButton = new SoftButton();
        private readonly Timer searchTimer = new Timer { Interval = 220 };
        private readonly Dictionary<TreeNode, int> fileCounts = new Dictionary<TreeNode, int>();
        private readonly Dictionary<TreeNode, string> changeBadges = new Dictionary<TreeNode, string>();
        private readonly Dictionary<TreeNode, string> plannedPaths = new Dictionary<TreeNode, string>();
        private readonly List<TreeNode> searchMatches = new List<TreeNode>();
        private bool syncingSelection;
        private int searchIndex = -1;

        private static readonly Dictionary<string, string> FileKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { ".pdf", "file-pdf" },
            { ".png", "file-image" }, { ".jpg", "file-image" }, { ".jpeg", "file-image" }, { ".gif", "file-image" },
            { ".bmp", "file-image" }, { ".webp", "file-image" }, { ".svg", "file-image" }, { ".heic", "file-image" },
            { ".hwp", "file-doc" }, { ".hwpx", "file-doc" }, { ".doc", "file-doc" }, { ".docx", "file-doc" },
            { ".rtf", "file-doc" }, { ".odt", "file-doc" },
            { ".xls", "file-sheet" }, { ".xlsx", "file-sheet" }, { ".xlsm", "file-sheet" }, { ".csv", "file-sheet" },
            { ".ppt", "file-slide" }, { ".pptx", "file-slide" }, { ".key", "file-slide" },
            { ".zip", "file-archive" }, { ".7z", "file-archive" }, { ".rar", "file-archive" }, { ".egg", "file-archive" },
            { ".js", "file-code" }, { ".ts", "file-code" }, { ".json", "file-code" }, { ".html", "file-code" },
            { ".htm", "file-code" }, { ".css", "file-code" }, { ".cs", "file-code" }, { ".py", "file-code" },
            { ".xml", "file-code" }, { ".md", "file-code" }
        };

        private static string VisualIconKey(PlanKind kind, string name = "")
        {
            if (kind != PlanKind.File) return "folder";
            string extension = "";
            try { extension = Path.GetExtension(name ?? ""); } catch { }
            string key;
            return FileKinds.TryGetValue(extension, out key) ? key : "file";
        }

        private static Bitmap FolderImage()
        {
            Bitmap image = new Bitmap(IconSize, IconSize);
            using (Graphics graphics = Graphics.FromImage(image))
                SoftDraw.FolderGlyph(graphics, new Rectangle(0, 0, IconSize, IconSize),
                    Color.FromArgb(76, 132, 222), Color.FromArgb(146, 183, 240));
            return image;
        }

        private static Bitmap FileImage(Color band)
        {
            Bitmap image = new Bitmap(IconSize, IconSize);
            using (Graphics graphics = Graphics.FromImage(image))
            using (SolidBrush paper = new SolidBrush(Color.White))
            using (Pen outline = new Pen(Color.FromArgb(190, 201, 216)))
            using (SolidBrush mark = new SolidBrush(band))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Point[] shape = { new Point(4, 1), new Point(12, 1), new Point(16, 5),
                    new Point(16, 18), new Point(4, 18) };
                graphics.FillPolygon(paper, shape);
                graphics.DrawPolygon(outline, shape);
                graphics.DrawLine(outline, 12, 1, 12, 5);
                graphics.DrawLine(outline, 12, 5, 16, 5);
                using (GraphicsPath bar = SoftDraw.Round(new Rectangle(6, 12, 8, 3), 1))
                    graphics.FillPath(mark, bar);
            }
            return image;
        }

        private void InitializeVisualTree(TreeView tree)
        {
            if (visualImages.Images.Count == 0)
            {
                visualImages.Images.Add("folder", FolderImage());
                visualImages.Images.Add("file", FileImage(Color.FromArgb(140, 154, 173)));
                visualImages.Images.Add("file-pdf", FileImage(Color.FromArgb(229, 83, 75)));
                visualImages.Images.Add("file-image", FileImage(Color.FromArgb(63, 140, 226)));
                visualImages.Images.Add("file-doc", FileImage(Color.FromArgb(70, 110, 220)));
                visualImages.Images.Add("file-sheet", FileImage(Color.FromArgb(33, 150, 83)));
                visualImages.Images.Add("file-slide", FileImage(Color.FromArgb(232, 126, 44)));
                visualImages.Images.Add("file-archive", FileImage(Color.FromArgb(128, 128, 140)));
                visualImages.Images.Add("file-code", FileImage(Color.FromArgb(132, 98, 206)));
            }
            tree.ImageList = visualImages;
            tree.Font = treeFont;
            tree.ItemHeight = 32;
            tree.Indent = 22;
            tree.ShowLines = false;
            tree.FullRowSelect = true;
            tree.BorderStyle = BorderStyle.None;
            tree.DrawMode = TreeViewDrawMode.OwnerDrawAll;
            tree.DrawNode += DrawVisualNode;
            tree.AfterSelect += TreeSelectionChanged;
            HookFileSelection(tree);
        }

        private static Color BadgeFill(string badge)
        {
            return badge == "이동" ? SoftUi.AccentSoft : SoftDraw.GreenSoft;
        }

        private static Color BadgeInk(string badge)
        {
            return badge == "이동" ? SoftDraw.BlueInk : SoftDraw.GreenInk;
        }

        // Draws the whole row. Positions are derived from the native label bounds so clicks on
        // the chevron, icon and name still hit the same places the TreeView expects.
        private void DrawVisualNode(object sender, DrawTreeNodeEventArgs e)
        {
            e.DrawDefault = false;
            TreeView tree = (TreeView)sender;
            if (e.Node == null || e.Bounds.IsEmpty || e.Bounds.Height <= 0) return;
            Graphics g = e.Graphics;
            Rectangle row = new Rectangle(0, e.Bounds.Top, tree.ClientSize.Width, e.Bounds.Height);
            using (SolidBrush back = new SolidBrush(tree.BackColor)) g.FillRectangle(back, row);
            PlanTag tag = NodeTag(e.Node);
            if (tag == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle label = e.Node.Bounds;
            int iconLeft = label.Left - IconSize - 3;
            bool section = tag.Kind == PlanKind.Hold;
            bool dropTarget = e.Node == dragTarget;
            HashSet<TreeNode> chosen = SelectedFiles(tree);
            bool grouped = chosen.Count > 1 && chosen.Contains(e.Node);
            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            bool searchMatch = !dropTarget && e.Node.BackColor != Color.Empty;
            string badge;
            if (!changeBadges.TryGetValue(e.Node, out badge) || badge == null) badge = "";
            bool moved = tree == proposed && tag.Kind == PlanKind.File && badge == "이동";

            if (section && e.Node.PrevVisibleNode != null)
                using (Pen line = new Pen(Color.FromArgb(232, 237, 244)))
                    g.DrawLine(line, 4, row.Top + 1, row.Right - 8, row.Top + 1);

            int bandLeft = Math.Max(2, iconLeft - 8);
            Rectangle band = new Rectangle(bandLeft, row.Top + 2, Math.Max(10, row.Right - bandLeft - 6), row.Height - 4);
            Color fill = Color.Empty, edge = Color.Empty;
            if (dropTarget)
            {
                fill = dragTargetAllowed ? Color.FromArgb(199, 239, 209) : Color.FromArgb(255, 225, 202);
                edge = dragTargetAllowed ? Color.FromArgb(39, 150, 80) : Color.FromArgb(211, 114, 49);
            }
            else if (selected || grouped) { fill = Color.FromArgb(232, 241, 253); edge = Color.FromArgb(178, 204, 239); }
            else if (searchMatch) fill = e.Node.BackColor;
            else if (moved) { fill = Color.FromArgb(237, 248, 241); edge = Color.FromArgb(196, 230, 209); }
            if (fill != Color.Empty)
                using (GraphicsPath path = SoftDraw.Round(band, 8))
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                    if (edge != Color.Empty) using (Pen pen = new Pen(edge)) g.DrawPath(pen, path);
                }

            if (e.Node.Nodes.Count > 0)
            {
                int cx = iconLeft - tree.Indent / 2 + 1, cy = row.Top + row.Height / 2;
                Point[] arrow = e.Node.IsExpanded ?
                    new[] { new Point(cx - 5, cy - 2), new Point(cx + 5, cy - 2), new Point(cx, cy + 4) } :
                    new[] { new Point(cx - 2, cy - 5), new Point(cx - 2, cy + 5), new Point(cx + 4, cy) };
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(132, 146, 166))) g.FillPolygon(brush, arrow);
            }

            if (!section)
            {
                Image image = visualImages.Images[e.Node.ImageKey] ?? visualImages.Images["file"];
                g.DrawImage(image, iconLeft, row.Top + (row.Height - IconSize) / 2, IconSize, IconSize);
            }

            Font font = section ? sectionFont : e.Node.NodeFont ?? tree.Font;
            Color ink = dropTarget ? (dragTargetAllowed ? Color.FromArgb(20, 87, 43) : Color.FromArgb(136, 59, 18)) :
                selected || grouped ? Color.FromArgb(25, 74, 130) : section ? SoftUi.Muted :
                tag.Kind == PlanKind.File && tree == current ? Color.FromArgb(70, 84, 104) : SoftUi.Text;
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            int count;
            string countText = tag.Kind != PlanKind.File && fileCounts.TryGetValue(e.Node, out count) ? count + "개" : "";
            int countWidth = countText == "" ? 0 :
                TextRenderer.MeasureText(g, countText, countFont, new Size(1000, row.Height), flags).Width + 10;
            int pillWidth = badge == "" ? 0 : SoftDraw.PillWidth(badge, badgeFont);
            int labelLeft = section ? Math.Max(4, iconLeft) : label.Left;
            int textWidth = TextRenderer.MeasureText(g, e.Node.Text, font, new Size(int.MaxValue, row.Height), flags).Width + 2;
            int limit = row.Right - 14 - countWidth - (pillWidth > 0 ? pillWidth + 12 : 0) - labelLeft;
            Rectangle textBox = new Rectangle(labelLeft, row.Top, Math.Max(24, Math.Min(textWidth, limit)), row.Height);
            TextRenderer.DrawText(g, e.Node.Text, font, textBox, ink, flags | TextFormatFlags.EndEllipsis);
            int x = textBox.Right + 10;
            if (countText != "")
            {
                TextRenderer.DrawText(g, countText, countFont, new Rectangle(x, row.Top, countWidth, row.Height),
                    selected || grouped ? Color.FromArgb(25, 74, 130) : Color.FromArgb(138, 151, 170), flags);
                x += countWidth;
            }
            if (badge != "")
            {
                Rectangle pill = new Rectangle(Math.Max(x + 2, row.Right - 14 - pillWidth),
                    row.Top + (row.Height - 22) / 2, pillWidth, 22);
                SoftDraw.Pill(g, pill, badge, badgeFont, BadgeFill(badge), BadgeInk(badge));
            }
        }

        private Control CreateSearchBar()
        {
            TableLayoutPanel bar = new TableLayoutPanel {
                Dock = DockStyle.Top, Height = 36, ColumnCount = 3, RowCount = 1,
                Margin = new Padding(0, 2, 0, 0), BackColor = SoftUi.Surface
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            searchBox.Dock = DockStyle.Fill;
            searchBox.Margin = new Padding(0, 6, 8, 4);
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
            bar.Controls.Add(searchBox, 0, 0);
            nextMatchButton.Text = "다음 결과";
            nextMatchButton.Dock = DockStyle.Fill;
            nextMatchButton.Margin = new Padding(0, 2, 6, 0);
            nextMatchButton.Enabled = false;
            nextMatchButton.Click += delegate { GoToNextMatch(); };
            bar.Controls.Add(nextMatchButton, 1, 0);
            searchResultLabel.Text = "";
            searchResultLabel.Dock = DockStyle.Fill;
            searchResultLabel.ForeColor = SoftUi.Muted;
            searchResultLabel.TextAlign = ContentAlignment.MiddleCenter;
            bar.Controls.Add(searchResultLabel, 2, 0);
            helpTips.SetToolTip(searchBox, "파일이나 폴더 이름을 입력합니다. Enter를 누르면 다음 결과로 이동합니다.");
            helpTips.SetToolTip(nextMatchButton, "검색 결과를 차례대로 선택합니다.");
            return bar;
        }

        // Bottom card: what is selected, where it is now, where it will go, and the apply button.
        private Control CreateSelectionCard(Button apply, Button close)
        {
            CardPanel card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 12, 22, 10), Margin = new Padding(0, 0, 0, 4) };
            TableLayoutPanel body = Rows(74, 13, -1);
            TableLayoutPanel details = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = new Padding(0), BackColor = SoftUi.Surface
            };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            details.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selectionCaption = TextLabel("선택한 항목", 9F, FontStyle.Regular, SoftUi.Muted);
            selectedNameLabel = TextLabel("항목을 선택하면 현재 위치와 적용 후 위치가 보여요", 12.5F, FontStyle.Bold, SoftUi.Muted);
            currentLocationLabel = TextLabel("-", 11.5F, FontStyle.Bold, SoftUi.Text);
            plannedLocationLabel = TextLabel("-", 11.5F, FontStyle.Bold, SoftDraw.GreenInk);
            TableLayoutPanel name = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = SoftUi.Surface
            };
            name.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
            name.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            name.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selectedIcon.Dock = DockStyle.Fill; selectedIcon.SizeMode = PictureBoxSizeMode.CenterImage;
            selectedIcon.Margin = new Padding(0); selectedIcon.BackColor = SoftUi.Surface;
            name.Controls.Add(selectedIcon, 0, 0);
            name.Controls.Add(selectedNameLabel, 1, 0);
            details.Controls.Add(selectionCaption, 0, 0);
            details.Controls.Add(name, 0, 1);
            details.Controls.Add(TextLabel("현재 위치", 9F, FontStyle.Regular, SoftUi.Muted), 1, 0);
            details.Controls.Add(currentLocationLabel, 1, 1);
            Label arrow = TextLabel("⟶", 18F, FontStyle.Regular, Color.FromArgb(150, 165, 186));
            arrow.TextAlign = ContentAlignment.MiddleCenter;
            details.Controls.Add(arrow, 2, 1);
            details.Controls.Add(TextLabel("적용 후 위치", 9F, FontStyle.Regular, SoftDraw.GreenInk), 3, 0);
            details.Controls.Add(plannedLocationLabel, 3, 1);
            body.Controls.Add(details, 0, 0);
            body.Controls.Add(Divider(), 0, 1);

            TableLayoutPanel footer = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = SoftUi.Surface
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            changeSummaryLabel.Text = "변경 예정 항목을 계산하는 중입니다.";
            changeSummaryLabel.Dock = DockStyle.Fill;
            changeSummaryLabel.AutoEllipsis = true;
            changeSummaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            changeSummaryLabel.ForeColor = SoftUi.Muted;
            changeSummaryLabel.Font = new Font("Malgun Gothic", 9.5F);
            footer.Controls.Add(changeSummaryLabel, 0, 0);
            FlowLayoutPanel buttons = new FlowLayoutPanel {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Margin = new Padding(0), BackColor = SoftUi.Surface
            };
            apply.Margin = new Padding(8, 2, 0, 0); close.Margin = new Padding(8, 2, 0, 0);
            buttons.Controls.Add(apply); buttons.Controls.Add(close);
            footer.Controls.Add(buttons, 1, 0);
            body.Controls.Add(footer, 0, 2);
            card.Controls.Add(body);

            selectedPathLabel.Text = "항목을 선택하면 현재 위치와 변경할 위치가 표시됩니다.";
            helpTips.SetToolTip(changeSummaryLabel, "오른쪽 계획에 포함된 파일 및 폴더 변경 수입니다. 아직 실제 파일은 바뀌지 않았습니다.");
            return card;
        }

        private static string FolderName(string path)
        {
            string name = Path.GetFileName(Engine.Full(path).TrimEnd(Path.DirectorySeparatorChar));
            return name == "" ? path : name;
        }

        private void UpdateLocationCards()
        {
            bool same = source.Equals(root, StringComparison.OrdinalIgnoreCase);
            sourceCard.SetContent(same ? "새 파일이 들어오는 곳 · 정리할 폴더와 같음" : "새 파일이 들어오는 곳",
                FolderName(source), source, "감시 폴더");
            string planned = ProposedRootPath();
            bool renamed = !planned.Equals(root, StringComparison.OrdinalIgnoreCase);
            string title = renamed ? FolderName(root) + "  →  " + FolderName(planned) : FolderName(root);
            if (targetCard.TitleText != title)
                targetCard.SetContent("이번에 구조를 바꿀 곳", title, root, renamed ? "이름 변경 예정" : "정리할 폴더");
        }

        // Readable folder location using the real folder names, e.g. "교육자료 > 홍보".
        private string FriendlyLocation(string path)
        {
            if (String.IsNullOrEmpty(path)) return "-";
            string full = Engine.Full(path);
            string newRoot = ProposedRootPath();
            foreach (string anchor in new[] { newRoot, root, source })
                if (Engine.SameOrInside(full, anchor))
                {
                    string relative = full.Substring(anchor.Length).TrimStart(Path.DirectorySeparatorChar);
                    string name = FolderName(anchor);
                    return relative == "" ? name : name + " > " +
                        relative.Replace(Path.DirectorySeparatorChar.ToString(), " > ");
                }
            return full;
        }

        private void ShowSelection(string caption, TreeNode iconNode, string name, bool nameMuted,
            string currentText, string plannedText, bool changed)
        {
            if (selectedNameLabel == null) return;
            selectionCaption.Text = caption;
            selectedIcon.Image = iconNode == null ? null : visualImages.Images[iconNode.ImageKey];
            selectedNameLabel.Text = name;
            selectedNameLabel.ForeColor = nameMuted ? SoftUi.Muted : SoftUi.Text;
            currentLocationLabel.Text = currentText;
            plannedLocationLabel.Text = plannedText;
            plannedLocationLabel.ForeColor = changed ? SoftDraw.GreenInk : SoftUi.Muted;
            helpTips.SetToolTip(currentLocationLabel, currentText);
            helpTips.SetToolTip(plannedLocationLabel, plannedText);
            helpTips.SetToolTip(selectedNameLabel, name);
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
            // An item that only follows its renamed or moved parent is not a change of its own.
            PlanTag parentTag = NodeTag(node.Parent);
            bool inherited = tag.OriginalPath != "" && parentTag != null && parentTag.OriginalPath != "" &&
                (tag.Kind == PlanKind.File || tag.Kind == PlanKind.Folder) &&
                Path.GetDirectoryName(tag.OriginalPath).Equals(parentTag.OriginalPath, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(tag.OriginalPath).Equals(node.Text, StringComparison.OrdinalIgnoreCase);
            string badge = "";
            if (tag.Kind == PlanKind.Root && !planned.Equals(root, StringComparison.OrdinalIgnoreCase))
            { badge = "이름 변경"; changes[4]++; }
            else if (tag.Kind == PlanKind.Folder)
            {
                if (tag.OriginalPath == "") { badge = "새 폴더"; changes[2]++; }
                else
                {
                    represented.Add(tag.OriginalPath);
                    if (!tag.OriginalPath.Equals(planned, StringComparison.OrdinalIgnoreCase) && !inherited)
                    {
                        badge = Path.GetDirectoryName(tag.OriginalPath).Equals(Path.GetDirectoryName(planned),
                            StringComparison.OrdinalIgnoreCase) ? "이름 변경" : "이동";
                        changes[badge == "이동" ? 1 : 4]++;
                    }
                }
            }
            else if (tag.Kind == PlanKind.File && !inherited &&
                !tag.OriginalPath.Equals(planned, StringComparison.OrdinalIgnoreCase))
            {
                badge = Path.GetDirectoryName(tag.OriginalPath).Equals(
                    Path.GetDirectoryName(planned), StringComparison.OrdinalIgnoreCase) ?
                    "이름 변경" : "이동";
                changes[badge == "이동" ? 0 : 3]++;
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
            int[] changes = new int[5];
            DecoratePlan(proposedRoot, root, false, represented, changes);
            DecoratePlan(proposedHold, source, true, represented, changes);
            int removed = snapshot == null ? 0 : snapshot.TargetDirectories.Count - represented.Count;
            List<string> parts = new List<string>();
            if (changes[0] > 0) parts.Add("파일 " + changes[0] + "개 이동");
            if (changes[3] > 0) parts.Add("파일 이름 " + changes[3] + "개 변경");
            if (changes[1] > 0) parts.Add("폴더 " + changes[1] + "개 이동");
            if (changes[4] > 0) parts.Add("폴더 이름 " + changes[4] + "개 변경");
            if (changes[2] > 0) parts.Add("새 폴더 " + changes[2] + "개");
            if (removed > 0) parts.Add("빈 폴더 " + removed + "개 정리");
            changeSummaryLabel.Text = parts.Count == 0 ? "변경 예정: 아직 바뀐 것이 없어요" :
                "변경 예정: " + String.Join(" · ", parts.ToArray());
            current.Invalidate(); proposed.Invalidate();
            UpdateLocationCards();
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
            // The selection set can briefly hold nodes that are no longer in the tree (after a
            // move or rebuild); only nodes still in the tree count, and the list may be empty.
            List<TreeNode> ordered = grouped == null ? new List<TreeNode>() :
                AllNodes(grouped == selectedProposedFiles ? proposed : current)
                    .Where(n => grouped.Contains(n)).ToList();
            if (ordered.Count > 1)
            {
                selectedPathLabel.Text = DragItemsLabel(ordered) +
                    " 선택됨 · 끌어 옮기거나 일괄 이름 변경을 누르세요.";
                helpTips.SetToolTip(selectedPathLabel, DragItemsDetails(ordered));
                List<string> folders = ordered.Select(n => NodeTag(n) == null ? "" :
                    Path.GetDirectoryName(NodeTag(n).OriginalPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                ShowSelection("선택한 파일 " + ordered.Count + "개", ordered[0], DragItemsLabel(ordered), false,
                    folders.Count == 1 ? FriendlyLocation(folders[0]) : "여러 위치",
                    "끌어 옮기거나 일괄 이름 변경", false);
                return;
            }
            TreeNode node = proposed.SelectedNode ?? current.SelectedNode;
            if (node == null)
            {
                selectedPathLabel.Text = "항목을 선택하면 현재 위치와 변경할 위치가 표시됩니다.";
                ShowSelection("선택한 항목", null, "항목을 선택하면 현재 위치와 적용 후 위치가 보여요", true, "-", "-", false);
                return;
            }
            PlanTag tag = NodeTag(node);
            string planned;
            if (tag == null || !plannedPaths.TryGetValue(node, out planned)) planned = tag == null ? "" : tag.OriginalPath;
            string original = tag == null ? "" : tag.OriginalPath;
            bool isFile = tag != null && tag.Kind == PlanKind.File;
            string caption = isFile ? "선택한 파일" : "선택한 폴더";
            Func<string, string> place = path => String.IsNullOrEmpty(path) ? "-" :
                FriendlyLocation(isFile ? Path.GetDirectoryName(path) : path);
            if (node.TreeView == current && tag != null && tag.Kind == PlanKind.Folder &&
                Counterpart(proposed, tag) == null)
            {
                selectedPathLabel.Text = "현재: " + DisplayLocation(original) + "    →    변경 후: 빈 폴더 정리";
                ShowSelection(caption, node, node.Text, false, place(original), "빈 폴더 정리", true);
            }
            else if (original == "")
            {
                selectedPathLabel.Text = "새 항목: " + DisplayLocation(planned);
                ShowSelection(caption, node, node.Text, false, "새로 만들 폴더", place(planned), true);
            }
            else if (original.Equals(planned, StringComparison.OrdinalIgnoreCase))
            {
                selectedPathLabel.Text = "위치: " + DisplayLocation(planned);
                ShowSelection(caption, node, node.Text, false, place(original), "변경 없음", false);
            }
            else
            {
                selectedPathLabel.Text = "현재: " + DisplayLocation(original) +
                    "    →    변경 후: " + DisplayLocation(planned);
                bool renamedOnly = Path.GetDirectoryName(original).Equals(Path.GetDirectoryName(planned),
                    StringComparison.OrdinalIgnoreCase);
                ShowSelection(caption, node, node.Text, false, place(original),
                    renamedOnly && isFile ? place(planned) + " · 새 이름 " + Path.GetFileName(planned) : place(planned), true);
            }
            helpTips.SetToolTip(selectedPathLabel, "현재: " + original + "\n변경 후: " + planned);
        }

        private void DisposeVisualResources()
        {
            searchTimer.Stop(); searchTimer.Dispose();
            visualImages.Dispose();
            treeFont.Dispose(); sectionFont.Dispose(); countFont.Dispose(); badgeFont.Dispose();
        }
    }
}
