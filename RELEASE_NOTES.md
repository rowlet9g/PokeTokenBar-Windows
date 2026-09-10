# PokeTokenBar for Windows 0.5.4

## 수정 사항

- 검증되지 않은 Higgsfield CLI 크레딧 집계와 성장 환산을 제거했습니다.
- 사용량 공급자는 로컬 기록을 검증할 수 있는 Claude Code, Codex, Gemini CLI(레거시), Antigravity, OpenCode, Hermes Agent, Cursor, Grok CLI, Copilot CLI, Kiro CLI, Pi Agent, omp로 한정됩니다.
- 앱 좌측 상단 제목 옆에서 현재 설치 버전을 바로 확인할 수 있습니다.
- v0.5.2에서 통일한 `%USERPROFILE%\.poketokenbar` 진행 저장소와 기존 데이터 이전 정책을 유지합니다.

## 설치와 업데이트

- 실행 중인 앱을 종료하고 `PokeTokenBar-0.5.4-win-x64-setup.exe`를 기존 버전 위에 설치한 뒤 시작 메뉴에서 실행하세요.
- Portable: `PokeTokenBar-0.5.4-win-x64.zip`
- Windows 10 이상 x64용이며 .NET 런타임을 포함합니다.
- 기존 AppData 원본과 새 사용자 데이터는 업데이트·제거 시 모두 보존됩니다.
- 이전 버전에서 이미 서로 다른 저장본이 만들어졌다면 이를 자동 합산하지 않습니다. 필요한 기록은 설정의 세이브 내보내기·가져오기로 옮길 수 있습니다.
- SHA256SUMS.txt와 release-manifest.json을 함께 제공합니다.

## 검증과 제한

- Release 빌드와 170개 회귀 테스트를 통과했습니다.
- 코드 서명과 앱 내 자동 업데이트는 아직 제공하지 않습니다.
