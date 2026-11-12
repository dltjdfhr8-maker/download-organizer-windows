param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'dist\DownloadOrganizer.exe')
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework C# 컴파일러를 찾을 수 없습니다.' }

$sources = @(
    'DownloadOrganizer.cs', 'DesktopInstance.cs', 'MainForm.Async.cs', 'MainForm.Tray.cs',
    'StructurePlanner.cs', 'StructureEditor.cs', 'StructureEditor.Visual.cs',
    'StructureEditor.Drag.cs', 'StructureEditor.Selection.cs', 'StructureEditor.Rename.cs',
    'StructureEditor.Clipboard.cs',
    'SoftUi.cs', 'DragGhost.cs', 'LocalSearchIndex.cs', 'ChangeJournal.cs', 'HistoryForm.cs',
    'TopicSuggester.cs', 'TopicSuggestionForm.cs'
) | ForEach-Object { Join-Path $PSScriptRoot $_ }

$absoluteOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $absoluteOutput
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$icon = Join-Path $PSScriptRoot 'DownloadOrganizer.ico'
& $compiler /nologo /target:winexe "/out:$absoluteOutput" "/win32icon:$icon" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll $sources
if ($LASTEXITCODE -ne 0) { throw "빌드 실패: 종료 코드 $LASTEXITCODE" }
Write-Output "빌드 완료: $absoluteOutput"
