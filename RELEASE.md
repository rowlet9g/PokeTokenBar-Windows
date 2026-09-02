# Windows 릴리스 절차

PokeTokenBar Windows 포팅판은 `Windows/Directory.Build.props`의 버전을 단일 기준으로
사용합니다. `vMAJOR.MINOR.PATCH` 태그가 같은 버전의 커밋을 가리킬 때 GitHub Actions가
빌드, 테스트, 패키징과 GitHub Release 생성을 수행합니다.

## 로컬 산출물 만들기

Portable ZIP만 만들려면 다음 명령을 사용합니다.

```powershell
.\Windows\publish-release.ps1 -SkipInstaller
```

Inno Setup 7이 설치되어 있다면 Installer까지 함께 만들어집니다.

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
2. Release 빌드와 전체 테스트를 통과시킵니다.
3. 변경을 커밋하고 `main`에 푸시합니다.
4. 해당 커밋에 버전 태그를 만들고 푸시합니다.

```powershell
git tag v0.1.0
git push origin v0.1.0
```

태그와 프로젝트 버전이 일치하지 않거나 태그 형식이 잘못되면 Release workflow는
배포 전에 실패합니다. 성공하면 GitHub Release에 Installer, Portable ZIP, 체크섬,
manifest가 첨부됩니다.

## 설치와 제거 정책

- 설치 대상: `%LOCALAPPDATA%\Programs\PokeTokenBar`
- 시작 메뉴 바로가기 생성
- 바탕화면 바로가기는 선택 사항이며 기본 해제
- 관리자 권한을 요구하지 않는 현재 사용자 설치
- 제거 시 로그인 자동 실행 레지스트리 값 제거
- 제거·업데이트 시 `%LOCALAPPDATA%\PokeTokenBar` 사용자 진행 상황 보존

## 릴리스 전 수동 확인

먼저 설치·업데이트·제거 과정에서 핵심 사용자 파일의 SHA-256이 유지되는지 자동으로
검사할 수 있습니다. 이 스크립트는 검증 후 앱을 다시 설치합니다.

```powershell
.\Windows\test-installer.ps1 -LaunchAfter
```

- [ ] 새 설치 후 앱과 트레이 아이콘이 실행되는가
- [ ] 기존 버전 위에 설치해도 포켓몬 진행 상황이 유지되는가
- [ ] 시작 메뉴 바로가기가 정상이며 바탕화면 바로가기는 선택했을 때만 생성되는가
- [ ] 제거 후 프로그램 파일과 바로가기는 사라지는가
- [ ] 제거 후 `%LOCALAPPDATA%\PokeTokenBar` 데이터는 남아 있는가
- [ ] Portable ZIP을 별도 폴더에서 실행할 수 있는가
- [ ] `SHA256SUMS.txt`의 해시가 실제 파일과 일치하는가

## 아직 자동화하지 않은 항목

- Authenticode 코드 서명
- 서명 인증서와 timestamp 서버 설정
- 앱 내부 업데이트 확인 및 다운로드

서명은 인증서와 배포 정책이 결정된 뒤 Release workflow에 별도 단계로 추가합니다.
