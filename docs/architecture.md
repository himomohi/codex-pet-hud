# Architecture

## Boundary

Codex Pet HUD has two native shells with one shared contract layer:

```text
Codex state + auth + usage API
              |
       shared/contracts
          /         \
 Swift/AppKit      .NET/WPF
    macOS           Windows
```

The platform applications do not share UI source code. This keeps AppKit/launchd behavior out of Windows and WPF/registry behavior out of macOS. `shared/` contains only portable web assets, schemas, and deterministic fixtures.

## Runtime ownership

| Concern | macOS | Windows |
|---|---|---|
| HUD | one transparent AppKit window with potion hit regions | two transparent WPF windows, leaving the pet center uncovered |
| Tray | `NSStatusItem` | `System.Windows.Forms.NotifyIcon` |
| Settings | local WKWebView using `shared/settings` | native WPF settings window |
| Startup | per-user LaunchAgent | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| App data | `~/Library/Application Support/CodexPetLimitRings` | `%LOCALAPPDATA%\CodexPetLimitRings` |
| Cleanup | app-owned HTTP/cache/temp/log data only | app-owned Cache/Temp/Logs only |

## Windows 포션 선택 연결

`Views/SettingsWindow`의 첫 화면은 기본 모양과 새 포션 5종을 표시한다.
`SettingsChanged` → `MainController.ApplySettings` → `SettingsStore.SaveSettings`와
두 `PotionWindow.ApplyStyle` 호출로 선택과 HUD 표시를 연결한다.
`OverlaySettings.PotionStyle`은 `potionStyle` 키로 저장하고 기본값은 `classic`이다.
이전 설정과 알 수 없는 JSON 키는 보존하며, 모양 선택 시 배치·알림 값을 덮어쓰지 않는다.

`PotionStyles`는 15개 WPF 내장 PNG(미리보기·프레임·마스크)를 공유한다.
`PotionWindow.UpdateUsage`는 각 병의 마스크에 0~100% 액체를 채우고,
기존 92×110 기준 크기와 입력·드래그 계약을 유지한다.
`tests/PotionTests`는 6종 직렬화, 내장 이미지, 미확인·0·50·100%, 세 가지 배율,
480/520px 첫 화면 노출을 검사한다. 실제 클릭·키보드 선택·저장은 설치본에서 별도로 확인한다.

## macOS 포션 선택 연결

메뉴 막대의 **포션 디자인**과 `shared/settings`의 선택 카드는 같은 `potionStyle` 계약을 사용한다.
웹 설정의 `save` 메시지 → `SettingsWindowController` → `RingsApp.applyOverlaySettings`가 JSON 저장, HUD 갱신, 열린 설정 화면 동기화를 담당한다.
`RingsView`는 `shared/settings/potions`의 프레임·마스크로 실제 잔여량을 그리며 기존 5시간 빨강·주간 파랑을 유지한다.
기본 모양은 기존 AppKit 렌더링을 사용한다. 소스 설치와 릴리즈 빌드는 모두 `settings` 폴더를 앱 리소스로 복사한다.

`scripts/test-macos.sh`는 이전 설정 및 6종 저장·렌더링을 검사하고, `scripts/test-potion-assets.mjs`는 Windows와 macOS PNG 15개의 일치를 검사한다.
릴리즈는 양쪽 네이티브 테스트와 macOS universal/Windows x64·ARM64 빌드, 압축 리소스 확인을 통과해야 공개한다.

## Trigger invariant

Both native apps must show the HUD only when:

1. `electron-avatar-overlay-open` exists and is exactly `true`;
2. `electron-avatar-overlay-bounds` contains the legacy window plus `mascot`/`anchor` rectangles; on macOS, the integrated app's direct `x`/`y` anchor is also accepted;
3. the matching Codex pet window is currently visible, not minimized, and not DWM-cloaked.

When the invariant becomes false, potion windows, detail UI, transient alerts, and usage refresh stop immediately. Tray access remains available for settings and diagnostics.

## Secret handling

The usage access token is read directly from the platform user's `.codex/auth.json` only for the HTTPS request. It is never copied into app settings, shared fixtures, logs, notifications, or build artifacts.

## Windows release gate

`EnableWindowsTargeting` allows compilation on macOS, but a Windows release still requires real Windows 10/11 checks for:

- the current Codex state path and JSON contract;
- WPF transparency and non-activating potion clicks;
- mixed-DPI and negative-coordinate monitors;
- tray startup, balloon notifications, and uninstall;
- code signing and SmartScreen reputation.

Primary references: [Microsoft cross-targeting guidance](https://learn.microsoft.com/dotnet/core/tools/sdk-errors/netsdk1100), [WPF transparency](https://learn.microsoft.com/dotnet/api/system.windows.window.allowstransparency), and [NotifyIcon](https://learn.microsoft.com/dotnet/desktop/winforms/controls/app-icons-to-the-taskbar-with-wf-notifyicon).
