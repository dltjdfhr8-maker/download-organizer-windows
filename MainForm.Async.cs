using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal class ScanRow
    {
        public string Name, Folder, Reason, State;
        public Suggestion Suggestion;
    }

    internal class ScanResult
    {
        public string Source, Root, Mode;
        public List<FolderChoice> Folders;
        public List<ScanRow> Rows = new List<ScanRow>();
        public List<MoveEntry> Moves = new List<MoveEntry>();
        public List<string> SeenAdds = new List<string>();
        public HashSet<string> RetainedSeen = new HashSet<string>();
        public Dictionary<string, string> Observations;
        public int Total, Moved, Review, Waiting, Failed, Pending;
    }

    public partial class MainForm
    {
        private bool scanBusy, scanMovesFiles, previewQueued;
        private int automaticScanGeneration;
        private Action queuedAction;

        private void CancelAutomaticScan()
        {
            Interlocked.Increment(ref automaticScanGeneration);
        }

        private void RunQueuedAction()
        {
            Action action = queuedAction;
            queuedAction = null;
            if (action != null) action();
        }

        private void BeginPreview() { StartScan("preview"); }
        private void BeginAutomaticScan() { StartScan("automatic"); }
        private void BeginBatchMove() { StartScan("batch"); }

        private void BeginSelectedMove(string source, string destinationFolder, string folderLabel)
        {
            if (scanBusy)
            {
                queuedAction = delegate { BeginSelectedMove(source, destinationFolder, folderLabel); };
                return;
            }
            SetScanBusy(true, true);
            status.Text = Path.GetFileName(source) + "을(를) 옮기는 중입니다...";
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                if (!File.Exists(source)) throw new IOException("파일이 이미 이동되었거나 없어졌습니다.");
                FileInfo original = new FileInfo(source);
                if (!Engine.Ready(original)) throw new IOException("파일의 다운로드가 끝나기를 기다려 주세요.");
                string destination = Engine.Move(source, destinationFolder);
                FileInfo moved = new FileInfo(destination);
                e.Result = new MoveEntry {
                    Source = source, Destination = destination,
                    Length = moved.Length, LastWriteTicks = moved.LastWriteTimeUtc.Ticks,
                    When = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Reason = "사용자 선택"
                };
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed) return;
                SetScanBusy(false, false);
                if (e.Error != null)
                {
                    status.Text = "이동 실패: " + e.Error.Message;
                    FinishPendingExit();
                    return;
                }
                MoveEntry result = (MoveEntry)e.Result;
                string warning = "";
                try { CommitMoves(new List<MoveEntry> { result }); }
                catch (Exception ex) { warning = " 변경 이력 저장 실패: " + ex.Message; }
                if (Engine.Full(sourceBox.Text.Trim()).Equals(Engine.Full(rootBox.Text.Trim()),
                    StringComparison.OrdinalIgnoreCase))
                {
                    string fingerprint = Engine.Fingerprint(new FileInfo(result.Destination));
                    if (!settings.Seen.Contains(fingerprint)) { settings.Seen.Add(fingerprint); Save(); }
                }
                if (FinishPendingExit()) return;
                BeginPreview();
                status.Text = Path.GetFileName(source) + "을(를) " + folderLabel + "에 옮겼습니다." + warning;
            };
            worker.RunWorkerAsync();
        }

        private void CommitMoves(List<MoveEntry> moves)
        {
            foreach (MoveEntry move in moves)
                journal.AddFile(move.Source, move.Destination, move.Length,
                    move.LastWriteTicks, move.When, move.Reason, "");
            Write("changes.xml", journal);
        }

        private void SetScanBusy(bool busy, bool movesFiles)
        {
            scanBusy = busy; scanMovesFiles = busy && movesFiles;
            sourceBox.ReadOnly = busy; rootBox.ReadOnly = busy;
            sourceButton.Enabled = !busy; rootButton.Enabled = !busy;
            scanButton.Enabled = !busy; batchButton.Enabled = !busy;
            structureButton.Enabled = !busy; moveButton.Enabled = !busy;
            undoButton.Enabled = !busy;
            automaticBox.Enabled = !busy; folderBox.Enabled = !busy;
        }

        private void StartScan(string mode)
        {
            if (scanBusy)
            {
                if (mode != "automatic")
                    queuedAction = delegate { StartScan(mode); };
                return;
            }
            string source = sourceBox.Text.Trim(), root = rootBox.Text.Trim();
            string error;
            try { error = Engine.Validate(source, root); }
            catch (Exception ex) { status.Text = ex.Message; return; }
            if (error != "") { status.Text = error; return; }
            source = Engine.Full(source); root = Engine.Full(root);
            List<FolderChoice> cached = mode == "automatic" && folders.Count > 0 &&
                indexedRoot.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                (DateTime.UtcNow - indexedAtUtc).TotalSeconds < 60 ? folders : null;
            HashSet<string> seen = new HashSet<string>(settings.Seen);
            Dictionary<string, string> observed = new Dictionary<string, string>(observations);
            long startedAt = settings.AutoStartedAtTicks;
            int generation = Thread.VolatileRead(ref automaticScanGeneration);
            if (mode == "automatic")
            { scanBusy = true; scanMovesFiles = true; }
            else
            {
                SetScanBusy(true, mode != "preview");
                status.Text = mode == "preview" ? "파일과 폴더를 읽는 중입니다..." :
                    "현재 파일을 정리하는 중입니다...";
            }
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                e.Result = RunScan(source, root, mode, cached, seen, observed, startedAt,
                    mode == "automatic" ? new Func<bool>(delegate {
                        return Thread.VolatileRead(ref automaticScanGeneration) != generation;
                    }) : null);
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed) return;
                if (mode == "automatic") { scanBusy = false; scanMovesFiles = false; }
                else SetScanBusy(false, false);
                if (e.Error != null)
                {
                    if (mode != "automatic" ||
                        Thread.VolatileRead(ref automaticScanGeneration) == generation)
                        status.Text = "파일 확인 실패: " + e.Error.Message;
                    if (FinishPendingExit()) return;
                    RunQueuedAction();
                    if (previewQueued && !scanBusy) { previewQueued = false; BeginPreview(); }
                    return;
                }
                ScanResult result = (ScanResult)e.Result;
                bool current = ScanStillCurrent(result) &&
                    (mode != "automatic" || Thread.VolatileRead(ref automaticScanGeneration) == generation);
                if (current)
                {
                    ApplyFolderIndex(result.Folders, result.Root);
                    observations = result.Observations;
                    if (result.Moved > 0 && result.Source.Equals(result.Root, StringComparison.OrdinalIgnoreCase))
                        indexedAtUtc = DateTime.MinValue;
                }
                bool seenChanged = false;
                if (current && result.Mode == "automatic")
                {
                    HashSet<string> currentSeen = new HashSet<string>(settings.Seen);
                    if (!currentSeen.SetEquals(result.RetainedSeen))
                    {
                        settings.Seen = result.RetainedSeen.ToList();
                        seenChanged = true;
                    }
                }
                if (current && result.Mode == "batch" && result.SeenAdds.Count > 0)
                {
                    HashSet<string> currentSeen = new HashSet<string>(settings.Seen);
                    foreach (string fingerprint in result.SeenAdds)
                        if (currentSeen.Add(fingerprint)) seenChanged = true;
                    if (seenChanged) settings.Seen = currentSeen.ToList();
                }
                string historyWarning = "";
                if (result.Moves.Count > 0)
                    try { CommitMoves(result.Moves); }
                    catch (Exception ex) { historyWarning = " 변경 이력 저장 실패: " + ex.Message; }
                if (!current && result.Moved > 0) previewQueued = true;
                if (seenChanged) Save();
                if (FinishPendingExit()) return;
                if (current && result.Mode == "automatic") SchedulePendingRetry(result.Pending);
                if (current && result.Mode == "preview") ShowPreviewRows(result);
                else if (current && result.Mode == "automatic")
                {
                    if (result.Moved > 0 || result.Review > 0 || result.Failed > 0 || historyWarning != "")
                        status.Text = result.Moved + "개 이동, " + result.Review + "개 확인 필요, " +
                            result.Waiting + "개 완료 대기, " + result.Failed + "개 실패" + historyWarning;
                    if (result.Moved > 0 || result.Review > 0) previewQueued = true;
                }
                else if (current)
                    status.Text = result.Moved + "개 이동, " + result.Review + "개 확인 필요, " +
                        result.Waiting + "개 완료 대기, " + result.Failed + "개 실패" + historyWarning;
                RunQueuedAction();
                if (previewQueued && !scanBusy)
                {
                    previewQueued = false;
                    BeginPreview();
                }
            };
            worker.RunWorkerAsync();
        }

        // In separate-folder mode, a file below a subfolder of the watch folder. In same-folder
        // mode the subfolders are the sorted structure itself, so this never applies.
        internal static bool InWatchSubfolder(FileInfo file, string source, bool sameFolder)
        {
            return !sameFolder && !Engine.Full(file.DirectoryName).Equals(Engine.Full(source),
                StringComparison.OrdinalIgnoreCase);
        }

        private static ScanResult RunScan(string source, string root, string mode,
            List<FolderChoice> cached, HashSet<string> seen,
            Dictionary<string, string> observed, long startedAt, Func<bool> canceled = null)
        {
            ScanResult result = new ScanResult {
                Source = source, Root = root, Mode = mode,
                Folders = cached ?? Engine.Folders(root), Observations = observed
            };
            LocalSearchIndex search = new LocalSearchIndex(result.Folders);
            bool sameFolder = source.Equals(root, StringComparison.OrdinalIgnoreCase);
            List<FileInfo> files = Engine.SourceFiles(source, true);
            result.Total = files.Count;
            HashSet<string> presentFingerprints = mode == "automatic" ?
                new HashSet<string>() : null;
            if (mode == "preview")
            {
                foreach (FileInfo file in files.Take(1000))
                {
                    Suggestion suggestion = Engine.Suggest(file, result.Folders, search);
                    FolderChoice first = result.Folders.FirstOrDefault(f => f.Path == suggestion.Folder);
                    FolderChoice runner = result.Folders.FirstOrDefault(f => f.Path == suggestion.RunnerUp);
                    bool alreadySorted = suggestion.Certain &&
                        Engine.Full(Path.GetDirectoryName(file.FullName)).Equals(
                            Engine.Full(suggestion.Folder), StringComparison.OrdinalIgnoreCase);
                    string state = alreadySorted ? "이미 정리됨" : suggestion.Certain ? "추천" :
                        suggestion.ReviewState != "" ? suggestion.ReviewState :
                        suggestion.Score <= 0 ? "일치하는 이름 없음" :
                        suggestion.Score < 0.62 ? "이름 일치가 약함" : "후보 폴더가 비슷함";
                    string reason = suggestion.Reason;
                    if (!suggestion.Certain && runner != null && suggestion.Score >= 0.62)
                        reason += " / 2순위: " + runner.Label;
                    if (!Engine.Ready(file)) state = "다운로드 완료 대기";
                    else if (InWatchSubfolder(file, source, sameFolder) && !alreadySorted)
                        state = "하위 폴더 · 직접 옮기기";
                    result.Rows.Add(new ScanRow {
                        Name = file.FullName.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar),
                        Folder = first == null ? "" : first.Label,
                        Reason = reason, State = state, Suggestion = suggestion
                    });
                }
                return result;
            }
            foreach (FileInfo file in files)
            {
                if (mode == "automatic" && canceled != null && canceled()) break;
                // Files inside a watch subfolder are never moved automatically, even when certain.
                if (InWatchSubfolder(file, source, sameFolder)) continue;
                string fingerprint = Engine.Fingerprint(file);
                if (presentFingerprints != null) presentFingerprints.Add(fingerprint);
                if (mode == "automatic")
                {
                    if (seen.Contains(fingerprint))
                    { result.RetainedSeen.Add(fingerprint); continue; }
                    if (startedAt > 0 && file.CreationTimeUtc.Ticks <= startedAt) continue;
                }
                if (mode == "automatic")
                {
                    string state = file.Length + "|" + file.LastWriteTimeUtc.Ticks;
                    string previous;
                    if (!observed.TryGetValue(fingerprint, out previous) || previous != state)
                    { observed[fingerprint] = state; result.Pending++; continue; }
                }
                if (!Engine.Ready(file))
                { result.Waiting++; if (mode == "automatic") result.Pending++; continue; }
                Suggestion suggestion = Engine.Suggest(file, result.Folders, search);
                if (!suggestion.Certain)
                {
                    result.Review++;
                    if (mode == "automatic")
                    {
                        seen.Add(fingerprint); result.SeenAdds.Add(fingerprint);
                        result.RetainedSeen.Add(fingerprint);
                    }
                    continue;
                }
                if (Engine.Full(Path.GetDirectoryName(file.FullName)).Equals(
                    Engine.Full(suggestion.Folder), StringComparison.OrdinalIgnoreCase))
                {
                    if (mode == "automatic") result.RetainedSeen.Add(fingerprint);
                    continue;
                }
                try
                {
                    if (mode == "automatic" && canceled != null && canceled()) break;
                    string destination = Engine.Move(file.FullName, suggestion.Folder);
                    FileInfo moved = new FileInfo(destination);
                    result.Moves.Add(new MoveEntry {
                        Source = file.FullName, Destination = destination,
                        Length = moved.Length, LastWriteTicks = moved.LastWriteTimeUtc.Ticks,
                        When = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Reason = mode == "batch" ? "현재 파일 정리" : suggestion.Reason
                    });
                    result.Moved++;
                    if (sameFolder)
                    {
                        string destinationFingerprint = Engine.Fingerprint(moved);
                        if (mode == "automatic") result.RetainedSeen.Add(destinationFingerprint);
                        else result.SeenAdds.Add(destinationFingerprint);
                    }
                    if (mode == "automatic")
                    {
                        observed.Remove(fingerprint);
                        presentFingerprints.Remove(fingerprint);
                    }
                }
                catch { result.Failed++; }
            }
            if (presentFingerprints != null && (canceled == null || !canceled()))
                foreach (string fingerprint in observed.Keys.ToList())
                    if (!presentFingerprints.Contains(fingerprint)) observed.Remove(fingerprint);
            return result;
        }

        private void ApplyFolderIndex(List<FolderChoice> index, string root)
        {
            if (ReferenceEquals(index, folders) && indexedRoot.Equals(root, StringComparison.OrdinalIgnoreCase))
                return;
            bool samePaths = indexedRoot.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                folderBox.Items.Count == index.Count + 1 &&
                ((FolderChoice)folderBox.Items[0]).Path.Equals(root, StringComparison.OrdinalIgnoreCase);
            if (samePaths)
                for (int i = 0; i < index.Count; i++)
                    if (!((FolderChoice)folderBox.Items[i + 1]).Path.Equals(index[i].Path,
                        StringComparison.OrdinalIgnoreCase)) { samePaths = false; break; }
            if (samePaths)
            {
                folders = index;
                indexedAtUtc = DateTime.UtcNow;
                return;
            }
            folders = index;
            indexedRoot = root; indexedAtUtc = DateTime.UtcNow;
            FillFolderBox(root);
        }

        // The target root itself is always a destination, so the list is never empty
        // even before the root has any subfolders.
        private static FolderChoice RootChoice(string root)
        {
            string full = Engine.Full(root);
            string name = Path.GetFileName(full);
            if (name == "") name = full;
            return new FolderChoice { Path = full, Label = name + " (상위 폴더에 바로 두기)" };
        }

        private void FillFolderBox(string root)
        {
            string selected = folderBox.SelectedItem is FolderChoice ?
                ((FolderChoice)folderBox.SelectedItem).Path : "";
            folderBox.BeginUpdate();
            try
            {
                folderBox.Items.Clear();
                folderBox.Items.Add(RootChoice(root));
                foreach (FolderChoice folder in folders) folderBox.Items.Add(folder);
                foreach (object item in folderBox.Items)
                    if (((FolderChoice)item).Path == selected) { folderBox.SelectedItem = item; break; }
            }
            finally { folderBox.EndUpdate(); }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

        private void ShowFolderBoxHint()
        {
            const int SetCueBanner = 0x1703;
            try
            {
                if (folderBox.IsHandleCreated)
                    SendMessage(folderBox.Handle, SetCueBanner, IntPtr.Zero,
                        "목록에서 파일을 고른 뒤 옮길 폴더를 선택하세요");
            }
            catch { }
        }

        private bool ScanStillCurrent(ScanResult result)
        {
            try
            {
                return Engine.Full(sourceBox.Text.Trim()).Equals(result.Source, StringComparison.OrdinalIgnoreCase) &&
                    Engine.Full(rootBox.Text.Trim()).Equals(result.Root, StringComparison.OrdinalIgnoreCase) &&
                    (result.Mode != "automatic" || automaticBox.Checked);
            }
            catch { return false; }
        }

        private void ShowPreviewRows(ScanResult result)
        {
            list.BeginUpdate();
            try
            {
                list.SelectedIndices.Clear();
                previewRows = result.Rows;
                list.VirtualListSize = previewRows.Count;
                list.Invalidate();
            }
            finally { list.EndUpdate(); }
            status.Text = result.Total + "개 파일 중 최근 " + result.Rows.Count +
                "개를 표시했습니다. " + (result.Folders.Count == 0 ?
                "정리할 상위 폴더에 하위 폴더가 없어 자동 추천은 없습니다. 목적지에서 상위 폴더를 고르거나 '새 폴더 구조 제안'으로 폴더를 만드세요." :
                "하위 폴더 " + result.Folders.Count + "개를 비교했습니다.");
        }
    }
}
