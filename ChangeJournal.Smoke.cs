using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace DownloadOrganizer
{
    internal static class JournalSmoke
    {
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        private static void ExpectConflict(Action action)
        {
            bool refused = false;
            try { action(); }
            catch (IOException) { refused = true; }
            Check(refused, "conflicting undo was accepted");
        }

        public static int Main()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "DownloadOrganizerJournal-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(testRoot);
                string downloads = Path.Combine(testRoot, "downloads");
                string sorted = Path.Combine(testRoot, "sorted");
                Directory.CreateDirectory(downloads); Directory.CreateDirectory(sorted);

                string from = Path.Combine(downloads, "file.txt");
                string toFolder = Path.Combine(sorted, "topic");
                string to = Path.Combine(toFolder, "file.txt");
                Directory.CreateDirectory(toFolder);
                File.WriteAllText(from, "one");
                File.Move(from, to);
                FileInfo info = new FileInfo(to);
                ChangeJournal journal = new ChangeJournal();
                ChangeRecord file = journal.AddFile(from, to, info.Length, info.LastWriteTimeUtc.Ticks,
                    "now", "test", "");
                journal.Undo(file);
                Check(File.Exists(from) && !File.Exists(to) && file.Undone, "file undo failed");
                ExpectConflict(() => journal.Undo(file));

                File.Move(from, to);
                info = new FileInfo(to);
                ChangeRecord conflict = journal.AddFile(from, to, info.Length, info.LastWriteTimeUtc.Ticks,
                    "now", "test", "");
                File.WriteAllText(from, "occupied");
                ExpectConflict(() => journal.Undo(conflict));
                File.Delete(from);
                File.AppendAllText(to, "changed");
                ExpectConflict(() => journal.Undo(conflict));
                File.Delete(to);

                PlanData createdPlan = new PlanData();
                string empty = Path.Combine(sorted, "empty");
                Directory.CreateDirectory(empty);
                createdPlan.FolderChanges.Add(new FolderDelta { To = empty });
                journal.AddStructure(createdPlan, "now");
                ChangeRecord creation = journal.Records.Last();
                journal.Undo(creation);
                Check(!Directory.Exists(empty) && creation.Undone, "folder creation undo failed");

                string removed = Path.Combine(sorted, "removed");
                PlanData removedPlan = new PlanData();
                removedPlan.FolderChanges.Add(new FolderDelta { From = removed });
                journal.AddStructure(removedPlan, "now");
                ChangeRecord removal = journal.Records.Last();
                journal.Undo(removal);
                Check(Directory.Exists(removed) && removal.Undone, "folder removal undo failed");

                TestFolderMove(testRoot, false);
                TestFolderMove(testRoot, true);
                TestRemovedFolderAfterFile(testRoot);

                string xml = Path.Combine(testRoot, "changes.xml");
                using (FileStream stream = File.Create(xml))
                    new XmlSerializer(typeof(ChangeJournal)).Serialize(stream, journal);
                using (FileStream stream = File.OpenRead(xml))
                    Check(((ChangeJournal)new XmlSerializer(typeof(ChangeJournal)).Deserialize(stream)).Records.Count ==
                        journal.Records.Count, "history persistence failed");
                Console.WriteLine("PASS: per-file and per-folder undo, conflicts, both undo orders, persistence");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
            finally
            {
                if (testRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }

        private static void TestFolderMove(string testRoot, bool fileFirst)
        {
            string group = Path.Combine(testRoot, fileFirst ? "file-first" : "folder-first");
            string downloads = Path.Combine(group, "downloads");
            string root = Path.Combine(group, "sorted");
            string original = Path.Combine(root, "old");
            string proposed = Path.Combine(root, "new");
            Directory.CreateDirectory(downloads); Directory.CreateDirectory(original);
            string originalFile = Path.Combine(original, "inside.txt");
            string proposedFile = Path.Combine(proposed, "inside.txt");
            File.WriteAllText(originalFile, "contents");
            FileInfo info = new FileInfo(originalFile);
            PlanData plan = new PlanData();
            plan.Files.Add(new PlannedFile {
                Source = originalFile, Destination = proposedFile,
                Length = info.Length, LastWriteTicks = info.LastWriteTimeUtc.Ticks
            });
            plan.Directories.Add(proposed);
            plan.FolderChanges.Add(new FolderDelta { From = original, To = proposed });
            StructurePlanner.Apply(root, downloads, StructurePlanner.ScanTarget(root),
                plan.Files, plan.Directories);
            ChangeJournal journal = new ChangeJournal();
            journal.AddStructure(plan, "now");
            ChangeRecord folder = journal.Records.Single(r => r.Kind == "폴더 이동");
            ChangeRecord file = journal.Records.Single(r => r.Kind == "파일 이동");
            if (fileFirst)
            {
                journal.Undo(file);
                journal.Undo(folder);
            }
            else
            {
                journal.Undo(folder);
            }
            Check(File.Exists(originalFile) && !Directory.Exists(proposed) &&
                folder.Undone && file.Undone, "folder move undo failed: " + fileFirst);
        }

        private static void TestRemovedFolderAfterFile(string testRoot)
        {
            string group = Path.Combine(testRoot, "removed-after-file");
            string source = Path.Combine(group, "downloads");
            string root = Path.Combine(group, "sorted");
            string oldFolder = Path.Combine(root, "old");
            string newFolder = Path.Combine(root, "new");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(oldFolder);
            Directory.CreateDirectory(newFolder);
            string original = Path.Combine(oldFolder, "inside.txt");
            string moved = Path.Combine(newFolder, "inside.txt");
            File.WriteAllText(original, "contents");
            FileInfo info = new FileInfo(original);
            PlanData plan = new PlanData();
            plan.Files.Add(new PlannedFile {
                Source = original, Destination = moved,
                Length = info.Length, LastWriteTicks = info.LastWriteTimeUtc.Ticks
            });
            plan.Directories.Add(newFolder);
            plan.FolderChanges.Add(new FolderDelta { From = oldFolder });
            StructurePlanner.Apply(root, source, StructurePlanner.ScanTarget(root),
                plan.Files, plan.Directories);
            ChangeJournal journal = new ChangeJournal();
            journal.AddStructure(plan, "now");
            journal.Undo(journal.Records.Single(r => r.Kind == "파일 이동"));
            ChangeRecord folder = journal.Records.Single(r => r.Kind == "폴더 제거");
            journal.Undo(folder);
            Check(File.Exists(original) && folder.Undone, "removed folder history after file undo failed");
        }
    }
}
