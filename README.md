# 다운로드 자동 정리 (Windows)

다운로드 파일을 사용자가 지정한 폴더 구조로 정리하는 Windows 데스크톱 프로그램입니다. 파일명과 대상 폴더명·기존 파일명을 비교하며, 확실한 경우에만 자동 이동합니다. 분류가 애매한 파일은 남겨 두고 미리보기에서 확인할 수 있습니다. 파일 본문이나 외부 LLM API는 사용하지 않습니다.

## 실행

1. [최신 실행 파일](DownloadOrganizer-v22.exe)을 내려받아 실행합니다. Windows의 .NET Framework 4.x가 필요합니다.
2. **감시할 폴더**와 **정리할 상위 폴더**를 선택합니다.
3. **미리보기 새로고침**으로 추천을 확인한 뒤, 원하면 **새 파일 자동 이동**을 켭니다.

감시 폴더의 하위 폴더까지 확인합니다. **새 폴더 구조 제안**에서는 현재 구조와 변경할 구조를 나란히 보고, 파일을 끌어 배치하거나 폴더 이름을 바꾼 뒤 적용할 수 있습니다. 적용 내역은 항목별로 되돌릴 수 있습니다. 자세한 사용법은 [사용방법.md](사용방법.md)를 참고하세요.

## 개발

Windows PowerShell에서 저장소 폴더를 연 뒤 실행합니다.

```powershell
.\build.ps1
.\test.ps1
```

빌드 결과는 `dist/DownloadOrganizer.exe`에 생성됩니다. 배포용 아이콘은 `DownloadOrganizer.ico`이며 `create_icon.py`로 다시 만들 수 있습니다(아이콘 재생성에만 Pillow 필요). 빌드와 프로그램 실행에는 Python이나 Node.js가 필요하지 않습니다.

현재 코드는 C# Windows Forms와 .NET Framework 4 컴파일러를 사용합니다. 분류 로직은 `DownloadOrganizer.cs`와 `LocalSearchIndex.cs`, 비동기 감시와 화면은 `MainForm.Async.cs`·`MainForm.Tray.cs`, 구조 편집과 복구는 `StructureEditor*.cs`·`StructurePlanner.cs`·`ChangeJournal.cs`에 있습니다.

Claude Code에서 이어 작업할 때는 이 저장소를 열고 [CLAUDE.md](CLAUDE.md)를 읽으면 현재 구현과 검증 방법을 확인할 수 있습니다. 제품 배경은 [문제정의서](problem.md)와 [설계 문서](다운로드_자동정리_설계.md)에 있습니다.

사용자의 실제 다운로드 파일, 폴더 경로, `%LOCALAPPDATA%\DownloadOrganizer`의 설정·이력은 이 저장소에 포함하지 않습니다.
