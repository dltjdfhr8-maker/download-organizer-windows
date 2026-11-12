# Agent project handoff

These are the project notes for Codex and other coding agents. `CLAUDE.md` holds the same notes for Claude Code. Keep the two files in sync when either changes.

## Project

- Windows desktop download organizer. C# WinForms, .NET Framework 4; no external LLM/API, Node.js, or Python runtime dependency.
- The user wants a portable Windows executable that watches a chosen folder recursively, compares new file names with destination folder names and existing file names, moves only confident matches, and leaves ambiguous files for review.
- UI: main preview and tray monitoring; side-by-side structure editor with drag and drop, Ctrl/Shift multi-selection, file and folder rename, staged apply, progress, and item history/undo.
- Latest checked-in executable: `DownloadOrganizer-v22.exe`. Keep the versioned executable only for a user-facing release. Do not overwrite a running executable; build to another name in `dist/` (git-ignored) with `.\build.ps1 -OutputPath dist\<name>.exe`.
- The app is single-instance per user: a second copy only signals the running one and exits, so close the tray app before trying a new build.
- If git reports "dubious ownership" (the folder was first created by a sandbox account), the user adds it with `git config --global --add safe.directory <path>`.

## Build and checks

- On Windows PowerShell: `.\build.ps1`; output is `dist/DownloadOrganizer.exe`.
- Run `.\test.ps1` for representative smoke checks. The checks create and clean up their own temporary test folders.
- Test any file-moving change with temporary files only. Never run a smoke test against the user's actual Downloads, OneDrive, or selected target folder.
- Read `사용방법.md` for behavior and `다운로드_자동정리_설계.md` / `problem.md` for product intent. Update these when behavior changes.

## Code map

- `DownloadOrganizer.cs`: entry point, settings, UI, file-name scoring and folder suggestions.
- `LocalSearchIndex.cs`: local BM25 and fuzzy candidate retrieval.
- `MainForm.Async.cs`, `MainForm.Tray.cs`: background scans, watcher, retries, tray lifecycle.
- `StructureEditor*.cs`: two-tree plan editor, selection, visuals, drag, rename.
- `TopicSuggester.cs`, `TopicSuggestionForm.cs`: topic-list suggestions and the review dialog.
- `SoftUi.cs`: shared colors plus card controls (`CardPanel`, `LocationCard`, `PillLabel`, `ArrowBadge`, `AppHeader`). The structure editor trees use `OwnerDrawAll`; row positions are derived from native `node.Bounds` so hit-testing (chevron, icon, label) stays native. Tests rely on node `BackColor` for drop/search state and on `selectedPathLabel`/`dragGuide` text, so keep those.
- Unhandled UI exceptions are appended to `%LocalAppData%\DownloadOrganizer\error.log` (`Program.LogError`); ask the user for that file when they report an error dialog.
- `[STAThread]` must stay directly on `Program.Main` (drag and drop needs an STA thread). Smoke tests have their own `Main`, so `MainForm.Destination.Smoke.cs` checks the app entry point explicitly.
- `StructurePlanner.cs`: validates snapshots, stages file moves, rolls back failures, handles target-root rename.
- `ChangeJournal.cs`, `HistoryForm.cs`: item history and undo.
- `*.Smoke.cs`: focused integration checks; use `FolderRename.Smoke.cs` for root and child folder rename and undo.

## Invariants and known follow-up

- Treat file paths as user data. Never hardcode a person's home directory; do not commit `settings.xml`, `history.xml`, `changes.xml`, `topics.txt`, or user files.
- Do not read document contents or send file data to a service. Classification currently uses names only; keep uncertain matches in place.
- Preserve no-overwrite behavior, the download-complete check, plan preview before apply, and rollback/undo safety.
- Watch folder subdirectories are shown as grouping nodes; their files can be moved, while the source folder itself is not renamed in the structure editor. Dragging a watch subfolder onto a target folder creates a new plan folder with the same name and moves its remaining files and inner layout; the emptied source folder stays on disk (no cleanup yet).
- In the structure editor, files already inside watch subfolders are never auto-placed (topic or existing-folder match) and are excluded from topic suggestions (`TopicSourceFiles`); they move only by drag. In separate-folder mode the main-window automatic and batch scans also skip them (`InWatchSubfolder`); preview marks them "하위 폴더 · 직접 옮기기" and manual "선택 파일 이동" still works. Same-folder mode is unaffected.
- The main-window destination box always lists the target root first (`RootChoice`), so items in `folderBox` are offset by one from `folders`. `MainForm.Destination.Smoke.cs` covers an empty target root.
- Topic suggestions (`TopicSuggester.cs`, `TopicSuggestionForm.cs`) use names only: word / word-pair / shared Hangul prefix counts plus greedy coverage. Topic placement in the editor and the dialog's expected counts share `TopicScore` and `Pick`; keep them in sync. `TopicSuggest.Smoke.cs` covers it. Quality on real folders has not been checked yet.
- The target root and child folders can be renamed. Root rename must update saved paths and watcher state; test separate-folder and same-folder modes and history undo.
- Root rename is journaled as a `폴더 이동` record with Reason `ChangeJournal.RootRenameReason`. While it is not undone, undo targets under the old root resolve under the current root (`ChangeJournal.ResolveOriginal`), so the old root is never recreated. `FolderRename.Smoke.cs` covers root + child rename + file move with both undo orders. Undo across several sequential root renames and persisted (reloaded) journals is not yet smoke-tested.
- `MainForm.Burst.Smoke.cs` and the F2-dialog step at the start of `FolderRename.Smoke.cs` are timing-sensitive; each failed once in several runs on 2026-11-12. Rerun before treating a failure as a regression.
- OneDrive sync and large real folder trees still need user-environment testing.
