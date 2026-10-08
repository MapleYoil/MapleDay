# 사용자 작업 규칙

- 사용자 요청(2026-10-08): 사용자가 직접 테스트를 완료했다고 확인하기 전에는 자동 배포하지 않는다. 로컬 EXE·MSIX·설치기 빌드와 자동 검증까지만 진행하며, GitHub 릴리즈·자동 업데이트 채널·웹/서버 배포는 보류한다. 사용자가 명시적으로 배포를 요청하면 해당 변경사항을 배포한다.
- 테스트에 Computer Use나 Windows 화면 조작을 사용하지 않는다. 앱을 전면에 띄워 조작하거나 전체 화면을 제어하지 않는다.
- 검증은 빌드, 자동 테스트, API 요청, 파일 및 로그 검사로 수행한다.
- 실행 가능한 테스트·배포 빌드는 scripts/Publish.ps1로 artifacts/MapleDay 한 폴더에 덮어쓴다. 별도의 MapleDay-* 실행 폴더를 만들지 않는다.
- 배포 폴더는 실행용 MapleDay.exe와 App 폴더로 구성한다. 앱 본체와 DLL·언어 폴더·에셋·런타임은 App 내부에 둔다. Windows Hidden/System 속성으로 숨기지 않는다.
- scripts/Publish.ps1은 실행 폴더와 함께 artifacts/MSIX/MapleDay.msix도 항상 생성·서명·검증한다. 제작자는 MapleYoil이며 MSIX 식별자와 퍼블리셔 표시 이름(메요일)은 Partner Center의 할당 값과 일치해야 한다. 스토어용 버전의 마지막 자리는 항상 0이다. 인증서 개인키를 내보내거나 인증서를 자동 신뢰 설치하지 않는다.
