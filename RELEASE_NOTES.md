# PokeTokenBar for Windows 0.6.2

## 수정 사항

- Antigravity 2.x/Desktop에서 오늘 사용한 token이 0으로 표시되던 문제를 수정했습니다.
- generation에서 제거된 사용 시각을 `steps.metadata`의 response/execution ID와 연결하여 정확한 날짜에 집계합니다.
- 1 MiB보다 큰 generation blob 뒤쪽에 기록된 token metadata도 빠뜨리지 않고 읽습니다.
- 최신 input/output/cache token field를 정확히 계산하면서 기존 Antigravity 형식도 계속 지원합니다.
- 공식 사용량 percentage와 로컬 token량은 각각 Desktop language server와 conversation SQLite에서 계속 독립적으로 가져옵니다.

## 설치와 업데이트

- 트레이 메뉴에서 실행 중인 앱을 종료하고 `PokeTokenBar-0.6.2-win-x64-setup.exe`를 기존 버전 위에 설치한 뒤 시작 메뉴에서 실행하세요.
- Portable: `PokeTokenBar-0.6.2-win-x64.zip`
- Windows 10 이상 x64용이며 .NET 런타임을 포함합니다.
- `%USERPROFILE%\.poketokenbar` 진행 데이터는 업데이트·제거 시 보존됩니다.
- SHA256SUMS.txt와 release-manifest.json을 함께 제공합니다.

## 검증과 제한

- Release 빌드와 195개 회귀 테스트를 통과했습니다.
- 실제 Antigravity Desktop DB에서 수정 전 오늘 0건이던 기록이 74,679 tokens로 복원되는 것을 확인했습니다.
- 1 MiB 초과 payload, `steps.metadata` 날짜 연결, 최신 token field와 레거시 형식을 자동 테스트합니다.
- 이번 변경에서 설치·업데이트·제거 수동 체크리스트 전체를 다시 수행하지는 않았습니다.
- 코드 서명과 앱 내 자동 업데이트는 아직 제공하지 않습니다.
