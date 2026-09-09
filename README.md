<div align="center">

<img src="assets/icon.png" width="128" alt="PokeTokenBar icon">

# PokeTokenBar for Windows

**AI agent token usage로 포켓몬을 키워 봅시다.**

![Windows](https://img.shields.io/badge/Windows-10%2B-0078d4?logo=windows)
![.NET](https://img.shields.io/badge/.NET-10-512bd4?logo=dotnet)
![WPF](https://img.shields.io/badge/UI-WPF-5c2d91)
![License](https://img.shields.io/badge/license-MIT-3fb950)

</div>

> 이 저장소는 Swift와 AppKit으로 작성된 macOS용
> [원본 PokeTokenBar](https://github.com/chattymin/PokeTokenBar)를 Windows 전용으로
> 포팅하는 프로젝트입니다. 현재 Windows 앱은 C#/.NET과 WPF로 독립 구현되어 있습니다.

PokeTokenBar는 로컬에 저장된 AI 코딩 도구의 사용량을 읽어 오늘·이번 주·이번 달
토큰을 집계합니다. 새로 사용한 토큰은 포켓몬 알의 부화와 성장에 반영되며,
진화·도감·상점·가방 진행 상황은 Windows 사용자 데이터 폴더에 저장됩니다.


## Details

- Windows 알림 영역(시스템 트레이) 아이콘과 WPF 팝업
- Claude Code, Codex, Gemini CLI(레거시), Antigravity, OpenCode, Hermes Agent, Cursor, Grok CLI, Copilot CLI, Kiro CLI, Pi Agent, omp 로컬 사용량 집계
- 오늘·이번 주·이번 달 토큰 표시와 자동/수동 새로고침
- Codex·Claude Code·Antigravity 공식 5시간·주간 사용량 한도와 reset countdown 표시
  (각 도구의 로컬 실행 파일 또는 OAuth 자격증명이 Windows에 있는 경우)
- 홈·상점·가방·컬렉션 탐색과 밝은 팝업 화면, 별도 사용량 상세·설정 버튼
- 사용량 상세(TOKENS): 기간별 공급자 합계·비율과 오늘의 Input / Output / Cache 상세
- 홈의 성격·희귀도·진화 단계 표시와 확정 경로의 진화 스프라이트 미리보기(분기는 숨김)
- 알 부화, 실제 진화 계보 기반 성장, 분기 진화, 성격과 색이 다른 포켓몬 여부
- 1–5세대 포켓몬 도감과 개체별 포획 기록
- 부화·1진화·2진화 애니메이션 및 Windows 트레이 알림
- 클릭으로 본창을 열고 닫을 수 있는 드래그 가능한 플로팅 펫
- 단일 실행 인스턴스와 사용자별 데이터·캐시·로그 저장

처음 발견한 각 공급자의 누적 사용량은 기준값으로만 저장됩니다. 설치 전에 사용한
토큰을 소급해 성장시키지 않으며, 그 뒤에 새로 증가한 토큰부터 포켓몬에게 반영됩니다.

TOKENS의 TODAY는 Input / Output / Cache(쓰기·읽기)를 나누어 표시하며,
WEEK와 MONTH는 공급자별 합계와 비율을 표시합니다. 선택 기간의 사용량이 0인
공급자는 숨깁니다. HOME은 오늘 전체 합계와 포켓몬 성장을 보여 줍니다.


## Token usage가 계산되는 agent 목록

| 도구 | 기본 검색 위치 | 형식 |
| --- | --- | --- |
| Codex | `%USERPROFILE%\.codex\sessions`, `archived_sessions` | JSONL |
| Gemini CLI (레거시) | `%USERPROFILE%\.gemini\tmp` | JSON / JSONL |
| Antigravity CLI / IDE | `%USERPROFILE%\.gemini\antigravity*\conversations` | SQLite / protobuf |
| Cursor | `%APPDATA%\Cursor\User\globalStorage` 및 Nightly 경로 | SQLite |
| GitHub Copilot CLI | `%USERPROFILE%\.copilot` 또는 `COPILOT_HOME` | SQLite |
| Claude Code | `%USERPROFILE%\.claude\projects`, `.config\claude\projects` | JSONL |
| OpenCode | `%USERPROFILE%\.local\share\opencode` | SQLite / JSON |
| Hermes Agent | `%USERPROFILE%\.hermes\state.db` | SQLite |
| Grok CLI | `%USERPROFILE%\.grok\sessions` | JSONL |
| Kiro CLI (추정) | `%USERPROFILE%\.kiro\sessions`, `.local\share\kiro-cli`, `%APPDATA%\kiro-cli` | SQLite / JSONL |
| Pi Agent | `%USERPROFILE%\.pi\agent\sessions` | JSONL |
| omp | `%USERPROFILE%\.omp\agent\sessions` | JSONL |

위 목록은 **로컬 도구의 기록** 지원 목록이며, Claude/Grok 웹사이트나 모바일 앱의
대화 사용량은 포함하지 않습니다. Gemini CLI의 기존 로컬 기록 지원은 유지합니다.

Kiro CLI는 원본과 같이 텍스트의 UTF-8 바이트 길이를 이용한 **추정치**이며, 실제
토큰 사용량과 다를 수 있습니다. 추정치도 전체 합계와 포켓몬 성장에 반영됩니다.
나머지 공급자는 기록에 있는 토큰 메타데이터를 집계합니다. Kiro 추정 과정에서는
대화 텍스트를 로컬에서 읽지만, 대화 원문을 별도로 저장하거나 외부로 전송하지 않습니다.

추가 검색 경로, 집계 방식과 검증 범위는 [로컬 공급자 참고](docs/reference/local-providers.md)를
참고하세요. 새 공급자 7종은 샘플 기록으로 검증했으며, 각 도구의 실제 사용 검증은 남아 있습니다.


## 설치

### 요구 사항

- Windows 10 이상, x64

### How to...

[최신 릴리스 다운로드](https://github.com/rowlet9g/PokeTokenBar-Windows/releases/latest)


## 데이터와 네트워크

- 사용자 데이터: `%USERPROFILE%\.poketokenbar` (v0.5.2부터 실행 환경과 관계없이 공유)
- 기존 `%LOCALAPPDATA%\PokeTokenBar` 데이터는 첫 실행 시 자동 복사하며 원본을 보존합니다. 이전에 실패하면 진행을 초기화하지 않고 실행을 중단합니다.
- 설치 위치: `%LOCALAPPDATA%\Programs\PokeTokenBar`
- 포켓몬 종·진화 정보: [PokéAPI](https://pokeapi.co/)에서 런타임에 가져와 로컬 캐시
- 포켓몬 스프라이트: PokeAPI sprites 저장소에서 런타임에 가져와 로컬 캐시

포켓몬 이미지와 종 데이터는 실행 파일에 포함되지 않습니다. 처음 만나는 포켓몬의
정보나 아직 캐시되지 않은 이미지를 받을 때는 인터넷 연결이 필요합니다. 토큰 사용량,
프롬프트, 응답 내용 및 프로젝트 경로는 PokéAPI로 전송하지 않습니다.


## 프로젝트 구조

```text
Windows/
├─ src/PokeTokenBar.Core/              # 사용량·포켓몬·저장 로직
├─ src/PokeTokenBar.Platform.Windows/   # 트레이·SQLite·시작프로그램 등 Windows 연동
└─ src/PokeTokenBar.Windows/            # WPF 애플리케이션과 화면
```

자세한 Windows 구현 내용은 [`Windows/README.md`](Windows/README.md), 기능별 진행
상태는 [`Windows/PORTING_STATUS.md`](Windows/PORTING_STATUS.md)에서 확인할 수 있습니다.


## LICENSE

이 포팅은 [chattymin/PokeTokenBar](https://github.com/chattymin/PokeTokenBar)를
기반으로 합니다. 원작과 이 저장소의 자체 소스 코드는 [`LICENSE`](LICENSE)의 MIT
라이선스를 따릅니다.

PokeTokenBar는 비공식·비상업적 팬 프로젝트이며 Nintendo, Game Freak, Creatures Inc.
또는 The Pokémon Company와 제휴·후원·승인 관계가 없습니다. Pokémon 관련 명칭,
캐릭터, 이미지 및 데이터의 권리는 각 권리자에게 있습니다.
