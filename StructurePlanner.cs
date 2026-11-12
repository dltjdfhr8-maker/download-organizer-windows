using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DownloadOrganizer
{
    public class PlannedFile
    {
        public string Source = "";
        public string Destination = "";
        public long Length;
        public long LastWriteTicks;
        public long CreationTicks;
        public bool FromDownloads;
        public bool IsCopy;
        internal string Stage = "";
    }

    public class StructureSnapshot
    {
        public HashSet<string> TargetFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> TargetDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public class ApplySummary
    {
        public int FilesMoved;
        public int FilesCopied;
        public int FoldersCreated;
        public int FoldersRemoved;
        public override string ToString()
        {
            return "파일 " + FilesMoved + "개 이동, " + FilesCopied + "개 복사, 폴더 " + FoldersCreated +
                "개 생성, 빈 폴더 " + FoldersRemoved + "개 정리";
        }
    }

    public static class StructurePlanner
    {
        public const string StagePrefix = ".download-organizer-stage-";
        private const int MaximumItems = 10000;

        public static StructureSnapshot ScanTarget(string root)
        {
            StructureSnapshot snapshot = new StructureSnapshot();
            Stack<string> pending = new Stack<string>();
            pending.Push(Engine.Full(root));
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string directory in Directory.GetDirectories(current))
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("연결 폴더가 있어 구조 편집을 중단했습니다: " + directory);
                    if (Path.GetFileName(directory).StartsWith(StagePrefix, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("이전 구조 변경의 임시 폴더가 남아 있습니다: " + directory);
                    snapshot.TargetDirectories.Add(Engine.Full(directory));
                    pending.Push(directory);
                }
                foreach (string file in Directory.GetFiles(current))
                    snapshot.TargetFiles.Add(Engine.Full(file));
                if (snapshot.TargetFiles.Count + snapshot.TargetDirectories.Count > MaximumItems)
                    throw new IOException("폴더와 파일이 10,000개를 넘어 구조 편집을 열 수 없습니다.");
            }
            return snapshot;
        }

        public static string[] PendingStages(string root)
        {
            return Directory.GetDirectories(root, StagePrefix + "*", SearchOption.TopDirectoryOnly);
        }

        public static string Destination(string root, string relativeFolder, string fileName)
        {
            string folder = relativeFolder == "" ? Engine.Full(root) : Engine.StructurePath(root, relativeFolder);
            string destination = Engine.Full(Path.Combine(folder, fileName));
            if (!Engine.SameOrInside(destination, root) || destination.Equals(Engine.Full(root), StringComparison.OrdinalIgnoreCase))
                throw new IOException("정리할 상위 폴더 밖으로 파일을 옮길 수 없습니다.");
            return destination;
        }

        private static bool Same(string a, string b)
        {
            return Engine.Full(a).Equals(Engine.Full(b), StringComparison.OrdinalIgnoreCase);
        }

        private static int Depth(string path)
        {
            return Engine.Full(path).Count(c => c == Path.DirectorySeparatorChar);
        }

        internal static string RemapRoot(string path, string oldRoot, string newRoot)
        {
            string full = Engine.Full(path);
            oldRoot = Engine.Full(oldRoot); newRoot = Engine.Full(newRoot);
            if (!Engine.SameOrInside(full, oldRoot)) return full;
            return Engine.Full(newRoot + full.Substring(oldRoot.Length));
        }

        public static ApplySummary ApplyWithRootRename(string root, string source, string newRoot,
            StructureSnapshot snapshot, List<PlannedFile> files, List<string> desiredDirectories,
            Action<int, int, string, string> progress = null)
        {
            root = Engine.Full(root); source = Engine.Full(source); newRoot = Engine.Full(newRoot);
            if (Engine.Validate(source, root) != "") throw new IOException(Engine.Validate(source, root));
            if (Path.GetDirectoryName(root) == null || Path.GetDirectoryName(newRoot) == null ||
                Same(root, newRoot) || !Same(Path.GetDirectoryName(root), Path.GetDirectoryName(newRoot)))
                throw new IOException("상위 폴더 이름은 같은 부모 폴더 안에서만 바꿀 수 있습니다.");
            if (!Directory.Exists(root) || Directory.Exists(newRoot) || File.Exists(newRoot))
                throw new IOException("새 폴더 이름을 사용할 수 없거나 이미 같은 이름의 항목이 있습니다: " + newRoot);
            StructureSnapshot before = ScanTarget(root);
            if (!before.TargetFiles.SetEquals(snapshot.TargetFiles) ||
                !before.TargetDirectories.SetEquals(snapshot.TargetDirectories))
                throw new IOException("편집하는 동안 정리 폴더가 바뀌었습니다. 창을 다시 열어 확인하세요.");
            StructureSnapshot renamedSnapshot = new StructureSnapshot();
            foreach (string file in snapshot.TargetFiles)
                renamedSnapshot.TargetFiles.Add(RemapRoot(file, root, newRoot));
            foreach (string directory in snapshot.TargetDirectories)
                renamedSnapshot.TargetDirectories.Add(RemapRoot(directory, root, newRoot));
            List<PlannedFile> renamedFiles = files.Select(file => new PlannedFile {
                Source = RemapRoot(file.Source, root, newRoot),
                Destination = RemapRoot(file.Destination, root, newRoot),
                Length = file.Length, LastWriteTicks = file.LastWriteTicks,
                CreationTicks = file.CreationTicks, FromDownloads = file.FromDownloads,
                IsCopy = file.IsCopy
            }).ToList();
            List<string> renamedDirectories = desiredDirectories.Select(directory =>
                RemapRoot(directory, root, newRoot)).ToList();
            string renamedSource = Same(source, root) ? newRoot : source;
            Directory.Move(root, newRoot);
            try
            {
                return Apply(newRoot, renamedSource, renamedSnapshot, renamedFiles,
                    renamedDirectories, progress);
            }
            catch (Exception applyError)
            {
                if (PendingStages(newRoot).Length > 0)
                    throw new IOException(applyError.Message + "\n복구할 임시 파일이 새 이름의 폴더에 남아 있습니다: " + newRoot);
                try
                {
                    if (Directory.Exists(root))
                        throw new IOException("원래 폴더 이름에 다른 폴더가 생겼습니다.");
                    Directory.Move(newRoot, root);
                }
                catch (Exception rollbackError)
                {
                    throw new IOException(applyError.Message + "\n상위 폴더 이름을 되돌리지 못했습니다. 현재 위치: " +
                        newRoot + "\n복구 오류: " + rollbackError.Message);
                }
                throw new IOException(applyError.Message + "\n상위 폴더 이름은 원래대로 돌렸습니다.");
            }
        }

        public static ApplySummary Apply(string root, string source, StructureSnapshot snapshot,
            List<PlannedFile> files, List<string> desiredDirectories,
            Action<int, int, string, string> progress = null)
        {
            string error = Engine.Validate(source, root);
            if (error != "") throw new IOException(error);
            root = Engine.Full(root);
            source = Engine.Full(source);
            bool sameFolder = Same(source, root);
            StructureSnapshot now = ScanTarget(root);
            if (!now.TargetFiles.SetEquals(snapshot.TargetFiles) ||
                !now.TargetDirectories.SetEquals(snapshot.TargetDirectories))
                throw new IOException("편집하는 동안 정리 폴더가 바뀌었습니다. 창을 다시 열어 확인하세요.");

            HashSet<string> allSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string directory in desiredDirectories)
            {
                string full = Engine.Full(directory);
                if (!Engine.SameOrInside(full, root) || Same(full, root))
                    throw new IOException("정리할 상위 폴더 밖의 폴더가 포함되었습니다.");
                if (snapshot.TargetFiles.Contains(full))
                    throw new IOException("기존 파일과 같은 위치에 폴더를 만들 수 없습니다: " + full);
                desired.Add(full);
            }
            foreach (PlannedFile file in files)
            {
                string original = Engine.Full(file.Source), final = Engine.Full(file.Destination);
                if (!file.IsCopy && !allSources.Add(original))
                    throw new IOException("같은 원본 파일이 오른쪽 구조에 두 번 들어 있습니다: " + original);
                if (!destinations.Add(final)) throw new IOException("같은 목적지에 파일 두 개를 둘 수 없습니다: " + final);
                if (file.IsCopy && Same(original, final))
                    throw new IOException("원본 파일과 같은 위치에 복사할 수 없습니다: " + final);
                if (!File.Exists(original)) throw new IOException("파일이 없어졌습니다. 창을 다시 열어 확인하세요: " + original);
                FileInfo info = new FileInfo(original);
                if (info.Length != file.Length || info.LastWriteTimeUtc.Ticks != file.LastWriteTicks)
                    throw new IOException("파일이 편집 중 수정되었습니다. 창을 다시 열어 확인하세요: " + original);
                if (file.FromDownloads)
                {
                    if (!Engine.SameOrInside(original, source) || Same(original, source))
                        throw new IOException("다운로드 폴더 밖의 파일이 포함되었습니다.");
                    if (!Same(original, final) && !Engine.Ready(info))
                        throw new IOException("다운로드가 끝나지 않았거나 사용 중인 파일입니다: " + original);
                }
                else if (!snapshot.TargetFiles.Contains(original))
                    throw new IOException("정리 폴더에 없던 파일이 포함되었습니다.");
                if (file.IsCopy && !Engine.SameOrInside(final, root))
                    throw new IOException("복사본은 정리 폴더 안에만 둘 수 있습니다.");
                if (!file.FromDownloads && !Engine.SameOrInside(final, root))
                    throw new IOException("정리 폴더의 파일을 외부로 옮길 수 없습니다.");
                if (file.FromDownloads && !Same(original, final) &&
                    !Engine.SameOrInside(final, root) &&
                    !Engine.SameOrInside(final, source))
                    throw new IOException("다운로드 파일의 목적지가 정리 폴더 밖입니다.");
                if (Directory.Exists(final)) throw new IOException("파일 목적지에 이미 폴더가 있습니다: " + final);
            }
            HashSet<string> plannedTargetSources = new HashSet<string>(
                files.Where(f => !f.IsCopy && (sameFolder || !f.FromDownloads))
                    .Select(f => Engine.Full(f.Source)),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> expectedTargetSources = new HashSet<string>(snapshot.TargetFiles,
                StringComparer.OrdinalIgnoreCase);
            if (sameFolder)
            {
                HashSet<string> topLevelFiles = new HashSet<string>(
                    Engine.SourceFiles(source).Select(f => Engine.Full(f.FullName)),
                    StringComparer.OrdinalIgnoreCase);
                HashSet<string> plannedTopLevelFiles = new HashSet<string>(
                    files.Where(f => f.FromDownloads && !f.IsCopy).Select(f => Engine.Full(f.Source)),
                    StringComparer.OrdinalIgnoreCase);
                if (!plannedTopLevelFiles.SetEquals(topLevelFiles))
                    throw new IOException("최상위 파일이 편집 중 바뀌었습니다. 창을 다시 열어 확인하세요.");
                expectedTargetSources.RemoveWhere(path =>
                    Same(Path.GetDirectoryName(path), root) && !topLevelFiles.Contains(path));
            }
            else
            {
                HashSet<string> currentSourceFiles = new HashSet<string>(
                    Engine.SourceFiles(source, true).Select(f => Engine.Full(f.FullName)),
                    StringComparer.OrdinalIgnoreCase);
                HashSet<string> plannedSourceFiles = new HashSet<string>(
                    files.Where(f => f.FromDownloads && !f.IsCopy).Select(f => Engine.Full(f.Source)),
                    StringComparer.OrdinalIgnoreCase);
                if (!plannedSourceFiles.SetEquals(currentSourceFiles))
                    throw new IOException("감시 폴더의 파일이 편집 중 바뀌었습니다. 창을 다시 열어 확인하세요.");
            }
            if (!plannedTargetSources.SetEquals(expectedTargetSources))
                throw new IOException("오른쪽 구조에 빠진 기존 파일이 있습니다.");
            foreach (string final in destinations)
                if (File.Exists(final) && !allSources.Contains(final))
                    throw new IOException("목적지에 다른 파일이 있습니다: " + final);

            List<PlannedFile> copying = files.Where(f => f.IsCopy).ToList();
            List<PlannedFile> moving = files.Where(f => !f.IsCopy && !Same(f.Source, f.Destination)).ToList();
            List<PlannedFile> operations = copying.Concat(moving).ToList();
            List<string> created = new List<string>();
            List<string> removed = new List<string>();
            if (operations.Count == 0 && desired.SetEquals(snapshot.TargetDirectories))
                return new ApplySummary();

            string stageDirectory = Path.Combine(root, StagePrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stageDirectory);
            string manifest = Path.Combine(stageDirectory, "recovery.tsv");
            for (int i = 0; i < operations.Count; i++)
                operations[i].Stage = Path.Combine(stageDirectory, i.ToString("D6") + ".stage");
            File.WriteAllLines(manifest, operations.Select(f => f.Source + "\t" + f.Stage + "\t" +
                f.Destination + "\t" + f.Length + "\t" + f.LastWriteTicks + "\t" +
                (f.IsCopy ? "C" : "M")).ToArray(), Encoding.UTF8);
            try
            {
                foreach (PlannedFile file in copying)
                {
                    if (progress != null) progress(0, operations.Count, file.Source, "복사 준비 중");
                    File.Copy(file.Source, file.Stage);
                }
                foreach (PlannedFile file in moving)
                {
                    if (progress != null) progress(0, operations.Count, file.Source, "이동 준비 중");
                    File.Move(file.Source, file.Stage);
                }
                foreach (string directory in desired.OrderBy(Depth))
                {
                    if (File.Exists(directory)) throw new IOException("폴더 경로에 파일이 있습니다: " + directory);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                        created.Add(directory);
                    }
                }
                for (int i = 0; i < operations.Count; i++)
                {
                    PlannedFile file = operations[i];
                    if (File.Exists(file.Destination)) throw new IOException("목적지에 파일이 생겼습니다: " + file.Destination);
                    if (progress != null) progress(i, operations.Count, file.Source,
                        file.IsCopy ? "복사 중" : "이동 중");
                    File.Move(file.Stage, file.Destination);
                    if (progress != null) progress(i + 1, operations.Count, file.Source,
                        file.IsCopy ? "복사 완료" : "이동 완료");
                }
                if (progress != null && operations.Count > 0)
                    progress(operations.Count, operations.Count, "", "폴더 정리 중");
                foreach (string directory in snapshot.TargetDirectories.OrderByDescending(Depth))
                {
                    if (desired.Contains(directory) || !Directory.Exists(directory)) continue;
                    if (Directory.GetFileSystemEntries(directory).Length != 0)
                        throw new IOException("기존 폴더에 새 항목이 생겨 정리를 마치지 못했습니다: " + directory);
                    Directory.Delete(directory);
                    removed.Add(directory);
                }
                File.Delete(manifest);
                Directory.Delete(stageDirectory);
                return new ApplySummary { FilesMoved = moving.Count, FilesCopied = copying.Count,
                    FoldersCreated = created.Count, FoldersRemoved = removed.Count };
            }
            catch (Exception applyError)
            {
                List<string> recoveryErrors = new List<string>();
                foreach (string directory in removed.OrderBy(Depth))
                    try { Directory.CreateDirectory(directory); } catch (Exception ex) { recoveryErrors.Add(ex.Message); }
                foreach (PlannedFile file in copying)
                {
                    try
                    {
                        string present = File.Exists(file.Destination) && !File.Exists(file.Stage) ?
                            file.Destination : file.Stage;
                        if (File.Exists(present))
                        {
                            CheckRecoveryFile(present, new[] { file.Source, file.Stage, file.Destination,
                                file.Length.ToString(), file.LastWriteTicks.ToString(), "C" });
                            File.Delete(present);
                        }
                    }
                    catch (Exception ex) { recoveryErrors.Add(ex.Message); }
                }
                foreach (PlannedFile file in moving)
                {
                    try
                    {
                        if (File.Exists(file.Destination) && !File.Exists(file.Stage) && !Same(file.Source, file.Destination))
                            File.Move(file.Destination, file.Stage);
                    }
                    catch (Exception ex) { recoveryErrors.Add(ex.Message); }
                }
                foreach (PlannedFile file in moving)
                {
                    try
                    {
                        if (File.Exists(file.Stage) && !File.Exists(file.Source))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(file.Source));
                            File.Move(file.Stage, file.Source);
                        }
                    }
                    catch (Exception ex) { recoveryErrors.Add(ex.Message); }
                }
                foreach (string directory in created.OrderByDescending(Depth))
                    try { if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory); }
                    catch { }
                if (recoveryErrors.Count == 0)
                {
                    try { File.Delete(manifest); Directory.Delete(stageDirectory); }
                    catch (Exception ex) { recoveryErrors.Add(ex.Message); }
                }
                if (recoveryErrors.Count > 0)
                    throw new IOException(applyError.Message + "\n일부 복구가 끝나지 않았습니다. 임시 폴더: " + stageDirectory);
                throw new IOException(applyError.Message + "\n변경된 파일은 원래 위치로 되돌렸습니다.");
            }
        }

        public static void RecoverStage(string stageDirectory)
        {
            string manifest = Path.Combine(stageDirectory, "recovery.tsv");
            if (!File.Exists(manifest)) throw new IOException("복구 기록이 없습니다: " + stageDirectory);
            List<string[]> rows = File.ReadAllLines(manifest, Encoding.UTF8)
                .Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => x.Split('\t')).ToList();
            if (rows.Any(x => x.Length != 5 && (x.Length != 6 || (x[5] != "C" && x[5] != "M"))))
                throw new IOException("복구 기록 형식이 올바르지 않습니다.");
            foreach (string[] row in rows.Where(x => x.Length == 6 && x[5] == "C"))
            {
                if (File.Exists(row[1]) && File.Exists(row[2]))
                    throw new IOException("복사본이 임시 위치와 목적지에 모두 있어 자동 복구를 멈췄습니다: " + row[2]);
                string present = File.Exists(row[2]) ? row[2] : row[1];
                if (!File.Exists(present)) continue;
                CheckRecoveryFile(present, row);
                File.Delete(present);
            }
            foreach (string[] row in rows.Where(x => x.Length == 5 || x[5] == "M"))
            {
                if (File.Exists(row[2]) && !File.Exists(row[1]))
                {
                    CheckRecoveryFile(row[2], row);
                    File.Move(row[2], row[1]);
                }
            }
            foreach (string[] row in rows.Where(x => x.Length == 5 || x[5] == "M"))
            {
                if (!File.Exists(row[1])) continue;
                CheckRecoveryFile(row[1], row);
                if (File.Exists(row[0])) throw new IOException("원래 위치에 파일이 생겨 복구할 수 없습니다: " + row[0]);
                Directory.CreateDirectory(Path.GetDirectoryName(row[0]));
                File.Move(row[1], row[0]);
            }
            File.Delete(manifest);
            Directory.Delete(stageDirectory);
        }

        private static void CheckRecoveryFile(string path, string[] record)
        {
            FileInfo info = new FileInfo(path);
            if (info.Length != Int64.Parse(record[3]) || info.LastWriteTimeUtc.Ticks != Int64.Parse(record[4]))
                throw new IOException("복구 대상 파일이 수정되어 자동 복구를 멈췄습니다: " + path);
        }
    }
}
