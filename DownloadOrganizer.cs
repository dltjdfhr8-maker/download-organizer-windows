using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace DownloadOrganizer
{
    public class Settings
    {
        public string Source = "";
        public string Root = "";
        public bool Automatic = false;
        public bool AutomaticStartedBefore = false;
        public long AutoStartedAtTicks = 0;
        public List<string> Seen = new List<string>();
    }

    public class MoveEntry
    {
        public string Source = "";
        public string Destination = "";
        public long Length;
        public long LastWriteTicks;
        public string When = "";
        public string Reason = "";
    }

    public class History
    {
        public List<MoveEntry> Entries = new List<MoveEntry>();
    }

    public class Suggestion
    {
        public string File = "";
        public string Folder = "";
        public string RunnerUp = "";
        public string Reason = "";
        public double Score;
        public double RunnerScore;
        public bool Certain;
        public double SearchRankScore;
        public string ReviewState = "";
    }

    public class FolderChoice
    {
        public string Path;
        public string Label;
        public List<string> SampleNames = new List<string>();
        public HashSet<string> Terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public override string ToString() { return Label; }
    }

    public static class Engine
    {
        private static readonly HashSet<string> Common = new HashSet<string>(new string[] {
            "자료", "문서", "파일", "다운로드", "기타", "최종", "사본", "복사본",
            "새", "new", "file", "download", "copy", "final"
        });
        private static readonly HashSet<string> Temporary = new HashSet<string>(new string[] {
            ".crdownload", ".part", ".partial", ".tmp", ".temp", ".download"
        });

        public static string Full(string path)
        {
            string full = Path.GetFullPath(path);
            if (full.Equals(Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase)) return full;
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static bool SameOrInside(string child, string parent)
        {
            string c = Full(child), p = Full(parent);
            string prefix = p.EndsWith(Path.DirectorySeparatorChar.ToString()) ? p : p + Path.DirectorySeparatorChar;
            return c.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static string Validate(string source, string root)
        {
            if (String.IsNullOrWhiteSpace(source) || String.IsNullOrWhiteSpace(root))
                return "감시할 폴더와 정리할 상위 폴더를 모두 선택하세요.";
            if (!Directory.Exists(source) || !Directory.Exists(root))
                return "선택한 폴더를 찾을 수 없습니다.";
            if (!Full(source).Equals(Full(root), StringComparison.OrdinalIgnoreCase) &&
                (SameOrInside(source, root) || SameOrInside(root, source)))
                return "두 폴더는 같은 위치이거나 서로 분리된 위치여야 합니다.";
            return "";
        }

        public static List<FolderChoice> Folders(string root)
        {
            List<FolderChoice> result = new List<FolderChoice>();
            Stack<string> pending = new Stack<string>();
            pending.Push(Full(root));
            while (pending.Count > 0 && result.Count < 1000)
            {
                string current = pending.Pop();
                string[] children;
                try { children = Directory.GetDirectories(current); }
                catch { continue; }
                Array.Sort(children, StringComparer.CurrentCultureIgnoreCase);
                foreach (string child in children)
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(child);
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        if (Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal)) continue;
                        string relative = child.Substring(Full(root).Length).TrimStart(Path.DirectorySeparatorChar);
                        FolderChoice choice = new FolderChoice { Path = child, Label = relative };
                        foreach (string term in Words(Path.GetFileName(child))) AddSearchKeys(choice.Terms, term);
                        try
                        {
                            choice.SampleNames = Directory.GetFiles(child)
                                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                                .Take(100)
                                .Select(x => Path.GetFileNameWithoutExtension(x)).ToList();
                            foreach (string sample in choice.SampleNames)
                                foreach (string term in Words(sample)) AddSearchKeys(choice.Terms, term);
                        }
                        catch { }
                        result.Add(choice);
                        pending.Push(child);
                        if (result.Count >= 1000) break;
                    }
                    catch { }
                }
            }
            return result;
        }

        public static List<FileInfo> SourceFiles(string source, bool includeSubdirectories = false)
        {
            List<FileInfo> result = new List<FileInfo>();
            Stack<string> pending = new Stack<string>();
            pending.Push(Full(source));
            int directoryCount = 0;
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (++directoryCount > 10000)
                    throw new IOException("파일이나 폴더가 10,000개를 넘어 자동 확인을 중단했습니다.");
                if (includeSubdirectories)
                {
                    string[] directories;
                    try { directories = Directory.GetDirectories(current); }
                    catch { directories = new string[0]; }
                    foreach (string directory in directories)
                    {
                        try
                        {
                            FileAttributes attributes = File.GetAttributes(directory);
                            string name = Path.GetFileName(directory);
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0 ||
                                name.StartsWith(".", StringComparison.Ordinal)) continue;
                            pending.Push(directory);
                        }
                        catch { }
                    }
                }
                string[] paths;
                try { paths = Directory.GetFiles(current); }
                catch { paths = new string[0]; }
                foreach (string path in paths)
                {
                    try
                    {
                        FileInfo info = new FileInfo(path);
                        if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        if (Temporary.Contains(info.Extension.ToLowerInvariant())) continue;
                        if (info.Name.StartsWith("~$", StringComparison.Ordinal)) continue;
                        result.Add(info);
                    }
                    catch { }
                }
                if (result.Count + directoryCount > 10000 || pending.Count > 10000)
                    throw new IOException("파일이나 폴더가 10,000개를 넘어 자동 확인을 중단했습니다.");
            }
            return result.OrderByDescending(f => f.CreationTimeUtc).ToList();
        }

        private static string Clean(string name)
        {
            return name.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        }

        internal static List<string> Words(string name)
        {
            List<string> words = new List<string>();
            foreach (Match match in Regex.Matches(Clean(name), @"[\p{L}\p{Nd}]+"))
            {
                string word = match.Value;
                if (word.Length < 2 || Common.Contains(word) || word.All(Char.IsDigit)) continue;
                words.Add(word);
            }
            return words.Distinct().ToList();
        }

        private static void AddSearchKeys(HashSet<string> keys, string word)
        {
            keys.Add(word);
            for (int i = 0; i < word.Length - 1; i++)
                keys.Add(word.Substring(i, 2));
        }

        public static string StructurePath(string root, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                throw new ArgumentException("상위 폴더 아래의 경로를 입력하세요.");
            string[] segments = relative.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(s => s == "." || s == ".." ||
                s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new ArgumentException("폴더 이름에 사용할 수 없는 문자가 있습니다.");
            string result = Full(Path.Combine(root, relative));
            if (!SameOrInside(result, root) || Full(result).Equals(Full(root), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("정리할 상위 폴더 안의 경로만 만들 수 있습니다.");
            return result;
        }

        private static HashSet<string> Bigrams(string value)
        {
            HashSet<string> grams = new HashSet<string>();
            for (int i = 0; i < value.Length - 1; i++) grams.Add(value.Substring(i, 2));
            return grams;
        }

        public static double Similarity(string incoming, string example)
        {
            List<string> a = Words(incoming), b = Words(example);
            if (a.Count == 0 || b.Count == 0) return 0;
            int exact = a.Intersect(b).Count();
            bool contained = a.Any(x => b.Any(y => x.Length >= 2 && y.Length >= 2 &&
                (x.Contains(y) || y.Contains(x))));
            if (exact == 0 && !contained) return 0;
            // Penalize unmatched words on either side. Dividing by the shorter name made
            // "보도자료" and "행정안전부_보도자료" almost equally strong matches.
            double word = 2.0 * exact / (a.Count + b.Count);
            if (exact == 0 && contained) word = 0.75;
            string left = String.Join("", a), right = String.Join("", b);
            HashSet<string> ga = Bigrams(left), gb = Bigrams(right);
            double dice = ga.Count + gb.Count == 0 ? 0 :
                2.0 * ga.Intersect(gb).Count() / (ga.Count + gb.Count);
            return Math.Min(1.0, 0.72 * word + 0.28 * dice);
        }

        public static Suggestion Suggest(FileInfo file, List<FolderChoice> folders)
        {
            return Suggest(file, folders, new LocalSearchIndex(folders));
        }

        private static FolderChoice UniqueExactFolderAnchor(string stem, string currentFolder,
            List<FolderChoice> folders, out bool duplicated)
        {
            duplicated = false;
            // A file already inside a destination subfolder may intentionally belong there.
            // Use this shortcut only for incoming or top-level files.
            if (folders.Any(folder => SameOrInside(currentFolder, folder.Path))) return null;
            HashSet<string> nameWords = new HashSet<string>(Words(stem));
            if (nameWords.Count == 0) return null;
            List<FolderChoice> matches = new List<FolderChoice>();
            foreach (FolderChoice folder in folders)
            {
                List<string> folderWords = Words(Path.GetFileName(folder.Path));
                if (folderWords.Count > 0 && nameWords.SetEquals(folderWords)) matches.Add(folder);
                if (matches.Count > 1) { duplicated = true; return null; }
            }
            return matches.Count == 1 ? matches[0] : null;
        }

        internal static Suggestion Suggest(FileInfo file, List<FolderChoice> folders,
            LocalSearchIndex index)
        {
            string stem = Path.GetFileNameWithoutExtension(file.Name);
            string currentFolder = Full(Path.GetDirectoryName(file.FullName));
            List<string> queryTerms = Words(stem);
            HashSet<string> queryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string term in queryTerms) AddSearchKeys(queryKeys, term);
            Dictionary<string, SearchEvidence> evidence = index.Score(stem, currentFolder);
            List<Suggestion> scores = new List<Suggestion>();
            foreach (FolderChoice folder in folders)
            {
                SearchEvidence extra = evidence[folder.Path];
                bool isCurrentFolder = currentFolder.Equals(folder.Path, StringComparison.OrdinalIgnoreCase);
                double folderScore = Similarity(stem, Path.GetFileName(folder.Path));
                if (folderScore <= 0 && extra.KeywordScore <= 0 && extra.FuzzyScore <= 0 &&
                    !queryKeys.Any(key => folder.Terms.Contains(key))) continue;
                double existingScore = 0;
                string existingName = "";
                try
                {
                    foreach (string sample in folder.SampleNames)
                    {
                        if (isCurrentFolder && String.Equals(sample, stem, StringComparison.OrdinalIgnoreCase)) continue;
                        double score = Similarity(stem, sample);
                        if (score > existingScore) { existingScore = score; existingName = sample; }
                    }
                }
                catch { }
                double scoreFinal = Math.Max(folderScore, existingScore * 0.92);
                string reason = folderScore >= existingScore * 0.92 ?
                    "폴더명: " + folder.Label : "기존 파일: " + existingName;
                if (scoreFinal <= 0 && extra.FuzzyScore > 0)
                    reason = "오타 유사 후보: " + extra.FuzzyWord;
                else if (scoreFinal <= 0 && extra.KeywordScore > 0)
                    reason = "키워드 후보: " + extra.Keyword;
                else if (extra.Keyword != "")
                    reason += " / 키워드: " + extra.Keyword;
                scores.Add(new Suggestion {
                    File = file.FullName, Folder = folder.Path, Score = scoreFinal,
                    SearchRankScore = scoreFinal + 0.18 * extra.KeywordScore +
                        0.08 * extra.FuzzyScore,
                    ReviewState = scoreFinal <= 0 && extra.FuzzyScore > 0 ?
                        "오타 유사 후보 · 확인 필요" : scoreFinal <= 0 && extra.KeywordScore > 0 ?
                        "키워드 후보 · 확인 필요" : "",
                    Reason = reason
                });
            }
            Suggestion originalBest = scores.OrderByDescending(s => s.Score).FirstOrDefault();
            Suggestion originalSecond = scores.OrderByDescending(s => s.Score).Skip(1).FirstOrDefault();
            Suggestion best = scores.OrderByDescending(s => s.SearchRankScore).FirstOrDefault();
            if (best == null) return new Suggestion { File = file.FullName, Reason = "하위 폴더가 없습니다" };
            if (best.SearchRankScore <= 0) return new Suggestion {
                File = file.FullName, Reason = "이름과 일치하는 폴더나 기존 파일이 없습니다"
            };
            bool duplicatedAnchor;
            FolderChoice exactAnchor = UniqueExactFolderAnchor(stem, currentFolder, folders,
                out duplicatedAnchor);
            if (duplicatedAnchor)
            {
                best.Certain = false;
                best.ReviewState = "후보 폴더가 비슷함";
                best.Reason += " / 같은 핵심 이름의 폴더가 여러 곳";
                return best;
            }
            if (exactAnchor != null)
            {
                Suggestion anchored = scores.FirstOrDefault(score => score.Folder.Equals(
                    exactAnchor.Path, StringComparison.OrdinalIgnoreCase));
                if (anchored != null)
                {
                    anchored.Certain = true;
                    anchored.ReviewState = "";
                    anchored.Reason = "폴더명: " + exactAnchor.Label +
                        " / 파일명 핵심 부분과 폴더명이 정확히 일치";
                    return anchored;
                }
            }
            Suggestion second = scores.OrderByDescending(s => s.SearchRankScore).Skip(1).FirstOrDefault();
            best.RunnerScore = second == null ? 0 : second.Score;
            best.RunnerUp = second == null ? "" : second.Folder;
            bool oldCertain = originalBest != null && originalBest.Score >= 0.62 &&
                originalBest.Score - (originalSecond == null ? 0 : originalSecond.Score) >= 0.15;
            best.Certain = oldCertain && best.Folder == originalBest.Folder;
            if (!best.Certain && oldCertain && best.Folder != originalBest.Folder)
                best.ReviewState = "키워드 후보가 달라 확인 필요";
            return best;
        }

        public static string Fingerprint(FileInfo file)
        {
            return file.FullName.ToLowerInvariant() + "|" + file.CreationTimeUtc.Ticks.ToString();
        }

        public static bool Ready(FileInfo file)
        {
            try
            {
                file.Refresh();
                if (!file.Exists || (DateTime.UtcNow - file.LastWriteTimeUtc).TotalSeconds < 3) return false;
                using (FileStream stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                return true;
            }
            catch { return false; }
        }

        public static string Move(string source, string destinationFolder)
        {
            if (!File.Exists(source) || !Directory.Exists(destinationFolder)) throw new IOException("파일이나 목적지 폴더가 없습니다.");
            if (Full(Path.GetDirectoryName(source)).Equals(Full(destinationFolder), StringComparison.OrdinalIgnoreCase))
                return source;
            string stem = Path.GetFileNameWithoutExtension(source), extension = Path.GetExtension(source);
            string destination = Path.Combine(destinationFolder, Path.GetFileName(source));
            int number = 2;
            while (File.Exists(destination))
            {
                destination = Path.Combine(destinationFolder, stem + " (" + number + ")" + extension);
                number++;
            }
            File.Move(source, destination);
            return destination;
        }
    }

    public partial class MainForm : Form
    {
        private const string PreviewListHelp =
            "현재 폴더의 파일과 바로 아래 폴더만 보입니다. 판단 근거 칸에 마우스를 올리면 이유가 나옵니다.";
        private TextBox sourceBox = new TextBox(), rootBox = new TextBox();
        private Button sourceButton = new SoftButton(), rootButton = new SoftButton(), scanButton = new SoftButton(), batchButton = new SoftButton(), structureButton = new SoftButton();
        private Button moveButton = new SoftButton(), undoButton = new SoftButton();
        private CheckBox automaticBox = new CheckBox(), startupBox = new CheckBox();
        private ListView list = new ListView();
        private List<ScanRow> allPreviewRows = new List<ScanRow>();
        private List<ScanRow> previewRows = new List<ScanRow>();
        private string previewFolder = "";
        private Button previewBackButton = new SoftButton();
        private Label previewPathLabel = new Label();
        private ComboBox reasonFilter = new ComboBox();
        private ComboBox folderBox = new ComboBox();
        private Label status = new Label();
        private Timer timer = new Timer();
        private readonly ToolTip helpTips = new ToolTip {
            InitialDelay = 450, ReshowDelay = 150, AutoPopDelay = 10000, ShowAlways = true
        };
        private string currentPreviewToolTip = "";
        private int hoverReasonIndex = -1;
        private Settings settings;
        private History history;
        private ChangeJournal journal;
        private List<FolderChoice> folders = new List<FolderChoice>();
        private Dictionary<string, string> observations = new Dictionary<string, string>();
        private bool updatingRootAfterRename;
        private string dataDirectory;
        private string indexedRoot = "";
        private DateTime indexedAtUtc = DateTime.MinValue;

        public MainForm() : this(null) { }

        internal MainForm(string dataFolderOverride)
        {
            Text = "다운로드 자동 정리";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
            catch { Icon = SystemIcons.Application; }
            Width = 930; Height = 650; MinimumSize = new Size(840, 500);
            Font = new Font("Malgun Gothic", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            dataDirectory = dataFolderOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownloadOrganizer");
            Directory.CreateDirectory(dataDirectory);
            settings = Read<Settings>("settings.xml", new Settings());
            history = Read<History>("history.xml", new History());
            journal = Read<ChangeJournal>("changes.xml", new ChangeJournal());
            if (journal.Records.Count == 0 && history.Entries.Count > 0)
            {
                foreach (MoveEntry old in history.Entries)
                    journal.AddFile(old.Source, old.Destination, old.Length,
                        old.LastWriteTicks, old.When, old.Reason, "이전 버전");
                Write("changes.xml", journal);
            }
            if (String.IsNullOrEmpty(settings.Source)) settings.Source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            BuildUi();
            SoftUi.Apply(this);
            // The flat style hid the box's border, so it looked like empty space.
            folderBox.FlatStyle = FlatStyle.System;
            folderBox.HandleCreated += delegate { ShowFolderBoxHint(); };
            sourceBox.Text = settings.Source; rootBox.Text = settings.Root;
            sourceBox.TextChanged += FolderSettingChanged;
            rootBox.TextChanged += FolderSettingChanged;
            InitializeTrayAndMonitoring();
            automaticBox.Checked = settings.Automatic;
            startupBox.Checked = IsStartupEnabled();
            Shown += delegate {
                if (automaticBox.Checked) StartAutomaticMonitoring();
                else BeginPreview();
            };
            FormClosing += HandleFormClosing;
            Disposed += delegate { helpTips.Dispose(); };
        }

        private T Read<T>(string name, T fallback)
        {
            try
            {
                using (FileStream stream = File.OpenRead(Path.Combine(dataDirectory, name)))
                    return (T)new XmlSerializer(typeof(T)).Deserialize(stream);
            }
            catch { return fallback; }
        }

        private void Write<T>(string name, T value)
        {
            string final = Path.Combine(dataDirectory, name), temporary = final + ".tmp";
            using (FileStream stream = File.Create(temporary)) new XmlSerializer(typeof(T)).Serialize(stream, value);
            if (File.Exists(final)) File.Replace(temporary, final, null);
            else File.Move(temporary, final);
        }

        private void Save()
        {
            try
            {
                settings.Source = sourceBox.Text.Trim(); settings.Root = rootBox.Text.Trim();
                settings.Automatic = automaticBox.Checked;
                Write("settings.xml", settings);
            }
            catch (Exception ex) { status.Text = "설정 저장 실패: " + ex.Message; }
        }

        private void BuildUi()
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill; layout.Padding = new Padding(16);
            layout.ColumnCount = 3; layout.RowCount = 8;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            Controls.Add(layout);
            Label sourceLabel = new Label { Text = "감시할 폴더", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            Label rootLabel = new Label { Text = "정리할 상위 폴더", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            sourceBox.Dock = DockStyle.Fill; rootBox.Dock = DockStyle.Fill;
            sourceBox.Margin = new Padding(4, 8, 4, 8);
            rootBox.Margin = new Padding(4, 8, 4, 8);
            sourceButton.Text = "선택"; sourceButton.Dock = DockStyle.Fill;
            rootButton.Text = "선택"; rootButton.Dock = DockStyle.Fill;
            sourceButton.Click += delegate { ChooseFolder(sourceBox); };
            rootButton.Click += delegate { ChooseFolder(rootBox); };
            TableLayoutPanel rootPathPanel = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0)
            };
            rootPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rootPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            Button sameFolderButton = new SoftButton { Text = "같은 폴더", Dock = DockStyle.Fill };
            sameFolderButton.Click += delegate {
                rootBox.Text = sourceBox.Text.Trim();
                BeginPreview();
            };
            rootPathPanel.Controls.Add(rootBox, 0, 0);
            rootPathPanel.Controls.Add(sameFolderButton, 1, 0);
            layout.Controls.Add(sourceLabel, 0, 0); layout.Controls.Add(sourceBox, 1, 0); layout.Controls.Add(sourceButton, 2, 0);
            layout.Controls.Add(rootLabel, 0, 1); layout.Controls.Add(rootPathPanel, 1, 1); layout.Controls.Add(rootButton, 2, 1);

            FlowLayoutPanel commands = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            scanButton.Text = "미리보기 새로고침"; scanButton.Width = 150; scanButton.Height = 34;
            ((SoftButton)scanButton).Primary = true;
            batchButton.Text = "확실한 추천만 정리"; batchButton.Width = 160; batchButton.Height = 34;
            batchButton.Click += delegate { BeginBatchMove(); };
            structureButton.Text = "새 폴더 구조 제안"; structureButton.Width = 140; structureButton.Height = 34;
            structureButton.Click += delegate { ShowStructureEditor(); };
            automaticBox.Text = "새 파일 자동 이동"; automaticBox.Width = 155; automaticBox.Padding = new Padding(10, 5, 0, 0);
            startupBox.Text = "로그인 시 실행"; startupBox.Width = 135; startupBox.Padding = new Padding(10, 5, 0, 0);
            startupBox.CheckedChanged += delegate { SetStartup(startupBox.Checked); };
            commands.Controls.Add(scanButton); commands.Controls.Add(batchButton); commands.Controls.Add(structureButton);
            commands.Controls.Add(automaticBox); commands.Controls.Add(startupBox);
            layout.Controls.Add(commands, 0, 2); layout.SetColumnSpan(commands, 3);
            scanButton.Click += delegate { BeginPreview(); };
            automaticBox.CheckedChanged += AutomaticChanged;

            TableLayoutPanel previewNavigation = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0)
            };
            previewNavigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            previewNavigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            previewNavigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            previewNavigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            previewNavigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewBackButton.Text = "← 상위 폴더"; previewBackButton.Dock = DockStyle.Fill;
            previewBackButton.Enabled = false;
            previewBackButton.Click += delegate { PreviewParentFolder(); };
            previewPathLabel.Text = "감시 폴더"; previewPathLabel.Dock = DockStyle.Fill;
            previewPathLabel.TextAlign = ContentAlignment.MiddleLeft;
            previewPathLabel.AutoEllipsis = true;
            previewPathLabel.Padding = new Padding(12, 0, 0, 0);
            previewNavigation.Controls.Add(previewBackButton, 0, 0);
            previewNavigation.Controls.Add(previewPathLabel, 1, 0);
            previewNavigation.Controls.Add(new Label { Text = "판단 근거", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight }, 2, 0);
            reasonFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            reasonFilter.Dock = DockStyle.Fill;
            reasonFilter.Items.AddRange(new object[] { "전체", "폴더명", "기존 파일", "키워드",
                "오타 유사", "근거 없음" });
            reasonFilter.SelectedIndex = 0;
            reasonFilter.SelectedIndexChanged += delegate { ApplyReasonFilter(true); };
            previewNavigation.Controls.Add(reasonFilter, 3, 0);
            layout.Controls.Add(previewNavigation, 0, 3); layout.SetColumnSpan(previewNavigation, 3);

            list.Dock = DockStyle.Fill; list.View = View.Details; list.FullRowSelect = true;
            list.HideSelection = false; list.VirtualMode = true;
            list.Columns.Add("파일", 220); list.Columns.Add("추천 후보", 190);
            list.Columns.Add("판단 근거", 260); list.Columns.Add("상태", 150);
            list.RetrieveVirtualItem += delegate(object sender, RetrieveVirtualItemEventArgs e)
            {
                if (e.ItemIndex < 0 || e.ItemIndex >= previewRows.Count)
                { e.Item = new ListViewItem(""); return; }
                ScanRow row = previewRows[e.ItemIndex];
                e.Item = new ListViewItem(new string[] { row.Name, row.Folder, row.Reason, row.State });
                if (row.DirectoryPath != null)
                { e.Item.BackColor = Color.FromArgb(236, 245, 255); e.Item.ForeColor = Color.FromArgb(29, 83, 153); }
                else if ((e.ItemIndex & 1) != 0) e.Item.BackColor = Color.FromArgb(249, 251, 254);
            };
            list.SelectedIndexChanged += delegate { SelectSuggestedFolder(); };
            list.MouseMove += delegate(object sender, MouseEventArgs e) { UpdatePreviewToolTip(e.Location); };
            list.MouseLeave += delegate { hoverReasonIndex = -1; SetPreviewToolTip(PreviewListHelp); };
            list.DoubleClick += delegate { OpenSelectedPreviewFolder(); };
            list.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Enter) { OpenSelectedPreviewFolder(); e.Handled = true; }
                else if (e.KeyCode == Keys.Back) { PreviewParentFolder(); e.Handled = true; }
            };
            layout.Controls.Add(list, 0, 4); layout.SetColumnSpan(list, 3);

            Label folderLabel = new Label { Text = "선택 파일의 목적지", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            folderBox.Dock = DockStyle.Fill; folderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            layout.Controls.Add(folderLabel, 0, 5); layout.Controls.Add(folderBox, 1, 5);
            moveButton.Text = "선택 파일 이동"; moveButton.Dock = DockStyle.Fill;
            ((SoftButton)moveButton).Primary = true;
            moveButton.Click += delegate { MoveSelected(); };
            layout.Controls.Add(moveButton, 2, 5);

            undoButton.Text = "항목별 되돌리기"; undoButton.Width = 150; undoButton.Height = 34;
            undoButton.Click += delegate { ShowHistory(); };
            Label hint = new Label { Text = "처음에는 미리보기로 확인하세요. 애매한 파일은 자동으로 이동하지 않습니다.", AutoSize = true, ForeColor = SoftUi.Muted, Padding = new Padding(8, 9, 0, 0) };
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
            actions.Controls.Add(undoButton); actions.Controls.Add(hint);
            layout.Controls.Add(actions, 0, 6); layout.SetColumnSpan(actions, 3);
            status.Text = "폴더를 선택하고 미리보기를 누르세요.";
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(status, 0, 7); layout.SetColumnSpan(status, 3);

            helpTips.SetToolTip(sourceLabel, "새 파일을 확인할 폴더입니다. 보통 다운로드 폴더를 선택합니다.");
            helpTips.SetToolTip(sourceBox, "새 파일을 확인할 폴더 경로입니다.");
            helpTips.SetToolTip(sourceButton, "새 파일을 확인할 폴더를 선택합니다.");
            helpTips.SetToolTip(rootLabel, "정리된 파일을 보관할 상위 폴더입니다.");
            helpTips.SetToolTip(rootBox, "파일을 분류해 넣을 상위 폴더 경로입니다.");
            helpTips.SetToolTip(sameFolderButton, "선택한 폴더와 그 하위 폴더의 파일을 같은 구조 안에서 정리합니다.");
            helpTips.SetToolTip(rootButton, "정리된 파일을 보관할 상위 폴더를 선택합니다.");
            helpTips.SetToolTip(scanButton, "현재 파일의 추천 폴더를 다시 표시합니다. 파일은 옮기지 않습니다.");
            helpTips.SetToolTip(previewBackButton, "현재 미리보기의 한 단계 위 폴더로 돌아갑니다.");
            helpTips.SetToolTip(previewPathLabel, "지금 보고 있는 감시 폴더의 위치입니다. 폴더를 두 번 클릭하면 안으로 들어갑니다.");
            helpTips.SetToolTip(reasonFilter,
                "폴더명: 파일명과 폴더 이름이 비슷할 때\n" +
                "기존 파일: 그 폴더에 있던 파일명과 더 비슷할 때\n" +
                "키워드: 파일명 단어가 폴더 경로나 기존 파일명에 나올 때\n" +
                "오타 유사: 폴더 이름과 1~2글자 차이의 후보가 있을 때\n" +
                "근거 없음: 이름이나 단어로 연결할 폴더를 찾지 못했을 때\n" +
                "필터는 화면 표시만 바꿉니다. 하위 폴더 줄은 계속 보입니다.");
            helpTips.SetToolTip(batchButton, "선택 여부와 관계없이 추천이 확실한 파일만 옮깁니다. '후보 폴더가 비슷함' 파일은 아래에서 목적지를 골라 직접 이동하세요.");
            helpTips.SetToolTip(structureButton, "현재 폴더와 추천 구조를 나란히 보고 수정합니다.");
            helpTips.SetToolTip(automaticBox, "새로 받은 파일을 확인해 추천이 확실하면 자동으로 옮깁니다. 감시 폴더의 하위 폴더 안 파일은 옮기지 않습니다. 한 폴더 방식에서는 하위 폴더의 새 파일도 확인합니다.");
            helpTips.SetToolTip(startupBox, "Windows에 로그인할 때 이 프로그램을 자동으로 실행합니다.");
            SetPreviewToolTip(PreviewListHelp);
            helpTips.SetToolTip(folderLabel, "선택한 파일을 옮길 폴더입니다.");
            helpTips.SetToolTip(folderBox, "선택한 파일의 목적지 폴더를 고릅니다.");
            helpTips.SetToolTip(moveButton, "선택한 파일을 모두 지정한 폴더로 옮깁니다. 여러 파일은 이동 전에 다시 확인합니다.");
            helpTips.SetToolTip(undoButton, "파일·폴더별 변경 이력을 보고 원하는 항목을 되돌립니다.");
        }

        private void SetPreviewToolTip(string message)
        {
            if (message == currentPreviewToolTip) return;
            if (list.IsHandleCreated) helpTips.Hide(list);
            helpTips.SetToolTip(list, message);
            currentPreviewToolTip = message;
        }

        private void UpdatePreviewToolTip(Point location)
        {
            ListViewHitTestInfo hit = list.HitTest(location);
            if (hit.Item == null || hit.SubItem == null || hit.Item.SubItems.Count <= 2 ||
                hit.SubItem != hit.Item.SubItems[2] || hit.Item.Index < 0 ||
                hit.Item.Index >= previewRows.Count)
            { hoverReasonIndex = -1; SetPreviewToolTip(PreviewListHelp); return; }
            if (hoverReasonIndex == hit.Item.Index) return;
            hoverReasonIndex = hit.Item.Index;
            ScanRow row = previewRows[hit.Item.Index];
            SetPreviewToolTip(row.DirectoryPath == null ? ExplainJudgmentReason(row) : PreviewListHelp);
        }

        private static string ExplainJudgmentReason(ScanRow row)
        {
            string reason = row.Reason ?? "";
            StringBuilder explanation = new StringBuilder();
            explanation.AppendLine("판단 근거: " + (reason == "" ? "없음" : reason));
            if (reason.StartsWith("폴더명:", StringComparison.Ordinal))
                explanation.AppendLine("파일 이름과 폴더 이름의 단어가 비슷하고, 이 근거가 기존 파일명과 같거나 더 강할 때 표시됩니다.");
            else if (reason.StartsWith("기존 파일:", StringComparison.Ordinal))
                explanation.AppendLine("그 폴더에 이미 있는 파일의 이름이 폴더 이름보다 더 강한 단서일 때 표시됩니다.");
            else if (reason.StartsWith("키워드 후보:", StringComparison.Ordinal))
                explanation.AppendLine("파일명과 직접 비슷한 이름은 없지만, 폴더 경로나 기존 파일명의 단어가 검색되어 후보가 됐습니다.");
            else if (reason.StartsWith("오타 유사 후보:", StringComparison.Ordinal))
                explanation.AppendLine("파일명 단어와 폴더명 단어의 첫 글자가 같고 1~2글자 차이가 나 후보가 됐습니다.");
            else if (reason.StartsWith("하위 폴더가 없습니다", StringComparison.Ordinal))
                explanation.AppendLine("정리할 상위 폴더 아래에 비교할 하위 폴더가 없을 때 표시됩니다.");
            else if (reason.StartsWith("이름과 일치하는 폴더나 기존 파일이 없습니다", StringComparison.Ordinal))
                explanation.AppendLine("파일명과 연결할 폴더명·기존 파일명·키워드를 찾지 못했을 때 표시됩니다.");
            if (reason.IndexOf("/ 키워드:", StringComparison.Ordinal) >= 0)
                explanation.AppendLine("키워드는 파일명 단어가 폴더 경로나 기존 파일명에도 나와 검색 순위에 반영됐다는 뜻입니다.");
            if (row.State == "후보 폴더가 비슷함")
                explanation.AppendLine("상위 후보들의 점수 차이가 작아 자동 이동은 보류했습니다.");
            else if (row.State == "이름 일치가 약함")
                explanation.AppendLine("이름 유사도가 자동 이동 기준에 못 미쳐 보류했습니다.");
            return explanation.ToString().TrimEnd();
        }

        private void ChooseFolder(TextBox box)
        {
            if (scanBusy)
            {
                queuedAction = delegate { ChooseFolder(box); };
                return;
            }
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "폴더를 선택하세요";
                if (Directory.Exists(box.Text)) dialog.SelectedPath = box.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) { box.Text = dialog.SelectedPath; BeginPreview(); }
            }
        }

        private void FolderSettingChanged(object sender, EventArgs e)
        {
            if (updatingRootAfterRename) return;
            CancelAutomaticScan();
            if (automaticBox.Checked) automaticBox.Checked = false;
            settings.AutomaticStartedBefore = false;
            settings.AutoStartedAtTicks = 0;
            settings.Seen.Clear();
            observations.Clear();
            indexedRoot = "";
            previewFolder = "";
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key == null) return false;
                    string configured = Convert.ToString(key.GetValue("DownloadOrganizer")) ?? "";
                    if (configured == "\"" + Application.ExecutablePath + "\"") return true;
                    if (configured.Length < 2 || configured[0] != '"' ||
                        configured[configured.Length - 1] != '"') return false;
                    string previous = configured.Substring(1, configured.Length - 2);
                    return Regex.IsMatch(Path.GetFileName(Application.ExecutablePath),
                        @"^DownloadOrganizer-v\d+\.exe$", RegexOptions.IgnoreCase) &&
                        Regex.IsMatch(Path.GetFileName(previous),
                        @"^DownloadOrganizer-v\d+\.exe$", RegexOptions.IgnoreCase) &&
                        Path.GetDirectoryName(previous).Equals(
                            Path.GetDirectoryName(Application.ExecutablePath),
                            StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private void SetStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) throw new IOException("시작 프로그램 설정을 열 수 없습니다.");
                    if (enabled) key.SetValue("DownloadOrganizer", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("DownloadOrganizer", false);
                }
                status.Text = enabled ? "Windows 로그인 시 자동으로 실행됩니다." : "로그인 시 실행을 껐습니다.";
            }
            catch (Exception ex) { status.Text = "시작 프로그램 설정 실패: " + ex.Message; }
        }

        private bool Prepare(bool refresh = false)
        {
            try
            {
                string error = Engine.Validate(sourceBox.Text.Trim(), rootBox.Text.Trim());
                if (error != "") { status.Text = error; return false; }
                string root = Engine.Full(rootBox.Text.Trim());
                if (!refresh && folders.Count > 0 && indexedRoot == root &&
                    (DateTime.UtcNow - indexedAtUtc).TotalSeconds < 60) return true;
                folders = Engine.Folders(root);
                indexedRoot = root; indexedAtUtc = DateTime.UtcNow;
                FillFolderBox(root);
                if (folders.Count == 0) { status.Text = "정리할 상위 폴더 안에 하위 폴더가 없습니다."; return false; }
                return true;
            }
            catch (Exception ex) { status.Text = "폴더 읽기 실패: " + ex.Message; return false; }
        }

        private void Preview()
        {
            if (!Prepare(true)) return;
            list.BeginUpdate(); list.Items.Clear();
            try
            {
                foreach (FileInfo file in Engine.SourceFiles(sourceBox.Text.Trim()))
                {
                    Suggestion suggestion = Engine.Suggest(file, folders);
                    string label = folders.FirstOrDefault(f => f.Path == suggestion.Folder) == null ? "" :
                        folders.First(f => f.Path == suggestion.Folder).Label;
                    string state = suggestion.Certain ? "추천" :
                        suggestion.Score <= 0 ? "일치하는 이름 없음" :
                        suggestion.Score < 0.62 ? "이름 일치가 약함" : "후보 폴더가 비슷함";
                    string reason = suggestion.Reason;
                    if (!suggestion.Certain && suggestion.RunnerUp != "" && suggestion.Score >= 0.62)
                    {
                        FolderChoice runner = folders.FirstOrDefault(f => f.Path == suggestion.RunnerUp);
                        if (runner != null) reason += " / 2순위: " + runner.Label;
                    }
                    if (!Engine.Ready(file)) state = "다운로드 완료 대기";
                    ListViewItem item = new ListViewItem(new string[] {
                        file.Name, label, reason, state
                    });
                    item.Tag = suggestion;
                    list.Items.Add(item);
                }
                status.Text = list.Items.Count + "개 파일을 확인했습니다. 하위 폴더 " + folders.Count + "개를 비교했습니다.";
            }
            catch (Exception ex) { status.Text = "미리보기 실패: " + ex.Message; }
            finally { list.EndUpdate(); }
        }

        private void SelectSuggestedFolder()
        {
            int count = list.SelectedIndices.Count;
            moveButton.Text = count > 1 ? "선택 " + count + "개 이동" : "선택 파일 이동";
            if (count == 0) return;
            List<ScanRow> selected = list.SelectedIndices.Cast<int>()
                .Where(index => index >= 0 && index < previewRows.Count)
                .Select(index => previewRows[index]).ToList();
            if (selected.Count == 0) return;
            if (selected.Any(row => row.DirectoryPath != null))
            {
                folderBox.SelectedIndex = -1;
                status.Text = "폴더는 두 번 클릭해 안의 파일을 확인하세요.";
                return;
            }
            string destination = selected[0].Suggestion.Folder;
            folderBox.SelectedIndex = -1;
            if (destination != "" && selected.All(row => String.Equals(row.Suggestion.Folder,
                destination, StringComparison.OrdinalIgnoreCase)))
                for (int i = 1; i < folderBox.Items.Count; i++)
                    if (String.Equals(((FolderChoice)folderBox.Items[i]).Path, destination,
                        StringComparison.OrdinalIgnoreCase))
                    { folderBox.SelectedIndex = i; break; }
            if (selected.Any(row => row.State == "후보 폴더가 비슷함"))
                status.Text = "후보 폴더가 비슷해 자동으로는 옮기지 않습니다. 아래 목적지를 확인하고 '" +
                    moveButton.Text + "'을 누르세요.";
            else if (folderBox.SelectedIndex < 0)
                status.Text = "공통 추천 폴더가 없습니다. 아래에서 옮길 폴더를 직접 고르세요.";
        }

        private void OpenSelectedPreviewFolder()
        {
            if (list.SelectedIndices.Count != 1) return;
            int index = list.SelectedIndices[0];
            if (index < 0 || index >= previewRows.Count) return;
            string folder = previewRows[index].DirectoryPath;
            if (folder != null) NavigatePreviewFolder(folder);
        }

        private void PreviewParentFolder()
        {
            string source;
            try { source = Engine.Full(sourceBox.Text.Trim()); }
            catch { return; }
            string current = previewFolder == "" ? source : previewFolder;
            if (current.Equals(source, StringComparison.OrdinalIgnoreCase)) return;
            NavigatePreviewFolder(Path.GetDirectoryName(current));
        }

        private void NavigatePreviewFolder(string folder)
        {
            string source;
            try { source = Engine.Full(sourceBox.Text.Trim()); }
            catch { return; }
            if (folder == null || !Engine.SameOrInside(folder, source) || !Directory.Exists(folder))
            { status.Text = "미리볼 폴더를 찾을 수 없습니다."; return; }
            previewFolder = Engine.Full(folder);
            BeginPreview();
        }

        private void AutomaticChanged(object sender, EventArgs e)
        {
            SyncTrayState();
            if (!automaticBox.Checked)
            {
                CancelAutomaticScan();
                StopAutomaticMonitoring();
                Save(); status.Text = "자동 이동을 중지했습니다."; return;
            }
            string error = Engine.Validate(sourceBox.Text.Trim(), rootBox.Text.Trim());
            if (error != "") { automaticBox.Checked = false; status.Text = error; return; }
            if (!settings.AutomaticStartedBefore)
            {
                settings.AutoStartedAtTicks = DateTime.UtcNow.Ticks;
                settings.AutomaticStartedBefore = true;
                status.Text = "자동 이동을 시작했습니다. 기존 파일은 '확실한 추천만 정리'에서 정리하세요.";
            }
            else status.Text = "자동 이동을 시작했습니다.";
            Save();
            StartAutomaticMonitoring();
        }

        private void AutomaticScan()
        {
            if (folders.Count == 0 || indexedRoot != Engine.Full(rootBox.Text.Trim()))
            { status.Text = "먼저 미리보기를 새로고침하세요."; BeginPreview(); return; }
            try
            {
                List<FileInfo> files = Engine.SourceFiles(sourceBox.Text.Trim());
                HashSet<string> seen = new HashSet<string>(settings.Seen);
                bool changed = false;
                int moved = 0, review = 0;
                foreach (FileInfo file in files)
                {
                    string fingerprint = Engine.Fingerprint(file);
                    if (seen.Contains(fingerprint)) continue;
                    string state = file.Length + "|" + file.LastWriteTimeUtc.Ticks;
                    string previous;
                    if (!observations.TryGetValue(fingerprint, out previous) || previous != state)
                    { observations[fingerprint] = state; continue; }
                    if (!Engine.Ready(file)) continue;
                    Suggestion suggestion = Engine.Suggest(file, folders);
                    if (!suggestion.Certain)
                    {
                        settings.Seen.Add(fingerprint); seen.Add(fingerprint);
                        changed = true; review++;
                        continue;
                    }
                    try
                    {
                        string destination = Engine.Move(file.FullName, suggestion.Folder);
                        Record(file.FullName, destination, suggestion.Reason);
                        settings.Seen.Add(fingerprint); seen.Add(fingerprint);
                        changed = true; moved++;
                    }
                    catch (Exception ex) { status.Text = "이동 실패: " + ex.Message; }
                }
                if (changed) Save();
                if (moved > 0 || review > 0)
                {
                    Preview();
                    status.Text = "새 파일 " + moved + "개 이동, " + review + "개 확인 필요";
                }
            }
            catch (Exception ex) { status.Text = "자동 확인 실패: " + ex.Message; }
        }

        private void Record(string source, string destination, string reason)
        {
            FileInfo info = new FileInfo(destination);
            MoveEntry entry = new MoveEntry {
                Source = source, Destination = destination, Length = info.Length,
                LastWriteTicks = info.LastWriteTimeUtc.Ticks, When = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Reason = reason
            };
            history.Entries.Add(entry);
            Write("history.xml", history);
            journal.AddFile(entry.Source, entry.Destination, entry.Length, entry.LastWriteTicks,
                entry.When, entry.Reason, "");
            Write("changes.xml", journal);
        }

        private void BatchMove()
        {
            if (!Prepare(true)) return;
            int moved = 0, waiting = 0, review = 0, failed = 0;
            try
            {
                foreach (FileInfo file in Engine.SourceFiles(sourceBox.Text.Trim()))
                {
                    if (!Engine.Ready(file)) { waiting++; continue; }
                    Suggestion suggestion = Engine.Suggest(file, folders);
                    if (!suggestion.Certain) { review++; continue; }
                    try
                    {
                        string fingerprint = Engine.Fingerprint(file);
                        string destination = Engine.Move(file.FullName, suggestion.Folder);
                        Record(file.FullName, destination, suggestion.Reason);
                        settings.Seen.Add(fingerprint);
                        moved++;
                    }
                    catch { failed++; }
                }
                Save();
                BeginPreview();
                status.Text = moved + "개 이동, " + review + "개 확인 필요, " + waiting + "개 완료 대기, " + failed + "개 실패";
            }
            catch (Exception ex) { status.Text = "현재 파일 정리 실패: " + ex.Message; }
        }

        private void MoveSelected()
        {
            if (list.SelectedIndices.Cast<int>().Any(index => index >= 0 &&
                index < previewRows.Count && previewRows[index].DirectoryPath != null))
            { status.Text = "폴더는 두 번 클릭해 안의 파일을 확인하세요. 이동할 파일만 선택해 주세요."; return; }
            if (list.SelectedIndices.Count == 0 || !(folderBox.SelectedItem is FolderChoice))
            { status.Text = "파일과 목적지 폴더를 선택하세요."; return; }
            List<string> selectedSources = list.SelectedIndices.Cast<int>()
                .Where(index => index >= 0 && index < previewRows.Count)
                .Select(index => previewRows[index].Suggestion.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (selectedSources.Count == 0) return;
            string selectedPath = ((FolderChoice)folderBox.SelectedItem).Path;
            if (!indexedRoot.Equals(Engine.Full(rootBox.Text.Trim()), StringComparison.OrdinalIgnoreCase))
            { status.Text = "먼저 미리보기를 새로고침하세요."; BeginPreview(); return; }
            FolderChoice selected = selectedPath.Equals(indexedRoot, StringComparison.OrdinalIgnoreCase) ?
                (FolderChoice)folderBox.SelectedItem : folders.FirstOrDefault(f => f.Path == selectedPath);
            if (selected == null) { status.Text = "선택한 목적지 폴더를 찾을 수 없습니다."; return; }
            if (selectedSources.Count == 1 && Engine.Full(Path.GetDirectoryName(selectedSources[0]))
                .Equals(Engine.Full(selected.Path), StringComparison.OrdinalIgnoreCase))
            { status.Text = "파일이 이미 선택한 폴더에 있습니다."; return; }
            if (selectedSources.Count > 1 && MessageBox.Show(this,
                selectedSources.Count + "개 파일을 '" + selected.Label + "' 폴더로 옮길까요?\n" +
                "이미 해당 폴더에 있는 파일은 건너뜁니다.", "선택 파일 이동",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            BeginSelectedMoves(selectedSources, selected.Path, selected.Label);
        }

        private void Undo()
        {
            if (history.Entries.Count == 0) { status.Text = "되돌릴 이동이 없습니다."; return; }
            MoveEntry entry = history.Entries[history.Entries.Count - 1];
            if (!File.Exists(entry.Destination) || File.Exists(entry.Source))
            { status.Text = "원본 또는 목적지 상태가 달라 되돌릴 수 없습니다."; return; }
            FileInfo info = new FileInfo(entry.Destination);
            if (info.Length != entry.Length || info.LastWriteTimeUtc.Ticks != entry.LastWriteTicks)
            { status.Text = "이동한 파일이 수정되어 되돌릴 수 없습니다."; return; }
            try
            {
                File.Move(entry.Destination, entry.Source);
                history.Entries.RemoveAt(history.Entries.Count - 1);
                Write("history.xml", history);
                settings.Seen.Add(Engine.Fingerprint(new FileInfo(entry.Source)));
                Save();
                BeginPreview();
                status.Text = "마지막 이동을 되돌렸습니다.";
            }
            catch (Exception ex) { status.Text = "되돌리기 실패: " + ex.Message; }
        }

        private void ShowHistory()
        {
            if (scanBusy)
            {
                queuedAction = delegate { ShowHistory(); };
                return;
            }
            SuspendAutomaticMonitoring();
            bool changed = false;
            bool watcherPathChanged = false;
            try
            {
                using (HistoryForm dialog = new HistoryForm(journal, delegate(ChangeRecord record)
                {
                    if (record.Kind == "폴더 이동" && record.Undone &&
                        Engine.Full(rootBox.Text.Trim()).Equals(Engine.Full(record.To),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        bool sameFolder = Engine.Full(sourceBox.Text.Trim()).Equals(
                            Engine.Full(rootBox.Text.Trim()), StringComparison.OrdinalIgnoreCase);
                        updatingRootAfterRename = true;
                        try
                        {
                            rootBox.Text = record.From;
                            if (sameFolder) sourceBox.Text = record.From;
                        }
                        finally { updatingRootAfterRename = false; }
                        indexedRoot = "";
                        if (sameFolder)
                        {
                            settings.Seen.Clear();
                            observations.Clear();
                            settings.AutoStartedAtTicks = DateTime.UtcNow.Ticks;
                            DisposeWatcher();
                            watcherPathChanged = true;
                        }
                    }
                    if (record.Kind == "파일 이동" && File.Exists(record.Current) &&
                        (Path.GetDirectoryName(Engine.Full(record.Current)).Equals(
                            Engine.Full(sourceBox.Text.Trim()), StringComparison.OrdinalIgnoreCase) ||
                         (Engine.Full(sourceBox.Text.Trim()).Equals(Engine.Full(rootBox.Text.Trim()),
                            StringComparison.OrdinalIgnoreCase) &&
                          Engine.SameOrInside(record.Current, sourceBox.Text.Trim()))))
                    {
                        string fingerprint = Engine.Fingerprint(new FileInfo(record.Current));
                        if (!settings.Seen.Contains(fingerprint)) settings.Seen.Add(fingerprint);
                    }
                    changed = true;
                }, delegate { Write("changes.xml", journal); })) dialog.ShowDialog(this);
            }
            catch (Exception ex) { status.Text = "이력 열기 실패: " + ex.Message; }
            finally
            {
                ResumeAutomaticMonitoring();
                if (watcherPathChanged && automaticBox.Checked) StartAutomaticMonitoring();
            }
            if (changed) { Save(); BeginPreview(); status.Text = "선택한 이력을 되돌렸습니다."; }
        }

        private void ShowStructureEditor()
        {
            if (scanBusy)
            {
                queuedAction = delegate { ShowStructureEditor(); };
                return;
            }
            SuspendAutomaticMonitoring();
            bool watcherPathChanged = false;
            try
            {
                string source = sourceBox.Text.Trim(), root = rootBox.Text.Trim();
                string error = Engine.Validate(source, root);
                if (error != "") { status.Text = error; return; }
                using (StructureEditor editor = new StructureEditor(source, root))
                {
                    DialogResult editorResult = editor.ShowDialog(this);
                    bool selectedPathsChanged = !editor.SelectedSource.Equals(Engine.Full(source),
                        StringComparison.OrdinalIgnoreCase) ||
                        !editor.SelectedRoot.Equals(Engine.Full(root), StringComparison.OrdinalIgnoreCase);
                    if (selectedPathsChanged)
                    {
                        sourceBox.Text = editor.SelectedSource;
                        rootBox.Text = editor.SelectedRoot;
                        indexedRoot = "";
                        Save();
                    }
                    if (editorResult == DialogResult.OK)
                    {
                        string historyWarning = "";
                        if (editor.AppliedPlan != null)
                        {
                            try
                            {
                                journal.AddStructure(editor.AppliedPlan, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                                Write("changes.xml", journal);
                            }
                            catch (Exception ex) { historyWarning = " 변경 이력 저장 실패: " + ex.Message; }
                        }
                        bool sameFolder = editor.SelectedSource.Equals(editor.SelectedRoot,
                            StringComparison.OrdinalIgnoreCase);
                        if (editor.AppliedRoot != null &&
                            !editor.AppliedRoot.Equals(editor.SelectedRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            updatingRootAfterRename = true;
                            try
                            {
                                rootBox.Text = editor.AppliedRoot;
                                if (sameFolder) sourceBox.Text = editor.AppliedRoot;
                            }
                            finally { updatingRootAfterRename = false; }
                            if (sameFolder)
                            {
                                settings.Seen.Clear();
                                observations.Clear();
                                DisposeWatcher();
                                watcherPathChanged = true;
                            }
                        }
                        HashSet<string> seen = new HashSet<string>(settings.Seen);
                        foreach (PlannedFile file in editor.AppliedPlan.Files.Where(f =>
                            sameFolder || (f.FromDownloads && f.Source.Equals(f.Destination,
                                StringComparison.OrdinalIgnoreCase))))
                        {
                            if (!File.Exists(file.Destination)) continue;
                            string fingerprint = Engine.Fingerprint(new FileInfo(file.Destination));
                            if (seen.Add(fingerprint)) settings.Seen.Add(fingerprint);
                        }
                        Save();
                        indexedRoot = "";
                        BeginPreview();
                        status.Text = editor.ResultMessage + historyWarning;
                    }
                    else if (selectedPathsChanged)
                    {
                        BeginPreview();
                        status.Text = "감시·정리 폴더 경로를 바꿨습니다. 자동 이동은 다시 켜 주세요.";
                    }
                }
            }
            catch (Exception ex) { status.Text = "구조 편집기 열기 실패: " + ex.Message; }
            finally
            {
                ResumeAutomaticMonitoring();
                if (watcherPathChanged && automaticBox.Checked) StartAutomaticMonitoring();
            }
        }


    }

    public static class Program
    {
        // Appends the full error (with stack trace) to a local log so it can be diagnosed later.
        // Only the exception is written: no file contents, nothing leaves the computer.
        internal static string LogError(Exception error)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "DownloadOrganizer");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "error.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " +
                    Path.GetFileName(Application.ExecutablePath) + "\r\n" + error + "\r\n\r\n", Encoding.UTF8);
                return path;
            }
            catch { return ""; }
        }

        // Must stay directly on Main: drag and drop and other OLE features need an STA thread.
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest();
            string scope = DesktopInstance.ScopeKey();
            string mutexName = @"Local\DownloadOrganizer.SingleInstance." + scope;
            string wakeName = @"Local\DownloadOrganizer.ShowWindow." + scope;
            string exitName = @"Local\DownloadOrganizer.Exit." + scope;
            bool exitOnly = args.Length > 0 && args[0] == "--exit";
            bool firstInstance;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, mutexName, out firstInstance))
            {
                if (!firstInstance)
                {
                    string signalName = exitOnly ? exitName : wakeName;
                    using (System.Threading.EventWaitHandle signal = new System.Threading.EventWaitHandle(
                        false, System.Threading.EventResetMode.AutoReset, signalName)) signal.Set();
                    return 0;
                }
                try
                {
                    if (exitOnly) return 0;
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                    {
                        string log = LogError(e.Exception);
                        MessageBox.Show("예상하지 못한 오류가 생겼지만 작업은 계속할 수 있습니다.\n\n" + e.Exception.Message +
                            (log == "" ? "" : "\n\n자세한 내용을 기록했습니다:\n" + log),
                            "다운로드 자동 정리", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                    {
                        Exception error = e.ExceptionObject as Exception;
                        if (error != null) LogError(error);
                    };
                    using (System.Threading.EventWaitHandle wake = new System.Threading.EventWaitHandle(
                        false, System.Threading.EventResetMode.AutoReset, wakeName))
                    using (System.Threading.EventWaitHandle exit = new System.Threading.EventWaitHandle(
                        false, System.Threading.EventResetMode.AutoReset, exitName))
                    using (MainForm form = new MainForm())
                    {
                        System.Threading.RegisteredWaitHandle showRegistration =
                            System.Threading.ThreadPool.RegisterWaitForSingleObject(wake,
                                delegate(object state, bool timedOut)
                                {
                                    try
                                    {
                                        if (form.IsHandleCreated && !form.IsDisposed)
                                            form.BeginInvoke(new MethodInvoker(form.RestoreFromTray));
                                    }
                                    catch { }
                                }, null, System.Threading.Timeout.Infinite, false);
                        System.Threading.RegisteredWaitHandle exitRegistration =
                            System.Threading.ThreadPool.RegisterWaitForSingleObject(exit,
                                delegate(object state, bool timedOut)
                                {
                                    try
                                    {
                                        if (form.IsHandleCreated && !form.IsDisposed)
                                            form.BeginInvoke(new MethodInvoker(form.RequestExit));
                                    }
                                    catch { }
                                }, null, System.Threading.Timeout.Infinite, false);
                        try { Application.Run(form); }
                        finally
                        {
                            showRegistration.Unregister(null);
                            exitRegistration.Unregister(null);
                        }
                    }
                }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }

        private static int SelfTest()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "DownloadOrganizerTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(testRoot, "incoming"), root = Path.Combine(testRoot, "sorted");
                string budget = Path.Combine(root, "예산"), meetings = Path.Combine(root, "회의록");
                Directory.CreateDirectory(source); Directory.CreateDirectory(budget); Directory.CreateDirectory(meetings);
                File.WriteAllText(Path.Combine(budget, "예산_2025.txt"), "sample");
                File.WriteAllText(Path.Combine(meetings, "회의록_2025.txt"), "sample");
                string incoming = Path.Combine(source, "예산_2026.txt");
                File.WriteAllText(incoming, "new");
                File.SetLastWriteTimeUtc(incoming, DateTime.UtcNow.AddSeconds(-10));
                if (!Engine.Ready(new FileInfo(incoming))) throw new Exception("완료 파일 검사 실패");
                using (FileStream held = new FileStream(incoming, FileMode.Open, FileAccess.Read, FileShare.None))
                    if (Engine.Ready(new FileInfo(incoming))) throw new Exception("사용 중 파일 검사 실패");
                Suggestion suggestion = Engine.Suggest(new FileInfo(incoming), Engine.Folders(root));
                if (!suggestion.Certain || suggestion.Folder != budget) throw new Exception("분류 실패");
                string unrelated = Path.Combine(source, "관계없는_항목.txt");
                File.WriteAllText(unrelated, "unknown");
                if (Engine.Suggest(new FileInfo(unrelated), Engine.Folders(root)).Certain) throw new Exception("애매한 파일 처리 실패");
                string nested = Engine.StructurePath(root, @"업무\연수");
                Directory.CreateDirectory(nested);
                if (!Directory.Exists(nested)) throw new Exception("하위 폴더 생성 실패");
                bool escaped = false;
                try { Engine.StructurePath(root, @"..\외부"); }
                catch (ArgumentException) { escaped = true; }
                if (!escaped) throw new Exception("상위 폴더 이탈 검사 실패");
                File.WriteAllText(Path.Combine(source, "진행중.crdownload"), "partial");
                if (Engine.SourceFiles(source).Any(f => f.Extension == ".crdownload")) throw new Exception("임시 파일 제외 실패");
                if (Engine.Validate(source, root) != "") throw new Exception("경로 검사 실패");
                if (Engine.Validate(source, source) != "") throw new Exception("한 폴더 정리 경로 검사 실패");
                if (Engine.Validate(root, budget) == "") throw new Exception("포함 관계 경로 검사 실패");
                File.WriteAllText(Path.Combine(budget, "예산_2026.txt"), "old");
                string moved = Engine.Move(incoming, budget);
                if (!File.Exists(moved) || moved == Path.Combine(budget, "예산_2026.txt")) throw new Exception("중복 파일 검사 실패");
                File.Move(moved, incoming);
                if (!File.Exists(incoming)) throw new Exception("되돌리기 검사 실패");
                string planSource = Path.Combine(testRoot, "plan-incoming");
                string planRoot = Path.Combine(testRoot, "plan-sorted");
                string folderA = Path.Combine(planRoot, "A"), folderB = Path.Combine(planRoot, "B");
                string folderC = Path.Combine(planRoot, "C"), folderOld = Path.Combine(planRoot, "Old");
                string folderNew = Path.Combine(planRoot, "New");
                Directory.CreateDirectory(planSource); Directory.CreateDirectory(folderA);
                Directory.CreateDirectory(folderB); Directory.CreateDirectory(folderOld);
                string first = Path.Combine(folderA, "swap.txt"), second = Path.Combine(folderB, "swap.txt");
                string third = Path.Combine(planSource, "new.txt");
                File.WriteAllText(first, "A"); File.WriteAllText(second, "B"); File.WriteAllText(third, "C");
                File.SetLastWriteTimeUtc(third, DateTime.UtcNow.AddSeconds(-10));
                StructureSnapshot before = StructurePlanner.ScanTarget(planRoot);
                FileInfo firstInfo = new FileInfo(first), secondInfo = new FileInfo(second), thirdInfo = new FileInfo(third);
                List<PlannedFile> filePlan = new List<PlannedFile> {
                    new PlannedFile { Source = first, Destination = Path.Combine(folderB, "swap.txt"), Length = firstInfo.Length, LastWriteTicks = firstInfo.LastWriteTimeUtc.Ticks },
                    new PlannedFile { Source = second, Destination = Path.Combine(folderA, "swap.txt"), Length = secondInfo.Length, LastWriteTicks = secondInfo.LastWriteTimeUtc.Ticks },
                    new PlannedFile { Source = third, Destination = Path.Combine(folderC, "new.txt"), Length = thirdInfo.Length, LastWriteTicks = thirdInfo.LastWriteTimeUtc.Ticks, FromDownloads = true }
                };
                ApplySummary applied = StructurePlanner.Apply(planRoot, planSource, before,
                    filePlan, new List<string> { folderA, folderB, folderC, folderNew });
                if (applied.FilesMoved != 3 || File.ReadAllText(first) != "B" || File.ReadAllText(second) != "A" ||
                    File.ReadAllText(Path.Combine(folderC, "new.txt")) != "C" ||
                    File.Exists(third) || Directory.Exists(folderOld) || !Directory.Exists(folderNew))
                    throw new Exception("구조 적용 실패");
                if (StructurePlanner.PendingStages(planRoot).Length != 0) throw new Exception("임시 파일 정리 실패");
                string rollbackRoot = Path.Combine(testRoot, "rollback-sorted");
                string rollbackSource = Path.Combine(testRoot, "rollback-incoming");
                string rollA = Path.Combine(rollbackRoot, "A"), rollB = Path.Combine(rollbackRoot, "B");
                Directory.CreateDirectory(rollbackSource); Directory.CreateDirectory(rollA); Directory.CreateDirectory(rollB);
                string rollFirst = Path.Combine(rollA, "first.txt"), rollSecond = Path.Combine(rollB, "second.txt");
                File.WriteAllText(rollFirst, "first"); File.WriteAllText(rollSecond, "second");
                StructureSnapshot rollbackSnapshot = StructurePlanner.ScanTarget(rollbackRoot);
                FileInfo rollFirstInfo = new FileInfo(rollFirst), rollSecondInfo = new FileInfo(rollSecond);
                List<PlannedFile> rollbackPlan = new List<PlannedFile> {
                    new PlannedFile { Source = rollFirst, Destination = Path.Combine(rollB, "first.txt"), Length = rollFirstInfo.Length, LastWriteTicks = rollFirstInfo.LastWriteTimeUtc.Ticks },
                    new PlannedFile { Source = rollSecond, Destination = Path.Combine(rollA, "second.txt"), Length = rollSecondInfo.Length, LastWriteTicks = rollSecondInfo.LastWriteTimeUtc.Ticks }
                };
                bool rollbackFailedAsExpected = false;
                using (FileStream locked = new FileStream(rollSecond, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    try { StructurePlanner.Apply(rollbackRoot, rollbackSource, rollbackSnapshot, rollbackPlan,
                        new List<string> { rollA, rollB }); }
                    catch (IOException) { rollbackFailedAsExpected = true; }
                }
                if (!rollbackFailedAsExpected || !File.Exists(rollFirst) || !File.Exists(rollSecond) ||
                    File.ReadAllText(rollFirst) != "first" || StructurePlanner.PendingStages(rollbackRoot).Length != 0)
                    throw new Exception("적용 실패 후 복구 실패");
                Console.WriteLine("PASS: 완료 확인, 분류, 구조 적용·파일 맞바꾸기·빈 폴더 정리, 적용 오류 복구, 경로 제한");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
            finally
            {
                string tempRoot = Path.GetFullPath(Path.GetTempPath());
                if (testRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(testRoot))
                    Directory.Delete(testRoot, true);
            }
        }
    }
}
