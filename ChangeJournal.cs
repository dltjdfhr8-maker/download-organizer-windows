using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DownloadOrganizer
{
    public class ChangeRecord
    {
        public string Id = "";
        public string GroupId = "";
        public string Kind = "";
        public string From = "";
        public string To = "";
        public string Current = "";
        public string When = "";
        public string Reason = "";
        public long Length;
        public long LastWriteTicks;
        public bool Undone;
        public List<string> RelatedFileIds = new List<string>();
    }

    public class ChangeJournal
    {
        public List<ChangeRecord> Records = new List<ChangeRecord>();

        public ChangeRecord AddFile(string from, string to, long length, long ticks,
            string when, string reason, string groupId)
        {
            ChangeRecord record = new ChangeRecord {
                Id = Guid.NewGuid().ToString("N"), GroupId = groupId,
                Kind = "파일 이동", From = Engine.Full(from), To = Engine.Full(to),
                Current = Engine.Full(to), Length = length, LastWriteTicks = ticks,
                When = when, Reason = reason
            };
            Records.Add(record);
            return record;
        }

        internal void AddStructure(PlanData plan, string when)
        {
            int previousCount = Records.Count;
            try
            {
            string groupId = Guid.NewGuid().ToString("N");
            List<ChangeRecord> fileRecords = new List<ChangeRecord>();
            foreach (PlannedFile file in plan.Files)
            {
                if (Engine.Full(file.Source).Equals(Engine.Full(file.Destination), StringComparison.OrdinalIgnoreCase))
                    continue;
                FileInfo info = new FileInfo(file.Destination);
                string reason = Path.GetDirectoryName(file.Source).Equals(
                    Path.GetDirectoryName(file.Destination), StringComparison.OrdinalIgnoreCase) ?
                    "파일 이름 변경" : "구조 편집";
                fileRecords.Add(AddFile(file.Source, file.Destination,
                    info.Length, info.LastWriteTimeUtc.Ticks, when, reason, groupId));
            }
            foreach (FolderDelta folder in plan.FolderChanges)
            {
                string kind = folder.From == "" ? "폴더 생성" :
                    folder.To == "" ? "폴더 제거" : "폴더 이동";
                ChangeRecord record = new ChangeRecord {
                    Id = Guid.NewGuid().ToString("N"), GroupId = groupId,
                    Kind = kind, From = folder.From, To = folder.To,
                    Current = folder.To, When = when, Reason = "구조 편집"
                };
                record.RelatedFileIds = fileRecords
                    .Where(f => folder.To != "" ? Engine.SameOrInside(f.To, folder.To) :
                        Engine.SameOrInside(f.From, folder.From))
                    .Select(f => f.Id).ToList();
                Records.Add(record);
            }
            }
            catch
            {
                Records.RemoveRange(previousCount, Records.Count - previousCount);
                throw;
            }
        }

        public string Undo(ChangeRecord record)
        {
            if (record == null || record.Undone) throw new IOException("이미 되돌린 이력입니다.");
            if (record.Kind == "파일 이동") return UndoFile(record);
            if (record.Kind == "폴더 생성") return UndoCreatedFolder(record);
            if (record.Kind == "폴더 제거") return UndoRemovedFolder(record);
            if (record.Kind == "폴더 이동") return UndoMovedFolder(record);
            throw new IOException("알 수 없는 이력 종류입니다.");
        }

        public List<ChangeRecord> PendingRenameGroup(ChangeRecord record)
        {
            if (record == null || record.Kind != "파일 이동" ||
                record.Reason != "파일 이름 변경" || String.IsNullOrEmpty(record.GroupId))
                return new List<ChangeRecord>();
            return Records.Where(r => r.GroupId == record.GroupId && r.Kind == "파일 이동" &&
                r.Reason == "파일 이름 변경" && !r.Undone).ToList();
        }

        public List<ChangeRecord> UndoRenameGroup(ChangeRecord selected)
        {
            List<ChangeRecord> pending = PendingRenameGroup(selected);
            if (pending.Count == 0) throw new IOException("이 작업에서 되돌릴 파일 이름이 없습니다.");
            HashSet<string> currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> originalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ChangeRecord record in pending)
            {
                string current = record.Current == "" ? record.To : record.Current;
                if (!currentPaths.Add(current) || !originalPaths.Add(record.From))
                    throw new IOException("이력에 같은 파일 경로가 두 번 기록되어 있습니다.");
                if (!File.Exists(current))
                    throw new IOException("현재 위치에 파일이 없습니다: " + current);
                FileInfo info = new FileInfo(current);
                if (info.Length != record.Length || info.LastWriteTimeUtc.Ticks != record.LastWriteTicks)
                    throw new IOException("파일이 변경되어 자동으로 되돌릴 수 없습니다: " + current);
                if (!Directory.Exists(Path.GetDirectoryName(record.From)))
                    throw new IOException("원래 폴더가 없어 이름을 되돌릴 수 없습니다: " + record.From);
            }
            foreach (ChangeRecord record in pending)
                if (Directory.Exists(record.From) ||
                    (File.Exists(record.From) && !currentPaths.Contains(record.From)))
                    throw new IOException("원래 이름에 다른 항목이 있습니다: " + record.From);

            Dictionary<ChangeRecord, string> stages = new Dictionary<ChangeRecord, string>();
            List<ChangeRecord> restored = new List<ChangeRecord>();
            try
            {
                foreach (ChangeRecord record in pending)
                {
                    string current = record.Current == "" ? record.To : record.Current;
                    string stage;
                    do stage = Path.Combine(Path.GetDirectoryName(current),
                        ".download-organizer-undo-" + Guid.NewGuid().ToString("N") + ".stage");
                    while (File.Exists(stage) || Directory.Exists(stage));
                    File.Move(current, stage);
                    stages.Add(record, stage);
                }
                foreach (ChangeRecord record in pending)
                {
                    if (File.Exists(record.From) || Directory.Exists(record.From))
                        throw new IOException("되돌리는 중 원래 이름에 다른 항목이 생겼습니다: " + record.From);
                    File.Move(stages[record], record.From);
                    restored.Add(record);
                }
                foreach (ChangeRecord record in pending)
                { record.Current = record.From; record.Undone = true; }
                return pending;
            }
            catch (Exception error)
            {
                List<string> failures = new List<string>();
                foreach (ChangeRecord record in restored.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (File.Exists(record.From) && !File.Exists(stages[record]))
                            File.Move(record.From, stages[record]);
                    }
                    catch (Exception ex) { failures.Add(ex.Message); }
                }
                foreach (KeyValuePair<ChangeRecord, string> stage in stages.Reverse())
                {
                    try
                    {
                        string current = stage.Key.Current == "" ? stage.Key.To : stage.Key.Current;
                        if (File.Exists(stage.Value) && !File.Exists(current))
                            File.Move(stage.Value, current);
                    }
                    catch (Exception ex) { failures.Add(ex.Message); }
                }
                if (failures.Count > 0)
                    throw new IOException(error.Message + "\n일부 파일을 원래 상태로 복구하지 못했습니다: " +
                        String.Join(" / ", failures.ToArray()));
                throw new IOException(error.Message + "\n변경된 파일은 되돌리기 전 상태로 복구했습니다.");
            }
        }

        private string UndoFile(ChangeRecord record)
        {
            string current = record.Current == "" ? record.To : record.Current;
            if (!File.Exists(current)) throw new IOException("현재 위치에 파일이 없습니다: " + current);
            FileInfo info = new FileInfo(current);
            if (info.Length != record.Length || info.LastWriteTimeUtc.Ticks != record.LastWriteTicks)
                throw new IOException("파일이 변경되어 자동으로 되돌릴 수 없습니다: " + current);
            if (File.Exists(record.From) || Directory.Exists(record.From))
                throw new IOException("이전 위치에 같은 이름의 항목이 있습니다: " + record.From);
            Directory.CreateDirectory(Path.GetDirectoryName(record.From));
            File.Move(current, record.From);
            record.Current = record.From; record.Undone = true;
            return (record.Reason == "파일 이름 변경" ? "파일 이름을 되돌렸습니다: " :
                "파일을 이전 위치로 돌렸습니다: ") + Path.GetFileName(record.From);
        }

        private string UndoCreatedFolder(ChangeRecord record)
        {
            if (!Directory.Exists(record.To)) throw new IOException("생성된 폴더가 없습니다: " + record.To);
            if (Directory.GetFileSystemEntries(record.To).Length != 0)
                throw new IOException("폴더가 비어 있지 않습니다. 안의 파일과 폴더 이력을 먼저 되돌리세요.");
            Directory.Delete(record.To);
            record.Undone = true; record.Current = "";
            return "생성된 빈 폴더를 제거했습니다: " + record.To;
        }

        private string UndoRemovedFolder(ChangeRecord record)
        {
            if (File.Exists(record.From))
                throw new IOException("이전 위치에 이미 파일이 있습니다: " + record.From);
            if (Directory.Exists(record.From))
            {
                List<ChangeRecord> related = record.RelatedFileIds
                    .Select(id => Records.FirstOrDefault(r => r.Id == id)).ToList();
                if (related.Count == 0 || related.Any(r => r == null || !r.Undone ||
                    !Engine.SameOrInside(r.Current, record.From)))
                    throw new IOException("이전 위치에 이미 폴더가 있습니다: " + record.From);
                HashSet<string> expected = new HashSet<string>(
                    related.Select(r => Engine.Full(r.Current)), StringComparer.OrdinalIgnoreCase);
                if (!StructurePlanner.ScanTarget(record.From).TargetFiles.SetEquals(expected))
                    throw new IOException("복구된 폴더에 다른 파일이 있어 이력을 완료할 수 없습니다.");
                record.Undone = true; record.Current = record.From;
                return "파일 복구로 다시 생긴 폴더의 이력을 완료했습니다: " + record.From;
            }
            if (!Directory.Exists(Path.GetDirectoryName(record.From)))
                throw new IOException("상위 폴더를 먼저 되돌리세요: " + record.From);
            Directory.CreateDirectory(record.From);
            record.Undone = true; record.Current = record.From;
            return "이전에 있던 폴더를 다시 만들었습니다: " + record.From;
        }

        private string UndoMovedFolder(ChangeRecord record)
        {
            string current = record.Current == "" ? record.To : record.Current;
            if (!Directory.Exists(current)) throw new IOException("현재 위치에 폴더가 없습니다: " + current);
            if (Records.Any(r => r.Id != record.Id && !r.Undone && r.Kind != "파일 이동" &&
                r.To != "" && Engine.SameOrInside(r.Current == "" ? r.To : r.Current, current)))
                throw new IOException("하위 폴더 변경 이력을 먼저 되돌리세요.");
            List<ChangeRecord> related = record.RelatedFileIds
                .Select(id => Records.FirstOrDefault(r => r.Id == id)).ToList();
            if (related.Any(r => r == null))
                throw new IOException("관련 파일 이력을 찾을 수 없습니다.");
            bool originalExists = Directory.Exists(record.From) || File.Exists(record.From);
            if (originalExists)
            {
                if (!Directory.Exists(record.From) || related.Count == 0 ||
                    related.Any(r => !r.Undone || !Engine.SameOrInside(r.Current, record.From)) ||
                    Directory.GetFileSystemEntries(current).Length != 0)
                    throw new IOException("이전 위치에 이미 항목이 있습니다: " + record.From);
                Directory.Delete(current);
                record.Current = record.From; record.Undone = true;
                return "빈 폴더를 정리했습니다. 파일은 이미 이전 위치에 있습니다: " + record.From;
            }
            if (related.Any(r => r.Undone))
                throw new IOException("관련 파일 일부가 이미 되돌려졌습니다. 나머지 파일도 먼저 되돌리세요.");
            HashSet<string> expected = new HashSet<string>(
                related.Select(r => Engine.Full(r.Current)), StringComparer.OrdinalIgnoreCase);
            HashSet<string> actual = StructurePlanner.ScanTarget(current).TargetFiles;
            if (!actual.SetEquals(expected))
                throw new IOException("폴더 안의 파일이 변경되어 자동으로 되돌릴 수 없습니다.");
            foreach (ChangeRecord file in related)
            {
                FileInfo info = new FileInfo(file.Current);
                if (info.Length != file.Length || info.LastWriteTimeUtc.Ticks != file.LastWriteTicks)
                    throw new IOException("폴더 안의 파일이 수정되어 자동으로 되돌릴 수 없습니다: " + file.Current);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(record.From));
            Directory.Move(current, record.From);
            foreach (ChangeRecord file in related)
            {
                string relative = file.Current.Substring(current.Length).TrimStart(Path.DirectorySeparatorChar);
                string restored = Path.Combine(record.From, relative);
                file.Current = restored;
                if (Engine.Full(file.From).Equals(Engine.Full(restored), StringComparison.OrdinalIgnoreCase))
                    file.Undone = true;
            }
            record.Current = record.From; record.Undone = true;
            return "폴더를 이전 위치로 돌렸습니다: " + Path.GetFileName(record.From);
        }
    }
}
