using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DownloadOrganizer
{
    public static class CopySmoke
    {
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "CopySmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(temp, "downloads");
                string root = Path.Combine(temp, "sorted");
                string work = Path.Combine(root, "업무");
                string copyFolder = Path.Combine(root, "업무 (2)");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(root);
                string incoming = Path.Combine(source, "새자료.txt");
                string original = Path.Combine(root, "원본.txt");
                File.WriteAllText(incoming, "incoming");
                File.WriteAllText(original, "original");
                File.SetLastWriteTimeUtc(incoming, DateTime.UtcNow.AddSeconds(-10));
                File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddSeconds(-10));
                FileInfo incomingInfo = new FileInfo(incoming), originalInfo = new FileInfo(original);
                StructureSnapshot before = StructurePlanner.ScanTarget(root);
                List<PlannedFile> files = new List<PlannedFile> {
                    Item(incomingInfo, Path.Combine(work, "새자료.txt"), true, false),
                    Item(originalInfo, original, false, false),
                    Item(originalInfo, Path.Combine(work, "원본.txt"), false, true),
                    Item(incomingInfo, Path.Combine(copyFolder, "새자료.txt"), true, true),
                    Item(originalInfo, Path.Combine(copyFolder, "원본.txt"), false, true)
                };
                ApplySummary summary = StructurePlanner.Apply(root, source, before, files,
                    new List<string> { work, copyFolder });
                if (summary.FilesMoved != 1 || summary.FilesCopied != 3 ||
                    !File.Exists(original) || !File.Exists(Path.Combine(work, "원본.txt")) ||
                    !File.Exists(Path.Combine(work, "새자료.txt")) ||
                    !File.Exists(Path.Combine(copyFolder, "원본.txt")) ||
                    !File.Exists(Path.Combine(copyFolder, "새자료.txt")) || File.Exists(incoming))
                    throw new Exception("복사·이동 적용 실패");
                PlanData plan = new PlanData();
                plan.Files.AddRange(files);
                plan.FolderChanges.Add(new FolderDelta { To = work });
                plan.FolderChanges.Add(new FolderDelta { To = copyFolder });
                ChangeJournal journal = new ChangeJournal();
                journal.AddStructure(plan, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                if (journal.Records.Count(record => record.Kind == "파일 복사") != 3)
                    throw new Exception("파일 복사 이력 실패");
                ChangeRecord copied = journal.Records.First(record => record.Kind == "파일 복사");
                string copiedPath = copied.To;
                journal.Undo(copied);
                if (File.Exists(copiedPath) || !File.Exists(original) || !copied.Undone)
                    throw new Exception("복사본만 되돌리기 실패");

                string secondSource = Path.Combine(temp, "second-downloads");
                string secondRoot = Path.Combine(temp, "second-sorted");
                Directory.CreateDirectory(secondSource);
                Directory.CreateDirectory(secondRoot);
                string secondOriginal = Path.Combine(secondSource, "다시.txt");
                File.WriteAllText(secondOriginal, "safe");
                File.SetLastWriteTimeUtc(secondOriginal, DateTime.UtcNow.AddSeconds(-10));
                FileInfo secondInfo = new FileInfo(secondOriginal);
                string conflict = Path.Combine(secondRoot, "복사본.txt");
                bool injected = false;
                try
                {
                    StructurePlanner.Apply(secondRoot, secondSource,
                        StructurePlanner.ScanTarget(secondRoot), new List<PlannedFile> {
                            Item(secondInfo, secondOriginal, true, false),
                            Item(secondInfo, conflict, true, true)
                        }, new List<string>(), delegate(int done, int total, string path, string phase) {
                            if (!injected && phase == "복사 중")
                            { File.WriteAllText(conflict, "other"); injected = true; }
                        });
                    throw new Exception("목적지 충돌을 감지하지 못했습니다.");
                }
                catch (IOException) { }
                if (!injected || !File.Exists(secondOriginal) || File.ReadAllText(conflict) != "other" ||
                    StructurePlanner.PendingStages(secondRoot).Length != 0)
                    throw new Exception("복사 실패 시 원본·외부 충돌 파일 보호 실패: injected=" + injected +
                        ", original=" + File.Exists(secondOriginal) + ", conflict=" +
                        (File.Exists(conflict) ? File.ReadAllText(conflict) : "(없음)") +
                        ", stages=" + StructurePlanner.PendingStages(secondRoot).Length);
                Console.WriteLine("PASS: 파일 복사·이동, 복사본만 되돌리기, 충돌 시 원본 보호");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
            finally
            {
                if (temp.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(temp)) Directory.Delete(temp, true);
            }
        }

        private static PlannedFile Item(FileInfo file, string destination, bool downloads, bool copy)
        {
            return new PlannedFile {
                Source = file.FullName, Destination = destination,
                Length = file.Length, LastWriteTicks = file.LastWriteTimeUtc.Ticks,
                CreationTicks = file.CreationTimeUtc.Ticks,
                FromDownloads = downloads, IsCopy = copy
            };
        }
    }
}
