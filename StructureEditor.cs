using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal enum PlanKind { Root, Hold, Folder, File, SourceFolder }

    internal class PlanTag
    {
        public PlanKind Kind;
        public string OriginalPath = "";
        public bool FromDownloads;
        public bool IsCopy;
        public long Length;
        public long LastWriteTicks;
        public long CreationTicks;
    }

    internal class DraggedNode
    {
        public TreeNode Node;
        public List<TreeNode> Nodes = new List<TreeNode>();
        public bool FromLeft;
    }

    internal class PlanData
    {
        public List<PlannedFile> Files = new List<PlannedFile>();
        public List<string> Directories = new List<string>();
        public List<FolderDelta> FolderChanges = new List<FolderDelta>();
        public HashSet<string> RepresentedOriginalDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal class FolderDelta
    {
        public string From = "", To = "";
        public string Reason = "";
    }

    internal class StructureApplyProgress
    {
        public int Completed, Total;
        public string FilePath, Phase;
    }

    internal class TreeBuildResult
    {
        public StructureSnapshot Snapshot;
        public HashSet<string> DownloadFiles;
        public TreeNode CurrentRoot, CurrentDownloads, ProposedRoot, ProposedHold;
    }

    public partial class StructureEditor : Form
    {
        private string source;
        private string root;
        private readonly string topicsFile;
        private StructureSnapshot snapshot;
        private HashSet<string> downloadFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private TreeView current = new TreeView(), proposed = new TreeView();
        private SplitContainer split = new SplitContainer();
        private TreeNode currentRoot, currentDownloads, proposedRoot, proposedHold;
        private TextBox topics = new TextBox();
        private Label status = new Label();
        private TableLayoutPanel statusLine = new TableLayoutPanel();
        private ProgressBar applyProgress = new ProgressBar();
        private Button recommendButton = new SoftButton(), addButton = new SoftButton(), renameButton = new SoftButton(), batchRenameButton = new SoftButton(), removeButton = new SoftButton();
        private Button applyButton = new SoftButton(), resetButton = new SoftButton(), suggestTopicsButton = new SoftButton();
        private bool suggesting;
        private readonly ToolTip helpTips = new ToolTip {
            InitialDelay = 450, ReshowDelay = 150, AutoPopDelay = 10000, ShowAlways = true
        };
        private bool edited, working, loading;
        public bool Loaded { get; private set; }
        public string ResultMessage { get; private set; }
        internal string AppliedRoot { get; private set; }
        internal string SelectedSource { get { return source; } }
        internal string SelectedRoot { get { return root; } }
        internal PlanData AppliedPlan { get; private set; }
        internal StructureSnapshot PreviousSnapshot { get; private set; }

        public StructureEditor(string sourceFolder, string targetRoot)
            : this(sourceFolder, targetRoot, null) { }

        internal StructureEditor(string sourceFolder, string targetRoot, string dataFolderOverride)
        {
            source = Engine.Full(sourceFolder);
            root = Engine.Full(targetRoot);
            Text = "폴더 구조 편집";
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Width = Math.Min(1340, area.Width); Height = Math.Min(980, area.Height);
            MinimumSize = new Size(Math.Min(1040, area.Width), Math.Min(760, area.Height));
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Malgun Gothic", 9F);
            string dataFolder = dataFolderOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownloadOrganizer");
            Directory.CreateDirectory(dataFolder);
            topicsFile = Path.Combine(dataFolder, "topics.txt");
            if (File.Exists(topicsFile)) topics.Text = File.ReadAllText(topicsFile, Encoding.UTF8);
            BuildUi();
            SoftUi.Apply(this);
            // Trees sit inside cards, so they draw no border of their own.
            current.BorderStyle = BorderStyle.None; proposed.BorderStyle = BorderStyle.None;
            UpdateLocationCards();
            CheckRecovery();
            Shown += delegate
            {
                if (split.Width > 700)
                {
                    split.SplitterDistance = split.Width / 2;
                    split.Panel1MinSize = 300; split.Panel2MinSize = 300;
                }
                ActiveControl = proposed;
                StartBuild();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (working) { e.Cancel = true; return; }
                try { File.WriteAllText(topicsFile, topics.Text, Encoding.UTF8); } catch { }
            };
            Disposed += delegate { helpTips.Dispose(); DisposeVisualResources(); DisposeDragResources(); };
        }

        private static PlanTag NodeTag(TreeNode node) { return node == null ? null : node.Tag as PlanTag; }
        private static bool IsFolder(TreeNode node)
        {
            PlanTag tag = NodeTag(node);
            return tag != null && (tag.Kind == PlanKind.Folder || tag.Kind == PlanKind.Root ||
                tag.Kind == PlanKind.Hold || tag.Kind == PlanKind.SourceFolder);
        }

        private TreeNode MakeNode(string label, PlanTag tag, Color color)
        {
            TreeNode node = new TreeNode(label);
            node.Tag = tag; node.ForeColor = color;
            node.ImageKey = VisualIconKey(tag.Kind, label);
            if (tag.Kind != PlanKind.File && tag.Kind != PlanKind.Hold) node.NodeFont = FolderNodeFont;
            node.SelectedImageKey = node.ImageKey;
            if (tag.OriginalPath != "") node.ToolTipText = tag.OriginalPath;
            return node;
        }

        private static Label TextLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label {
                Text = text, Dock = DockStyle.Fill, AutoEllipsis = true, Margin = new Padding(0),
                Font = new Font("Malgun Gothic", size, style), ForeColor = color,
                TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent
            };
        }

        private static Control Divider()
        {
            return new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(232, 237, 244), Margin = new Padding(0, 6, 0, 6) };
        }

        private static Control CardTitle(string title, PillLabel pill)
        {
            FlowLayoutPanel row = new FlowLayoutPanel {
                Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), BackColor = SoftUi.Surface
            };
            row.Controls.Add(new Label {
                Text = title, AutoSize = true, Margin = new Padding(0, 2, 0, 0),
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold), ForeColor = SoftUi.Text
            });
            pill.Margin = new Padding(12, 9, 0, 0);
            row.Controls.Add(pill);
            return row;
        }

        private static TableLayoutPanel Rows(params float[] heights)
        {
            TableLayoutPanel table = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length,
                Margin = new Padding(0), Padding = new Padding(0), BackColor = Color.Transparent
            };
            foreach (float height in heights)
                table.RowStyles.Add(height < 0 ? new RowStyle(SizeType.Percent, 100) :
                    new RowStyle(SizeType.Absolute, height));
            return table;
        }

        private void BuildUi()
        {
            TableLayoutPanel layout = Rows(30, 74, 44, 0, -1, 112, 24);
            layout.Padding = new Padding(20, 8, 20, 4);
            Controls.Add(layout);
            Controls.Add(new AppHeader("폴더 구조 편집"));

            TableLayoutPanel heading = Rows(-1);
            heading.Controls.Add(TextLabel("정리할 위치 확인", 11.5F, FontStyle.Bold, SoftUi.Text), 0, 0);
            layout.Controls.Add(heading, 0, 0);

            TableLayoutPanel places = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 6)
            };
            places.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            places.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            places.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            places.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sourceCard.Dock = DockStyle.Fill; targetCard.Dock = DockStyle.Fill;
            sourceCard.Cursor = Cursors.Hand; targetCard.Cursor = Cursors.Hand;
            sourceCard.Click += delegate { BrowseStructureFolder(true); };
            targetCard.Click += delegate { BrowseStructureFolder(false); };
            places.Controls.Add(sourceCard, 0, 0);
            places.Controls.Add(new ArrowBadge { Dock = DockStyle.Fill }, 1, 0);
            places.Controls.Add(targetCard, 2, 0);
            layout.Controls.Add(places, 0, 1);

            CardPanel quickCard = new CardPanel { Dock = DockStyle.Fill,
                Padding = new Padding(10, 2, 10, 2), Margin = new Padding(0, 0, 0, 6) };
            TableLayoutPanel quickTools = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                Margin = new Padding(0), BackColor = SoftUi.Surface
            };
            quickTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            quickTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            quickTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            quickTools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Button topicToggle = new SoftButton { Text = "업무·주제 편집 ▾", Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 12, 2) };
            quickTools.Controls.Add(topicToggle, 0, 0);
            quickTools.Controls.Add(TextLabel("파일·폴더 찾기", 9F, FontStyle.Bold, SoftUi.Muted), 1, 0);
            quickTools.Controls.Add(CreateSearchBar(), 2, 0);
            quickCard.Controls.Add(quickTools);
            layout.Controls.Add(quickCard, 0, 2);

            CardPanel toolsCard = new CardPanel { Dock = DockStyle.Fill,
                Padding = new Padding(20, 8, 20, 8), Margin = new Padding(0, 0, 0, 8), Visible = false };
            TableLayoutPanel tools = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0), BackColor = SoftUi.Surface
            };
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tools.Controls.Add(TextLabel("업무·주제 목록  ·  한 줄에 하나씩, 예: 예산 또는 업무 > 예산",
                9F, FontStyle.Bold, SoftUi.Muted), 0, 0);
            topics.Multiline = true; topics.ScrollBars = ScrollBars.Vertical; topics.Dock = DockStyle.Fill;
            topics.Margin = new Padding(0, 2, 10, 0);
            tools.Controls.Add(topics, 0, 1);
            recommendButton.Text = "추천 구조 만들기"; recommendButton.Dock = DockStyle.Fill;
            recommendButton.Click += delegate { RebuildFromTopics(); };
            suggestTopicsButton.Text = "주제 추천 받기"; suggestTopicsButton.Dock = DockStyle.Fill;
            suggestTopicsButton.Click += delegate { SuggestTopics(); };
            TableLayoutPanel topicButtons = Rows(-1, -1);
            topicButtons.RowStyles.Clear();
            topicButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            topicButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            topicButtons.Controls.Add(suggestTopicsButton, 0, 0);
            topicButtons.Controls.Add(recommendButton, 0, 1);
            tools.Controls.Add(topicButtons, 1, 0);
            tools.SetRowSpan(topicButtons, 2);
            toolsCard.Controls.Add(tools);
            layout.Controls.Add(toolsCard, 0, 3);
            topicToggle.Click += delegate {
                bool open = !toolsCard.Visible;
                toolsCard.Visible = open;
                layout.RowStyles[3].Height = open ? 112 : 0;
                topicToggle.Text = open ? "업무·주제 접기 ▴" : "업무·주제 편집 ▾";
                if (open) topics.Focus();
            };

            split.Dock = DockStyle.Fill; split.Orientation = Orientation.Vertical;
            split.SplitterWidth = 18; split.BackColor = SoftUi.Background; split.Margin = new Padding(0, 0, 0, 8);
            split.Panel1.BackColor = SoftUi.Background; split.Panel2.BackColor = SoftUi.Background;
            layout.Controls.Add(split, 0, 4);

            CardPanel leftCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            TableLayoutPanel leftBody = Rows(42, 22, 13, -1);
            leftBody.Controls.Add(CardTitle("현재 구조", new PillLabel("변경 전", SoftDraw.Gray, SoftDraw.GrayInk)), 0, 0);
            leftBody.Controls.Add(TextLabel("현재 파일이 있는 위치 · 읽기 전용, 항목을 오른쪽으로 끌 수 있어요",
                9F, FontStyle.Regular, SoftUi.Muted), 0, 1);
            leftBody.Controls.Add(Divider(), 0, 2);
            current.Dock = DockStyle.Fill; current.HideSelection = false; current.ShowNodeToolTips = true;
            current.Margin = new Padding(0);
            InitializeVisualTree(current);
            current.ItemDrag += delegate(object sender, ItemDragEventArgs e) { StartDrag((TreeNode)e.Item, true); };
            leftBody.Controls.Add(current, 0, 3);
            leftCard.Controls.Add(leftBody);
            split.Panel1.Controls.Add(leftCard);

            CardPanel rightCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            TableLayoutPanel rightBody = Rows(42, 22, 42, 13, -1, 40);
            rightBody.Controls.Add(CardTitle("변경 후 미리보기", new PillLabel("아직 적용 전", SoftDraw.GreenSoft, SoftDraw.GreenInk)), 0, 0);
            rightBody.Controls.Add(TextLabel("파일과 폴더를 끌어 놓아 수정할 수 있어요 · F2로 이름 수정",
                9F, FontStyle.Regular, SoftUi.Muted), 0, 1);
            FlowLayoutPanel editTools = new FlowLayoutPanel {
                Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), BackColor = SoftUi.Surface
            };
            addButton.Text = "새 폴더"; addButton.Width = 88; addButton.Height = 32;
            addButton.Click += AddFolder;
            renameButton.Text = "이름 변경"; renameButton.Width = 92; renameButton.Height = 32;
            renameButton.Click += delegate { RenameSelected(); };
            batchRenameButton.Text = "일괄 이름 변경"; batchRenameButton.Width = 120; batchRenameButton.Height = 32;
            batchRenameButton.Click += delegate { ShowBatchRename(); };
            removeButton.Text = "빈 폴더 제거"; removeButton.Width = 110; removeButton.Height = 32;
            removeButton.Click += RemoveFolder;
            resetButton.Text = "처음 구조로"; resetButton.Width = 104; resetButton.Height = 32;
            resetButton.Click += delegate { RebuildFromTopics(); };
            foreach (Button button in new[] { addButton, renameButton, batchRenameButton, removeButton, resetButton })
            { button.Margin = new Padding(0, 5, 8, 0); editTools.Controls.Add(button); }
            rightBody.Controls.Add(editTools, 0, 2);
            rightBody.Controls.Add(Divider(), 0, 3);
            proposed.Dock = DockStyle.Fill; proposed.HideSelection = false;
            proposed.ShowNodeToolTips = true; proposed.AllowDrop = true; proposed.Margin = new Padding(0);
            InitializeVisualTree(proposed);
            proposed.ItemDrag += delegate(object sender, ItemDragEventArgs e) { StartDrag((TreeNode)e.Item, false); };
            proposed.DragEnter += DragOverPlan;
            proposed.DragOver += DragOverPlan;
            proposed.DragLeave += delegate { ClearDragTarget(); };
            proposed.DragDrop += DropOnPlan;
            rightBody.Controls.Add(proposed, 0, 4);
            rightBody.Controls.Add(CreateDragGuide(), 0, 5);
            rightCard.Controls.Add(rightBody);
            split.Panel2.Controls.Add(rightCard);
            HookClipboardMenus();

            Button close = new SoftButton { Text = "닫기", Width = 96, Height = 40 };
            close.Click += delegate { Close(); };
            applyButton.Text = "변경 내용 확인 · 적용"; applyButton.Width = 230; applyButton.Height = 40;
            ((SoftButton)applyButton).Primary = true;
            applyButton.Click += ApplyClicked;
            applyButton.Height = 32; close.Height = 32;
            layout.Controls.Add(CreateSelectionCard(applyButton, close), 0, 5);

            status.Text = "오른쪽 구조를 고친 뒤 변경 내용을 확인하고 적용하세요.";
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            status.AutoEllipsis = true; status.ForeColor = SoftUi.Muted;
            statusLine.Dock = DockStyle.Fill; statusLine.RowCount = 1;
            statusLine.ColumnCount = 2; statusLine.Margin = new Padding(0);
            statusLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
            statusLine.Controls.Add(status, 0, 0);
            applyProgress.Dock = DockStyle.Fill; applyProgress.Margin = new Padding(4, 3, 0, 3);
            applyProgress.Visible = false;
            statusLine.Controls.Add(applyProgress, 1, 0);
            layout.Controls.Add(statusLine, 0, 6);

            helpTips.SetToolTip(topics, "업무나 주제를 한 줄에 하나씩 적습니다. '업무 > 예산'처럼 하위 폴더도 지정할 수 있습니다.");
            helpTips.SetToolTip(recommendButton, "입력한 주제로 오른쪽 추천 구조를 다시 만듭니다.");
            helpTips.SetToolTip(suggestTopicsButton, "감시 폴더 바로 아래 파일 이름을 보고 주제 목록을 추천합니다. 고른 뒤 직접 고칠 수 있습니다.");
            helpTips.SetToolTip(current, "현재 폴더와 파일 목록입니다. 폴더 옆 숫자는 하위 폴더까지 포함한 파일 수입니다. 항목을 오른쪽으로 끌 수 있습니다.");
            helpTips.SetToolTip(proposed, "추천 구조입니다. 폴더 옆 숫자는 하위 폴더까지 포함한 파일 수입니다. 파일이나 폴더를 끌어 위치를 바꿀 수 있습니다.");
            helpTips.SetToolTip(addButton, "오른쪽에서 선택한 폴더 안에 새 폴더를 추가합니다.");
            helpTips.SetToolTip(renameButton, "선택한 파일 또는 폴더의 이름을 바꿉니다. F2도 사용할 수 있습니다.");
            helpTips.SetToolTip(batchRenameButton, "선택한 여러 파일 이름의 앞이나 확장자 앞에 같은 문구를 붙입니다.");
            helpTips.SetToolTip(removeButton, "오른쪽에서 선택한 빈 폴더를 계획에서 제거합니다.");
            helpTips.SetToolTip(resetButton, "오른쪽의 수정 내용을 버리고 추천 구조를 다시 만듭니다.");
            helpTips.SetToolTip(applyButton, "변경할 파일과 폴더 수를 확인한 뒤 실제 구조에 적용합니다.");
            helpTips.SetToolTip(close, "파일과 폴더를 바꾸지 않고 창을 닫습니다.");
            helpTips.SetToolTip(sourceCard, "감시 폴더: " + source);
            helpTips.SetToolTip(targetCard, "정리할 상위 폴더: " + root);
        }

        private void BrowseStructureFolder(bool watchFolder)
        {
            if (loading || working || suggesting)
            { status.Text = "현재 작업이 끝난 뒤 폴더를 바꿔 주세요."; return; }
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = watchFolder ? "새 파일을 감시할 폴더를 선택하세요" :
                    "구조를 편집할 상위 폴더를 선택하세요";
                dialog.SelectedPath = watchFolder ? source : root;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string nextSource = watchFolder ? Engine.Full(dialog.SelectedPath) : source;
                string nextRoot = watchFolder ? root : Engine.Full(dialog.SelectedPath);
                string error = Engine.Validate(nextSource, nextRoot);
                if (error != "") { status.Text = error; MessageBox.Show(this, error, "폴더 변경 불가"); return; }
                if (nextSource.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                    nextRoot.Equals(root, StringComparison.OrdinalIgnoreCase)) return;
                if (edited && MessageBox.Show(this,
                    "현재 미리보기에서 수정한 배치가 초기화됩니다. 새 폴더의 구조를 열까요?",
                    "작업 폴더 변경", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                try { SwitchStructureFolders(nextSource, nextRoot); }
                catch (Exception ex)
                {
                    status.Text = ex.Message;
                    MessageBox.Show(this, ex.Message, "폴더 변경 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        internal void SwitchStructureFolders(string nextSource, string nextRoot)
        {
            if (loading || working || suggesting)
                throw new IOException("현재 작업이 끝난 뒤 폴더를 바꿔 주세요.");
            nextSource = Engine.Full(nextSource); nextRoot = Engine.Full(nextRoot);
            string error = Engine.Validate(nextSource, nextRoot);
            if (error != "") throw new IOException(error);
            if (nextSource.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                nextRoot.Equals(root, StringComparison.OrdinalIgnoreCase)) return;
            string previousSource = source, previousRoot = root;
            source = nextSource; root = nextRoot;
            try { CheckRecovery(); }
            catch { source = previousSource; root = previousRoot; throw; }
            Loaded = false;
            ClearSelectedFiles();
            current.Nodes.Clear(); proposed.Nodes.Clear();
            currentRoot = currentDownloads = proposedRoot = proposedHold = null;
            UpdateLocationCards();
            helpTips.SetToolTip(sourceCard, "감시 폴더: " + source);
            helpTips.SetToolTip(targetCard, "정리할 상위 폴더: " + root);
            StartBuild();
        }

        private void CheckRecovery()
        {
            string[] pending = StructurePlanner.PendingStages(root);
            if (pending.Length == 0) return;
            if (MessageBox.Show(this, "이전 구조 변경이 중단되어 임시 파일이 남았습니다. 원래 위치로 복구할까요?",
                "이전 변경 복구", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                throw new IOException("이전 변경을 먼저 복구해야 합니다: " + pending[0]);
            foreach (string directory in pending) StructurePlanner.RecoverStage(directory);
        }

        private static string[] ChildDirectories(string directory)
        {
            return Directory.GetDirectories(directory).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        private static void AddSourceFolders(string directory, TreeNode leftParent,
            TreeNode rightParent, Dictionary<string, TreeNode> leftFolders,
            Dictionary<string, TreeNode> rightFolders)
        {
            string[] children;
            try { children = ChildDirectories(directory); }
            catch { return; }
            foreach (string child in children)
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(child);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0 ||
                        Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal)) continue;
                    string path = Engine.Full(child);
                    PlanTag tag = new PlanTag { Kind = PlanKind.SourceFolder, OriginalPath = path,
                        FromDownloads = true };
                    TreeNode left = new TreeNode(Path.GetFileName(child));
                    left.Tag = tag; left.ImageKey = "folder"; left.SelectedImageKey = "folder";
                    left.ForeColor = Color.DimGray; left.ToolTipText = path; left.NodeFont = FolderNodeFont;
                    TreeNode right = new TreeNode(Path.GetFileName(child));
                    right.Tag = tag; right.ImageKey = "folder"; right.SelectedImageKey = "folder";
                    right.ForeColor = Color.DimGray; right.ToolTipText = path; right.NodeFont = FolderNodeFont;
                    leftParent.Nodes.Add(left); rightParent.Nodes.Add(right);
                    leftFolders.Add(path, left); rightFolders.Add(path, right);
                    if (leftFolders.Count > 10000)
                        throw new IOException("파일과 폴더가 10,000개를 넘어 구조 편집을 열 수 없습니다.");
                    AddSourceFolders(child, left, right, leftFolders, rightFolders);
                }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void AddExisting(StructureSnapshot existing, TreeNode leftParent, TreeNode rightParent,
            bool skipTopLevelFiles)
        {
            Dictionary<string, TreeNode> leftFolders = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, TreeNode> rightFolders = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            leftFolders.Add(root, leftParent);
            rightFolders.Add(root, rightParent);
            foreach (string child in existing.TargetDirectories.OrderBy(x =>
                x.Count(c => c == Path.DirectorySeparatorChar)).ThenBy(x => x,
                StringComparer.CurrentCultureIgnoreCase))
            {
                string parent = Engine.Full(Path.GetDirectoryName(child));
                PlanTag tag = new PlanTag { Kind = PlanKind.Folder, OriginalPath = Engine.Full(child) };
                TreeNode left = MakeNode(Path.GetFileName(child), tag, Color.Black);
                TreeNode right = MakeNode(Path.GetFileName(child), tag, Color.Black);
                leftFolders[parent].Nodes.Add(left); rightFolders[parent].Nodes.Add(right);
                leftFolders.Add(child, left); rightFolders.Add(child, right);
            }
            foreach (string file in existing.TargetFiles.OrderBy(x => x,
                StringComparer.CurrentCultureIgnoreCase))
            {
                string parent = Engine.Full(Path.GetDirectoryName(file));
                if (skipTopLevelFiles && parent.Equals(root, StringComparison.OrdinalIgnoreCase)) continue;
                FileInfo info = new FileInfo(file);
                PlanTag tag = new PlanTag {
                    Kind = PlanKind.File, OriginalPath = Engine.Full(file),
                    Length = info.Length, LastWriteTicks = info.LastWriteTimeUtc.Ticks
                };
                leftFolders[parent].Nodes.Add(MakeNode(info.Name, tag, Color.DimGray));
                rightFolders[parent].Nodes.Add(MakeNode(info.Name, tag, Color.Black));
            }
        }

        private List<string> TopicPaths(string[] lines)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines)
            {
                string[] segments = line.Trim().Replace(">", "\\").Replace("/", "\\")
                    .Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x != "").ToArray();
                string value = String.Join("\\", segments);
                if (value == "") continue;
                string path = Engine.StructurePath(root, value);
                string relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
                paths.Add(relative);
            }
            return paths.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private TreeNode FindFolderByOriginal(TreeNode parent, string path)
        {
            foreach (TreeNode child in parent.Nodes)
            {
                PlanTag tag = NodeTag(child);
                if (tag.Kind == PlanKind.Folder &&
                    tag.OriginalPath.Equals(path, StringComparison.OrdinalIgnoreCase)) return child;
                if (tag.Kind == PlanKind.Folder)
                {
                    TreeNode found = FindFolderByOriginal(child, path);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private TreeNode EnsureTopicPath(TreeNode planRoot, string relative)
        {
            TreeNode currentNode = planRoot;
            foreach (string segment in relative.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                TreeNode next = null;
                foreach (TreeNode child in currentNode.Nodes)
                    if (NodeTag(child).Kind == PlanKind.Folder &&
                        child.Text.Equals(segment, StringComparison.OrdinalIgnoreCase)) { next = child; break; }
                if (next == null)
                {
                    next = MakeNode(segment, new PlanTag { Kind = PlanKind.Folder }, Color.DarkBlue);
                    currentNode.Nodes.Add(next);
                }
                currentNode = next;
            }
            return currentNode;
        }

        private TreeBuildResult BuildTreeData(string[] topicLines)
        {
            bool sameFolder = source.Equals(root, StringComparison.OrdinalIgnoreCase);
            string rootName = Path.GetFileName(root);
            if (rootName == "") rootName = root;
            TreeBuildResult result = new TreeBuildResult();
            result.Snapshot = StructurePlanner.ScanTarget(root);
            result.DownloadFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            result.CurrentRoot = MakeNode(rootName,
                new PlanTag { Kind = PlanKind.Root, OriginalPath = root }, Color.Black);
            string sourceName = Path.GetFileName(source);
            if (sourceName == "") sourceName = source;
            result.CurrentDownloads = MakeNode(sameFolder ? "최상위 파일" : sourceName + " · 감시 중",
                new PlanTag { Kind = PlanKind.Hold, OriginalPath = source }, Color.Black);
            result.ProposedRoot = MakeNode(rootName,
                new PlanTag { Kind = PlanKind.Root, OriginalPath = root }, Color.Black);
            result.ProposedHold = MakeNode(sameFolder ? "최상위에 그대로 둘 파일" :
                "정리하지 않고 둘 파일 · " + sourceName,
                new PlanTag { Kind = PlanKind.Hold }, Color.Gray);
            result.CurrentDownloads.ToolTipText = "감시 폴더: " + source;
            result.ProposedHold.ToolTipText = "여기 있는 파일은 적용 후에도 감시 폴더의 원래 위치에 그대로 남습니다.";
            AddExisting(result.Snapshot, result.CurrentRoot, result.ProposedRoot, sameFolder);

            List<string> topicPaths = TopicPaths(topicLines);
            foreach (string path in topicPaths) EnsureTopicPath(result.ProposedRoot, path);
            List<TreeNode> topicLeaves = topicPaths.Select(x => EnsureTopicPath(result.ProposedRoot, x)).ToList();
            List<FolderChoice> existingFolders = Engine.Folders(root);
            LocalSearchIndex existingSearch = new LocalSearchIndex(existingFolders);
            Dictionary<string, TreeNode> leftSourceFolders = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, TreeNode> rightSourceFolders = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            leftSourceFolders.Add(source, result.CurrentDownloads);
            rightSourceFolders.Add(source, result.ProposedHold);
            if (!sameFolder)
                AddSourceFolders(source, result.CurrentDownloads, result.ProposedHold,
                    leftSourceFolders, rightSourceFolders);
            List<FileInfo> incoming = Engine.SourceFiles(source, !sameFolder);
            if ((sameFolder ? 0 : incoming.Count + leftSourceFolders.Count - 1) + result.Snapshot.TargetFiles.Count +
                result.Snapshot.TargetDirectories.Count > 10000)
                throw new IOException("파일과 폴더가 10,000개를 넘어 구조 편집을 열 수 없습니다.");
            foreach (FileInfo file in incoming)
            {
                PlanTag tag = new PlanTag {
                    Kind = PlanKind.File, OriginalPath = Engine.Full(file.FullName),
                    FromDownloads = true, Length = file.Length, LastWriteTicks = file.LastWriteTimeUtc.Ticks,
                    CreationTicks = file.CreationTimeUtc.Ticks
                };
                string fileFolder = Engine.Full(Path.GetDirectoryName(file.FullName));
                leftSourceFolders[fileFolder].Nodes.Add(MakeNode(file.Name, tag, Color.DimGray));
                result.DownloadFiles.Add(tag.OriginalPath);
                TreeNode destination = null;
                // Files already grouped in a watch subfolder are moved only by dragging.
                bool inSubfolder = !fileFolder.Equals(source, StringComparison.OrdinalIgnoreCase);
                if (inSubfolder)
                {
                    rightSourceFolders[fileFolder].Nodes.Add(MakeNode(file.Name, tag, Color.DarkBlue));
                    continue;
                }
                if (topicLeaves.Count > 0)
                {
                    string stem = Path.GetFileNameWithoutExtension(file.Name);
                    int pick = TopicSuggester.Pick(topicPaths, topicPaths.Select(path =>
                        TopicSuggester.TopicScore(stem, TopicSuggester.LeafName(path))).ToList());
                    if (pick >= 0) destination = topicLeaves[pick];
                }
                if (destination == null)
                {
                    Suggestion existing = Engine.Suggest(file, existingFolders, existingSearch);
                    if (existing.Certain) destination = FindFolderByOriginal(result.ProposedRoot, existing.Folder);
                }
                TreeNode placement = destination ?? rightSourceFolders[fileFolder];
                placement.Nodes.Add(MakeNode(file.Name, tag, Color.DarkBlue));
            }
            result.CurrentRoot.Expand(); result.CurrentDownloads.Expand();
            result.ProposedRoot.Expand(); result.ProposedHold.Expand();
            return result;
        }

        private void StartBuild()
        {
            if (loading || working || IsDisposed) return;
            ClearPlanClipboard();
            loading = true; Loaded = false;
            recommendButton.Enabled = false; resetButton.Enabled = false;
            applyButton.Enabled = false; addButton.Enabled = false;
            renameButton.Enabled = false; batchRenameButton.Enabled = false; removeButton.Enabled = false;
            status.Text = "폴더와 파일을 읽는 중입니다. 화면은 계속 사용할 수 있습니다...";
            string[] lines = topics.Lines.ToArray();
            BackgroundWorker loader = new BackgroundWorker();
            loader.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = BuildTreeData(lines); };
            loader.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed) return;
                loading = false;
                recommendButton.Enabled = true; resetButton.Enabled = true;
                if (e.Error != null) { status.Text = "구조 읽기 실패: " + e.Error.Message; return; }
                TreeBuildResult result = (TreeBuildResult)e.Result;
                current.BeginUpdate(); proposed.BeginUpdate();
                try
                {
                    current.Nodes.Clear(); proposed.Nodes.Clear();
                    selectedCurrentFiles.Clear(); selectedProposedFiles.Clear();
                    currentSelectionAnchor = null; proposedSelectionAnchor = null;
                    lastChosenNode = null;
                    snapshot = result.Snapshot; downloadFiles = result.DownloadFiles;
                    currentRoot = result.CurrentRoot; currentDownloads = result.CurrentDownloads;
                    proposedRoot = result.ProposedRoot; proposedHold = result.ProposedHold;
                    current.Nodes.Add(currentRoot); current.Nodes.Add(currentDownloads);
                    proposed.Nodes.Add(proposedRoot); proposed.Nodes.Add(proposedHold);
                    edited = false; Loaded = true;
                    RefreshPlanVisuals();
                    applyButton.Enabled = true; addButton.Enabled = true;
                    renameButton.Enabled = true; batchRenameButton.Enabled = true; removeButton.Enabled = true;
                    status.Text = "현재 구조를 불러왔습니다. 오른쪽에서 폴더와 파일을 옮겨 수정하세요.";
                }
                finally { current.EndUpdate(); proposed.EndUpdate(); }
            };
            loader.RunWorkerAsync();
        }

        internal static List<string> MergeTopicLines(IEnumerable<string> existing, IEnumerable<string> added, bool replace)
        {
            List<string> lines = replace ? new List<string>() :
                existing.Select(x => x.TrimEnd()).Where(x => x.Trim() != "").ToList();
            HashSet<string> seen = new HashSet<string>(lines.Select(TopicSuggestionForm.TopicPath),
                StringComparer.OrdinalIgnoreCase);
            foreach (string line in added)
                if (seen.Add(TopicSuggestionForm.TopicPath(line))) lines.Add(line);
            return lines;
        }

        // Files inside watch subfolders are already grouped by the user; suggest only from loose files.
        internal List<string> TopicSourceFiles()
        {
            return downloadFiles.Where(f => Engine.Full(Path.GetDirectoryName(f))
                .Equals(source, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void SuggestTopics()
        {
            if (suggesting || working || loading || !Loaded)
            { status.Text = "폴더를 다 읽은 뒤 다시 눌러 주세요."; return; }
            List<string> files = TopicSourceFiles();
            if (files.Count == 0)
            {
                status.Text = "감시 폴더 바로 아래에 파일이 없어 추천할 주제가 없습니다. 하위 폴더의 파일은 추천에서 제외됩니다.";
                return;
            }
            List<string> existingNames = snapshot.TargetDirectories.Select(d => Path.GetFileName(d)).ToList();
            suggesting = true; suggestTopicsButton.Enabled = false;
            status.Text = "파일 " + files.Count + "개의 이름을 살펴 주제를 고르는 중입니다...";
            List<string> stems = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                List<TopicCandidate> candidates = TopicSuggester.Suggest(source, files, existingNames);
                Dictionary<string, double[]> cache = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
                foreach (TopicCandidate candidate in candidates)
                {
                    string leaf = TopicSuggester.LeafName(TopicSuggestionForm.TopicPath(candidate.Line));
                    if (!cache.ContainsKey(leaf))
                        cache[leaf] = stems.Select(stem => TopicSuggester.TopicScore(stem, leaf)).ToArray();
                }
                e.Result = Tuple.Create(candidates, cache);
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed) return;
                suggesting = false; suggestTopicsButton.Enabled = true;
                if (e.Error != null) { status.Text = "주제 추천 실패: " + e.Error.Message; return; }
                Tuple<List<TopicCandidate>, Dictionary<string, double[]>> result =
                    (Tuple<List<TopicCandidate>, Dictionary<string, double[]>>)e.Result;
                if (result.Item1.Count == 0)
                {
                    status.Text = "여러 파일에 함께 나오는 이름을 찾지 못했습니다. 주제를 직접 적어 주세요.";
                    return;
                }
                using (TopicSuggestionForm dialog = new TopicSuggestionForm(result.Item1, stems, result.Item2))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    { status.Text = "주제 목록을 바꾸지 않았습니다."; return; }
                    topics.Lines = MergeTopicLines(topics.Lines, dialog.ChosenLines, dialog.ReplaceExisting).ToArray();
                    status.Text = "추천 주제 " + dialog.ChosenLines.Count + "개를 목록에 넣었습니다. 목록은 계속 고칠 수 있습니다.";
                }
                RebuildFromTopics();
            };
            worker.RunWorkerAsync();
        }

        private void RebuildFromTopics()
        {
            if (edited && MessageBox.Show(this, "오른쪽에서 수정한 배치가 초기화됩니다. 다시 만들까요?",
                "추천 구조 다시 만들기", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            StartBuild();
        }

        private TreeNode FindRightNode(TreeNode node, PlanTag sought)
        {
            if (NodeTag(node) != null && NodeTag(node).Kind == sought.Kind &&
                sought.OriginalPath != "" && NodeTag(node).OriginalPath.Equals(sought.OriginalPath, StringComparison.OrdinalIgnoreCase))
                return node;
            foreach (TreeNode child in node.Nodes)
            {
                TreeNode found = FindRightNode(child, sought);
                if (found != null) return found;
            }
            return null;
        }

        private void StartDrag(TreeNode node, bool fromLeft)
        {
            if (!Loaded || loading || working) return;
            PlanTag tag = NodeTag(node);
            if (tag == null || (tag.Kind != PlanKind.File && tag.Kind != PlanKind.Folder &&
                tag.Kind != PlanKind.SourceFolder)) return;
            TreeView view = fromLeft ? current : proposed;
            DraggedNode drag = new DraggedNode { Node = node, FromLeft = fromLeft };
            drag.Nodes.AddRange(DragSourceNodes(view, node));
            List<TreeNode> moving = ResolveMany(drag);
            if (moving.Count != drag.Nodes.Count || moving.Count == 0) return;
            BeginDragFeedback(node, moving);
            using (DragGhost ghost = new DragGhost(moving))
            {
                dragGhost = ghost;
                GiveFeedbackEventHandler feedback = delegate(object sender, GiveFeedbackEventArgs e)
                {
                    e.UseDefaultCursors = true;
                    if (dragGhost != null) dragGhost.FollowCursor();
                };
                QueryContinueDragEventHandler follow = delegate
                {
                    if (dragGhost != null) dragGhost.FollowCursor();
                };
                view.GiveFeedback += feedback;
                view.QueryContinueDrag += follow;
                try
                {
                    ghost.FollowCursor();
                    ghost.Show(this);
                    view.DoDragDrop(drag, DragDropEffects.Move);
                }
                finally
                {
                    view.GiveFeedback -= feedback;
                    view.QueryContinueDrag -= follow;
                    dragGhost = null;
                    ghost.Close();
                    EndDragFeedback();
                }
            }
        }

        private TreeNode DropTarget(DragEventArgs e)
        {
            Point point = proposed.PointToClient(new Point(e.X, e.Y));
            TreeNode target = RowNodeAt(proposed, point);
            if (target == null) return null;
            return IsFolder(target) ? target : target.Parent;
        }

        private TreeNode Resolve(DraggedNode drag)
        {
            if (drag == null || drag.Node == null) return null;
            if (drag.Node == dragSourceNode && dragResolvedNode != null) return dragResolvedNode;
            if (!drag.FromLeft) return drag.Node;
            PlanTag tag = NodeTag(drag.Node);
            foreach (TreeNode rootNode in proposed.Nodes)
            {
                TreeNode found = FindRightNode(rootNode, tag);
                if (found != null) return found;
            }
            return null;
        }

        private List<TreeNode> ResolveMany(DraggedNode drag)
        {
            if (drag == null || drag.Node == null) return new List<TreeNode>();
            if (drag.Node == dragSourceNode && dragResolvedNodes != null)
                return dragResolvedNodes;
            List<TreeNode> result = new List<TreeNode>();
            IEnumerable<TreeNode> nodes = drag.Nodes.Count > 0 ? (IEnumerable<TreeNode>)drag.Nodes :
                new TreeNode[] { drag.Node };
            foreach (TreeNode node in nodes)
            {
                TreeNode resolved = Resolve(new DraggedNode { Node = node, FromLeft = drag.FromLeft });
                if (resolved == null) return new List<TreeNode>();
                if (!result.Contains(resolved)) result.Add(resolved);
            }
            return result;
        }

        private bool CanDrop(TreeNode moving, TreeNode target)
        {
            return DropBlockReason(moving, target) == "";
        }

        private void DragOverPlan(object sender, DragEventArgs e)
        {
            if (!Loaded || loading || working)
            { e.Effect = DragDropEffects.None; ClearDragTarget(); return; }
            DraggedNode drag = e.Data.GetData(typeof(DraggedNode)) as DraggedNode;
            TreeNode target = DropTarget(e);
            List<TreeNode> moving = ResolveMany(drag);
            string reason = DropBlockReasonMany(moving, target);
            e.Effect = reason == "" ? DragDropEffects.Move : DragDropEffects.None;
            UpdateDragFeedback(moving, target, reason);
            UpdateDragAutoScroll(proposed.PointToClient(new Point(e.X, e.Y)));
        }

        private void DropOnPlan(object sender, DragEventArgs e)
        {
            if (!Loaded || loading || working) return;
            DraggedNode drag = e.Data.GetData(typeof(DraggedNode)) as DraggedNode;
            TreeNode target = DropTarget(e);
            List<TreeNode> moving = ResolveMany(drag);
            string reason = DropBlockReasonMany(moving, target);
            if (reason == "") MovePlannedNodes(moving, target);
            else status.Text = reason;
            EndDragFeedback();
        }

        internal bool MovePlannedNodes(List<TreeNode> moving, TreeNode target)
        {
            if (DropBlockReasonMany(moving, target) != "") return false;
            if (moving.Count == 1) return MovePlannedNode(moving[0], target);
            foreach (TreeNode node in moving) node.Remove();
            foreach (TreeNode node in moving) target.Nodes.Add(node);
            target.Expand();
            proposed.SelectedNode = moving[0];
            SetDraggedSelectionAfterMove(moving);
            edited = true;
            RefreshPlanVisuals();
            status.Text = moving.Count + "개 파일을 " + TargetLabel(target) +
                "에 배치했습니다. 실제 이동은 '변경 내용 확인·적용'에서 진행합니다.";
            return true;
        }

        // A watch-folder subfolder stays where it is on disk; dropping it creates a folder with
        // the same name and layout in the target and moves the files that are still under it.
        private static int MoveSourceFolderFiles(TreeNode sourceFolder, TreeNode destination,
            Func<string, TreeNode> makeFolder)
        {
            int moved = 0;
            foreach (TreeNode child in sourceFolder.Nodes.Cast<TreeNode>().ToList())
            {
                PlanTag tag = NodeTag(child);
                if (tag == null) continue;
                if (tag.Kind == PlanKind.File)
                {
                    child.Remove(); destination.Nodes.Add(child); moved++;
                }
                else if (tag.Kind == PlanKind.SourceFolder)
                {
                    TreeNode sub = makeFolder(child.Text);
                    int inner = MoveSourceFolderFiles(child, sub, makeFolder);
                    if (inner > 0) destination.Nodes.Add(sub);
                    moved += inner;
                }
            }
            return moved;
        }

        private static int FilesUnder(TreeNode node)
        {
            int count = 0;
            foreach (TreeNode child in node.Nodes)
                count += NodeTag(child) != null && NodeTag(child).Kind == PlanKind.File ? 1 : FilesUnder(child);
            return count;
        }

        internal bool MovePlannedNode(TreeNode moving, TreeNode target)
        {
            if (!CanDrop(moving, target)) return false;
            if (NodeTag(moving).Kind == PlanKind.SourceFolder)
            {
                TreeNode folder = MakeNode(moving.Text, new PlanTag { Kind = PlanKind.Folder }, Color.DarkBlue);
                int count = MoveSourceFolderFiles(moving, folder, name =>
                    MakeNode(name, new PlanTag { Kind = PlanKind.Folder }, Color.DarkBlue));
                target.Nodes.Add(folder); target.Expand(); folder.Expand();
                proposed.SelectedNode = folder;
                edited = true;
                RefreshPlanVisuals();
                status.Text = moving.Text + " 폴더와 파일 " + count + "개를 " + TargetLabel(folder) +
                    "에 배치했습니다. 원래 하위 폴더는 비운 채 남으며, 실제 이동은 적용할 때 진행합니다.";
                return true;
            }
            moving.Remove(); target.Nodes.Add(moving); target.Expand();
            proposed.SelectedNode = moving;
            edited = true;
            RefreshPlanVisuals();
            status.Text = moving.Text + "의 위치를 바꿨습니다. 아직 실제 파일은 이동하지 않았습니다.";
            return true;
        }

        private static bool ValidFolderName(string name)
        {
            return !String.IsNullOrWhiteSpace(name) && name != "." && name != ".." &&
                name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                name.TrimEnd(' ', '.') == name;
        }

        private TreeNode SelectedFolder()
        {
            TreeNode node = proposed.SelectedNode ?? proposedRoot;
            if (!IsFolder(node)) node = node.Parent;
            if (node == proposedHold || NodeTag(node).Kind == PlanKind.SourceFolder) return null;
            return node;
        }

        private void AddFolder(object sender, EventArgs e)
        {
            TreeNode parent = SelectedFolder();
            if (parent == null) { status.Text = "정리할 상위 폴더 안의 위치를 선택하세요."; return; }
            string name = "새 폴더";
            int number = 2;
            while (parent.Nodes.Cast<TreeNode>().Any(n => n.Text.Equals(name, StringComparison.OrdinalIgnoreCase)))
                name = "새 폴더 " + number++;
            TreeNode node = MakeNode(name, new PlanTag { Kind = PlanKind.Folder }, Color.DarkBlue);
            parent.Nodes.Add(node); parent.Expand(); proposed.SelectedNode = node;
            edited = true; RefreshPlanVisuals(); ShowFolderRenameDialog(node);
        }

        private void RemoveFolder(object sender, EventArgs e)
        {
            TreeNode node = proposed.SelectedNode;
            if (NodeTag(node) == null || NodeTag(node).Kind != PlanKind.Folder)
            { status.Text = "제거할 폴더를 선택하세요."; return; }
            if (node.Nodes.Count > 0)
            { status.Text = "폴더 안의 파일과 하위 폴더를 먼저 다른 위치로 옮기세요."; return; }
            string name = node.Text; node.Remove(); edited = true; RefreshPlanVisuals();
            status.Text = name + "을(를) 오른쪽 계획에서 제거했습니다. 실제 폴더는 적용할 때 정리됩니다.";
        }

        private void Collect(TreeNode folder, string relative, string originalParent, PlanData plan)
        {
            foreach (TreeNode node in folder.Nodes)
            {
                PlanTag tag = NodeTag(node);
                if (tag.Kind == PlanKind.Folder)
                {
                    if (!ValidFolderName(node.Text)) throw new IOException("폴더 이름을 확인하세요: " + node.Text);
                    string child = relative == "" ? node.Text : Path.Combine(relative, node.Text);
                    string path = Engine.StructurePath(root, child);
                    plan.Directories.Add(path);
                    if (tag.OriginalPath != "")
                    {
                        plan.RepresentedOriginalDirectories.Add(tag.OriginalPath);
                        bool inheritedChange = originalParent != "" &&
                            Path.GetDirectoryName(tag.OriginalPath).Equals(originalParent, StringComparison.OrdinalIgnoreCase) &&
                            Path.GetFileName(tag.OriginalPath).Equals(node.Text, StringComparison.OrdinalIgnoreCase);
                        if (!Engine.Full(tag.OriginalPath).Equals(path, StringComparison.OrdinalIgnoreCase) && !inheritedChange)
                            plan.FolderChanges.Add(new FolderDelta { From = tag.OriginalPath, To = path });
                    }
                    else if (!snapshot.TargetDirectories.Contains(path))
                        plan.FolderChanges.Add(new FolderDelta { To = path });
                    Collect(node, child, tag.OriginalPath, plan);
                }
                else if (tag.Kind == PlanKind.File)
                {
                    if (!ValidFileName(node.Text)) throw new IOException("파일 이름을 확인하세요: " + node.Text);
                    plan.Files.Add(new PlannedFile {
                        Source = tag.OriginalPath,
                        Destination = StructurePlanner.Destination(root, relative, node.Text),
                        Length = tag.Length, LastWriteTicks = tag.LastWriteTicks,
                        CreationTicks = tag.CreationTicks,
                        FromDownloads = tag.FromDownloads, IsCopy = tag.IsCopy
                    });
                }
            }
        }

        private void CollectHeld(TreeNode folder, string folderPath, PlanData plan)
        {
            foreach (TreeNode node in folder.Nodes)
            {
                PlanTag tag = NodeTag(node);
                if (tag == null || !tag.FromDownloads)
                    throw new IOException("감시 폴더 보류 영역에 다른 항목이 포함되었습니다.");
                if (tag.Kind == PlanKind.SourceFolder)
                {
                    string child = Engine.Full(Path.Combine(folderPath, node.Text));
                    if (!tag.OriginalPath.Equals(child, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("감시 폴더의 하위 폴더 위치가 바뀌었습니다. 추천 구조를 다시 만드세요.");
                    CollectHeld(node, child, plan);
                }
                else if (tag.Kind == PlanKind.File)
                {
                    if (!ValidFileName(node.Text)) throw new IOException("파일 이름을 확인하세요: " + node.Text);
                    plan.Files.Add(new PlannedFile {
                        Source = tag.OriginalPath,
                        Destination = Engine.Full(Path.Combine(folderPath, node.Text)),
                        Length = tag.Length, LastWriteTicks = tag.LastWriteTicks,
                        CreationTicks = tag.CreationTicks, FromDownloads = true
                    });
                }
                else throw new IOException("감시 폴더 보류 영역에는 감시 폴더의 파일만 둘 수 있습니다.");
            }
        }

        private PlanData ReadPlan()
        {
            PlanData plan = new PlanData();
            Collect(proposedRoot, "", root, plan);
            CollectHeld(proposedHold, source, plan);
            HashSet<string> plannedDownloads = new HashSet<string>(
                plan.Files.Where(f => f.FromDownloads).Select(f => f.Source), StringComparer.OrdinalIgnoreCase);
            if (!plannedDownloads.SetEquals(downloadFiles))
                throw new IOException("오른쪽 구조에 빠진 감시 폴더 파일이 있습니다. 추천 구조를 다시 만드세요.");
            foreach (string directory in snapshot.TargetDirectories)
                if (!plan.RepresentedOriginalDirectories.Contains(directory))
                    plan.FolderChanges.Add(new FolderDelta { From = directory });
            return plan;
        }

        private string ProposedRootPath()
        {
            if (proposedRoot == null || Path.GetDirectoryName(root) == null) return root;
            return Engine.Full(Path.Combine(Path.GetDirectoryName(root), proposedRoot.Text));
        }

        private PlanData HistoryPlanWithRenamedRoot(PlanData plan, string newRoot)
        {
            PlanData historyPlan = new PlanData();
            foreach (PlannedFile file in plan.Files)
                historyPlan.Files.Add(new PlannedFile {
                    Source = file.Source,
                    Destination = StructurePlanner.RemapRoot(file.Destination, root, newRoot),
                    Length = file.Length, LastWriteTicks = file.LastWriteTicks,
                    CreationTicks = file.CreationTicks, FromDownloads = file.FromDownloads,
                    IsCopy = file.IsCopy
                });
            foreach (FolderDelta folder in plan.FolderChanges)
                historyPlan.FolderChanges.Add(new FolderDelta {
                    From = folder.From,
                    To = folder.To == "" ? "" : StructurePlanner.RemapRoot(folder.To, root, newRoot)
                });
            historyPlan.FolderChanges.Add(new FolderDelta {
                From = root, To = newRoot, Reason = ChangeJournal.RootRenameReason
            });
            return historyPlan;
        }

        private void ApplyClicked(object sender, EventArgs e)
        {
            if (working || !Loaded || loading) return;
            PlanData plan;
            try { plan = ReadPlan(); }
            catch (Exception ex) { status.Text = ex.Message; return; }
            string newRoot = ProposedRootPath();
            bool renameRoot = !newRoot.Equals(root, StringComparison.OrdinalIgnoreCase);
            int copied = plan.Files.Count(f => f.IsCopy);
            int moved = plan.Files.Count(f => !f.IsCopy &&
                !f.Source.Equals(f.Destination, StringComparison.OrdinalIgnoreCase));
            int operations = moved + copied;
            int created = plan.Directories.Count(d => !snapshot.TargetDirectories.Contains(d));
            int removed = snapshot.TargetDirectories.Count(d => !plan.Directories.Contains(d, StringComparer.OrdinalIgnoreCase));
            string message = (renameRoot ? "정리할 상위 폴더 이름: " + Path.GetFileName(root) +
                " → " + Path.GetFileName(newRoot) + "\n" : "") +
                "파일 " + moved + "개 이동 · " + copied + "개 복사, 폴더 " + created +
                "개 생성, 빈 폴더 " + removed +
                "개 정리 예정입니다.\n\n오른쪽에 보이는 구조대로 적용할까요?";
            if (MessageBox.Show(this, message, "변경 내용 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes) return;
            working = true; applyButton.Enabled = false;
            status.Text = operations > 0 ? "파일 0/" + operations + "개 작업 준비 중..." : "폴더 구조를 적용하는 중입니다...";
            applyProgress.Minimum = 0; applyProgress.Maximum = Math.Max(1, operations);
            applyProgress.Value = 0; applyProgress.Visible = operations > 0;
            statusLine.ColumnStyles[1].Width = operations > 0 ? 180 : 0;
            recommendButton.Enabled = false; resetButton.Enabled = false;
            addButton.Enabled = false; renameButton.Enabled = false; batchRenameButton.Enabled = false; removeButton.Enabled = false;
            proposed.Enabled = false; topics.ReadOnly = true;
            BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += delegate(object workSender, DoWorkEventArgs workArgs)
            {
                System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
                long lastReport = -1000;
                bool stageReported = false, moveReported = false;
                Action<int, int, string, string> report = delegate(int completed, int total, string filePath, string phase)
                {
                    bool firstStage = (phase == "이동 준비 중" || phase == "복사 준비 중") && !stageReported;
                    bool firstMove = (phase == "이동 중" || phase == "복사 중") && !moveReported;
                    bool final = phase == "폴더 정리 중";
                    if (firstStage) stageReported = true;
                    if (firstMove) moveReported = true;
                    if (!firstStage && !firstMove && !final && timer.ElapsedMilliseconds - lastReport < 100)
                        return;
                    lastReport = timer.ElapsedMilliseconds;
                    worker.ReportProgress(completed, new StructureApplyProgress {
                        Completed = completed, Total = total, FilePath = filePath, Phase = phase
                    });
                };
                workArgs.Result = renameRoot ?
                    StructurePlanner.ApplyWithRootRename(root, source, newRoot, snapshot,
                        plan.Files, plan.Directories, report) :
                    StructurePlanner.Apply(root, source, snapshot, plan.Files,
                        plan.Directories, report);
            };
            worker.ProgressChanged += delegate(object workSender, ProgressChangedEventArgs progressEvent)
            {
                StructureApplyProgress progress = progressEvent.UserState as StructureApplyProgress;
                if (progress == null) return;
                applyProgress.Value = Math.Max(0, Math.Min(applyProgress.Maximum, progress.Completed));
                if (progress.Phase == "폴더 정리 중")
                    status.Text = "파일 " + progress.Total + "/" + progress.Total + "개 작업 완료 · 폴더 정리 중...";
                else
                    status.Text = progress.Phase + ": " + DisplayLocation(progress.FilePath) +
                        " (" + progress.Completed + "/" + progress.Total + "개 완료)";
                helpTips.SetToolTip(status, progress.FilePath);
            };
            worker.RunWorkerCompleted += delegate(object workSender, RunWorkerCompletedEventArgs result)
            {
                applyProgress.Visible = false;
                statusLine.ColumnStyles[1].Width = 0;
                working = false; applyButton.Enabled = true;
                recommendButton.Enabled = true; resetButton.Enabled = true;
                addButton.Enabled = true; renameButton.Enabled = true; batchRenameButton.Enabled = true; removeButton.Enabled = true;
                proposed.Enabled = true; topics.ReadOnly = false;
                if (result.Error != null)
                {
                    status.Text = result.Error.Message;
                    MessageBox.Show(this, result.Error.Message, "구조 변경 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                AppliedPlan = renameRoot ? HistoryPlanWithRenamedRoot(plan, newRoot) : plan;
                AppliedRoot = renameRoot ? newRoot : root;
                PreviousSnapshot = snapshot;
                ResultMessage = (renameRoot ? "정리할 상위 폴더 이름 변경 완료 · " : "") +
                    ((ApplySummary)result.Result).ToString();
                DialogResult = DialogResult.OK;
                Close();
            };
            worker.RunWorkerAsync();
        }
    }
}
