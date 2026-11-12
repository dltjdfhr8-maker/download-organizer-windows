$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework C# 컴파일러를 찾을 수 없습니다.' }

$sources = @(
    'DownloadOrganizer.cs', 'DesktopInstance.cs', 'MainForm.Async.cs', 'MainForm.Tray.cs',
    'StructurePlanner.cs', 'StructureEditor.cs', 'StructureEditor.Visual.cs',
    'StructureEditor.Drag.cs', 'StructureEditor.Selection.cs', 'StructureEditor.Rename.cs',
    'SoftUi.cs', 'DragGhost.cs', 'LocalSearchIndex.cs', 'ChangeJournal.cs', 'HistoryForm.cs'
) | ForEach-Object { Join-Path $PSScriptRoot $_ }

$outputDirectory = Join-Path $PSScriptRoot '.test-bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$checks = @(
    @{ Source = 'MainForm.Async.Smoke.cs'; Main = 'DownloadOrganizer.AsyncSmoke' },
    @{ Source = 'MainForm.Burst.Smoke.cs'; Main = 'DownloadOrganizer.BurstSmoke' },
    @{ Source = 'StructureEditor.Smoke.cs'; Main = 'DownloadOrganizer.StructureSmoke' },
    @{ Source = 'FolderRename.Smoke.cs'; Main = 'DownloadOrganizer.FolderRenameSmoke' },
    @{ Source = 'SingleFolder.Smoke.cs'; Main = 'DownloadOrganizer.SingleFolderSmoke' },
    @{ Source = 'SingleFolderWatcher.Smoke.cs'; Main = 'DownloadOrganizer.SingleFolderWatcherSmoke' }
)

foreach ($check in $checks) {
    $testSource = Join-Path $PSScriptRoot $check.Source
    $testOutput = Join-Path $outputDirectory ([IO.Path]::GetFileNameWithoutExtension($check.Source) + '.exe')
    & $compiler /nologo /target:exe "/main:$($check.Main)" "/out:$testOutput" `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll $testSource $sources
    if ($LASTEXITCODE -ne 0) { throw "시험 빌드 실패: $($check.Source)" }
    & $testOutput
    if ($LASTEXITCODE -ne 0) { throw "시험 실패: $($check.Source)" }
}
Write-Output '대표 기능 시험 완료'
