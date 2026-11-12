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
        private readonly string source;
        private readonly string root;
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
        internal PlanData AppliedPlan { get; private set; }
        internal StructureSnapshot PreviousSnapshot { get; private set; }

        public StructureEditor(string sourceFolder, string targetRoot)
            : this(sourceFolder, targetRoot, null) { }

        internal StructureEditor(string sourceFolder, string targetRoot, string dataFolderOverride)
        {
            source = Engine.Full(sourceFolder);
            root = Engine.Full(targetRoot);
            Text = "폴더 구조 편집";
            Width = 1160; Height = 760; MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Malgun Gothic", 9F);
            string dataFolder = dataFolderOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownloadOrganizer");
            Directory.CreateDirectory(dataFolder);
            topicsFile = Path.Combine(dataFolder, "topics.txt");
            if (File.Exists(topicsFile)) topics.Text = File.ReadAllText(topicsFile, Encoding.UTF8);
            BuildUi();
            SoftUi.Apply(this);
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
            node.ImageKey = VisualIconKey(tag.Kind);
            node.SelectedImageKey = node.ImageKey;
            if (tag.OriginalPath != "") node.ToolTipText = tag.OriginalPath;
            return node;
        }

        private void BuildUi()
        {
            TableLayoutPanel layout = new TableLayoutPanel {
                Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 6, ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            Controls.Add(layout);

            TableLayoutPanel topicArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            topicArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topicArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            topicArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            topicArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            topicArea.Controls.Add(new Label {
                Text = "업무·주제 목록 — 한 줄에 하나씩 입력하세요. 예: 예산 또는 업무 > 예산",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            topics.Multiline = true; topics.ScrollBars = ScrollBars.Vertical; topics.Dock = DockStyle.Fill;
            topics.Margin = new Padding(4, 3, 7, 3);
            topicArea.Controls.Add(topics, 0, 1);
            recommendButton.Text = "추천 구조 만들기"; recommendButton.Dock = DockStyle.Fill;
            recommendButton.Click += delegate { RebuildFromTopics(); };
            suggestTopicsButton.Text = "주제 추천 받기"; suggestTopicsButton.Dock = DockStyle.Fill;
            suggestTopicsButton.Click += delegate { SuggestTopics(); };
            TableLayoutPanel topicButtons = new TableLayoutPanel {
                Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0)
            };
            topicButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            topicButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            topicButtons.Controls.Add(suggestTopicsButton, 0, 0);
            topicButtons.Controls.Add(recommendButton, 0, 1);
            topicArea.Controls.Add(topicButtons, 1, 1);
            layout.Controls.Add(topicArea, 0, 0);
            layout.Controls.Add(CreateSearchBar(), 0, 1);

            split.Dock = DockStyle.Fill; split.Orientation = Orientation.Vertical;
            layout.Controls.Add(split, 0, 2);
            TableLayoutPanel leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftPanel.Controls.Add(new Label {
                Text = "현재 구조 — 읽기 전용", Dock = DockStyle.Fill,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            current.Dock = DockStyle.Fill; current.HideSelection = false; current.ShowNodeToolTips = true;
            InitializeVisualTree(current);
            current.ItemDrag += delegate(object sender, ItemDragEventArgs e) { StartDrag((TreeNode)e.Item, true); };
            leftPanel.Controls.Add(current, 0, 1);
            split.Panel1.Controls.Add(leftPanel);

            TableLayoutPanel rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            rightPanel.Controls.Add(new Label {
                Text = "변경할 구조 — 드래그로 이동, F2로 파일·폴더 이름 수정",
                Dock = DockStyle.Fill, Font = new Font("Malgun Gothic", 9F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            proposed.Dock = DockStyle.Fill; proposed.HideSelection = false;
            proposed.ShowNodeToolTips = true; proposed.AllowDrop = true;
            InitializeVisualTree(proposed);
            proposed.ItemDrag += delegate(object sender, ItemDragEventArgs e) { StartDrag((TreeNode)e.Item, false); };
            proposed.DragEnter += DragOverPlan;
            proposed.DragOver += DragOverPlan;
            proposed.DragLeave += delegate { ClearDragTarget(); };
            proposed.DragDrop += DropOnPlan;
            rightPanel.Controls.Add(proposed, 0, 1);
            rightPanel.Controls.Add(CreateDragGuide(), 0, 2);
            split.Panel2.Controls.Add(rightPanel);
            layout.Controls.Add(CreateSelectionBar(), 0, 3);

            FlowLayoutPanel buttons = new FlowLayoutPanel {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
            };
            Button close = new SoftButton { Text = "닫기", Width = 90, Height = 34 };
            close.Click += delegate { Close(); };
            applyButton.Text = "변경 내용 확인·적용"; applyButton.Width = 165; applyButton.Height = 34;
            ((SoftButton)applyButton).Primary = true;
            applyButton.Click += ApplyClicked;
            removeButton.Text = "빈 폴더 제거"; removeButton.Width = 120; removeButton.Height = 34;
            removeButton.Click += RemoveFolder;
            addButton.Text = "새 폴더"; addButton.Width = 95; addButton.Height = 34;
            addButton.Click += AddFolder;
            renameButton.Text = "이름 변경"; renameButton.Width = 95; renameButton.Height = 34;
            renameButton.Click += delegate { RenameSelected(); };
            batchRenameButton.Text = "일괄 이름 변경"; batchRenameButton.Width = 125; batchRenameButton.Height = 34;
            batchRenameButton.Click += delegate { ShowBatchRename(); };
            resetButton.Text = "처음 구조로"; resetButton.Width = 115; resetButton.Height = 34;
            resetButton.Click += delegate { RebuildFromTopics(); };
            buttons.Controls.Add(close); buttons.Controls.Add(applyButton); buttons.Controls.Add(removeButton);
            buttons.Controls.Add(batchRenameButton); buttons.Controls.Add(renameButton);
            buttons.Controls.Add(addButton); buttons.Controls.Add(resetButton);
            layout.Controls.Add(buttons, 0, 4);
            status.Text = "오른쪽 구조를 고친 뒤 변경 내용을 확인하고 적용하세요.";
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            status.AutoEllipsis = true;
            statusLine.Dock = DockStyle.Fill; statusLine.RowCount = 1;
            statusLine.ColumnCount = 2; statusLine.Margin = new Padding(0);
            statusLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
            statusLine.Controls.Add(status, 0, 0);
            applyProgress.Dock = DockStyle.Fill; applyProgress.Margin = new Padding(4, 3, 0, 3);
            applyProgress.Visible = false;
            statusLine.Controls.Add(applyProgress, 1, 0);
            layout.Controls.Add(statusLine, 0, 5);

            helpTips.SetToolTip(topics, "업무나 주제를 한 줄에 하나씩 적습니다. '업무 > 예산'처럼 하위 폴더도 지정할 수 있습니다.");
            helpTips.SetToolTip(recommendButton, "입력한 주제로 오른쪽 추천 구조를 다시 만듭니다.");
            helpTips.SetToolTip(suggestTopicsButton, "감시 폴더의 하위 폴더 이름과 파일 이름을 보고 주제 목록을 추천합니다. 고른 뒤 직접 고칠 수 있습니다.");
            helpTips.SetToolTip(current, "현재 폴더와 파일 목록입니다. 폴더 옆 숫자는 하위 폴더까지 포함한 파일 수입니다. 항목을 오른쪽으로 끌 수 있습니다.");
            helpTips.SetToolTip(proposed, "추천 구조입니다. 폴더 옆 숫자는 하위 폴더까지 포함한 파일 수입니다. 파일이나 폴더를 끌어 위치를 바꿀 수 있습니다.");
            helpTips.SetToolTip(addButton, "오른쪽에서 선택한 폴더 안에 새 폴더를 추가합니다.");
            helpTips.SetToolTip(renameButton, "선택한 파일 또는 폴더의 이름을 바꿉니다. F2도 사용할 수 있습니다.");
            helpTips.SetToolTip(batchRenameButton, "선택한 여러 파일 이름의 앞이나 확장자 앞에 같은 문구를 붙입니다.");
            helpTips.SetToolTip(removeButton, "오른쪽에서 선택한 빈 폴더를 계획에서 제거합니다.");
            helpTips.SetToolTip(resetButton, "오른쪽의 수정 내용을 버리고 추천 구조를 다시 만듭니다.");
            helpTips.SetToolTip(applyButton, "변경할 파일과 폴더 수를 확인한 뒤 실제 구조에 적용합니다.");
            helpTips.SetToolTip(close, "파일과 폴더를 바꾸지 않고 창을 닫습니다.");
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
                    left.ForeColor = Color.DimGray; left.ToolTipText = path;
                    TreeNode right = new TreeNode(Path.GetFileName(child));
                    right.Tag = tag; right.ImageKey = "folder"; right.SelectedImageKey = "folder";
                    right.ForeColor = Color.DimGray; right.ToolTipText = path;
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
            TreeNode target = proposed.GetNodeAt(point);
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
                        FromDownloads = tag.FromDownloads
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
                    CreationTicks = file.CreationTicks, FromDownloads = file.FromDownloads
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
            int moved = plan.Files.Count(f => !f.Source.Equals(f.Destination, StringComparison.OrdinalIgnoreCase));
            int created = plan.Directories.Count(d => !snapshot.TargetDirectories.Contains(d));
            int removed = snapshot.TargetDirectories.Count(d => !plan.Directories.Contains(d, StringComparer.OrdinalIgnoreCase));
            string message = (renameRoot ? "정리할 상위 폴더 이름: " + Path.GetFileName(root) +
                " → " + Path.GetFileName(newRoot) + "\n" : "") +
                "파일 " + moved + "개 변경, 폴더 " + created + "개 생성, 빈 폴더 " + removed +
                "개 정리 예정입니다.\n\n오른쪽에 보이는 구조대로 적용할까요?";
            if (MessageBox.Show(this, message, "변경 내용 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes) return;
            working = true; applyButton.Enabled = false;
            status.Text = moved > 0 ? "파일 0/" + moved + "개 이동 준비 중..." : "폴더 구조를 적용하는 중입니다...";
            applyProgress.Minimum = 0; applyProgress.Maximum = Math.Max(1, moved);
            applyProgress.Value = 0; applyProgress.Visible = moved > 0;
            statusLine.ColumnStyles[1].Width = moved > 0 ? 180 : 0;
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
                    bool firstStage = phase == "이동 준비 중" && !stageReported;
                    bool firstMove = phase == "이동 중" && !moveReported;
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
                    status.Text = "파일 " + progress.Total + "/" + progress.Total + "개 이동 완료 · 폴더 정리 중...";
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
