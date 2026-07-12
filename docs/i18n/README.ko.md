<div align="center">
  <img src="../images/hero.svg" alt="고양이와 두 개의 사용량 포션이 있는 Codex Pet HUD" width="920">
  <h1>Codex Pet HUD</h1>
  <p><strong>두 개의 포션, 하나의 펫, 방해는 제로.</strong></p>
  <p>Codex 펫 곁에 5시간·주간 사용량을 포션으로 보여주는 macOS·Windows 네이티브 HUD.</p>
  <p><strong>한국어</strong> · <a href="../../README.md">English</a> · <a href="README.zh-CN.md">简体中文</a> · <a href="README.ja.md">日本語</a></p>
</div>

## 소개

펫은 중앙에 그대로 남아 클릭할 수 있습니다. 왼쪽 빨간 포션은 5시간 사용량, 오른쪽 파란 포션은 주간 사용량을 보여줍니다. 마우스를 올리면 초기화까지 남은 시간이 표시되고, 클릭하면 상세 사용량과 수동 새로고침을 확인할 수 있습니다.

| 🐾 펫과 연동 | ⚡ 플랫폼 네이티브 | 🔒 조용하고 안전하게 |
|---|---|---|
| 펫이 숨으면 함께 사라지고 다시 나타나면 자동으로 돌아옵니다. | macOS는 Swift/AppKit, Windows는 .NET 8/WPF로 동작합니다. | 키보드 입력을 받지 않고 토큰을 기록하지 않으며 별도 펫을 만들지 않습니다. |

## 주요 기능

- 펫을 가리지 않는 디아블로 스타일 좌우 포션
- 실시간 잔여 사용량, 초기화 카운트다운, 20/10/5% 알림
- 크기·간격·중앙 정렬·위치 오프셋 설정
- macOS 상단 메뉴와 Windows 시스템 트레이 설정
- 펫이 숨겨지면 렌더링과 네트워크 갱신도 중단
- Codex 기록과 인증을 건드리지 않는 앱 전용 캐시 정리

## 플랫폼 지원

| 기능 | macOS | Windows |
|---|:---:|:---:|
| 펫 표시 상태 연동 | ✅ | ✅ |
| 좌우 포션과 호버 카운트다운 | ✅ | ✅ |
| 상세 사용량·알림·설정 | ✅ | ✅ |
| 실제 기기 검증 | ✅ | 🧪 프리뷰 |

> Windows는 x64/ARM64 빌드가 검증됐지만 첫 서명 릴리스 전에 실제 Windows 10/11에서 UI·DPI·트레이·SmartScreen 검증이 필요합니다.

## 설치

### macOS

```bash
git clone https://github.com/himomohi/codex-pet-hud.git
cd codex-pet-hud
./install.sh
```

제거: `./uninstall.sh` · 로그까지 제거: `./uninstall.sh --logs`

### Windows

```powershell
git clone https://github.com/himomohi/codex-pet-hud.git
cd codex-pet-hud
.\install.ps1
```

제거: `.\uninstall.ps1` · 설정까지 제거: `.\uninstall.ps1 -Data`

## 개인정보 보호

- 키보드 입력을 읽거나 기록하지 않습니다.
- Codex 토큰은 문서화되지 않은 ChatGPT 내부 사용량 API를 호출하는 동안 메모리에서만 사용하며 설정이나 로그에 복사하지 않습니다.
- 앱 소유 캐시와 임시 파일만 정리합니다.
- 펫이 숨겨져 있는 동안 네트워크 갱신을 멈춥니다.

> 이 프로젝트는 OpenAI의 공식 제품이 아닌 독립 커뮤니티 프로젝트입니다. 실시간 사용량 연동은 문서화되지 않은 내부 API에 의존하므로 예고 없이 변경될 수 있습니다.

구조와 보안 경계는 [아키텍처 문서](../architecture.md)를 확인하세요. 기여 방법은 [CONTRIBUTING.md](../../CONTRIBUTING.md), 라이선스는 [MIT](../../LICENSE)입니다.
