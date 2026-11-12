# Claude Code project handoff

## Project

- Windows desktop download organizer. C# WinForms, .NET Framework 4; no external LLM/API, Node.js, or Python runtime dependency.
- The user wants a portable Windows executable that watches a chosen folder recursively, compares new file names with destination folder names and existing file names, moves only confident matches, and leaves ambiguous files for review.
- UI: main preview and tray monitoring; side-by-side structure editor with drag and drop, Ctrl/Shift multi-selection, file and folder rename, staged apply, progress, and item history/undo.
- Latest checked-in executable: `DownloadOrganizer-v22.exe`. Keep the versioned executable only for a user-facing release. Do not overwrite a running executable.

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
- `StructurePlanner.cs`: validates snapshots, stages file moves, rolls back failures, handles target-root rename.
- `ChangeJournal.cs`, `HistoryForm.cs`: item history and undo.
- `*.Smoke.cs`: focused integration checks; use `FolderRename.Smoke.cs` for root and child folder rename and undo.

## Invariants and known follow-up

- Treat file paths as user data. Never hardcode a person's home directory; do not commit `settings.xml`, `history.xml`, `changes.xml`, `topics.txt`, or user files.
- Do not read document contents or send file data to a service. Classification currently uses names only; keep uncertain matches in place.
- Preserve no-overwrite behavior, the download-complete check, plan preview before apply, and rollback/undo safety.
- Watch folder subdirectories are shown as grouping nodes; their files can be moved, while the source folder itself is not renamed in the structure editor.
- The target root and child folders can be renamed. Root rename must update saved paths and watcher state; test separate-folder and same-folder modes and history undo.
- Root rename combined with other folder rearrangements should receive additional end-to-end undo testing before changing that flow. OneDrive sync and large real folder trees also need user-environment testing.
