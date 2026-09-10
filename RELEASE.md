# Windows 릴리스 절차

PokeTokenBar Windows 포팅판은 `Windows/Directory.Build.props`의 버전을 단일 기준으로
사용합니다. `vMAJOR.MINOR.PATCH` 태그가 같은 버전의 커밋을 가리킬 때 GitHub Actions가
빌드, 테스트, 패키징과 GitHub Release 생성을 수행합니다.

## 로컬 산출물 만들기

Portable ZIP만 만들려면 다음 명령을 사용합니다.

```powershell
.\Windows\publish-release.ps1 -SkipInstaller
```

Inno Setup 6 또는 7이 설치되어 있다면 Installer까지 함께 만들어집니다.

```powershell
.\Windows\publish-release.ps1 -RequireInstaller
```

필요하면 `ISCC_PATH` 환경 변수로 `ISCC.exe` 위치를 지정할 수 있습니다. 산출물은
`Windows/artifacts/release/<version>`에 생성되며 Git에서 제외됩니다.

- `PokeTokenBar-<version>-win-x64.zip`
- `PokeTokenBar-<version>-win-x64-setup.exe`
- `SHA256SUMS.txt`
- `release-manifest.json`

## 새 버전 배포

1. `Windows/Directory.Build.props`의 `VersionPrefix`, `AssemblyVersion`, `FileVersion`을
   같은 새 버전으로 변경합니다.
2. Release 빌드와 전체 테스트를 순차 실행하고 아래 릴리스 체크리스트를 확인합니다.
3. 변경을 커밋하고 `main`에 푸시합니다.
4. 해당 커밋에 버전 태그를 만들고 푸시합니다.

```powershell
[xml]$releaseProps = Get-Content -LiteralPath .\Windows\Directory.Build.props
$releaseTag = 'v' + [string]$releaseProps.Project.PropertyGroup.VersionPrefix
git tag $releaseTag
git push origin $releaseTag
```

태그와 프로젝트 버전이 일치하지 않거나 태그 형식이 잘못되면 Release workflow는
배포 전에 실패합니다. 성공하면 GitHub Release에 Installer, Portable ZIP, 체크섬,
manifest가 첨부됩니다.

위 명령은 실제 배포를 시작합니다. 버전 수정과 커밋·푸시를 끝낸 뒤 의도한
릴리스 커밋에서 실행합니다. 로컬 산출물 생성만으로 태그나 GitHub Release가
생기지는 않습니다.

## 설치와 제거 정책

- 설치 대상: `%LOCALAPPDATA%\Programs\PokeTokenBar`
- 시작 메뉴 바로가기 생성
- 바탕화면 바로가기는 선택 사항이며 기본 해제
- 관리자 권한을 요구하지 않는 현재 사용자 설치
- 제거 시 로그인 자동 실행 레지스트리 값 제거
- 사용자 데이터: `%USERPROFILE%\.poketokenbar` (v0.5.2부터)
- 최초 실행 시 기존 `%LOCALAPPDATA%\PokeTokenBar`를 복사하고 원본 보존
- 제거·업데이트 시 두 데이터 위치 모두 보존

## 릴리스 전 수동 확인

먼저 설치·업데이트·제거 과정에서 핵심 사용자 파일의 SHA-256이 유지되는지 자동으로
검사할 수 있습니다. 이 스크립트는 실행 중인 앱을 종료하고 설치·업데이트·제거·
재설치를 수행합니다. 아래 명령은 마지막에 앱을 실행하여 로그인 자동 실행 설정이
켜져 있으면 시작프로그램 등록도 복구하도록 합니다.

```powershell
.\Windows\test-installer.ps1 -InstallerPath .\Windows\artifacts\release\<version>\PokeTokenBar-<version>-win-x64-setup.exe -LaunchAfter
```

`<version>`은 검증할 산출물 버전으로 바꿉니다. 스크립트의 기본 경로는 0.2.0이므로
다른 버전을 검사할 때는 `-InstallerPath`를 반드시 지정합니다.
아래 목록은 새 릴리스마다 반복하는 확인 항목입니다. 이전 검증 결과는
[`Windows/PORTING_STATUS.md`](Windows/PORTING_STATUS.md)에 기록합니다.

- [ ] 새 설치 후 앱과 트레이 아이콘이 실행되는가
- [ ] 앱 실행 중 Installer를 열면 먼저 PokeTokenBar 종료 안내가 나타나는가
- [ ] 기존 버전 위에 설치해도 포켓몬 진행 상황이 유지되는가
- [ ] 시작 메뉴 바로가기가 정상이며 바탕화면 바로가기는 선택했을 때만 생성되는가
- [ ] 제거 후 프로그램 파일과 바로가기는 사라지는가
- [ ] 제거 후 `%USERPROFILE%\.poketokenbar` 및 기존 AppData 데이터는 남아 있는가
- [ ] 일반 실행과 Codex 등 패키지 앱에서 실행할 때 같은 실제 저장 위치와 진행을 사용하는가
- [ ] 기존 AppData 이전 실패 시 부분 저장소나 새 게임을 만들지 않는가
- [ ] Portable ZIP을 별도 폴더에서 실행할 수 있는가
- [ ] `SHA256SUMS.txt`의 해시가 실제 파일과 일치하는가

## 아직 자동화하지 않은 항목

- Authenticode 코드 서명
- 서명 인증서와 timestamp 서버 설정
- 앱 내부 업데이트 확인 및 다운로드

서명은 인증서와 배포 정책이 결정된 뒤 Release workflow에 별도 단계로 추가합니다.
