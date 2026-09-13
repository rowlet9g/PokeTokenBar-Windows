# PokeTokenBar for Windows 0.5.7

## 수정 사항

- 토큰 사용량은 정상 집계되지만 공식 사용량 한도에 `한도 갱신 실패 · codex: Codex rate limit 응답을 받지 못했습니다.`가 표시되는 문제를 수정했습니다.
- Codex app-server 실행 시 지원하지 않는 `--stdio` 옵션을 제거하고 기본 stdio 통신을 사용합니다. 실행 파일과 Windows 배치 실행 경로 모두 수정했습니다.
- 토큰 합계는 로컬 세션 파일에서, 공식 한도는 Codex app-server에서 각각 읽기 때문에 한도 조회만 실패할 수 있었습니다.
- 기존 30초 기본 자동 갱신 주기와 v0.5.6의 트레이 앱 예외 보호를 유지합니다.

## 설치와 업데이트

- 트레이 메뉴에서 실행 중인 앱을 종료하고 `PokeTokenBar-0.5.7-win-x64-setup.exe`를 기존 버전 위에 설치한 뒤 시작 메뉴에서 실행하세요.
- Portable: `PokeTokenBar-0.5.7-win-x64.zip`
- Windows 10 이상 x64용이며 .NET 런타임을 포함합니다.
- `%USERPROFILE%\.poketokenbar` 진행 데이터는 업데이트·제거 시 보존됩니다.
- SHA256SUMS.txt와 release-manifest.json을 함께 제공합니다.

## 검증과 제한

- Release 빌드와 173개 회귀 테스트를 통과했습니다. `.exe`, `.cmd`, `.bat` 실행 경로와 공백이 있는 배치 파일 경로를 검증합니다.
- 수정한 공급자로 실제 로그인된 Codex 계정의 한도 창 3개를 정상 조회했습니다.
- 이번 변경에서 설치·제거 수동 체크리스트 전체를 다시 수행하지는 않았습니다.
- 코드 서명과 앱 내 자동 업데이트는 아직 제공하지 않습니다.
