# PokeTokenBar for Windows 0.5.3

## 추가 사항

- Higgsfield 공식 CLI 계정의 크레딧 사용량을 TODAY, WEEK, MONTH에 표시합니다.
- Higgsfield 1 credit을 650,000 포켓몬 성장 토큰으로 환산합니다.
- 실제 `spend` 거래만 성장에 반영합니다. `refund`는 이후 사용량에서 먼저 상계하고,
  크레딧 구매·구독 지급은 성장에서 제외합니다.
- TOKENS 화면에서 성장 인정 크레딧, 환산율, 현재 잔액과 플랜을 확인할 수 있습니다.

## 사용 준비

- 공식 [`@higgsfield/cli`](https://github.com/higgsfield-ai/cli)를
  `npm install -g @higgsfield/cli`로 설치하고
  `higgsfield auth login`으로 로그인해야 합니다.
- CLI를 자동으로 찾지 못하면 `PTB_HIGGSFIELD_CLI` 환경 변수에 공식 실행 파일의
  전체 경로를 지정하세요.
- 처음 발견한 기존 사용량은 다른 공급자처럼 기준값으로만 저장하며 소급 성장시키지 않습니다.

## 설치와 업데이트

- 실행 중인 앱을 종료하고 `PokeTokenBar-0.5.3-win-x64-setup.exe`를 기존 버전 위에 설치하세요.
- Portable: `PokeTokenBar-0.5.3-win-x64.zip`
- Windows 10 이상 x64용이며 .NET 런타임을 포함합니다.
- 기존 `%USERPROFILE%\.poketokenbar` 진행 상황은 업데이트·제거 중 보존됩니다.
- SHA256SUMS.txt와 release-manifest.json을 함께 제공합니다.

## 검증과 제한

- Release 빌드 0경고·0오류, 회귀 테스트 177개 통과를 확인했습니다.
- 공식 CLI 응답 형식, 소수 크레딧, 페이지 이동, 중복 거래와 환불 상계를 검증했습니다.
- 실제 Higgsfield 계정으로 로그인한 Windows 환경의 라이브 검증은 남아 있습니다.
- 코드 서명과 앱 내 자동 업데이트는 아직 제공하지 않습니다.
