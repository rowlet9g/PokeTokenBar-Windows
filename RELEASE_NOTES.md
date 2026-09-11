# PokeTokenBar for Windows 0.5.5

## 수정 사항

- 기본 자동 갱신 주기를 2분에서 30초로 단축했습니다.
- 자동 갱신 선택지를 30초, 1분, 2분, 5분, 10분으로 조정했습니다.
- 기존 `refreshIntervalMinutes` 설정은 첫 실행 시 안전한 새 기본값 30초로 전환됩니다.
- 갱신이 겹치면 기존 요청을 병렬 실행하지 않고 한 번만 추가 수행하는 보호 로직을 유지합니다.

## 설치와 업데이트

- 실행 중인 앱을 종료하고 `PokeTokenBar-0.5.5-win-x64-setup.exe`를 기존 버전 위에 설치한 뒤 시작 메뉴에서 실행하세요.
- Portable: `PokeTokenBar-0.5.5-win-x64.zip`
- Windows 10 이상 x64용이며 .NET 런타임을 포함합니다.
- `%USERPROFILE%\.poketokenbar` 진행 데이터는 업데이트·제거 시 보존됩니다.
- SHA256SUMS.txt와 release-manifest.json을 함께 제공합니다.

## 검증과 제한

- 49MB Codex 세션에서 사용량 파싱이 약 0.4~0.6초에 완료되는 것을 확인했습니다.
- Release 빌드와 172개 회귀 테스트를 통과했습니다.
- 코드 서명과 앱 내 자동 업데이트는 아직 제공하지 않습니다.
