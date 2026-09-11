# PokeTokenBar for Windows 0.5.6

## 수정 사항

- 자동 사용량 갱신 중 예상하지 못한 예외가 발생해도 트레이 앱이 바로 종료되지 않도록 보호했습니다.
- 앱 시작, 명시적 종료, 자동 갱신 오류, UI 미처리 예외를 `%USERPROFILE%\.poketokenbar\Logs\runtime.log`에 기록합니다.
- Windows 오류 이벤트가 남지 않는 간헐적 종료도 다음 발생 시 정상 종료인지 예외인지 구분할 수 있습니다.
- v0.5.5의 30초 기본 자동 갱신 주기를 유지합니다.

## 설치와 업데이트

- 실행 중인 앱을 종료하고 `PokeTokenBar-0.5.6-win-x64-setup.exe`를 기존 버전 위에 설치한 뒤 시작 메뉴에서 실행하세요.
- Portable: `PokeTokenBar-0.5.6-win-x64.zip`
- Windows 10 이상 x64용이며 .NET 런타임을 포함합니다.
- `%USERPROFILE%\.poketokenbar` 진행 데이터는 업데이트·제거 시 보존됩니다.
- SHA256SUMS.txt와 release-manifest.json을 함께 제공합니다.

## 검증과 제한

- Release 빌드와 172개 회귀 테스트를 통과했습니다.
- 현재 Windows Application Error, .NET Runtime, WER에는 기존 종료에 대응하는 충돌 기록이 없어 과거 종료의 단일 원인은 확정할 수 없습니다.
- 코드 서명과 앱 내 자동 업데이트는 아직 제공하지 않습니다.
