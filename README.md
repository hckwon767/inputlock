# 입력 잠금 타이머

키보드와 마우스가 설정한 시간 동안 사용되지 않으면 입력을 잠그는 Windows 데스크톱 앱입니다.
잠금 직전에 화면 중앙에 카운트다운을 표시하고, 사용자가 지정한 단축키로 취소·해제합니다.

## 사용 방법

1. [Actions](../../actions) 의 최신 실행(또는 Releases)에서 `InputLockTimer-win-x64` 아티팩트를 내려받아 `InputLockTimer.exe`를 실행합니다. .NET을 따로 설치할 필요가 없습니다.
2. 처음 실행하면 설정 창이 열립니다. **해제 단축키의 [시험…] 버튼으로 감지 여부를 확인**한 뒤 `잠금 기능 사용`을 켜고 저장합니다. (기능을 켜기 전에는 입력이 차단되지 않습니다.)
3. 알림 영역(트레이) 아이콘으로 상태를 확인합니다. 아이콘 색: 회색(꺼짐) · 초록(대기) · 주황(카운트다운) · 빨강(잠김). 마우스를 올리면 잠금까지 남은 시간이 표시됩니다.
4. 종료는 트레이 메뉴의 `종료`입니다.

> exe는 코드 서명이 되어 있지 않아 Windows SmartScreen 경고가 나올 수 있습니다.

## 동작

1. 앱 실행 후 설정을 불러옵니다.
2. 비활성 시간을 잽니다. 키보드·마우스 입력이 있으면 초기화됩니다.
3. 설정한 시간이 지나면 화면 중앙에 카운트다운 OSD(최상위 창)를 표시합니다.
4. 취소 단축키, 또는 (설정한 경우) 일반 입력이 감지되면 카운트다운을 취소하고 타이머를 초기화합니다.
5. 카운트다운이 끝나면 입력을 차단하고 화면 상단에 `입력 잠김 · <단축키> 로 해제` 배너를 표시합니다.
6. 해제 단축키를 감지하면 차단을 풀고 타이머를 초기화합니다.

### 설정 항목

| 설정 | 기본값 | 설명 |
|---|---:|---|
| 비활성 시간 | 5분 | 시·분·초. 0초는 허용하지 않음 |
| 카운트다운 | 5초 | 1~30초 |
| 해제 단축키 | `Ctrl + Alt + K` | `Ctrl`/`Alt`/`Shift` 조합 + 일반 키, 또는 단일 키(예: `Shift + F12`) |
| 카운트다운 취소 단축키 | 해제 단축키와 동일 | 별도 지정 가능 |
| 카운트다운 중 일반 입력 시 취소 | 켬 | 끄면 단축키로만 취소 |

설정은 `%APPDATA%\InputLockTimer\settings.json` 에 저장됩니다.

## 안전 설계

- **후크는 필요한 동안만 설치**합니다. 대기 중에는 키보드·마우스 후크 없이 `GetLastInputInfo`로 비활성 시간만 확인하고, 카운트다운이 시작될 때 후크를 설치하며 대기 상태로 돌아오면 제거합니다.
- 입력 내용은 기록하지도, 외부로 전송하지도 않습니다. 후크는 "입력이 있었는가"와 "해제 단축키와 일치하는가"만 판단합니다.
- 해제 단축키가 설정·검증되지 않으면 기능을 켤 수 없고, 저장 전 `시험…`으로 실제 감지 여부를 확인할 수 있습니다. 충돌 가능성이 있는 조합(수식키 없는 문자 키, `Shift + 문자`, `Alt+F4` 등)은 저장 전에 경고합니다.
- 입력 차단이 남지 않도록 다음 경우에 모두 해제합니다: 앱 종료, 처리되지 않은 예외, 후크 콜백 오류(fail-open), 로그오프·세션 종료, 화면 잠금(`Win+L`), 원격 연결 해제, 절전·복귀.
- 잠금 중 차단된 키 업·버튼 업은 해제 시점에 시스템으로 보내 수식키나 마우스 버튼이 눌린 채 남지 않게 합니다.
- `Ctrl+Alt+Delete` 등 Windows 보안 화면·시스템 단축키는 가로채지 않으며, 해제 단축키로도 지정할 수 없습니다.
- 숨김 실행, 자동 시작 기능은 없습니다. 항상 트레이 아이콘이 표시됩니다.

## 제한 사항

- 일반 사용자 권한으로 실행되므로, **관리자 권한으로 실행 중인 창이 활성일 때는 입력 감지·차단이 제한**될 수 있습니다(UIPI). 필요하면 사용자가 직접 앱을 관리자 권한으로 실행해야 합니다.
- OSD는 최상위 창으로 표시되지만, 배타적 전체 화면(exclusive fullscreen) 게임 위에는 보이지 않을 수 있습니다. 테두리 없는 전체 화면 앱 위에는 표시됩니다.
- 터치·펜 입력은 시스템 비활성 시간에는 반영되지만, 후크를 통한 차단은 키보드·마우스에 한정됩니다.

## 빌드

요구 사항: Windows, .NET 8 SDK

```powershell
dotnet test tests/InputLockTimer.Tests
dotnet publish src/InputLockTimer/InputLockTimer.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o publish
```

GitHub Actions(`.github/workflows/build.yml`)는 푸시·PR마다 테스트와 단일 exe 게시를 수행해 `InputLockTimer-win-x64` 아티팩트로 올리고, `v*` 태그를 푸시하면 Release에도 첨부합니다.
크기를 줄이려면 `--self-contained false`로 바꿀 수 있으며, 이 경우 실행 PC에 .NET 8 Desktop Runtime이 필요합니다.

## 구조

```text
input-lock-timer/
├── src/InputLockTimer/
│   ├── Program.cs            진입점, 단일 인스턴스, 예외 시 입력 해제
│   ├── TrayApp.cs            트레이 아이콘·메뉴, 세션/전원 이벤트
│   ├── LockController.cs     상태 기계 + 후크 + OSD 연결
│   ├── LockStateMachine.cs   순수 상태 기계(테스트 대상)
│   ├── InputHook.cs          저수준 키보드·마우스 후크, 단축키 감지, 눌린 키 해제
│   ├── OverlayForm.cs        카운트다운 OSD / 잠금 배너
│   ├── SettingsForm.cs       설정 창, 단축키 입력 상자, 단축키 시험 창
│   ├── HotkeyCombo.cs        단축키 모델·파싱·검증·충돌 경고
│   ├── AppSettings.cs        설정 모델·검증
│   ├── SettingsStore.cs      설정 파일 입출력
│   └── NativeMethods.cs      Win32 P/Invoke
├── tests/InputLockTimer.Tests/
├── .github/workflows/build.yml
├── README.md
└── LICENSE
```

## 라이선스

MIT
