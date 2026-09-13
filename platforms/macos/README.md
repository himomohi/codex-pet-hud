# macOS

The macOS implementation remains a native Swift/AppKit menu-bar application. The repository-root `install.sh` and `uninstall.sh` commands are compatibility wrappers around the scripts in this folder.

```bash
./install.sh
launchctl print gui/$(id -u)/io.github.himomohi.codex-pet-hud
tail -20 platforms/macos/logs/stderr.log
```

Settings and alert state remain under `~/Library/Application Support/CodexPetLimitRings`, so moving the source tree does not reset user preferences.

메뉴 막대의 **포션 디자인** 또는 **설정 열기…** 첫 화면에서 기본 포션과 새 포션 5종을 선택한다. 선택한 디자인은 두 HUD에 즉시 적용되며 `settings.json`의 `potionStyle`로 저장된다. 이전 설정은 기본 모양으로 열리고 배치·크기·알림 값은 유지된다.

미리보기·프레임·액체 마스크는 `shared/settings/potions`에 있으며 소스 설치와 릴리즈 모두 앱의 `Contents/Resources/settings/potions`에 포함한다. HUD는 프레임과 마스크에 실제 잔여량을 채우며, 설정 화면의 사용량은 디자인 확인용 예시다.

`./scripts/test-macos.sh`로 설정 호환성·포션 렌더링·기존 펫 위치 테스트를 실행한다. GitHub 릴리즈 하네스는 macOS 실행기에서 테스트 후 Apple Silicon/Intel universal 앱을 빌드하고 압축 안의 포션 리소스를 확인한다.
