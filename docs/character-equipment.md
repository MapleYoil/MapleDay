# 캐릭터 정보

내 캐릭터 아래의 캐릭터 정보 메뉴는 스케줄러 등록 캐릭터만 표시합니다. 캐릭터를 선택하면 목록 대신 상세 화면을 표시하며, 캐릭터 목록 버튼이나 뒤로 가기로 돌아갑니다.

- 장비: 현재 장착 또는 장비 프리셋 1~3을 선택합니다. 반지·펜던트의 슬롯 번호를 유지하고 한벌옷은 상의 위치에 표시합니다. 슬롯에 스타포스·특수 스킬 반지 레벨·잠재능력 등급을 표시합니다. 마우스를 올리면 인게임 프레임·모리스체·색상을 사용한 툴팁에 강화 횟수·남은 횟수·황금망치·기본/추가옵션/주문서/스타포스 능력치·잠재능력·에디셔널·소울을 표시합니다. 클릭하지 않아도 포인터를 올리는 즉시 슬롯 옆에 표시하며 포인터를 따라 이동하고 슬롯에서 벗어나면 닫힙니다. 별도 상세 영역은 만들지 않습니다. Tab으로 슬롯에 초점을 맞추어도 동일한 정보를 표시하며 Esc로 닫습니다.
- 캐시 장비: 현재 코디를 표시합니다. PRESET을 펼치면 오른쪽에서 코디 프리셋 1~3을 비교합니다. API가 제공하는 추가 외형도 별도로 선택할 수 있습니다.
- 심볼: 장비창의 SYMBOL을 누르면 오른쪽 패널을 펼칩니다. ARCANE/AUTHENTIC 탭과 어센틱 패널의 전환 버튼으로 아케인·어센틱·그랜드 어센틱 심볼을 확인합니다. 아르크스/호텔 아르크스 표기를 모두 인식하며 API 배열 순서가 달라도 지역별 슬롯을 유지합니다. 레벨·성장 게이지·MAX를 표시하고 심볼에도 마우스 오버 정보를 제공합니다. 강화 표시는 조회 전용입니다. 좁은 창에서는 패널이 아래로 배치됩니다.

프리셋 선택은 조회만 하며 게임에 적용하지 않습니다. 중앙 캐릭터는 현재 외형이며 뒤 배경은 흰색으로 고정합니다. 펫·안드로이드 코디·데미지 스킨·뷰티룸 편집은 제공하지 않습니다. API에서 누락된 프리셋을 현재 장비로 대체하지 않습니다. 조회 결과와 공개 아이콘은 메모리에서만 사용하며 API 키 변경·캐릭터 해제·데이터 삭제·앱 종료 때 진행 중인 조회를 취소합니다. 각 엔드포인트의 실패는 나머지 결과를 지우지 않습니다.

## 리소스 재생성

설치된 게임의 `UI/UIEquip.img`에서 신형 장비·코디·심볼 창 PNG와 원점을 추출합니다. `WzComparerR2.WzLib.dll`은 기존 도구 경로 `artifacts/tools/WzComparerR2/bin/Lib/`에 필요합니다.

```powershell
dotnet run --project scripts/EquipmentAssetExtractor -- 'C:\Nexon\Maple\Data\UI' UIEquip.img . --export
dotnet run --project scripts/EquipmentAssetExtractor -- 'C:\Nexon\Maple\Data\UI' UIToolTip.img Item/Equip --export
python scripts/PrepareImageCache.py
python scripts/verify_equipment_assets.py
```

출처 경로·크기·원점·SHA-256은 `Assets/Equipment/UIEquip/manifest.json`에 보관합니다. 게임 창은 원본 크기 366×444, 심볼 패널은 228×339로 사용하며 장비 슬롯 위치는 추출 원점을 따릅니다. 변경된 게임 UI를 추출한 뒤 슬롯 배치 검증을 다시 실행해야 합니다. 자동 테스트는 화면 조작 없이 수행합니다.

API 필드는 NEXON Open API의 `character/item-equipment`, `character/cashitem-equipment`, `character/symbol-equipment` 공식 명세에 따릅니다. 현재 장비의 `item_equipment_slot`과 프리셋의 `equipment_slot`을 모두 읽습니다.

장비 툴팁의 글꼴 설정은 `UI/UIToolTip.img/Item/Equip/font`에서 추출한 `fonts.json`을 그대로 읽습니다. 제목은 원본 돋움 14px 굵게, 본문은 함께 제공하는 원본 MD모리스9 12px 보통이며 별도 외곽선·굵기 보정·자간 보정을 적용하지 않습니다. ‘현재 장착 중인 장비’는 원본 이미지입니다. 긴 툴팁도 전체 축소 없이 같은 크기로 유지하며 아이템 위의 마우스 휠이나 키보드 위/아래 키로 스크롤합니다.

심볼·치장·장비 전환·코디 프리셋 펼치기 버튼은 원본 normal/mouseOver/pressed/disabled 이미지를 사용하며 WinUI의 실제 포인터·누름 상태를 따라갑니다.
