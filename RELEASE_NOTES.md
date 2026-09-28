# PokeTokenBar for Windows 0.6.3

## 수정 사항

- Antigravity Desktop 2.17 업데이트 후 공식 사용량 한도가 표시되지 않던 문제를 수정했습니다.
- 새 버전에서 사라진 서버 시작 로그 대신 실행 중인 서버의 CSRF 연결 값을 읽습니다. 같은 Windows 세션, Antigravity 실행 파일 경로, 로컬 포트 소유 프로세스를 확인한 뒤 한도를 조회합니다.
- 연결 정보 조회는 시간 제한이 있는 숨겨진 Windows PowerShell 프로세스로 수행합니다. CSRF 값은 메모리에서만 사용하며 PokeTokenBar 로그나 상태 파일에 저장하지 않습니다.
- 이전 Desktop의 로그 기반 연결과 기존 CLI 방식도 유지합니다.

## 설치와 업데이트

- 실행 중인 PokeTokenBar를 트레이 메뉴에서 종료하고 `PokeTokenBar-0.6.3-win-x64-setup.exe`를 기존 버전 위에 설치한 뒤 실행하세요.
- Portable: `PokeTokenBar-0.6.3-win-x64.zip`
- Windows 10 이상 x64용이며 .NET 런타임을 포함합니다.
- 기존 `%USERPROFILE%\.poketokenbar` 진행 데이터는 보존됩니다.
- `SHA256SUMS.txt`와 `release-manifest.json`을 함께 제공합니다.

## 검증과 제한

- Release 빌드와 202개 테스트를 통과했습니다.
- 실제 Antigravity 2.17 계정에서 Gemini 및 외부 모델의 주간 사용률과 초기화 시각을 수신했습니다.
- 이번 변경에서 설치·업데이트·제거 수동 체크리스트 전체를 다시 수행하지는 않았습니다.
- 코드 서명과 앱 내 자동 업데이트는 아직 제공하지 않습니다.
