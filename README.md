<div align="center">

<img src="assets/icon.png" width="128" alt="PokeTokenBar icon">

# PokeTokenBar for Windows

**AI 코딩 도구에서 사용한 토큰으로 포켓몬을 키우는 Windows 트레이 앱**

![Windows](https://img.shields.io/badge/Windows-10%2B-0078d4?logo=windows)
![.NET](https://img.shields.io/badge/.NET-10-512bd4?logo=dotnet)
![WPF](https://img.shields.io/badge/UI-WPF-5c2d91)
![License](https://img.shields.io/badge/license-MIT-3fb950)

</div>

> 이 저장소는 Swift와 AppKit으로 작성된 macOS용
> [원본 PokeTokenBar](https://github.com/chattymin/PokeTokenBar)를 Windows 전용으로
> 포팅하는 프로젝트입니다. 현재 Windows 앱은 Swift 코드를 실행하지 않으며,
> C#/.NET과 WPF로 독립 구현되어 있습니다.

PokeTokenBar는 로컬에 저장된 AI 코딩 도구의 사용량을 읽어 오늘·이번 주·이번 달
토큰을 집계합니다. 새로 사용한 토큰은 포켓몬 알의 부화와 성장에 반영되며,
진화·도감·상점·가방 진행 상황은 Windows 사용자 데이터 폴더에 저장됩니다.

## 현재 구현된 기능

- Windows 알림 영역(시스템 트레이) 아이콘과 둥근 WPF 팝업
- Codex, Gemini CLI, Cursor, GitHub Copilot CLI 로컬 사용량 집계
- 오늘·이번 주·이번 달 토큰 표시와 자동/수동 새로고침
- 알 부화, 실제 진화 계보 기반 성장, 분기 진화, 성격과 이로치
- Gen 1–5 포켓몬 도감과 개체별 포획 기록
- 부화·진화·졸업 애니메이션 및 Windows 트레이 알림
- 클릭으로 본창을 열고 닫을 수 있는 드래그 가능한 플로팅 펫
- 토큰 지갑, 상점, 가방, 이상한 사탕, 민트, 이로치 부적, 등급별 알
- 알림, 항상 위, 새로고침 간격, Windows 로그인 시 자동 실행 설정
- 버전이 지정된 JSON 세이브 내보내기/가져오기와 복구 백업
- 단일 실행 인스턴스와 사용자별 데이터·캐시·로그 저장

처음 발견한 각 공급자의 누적 사용량은 기준값으로만 저장됩니다. 설치 전에 사용한
토큰을 소급해 성장시키지 않으며, 그 뒤에 새로 증가한 토큰부터 포켓몬에게 반영됩니다.

## 지원 도구와 로컬 데이터

| 도구 | 기본 검색 위치 | 형식 |
| --- | --- | --- |
| Codex | `%USERPROFILE%\.codex\sessions`, `archived_sessions` | JSONL |
| Gemini CLI | `%USERPROFILE%\.gemini\tmp` | JSON / JSONL |
| Cursor | `%APPDATA%\Cursor\User\globalStorage` 및 Nightly 경로 | SQLite |
| GitHub Copilot CLI | `%USERPROFILE%\.copilot` 또는 `COPILOT_HOME` | SQLite |

사용량 파서는 토큰 메타데이터만 집계합니다. 프롬프트와 응답 본문을 앱 화면에
표시하거나 별도로 업로드하지 않습니다.

## 설치

### 요구 사항

- Windows 10 이상, x64
- 소스에서 빌드하거나 설치 스크립트를 실행할 때 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

PowerShell에서 저장소 루트를 연 뒤 다음 명령을 실행합니다.

```powershell
.\Windows\install.ps1
```

스크립트는 self-contained `win-x64` 실행 파일을 만들고 다음 작업을 수행합니다.

- `%LOCALAPPDATA%\Programs\PokeTokenBar`에 설치
- 바탕화면과 시작 메뉴에 `PokeTokenBar` 바로가기 생성
- 기존 `%LOCALAPPDATA%\PokeTokenBar` 진행 상황 유지

이후에는 PowerShell 명령 없이 바로가기로 실행할 수 있습니다. 새 로컬 빌드로
교체하려면 같은 설치 명령을 다시 실행하면 됩니다.

> 아직 코드 서명된 설치 패키지나 GitHub Releases용 배포 파일은 제공하지 않습니다.
> 현재 설치 방식은 개발 저장소에서 실행하는 로컬 설치 스크립트입니다.

## 개발과 테스트

```powershell
dotnet build .\Windows\PokeTokenBar.Windows.sln -c Release
dotnet test .\Windows\PokeTokenBar.Windows.sln -c Release
```

개발 상태를 설치본과 분리해 실행하려면 다음 스크립트를 사용합니다.

```powershell
.\Windows\run-dev.ps1
```

이 명령은 Git에서 제외된 저장소 로컬 `.ptb-data` 폴더를 사용합니다. `PTB_DATA_DIR`
환경 변수로 다른 데이터 폴더를 지정할 수도 있습니다.

## 데이터와 네트워크

- 설치본 데이터: `%LOCALAPPDATA%\PokeTokenBar`
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
├─ src/PokeTokenBar.Windows/            # WPF 애플리케이션과 화면
├─ tests/PokeTokenBar.Core.Tests/       # Windows 포팅 계약/회귀 테스트
├─ install.ps1                          # self-contained 설치 및 바로가기 생성
└─ PORTING_STATUS.md                    # 기능별 포팅 현황과 남은 작업
```

자세한 Windows 구현 내용은 [`Windows/README.md`](Windows/README.md), 기능별 진행
상태는 [`Windows/PORTING_STATUS.md`](Windows/PORTING_STATUS.md)에서 확인할 수 있습니다.

## Swift/macOS 소스가 아직 남아 있는 이유

루트의 `Sources`, `Tests`, `Package.swift`와 일부 macOS용 스크립트·아이콘은 원작의
동작을 비교하기 위한 참고 자료입니다. 이 파일들은 Windows 솔루션에서 컴파일되거나
실행되지 않으므로 GitHub 언어 통계에 Swift가 많이 표시되어도 Windows 앱이 Swift에
의존한다는 뜻은 아닙니다.

남은 Windows 기능의 범위를 확정하고 필요한 동작과 테스트를 모두 C#으로 옮긴 뒤,
macOS 전용 소스는 마지막 정리 단계에서 제거할 예정입니다. Swift 파일을 줄 단위로
기계 번역하는 방식은 사용하지 않습니다.

## 아직 남은 작업

- 코드 서명과 정식 설치 패키지
- 서명된 배포 채널을 전제로 한 업데이트 확인
- Windows UI 다국어 지원(현재 한국어 중심)
- 필요성이 확인된 추가 사용량 공급자
- 포팅 완료 후 macOS 전용 참조 소스 정리

## 원작과 라이선스

이 포팅은 [chattymin/PokeTokenBar](https://github.com/chattymin/PokeTokenBar)를
기반으로 합니다. 원작과 이 저장소의 자체 소스 코드는 [`LICENSE`](LICENSE)의 MIT
라이선스를 따릅니다.

PokeTokenBar는 비공식·비상업적 팬 프로젝트이며 Nintendo, Game Freak, Creatures Inc.
또는 The Pokémon Company와 제휴·후원·승인 관계가 없습니다. Pokémon 관련 명칭,
캐릭터, 이미지 및 데이터의 권리는 각 권리자에게 있습니다.
