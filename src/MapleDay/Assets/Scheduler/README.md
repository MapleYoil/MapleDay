# 스케줄러 아이콘 원본

게임 데이터 `C:\Nexon\Maple\Data\UI`에서 추출한 로컬 PNG입니다.

- 보스·콘텐츠 아이콘 28개: `UI/UIMapleScheduler.img/main/entity/bossIcon/{id}/normal/0`, 25×25px
- 추가 보스 아이콘 8개: `UI/_Canvas/UIBoss.img/BossList/{id}/Icon/normal/0`, 25×25px. 림보(33), 발드릭스(34), 최초의 대적자(35), 카이(36), 찬란한 흉성(37), 유피테르(38), 메이린(40), 벨로나(41)
- 난이도 배지 5개: `UI/UIMapleScheduler.img/main/entity/difficulty/{easy,normal,hard,chaos,extreme}`, 60×18px
- `_outlink`로 연결된 `UI/_Canvas/UIMapleScheduler.img/...`의 실제 픽셀을 사용합니다. 1×1px 링크 자리 이미지는 사용하지 않습니다.
- 확대·축소·재그리기 없이 원본 해상도와 투명도를 유지합니다. 완료 표시의 회색 오버레이와 흰색 체크는 앱에서 별도로 표시합니다.
- `manifest.json`에는 게임 데이터 경로, 이름, 크기, PNG SHA-256을 기록했습니다.

보스 이름은 `UI/UIBoss.img/BossList/{id}/info`와 대응시킵니다. API에서 이름 앞에 붙는 ‘시즌 보스’는 이름 매핑 시 제거하므로 ‘시즌 보스 메이린’도 메이린의 원본 아이콘을 표시합니다. 보스·콘텐츠 아이콘은 총 36개입니다. 우르스(삭제)·해외 보스 3종·크루스는 정보 노드만 있고 이미지가 없어 추출에서 제외했습니다.

추출에는 [WzComparerR2](https://github.com/Kagamia/WzComparerR2)의 `WzComparerR2.WzLib`를 이용했습니다. net8 릴리스 `20261004.1`, 참고 소스 커밋 `621251801cb14a423ad6737d3cdeabb616b1635c`. 추출 CLI는 `artifacts/tools/SchedulerAssetExtractor`에 있으며 게임 데이터는 읽기만 합니다. 앱에는 추출한 PNG와 매니페스트만 포함되므로 실행할 때 WZ 파일이나 추출 도구가 필요하지 않습니다.
