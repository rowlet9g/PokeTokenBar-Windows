# PokeTokenBar for Windows 0.6.1

## 새로운 기능

- 실행 중인 Google Antigravity Desktop의 공식 사용량 한도를 HOME 화면에 표시합니다.
- Antigravity의 Gemini 모델 및 외부 모델 주간 사용률과 초기화 시각을 가져옵니다.
- Desktop 앱의 인증된 loopback language server를 사용하며 OAuth access token, refresh token, client secret, 프롬프트와 응답은 읽거나 저장하지 않습니다.
- Desktop 앱을 사용할 수 없을 때는 기존 Antigravity CLI token-file 조회 방식으로 자동 전환합니다.
- 종료된 language server의 오래된 로그를 현재 연결로 오인하지 않도록 방어합니다.

## 설치와 업데이트

- 트레이 메뉴에서 실행 중인 앱을 종료하고 `PokeTokenBar-0.6.1-win-x64-setup.exe`를 기존 버전 위에 설치한 뒤 시작 메뉴에서 실행하세요.
- Portable: `PokeTokenBar-0.6.1-win-x64.zip`
- Windows 10 이상 x64용이며 .NET 런타임을 포함합니다.
- `%USERPROFILE%\.poketokenbar` 진행 데이터는 업데이트·제거 시 보존됩니다.
- SHA256SUMS.txt와 release-manifest.json을 함께 제공합니다.

## 검증과 제한

- Release 빌드와 194개 회귀 테스트를 통과했습니다.
- Antigravity Desktop 2.15.1의 실제 계정에서 Gemini 및 외부 모델 주간 사용률과 초기화 시각 수신을 확인했습니다.
- 로컬 서버 검색, CSRF header 전달, 응답 wrapper 해석, 종료 로그 거부와 CLI fallback 격리를 자동 테스트합니다.
- 이번 변경에서 설치·업데이트·제거 수동 체크리스트 전체를 다시 수행하지는 않았습니다.
- 코드 서명과 앱 내 자동 업데이트는 아직 제공하지 않습니다.
