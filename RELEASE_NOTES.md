# PokeTokenBar for Windows 0.4.0

## 새로운 기능

- TOKENS 탭에서 TODAY / WEEK / MONTH별 전체 토큰과 공급자별 합계·비율을 확인할 수 있습니다.
- TODAY는 Input / Output / Cache를 표시하며 캐시 쓰기와 읽기도 구분합니다.
- 공급자별 갱신 시각과 새로고침·오류 상태를 표시합니다. 사용량이 0인 공급자는 숨깁니다.
- Antigravity CLI/IDE의 로컬 사용량을 집계합니다. 새 요청 후 토큰과 포켓몬 진행이 함께 증가하는 동작을 사용자 환경에서 확인했습니다.
- Claude Code, OpenCode, Hermes Agent, Grok CLI, Kiro CLI, Pi Agent, omp의 로컬 기록을 추가로 집계합니다.
- Kiro CLI는 원본 동작에 맞춰 UTF-8 텍스트 길이 기반 추정치로 표시하며, 화면에서 추정임을 구분합니다.
- Windows 설치·개발·배포 문서와 로컬 공급자 회계 참고 문서를 정리했습니다.

## 설치와 업데이트

- 일반 설치: `PokeTokenBar-0.4.0-win-x64-setup.exe`
- 압축 배포: `PokeTokenBar-0.4.0-win-x64.zip`
- Windows 10 이상, x64용이며 .NET 런타임을 포함합니다.
- 기존 설치본 위에 업데이트할 수 있으며 `%LOCALAPPDATA%\PokeTokenBar`의 포켓몬 진행과 설정을 보존합니다.
- 파일 검증용 `SHA256SUMS.txt`와 `release-manifest.json`을 함께 제공합니다.

## 알려진 제한

- 새로 추가된 일곱 공급자는 샘플 기록과 Windows SQLite fixture로 검증했으며, 모든 도구 버전의 실사용 검증을 마친 것은 아닙니다.
- 코드 서명은 아직 적용되지 않았으며 Windows에서 실행 경고가 나타날 수 있습니다.
- 앱 내 자동 업데이트는 제공하지 않습니다.
- WEEK / MONTH는 공급자별 합계와 비율만 제공하며 Input / Output / Cache 상세는 TODAY에 제공됩니다.
