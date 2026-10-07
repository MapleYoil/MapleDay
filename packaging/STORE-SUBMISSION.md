# Microsoft Store 제출

- Store ID: `9N133XQDNS1F`
- Package/Identity/Name: `ABB731EC.2576917430647`
- Package/Identity/Publisher: `CN=6E3CA1EF-722B-49DC-A96F-96F95E7B9BB8`
- Package/Properties/PublisherDisplayName: `메요일`
- Package/Properties/DisplayName 및 설치된 앱 표시 이름: `메요일`
- Package Family Name: `ABB731EC.2576917430647_mfxmmx6cxb3k4`
- 앱 제작자: `MapleYoil`

`MapleDay.msix`를 업로드합니다. 버전의 네 번째 숫자는 항상 0으로 유지하며, 빌드마다 세 번째 숫자를 올립니다. 퍼블리셔 표시 이름은 계정의 할당 값과 일치해야 하므로 제작자 정보와 별도로 유지합니다.

## runFullTrust 용도 설명

Partner Center의 제한된 기능 사용 설명 칸에 다음 내용을 입력할 수 있습니다. `runFullTrust`는 WinUI 3 데스크톱 프로세스에 필요하므로 패키지에 유지하며, 승인 여부는 Microsoft 심사에 따릅니다.

> MapleDay is a WinUI 3 desktop utility for MapleStory. The runFullTrust capability is required to run its Win32 desktop process, provide a notification-area tray icon, and activate the app through its COM Windows notification handler. Users enter their own Nexon Open API key to load game data. The app stores settings and cached data locally and displays task reminders and support replies through Windows notifications. It runs with standard user permissions and does not require administrator elevation or modify game files.

Store에 제출할 때 개발용 공개 인증서를 설치할 필요는 없습니다. 직접 MSIX를 설치하는 테스트 PC에서만 `INSTALL.txt`의 인증서 등록 안내를 따릅니다. Store는 인증 후 패키지를 다시 서명합니다.

참고: [패키지 요구 사항](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements), [제한된 기능 선언](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations).
