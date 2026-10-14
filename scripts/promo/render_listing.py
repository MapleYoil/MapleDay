"""NEXON Open API listing artwork from the existing offline film compositor.

No live desktop capture, API calls, credentials or unmasked character names.
Screens are clearly documented as asset-based presentation reconstructions.
"""
from pathlib import Path
import json
import zipfile
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import render_intro as film

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'docs/media/listing'
QA = ROOT / 'artifacts/Listing'
ASSETS = ROOT / 'src/MapleDay/Assets'
SIZE = (2000, 1000)
COLORS = dict(paper='#F5F7FB', white='#FFFFFF', ink='#152435', muted='#526276',
              blue='#2D6CF3', pale='#E9F0FF', line='#DCE3ED', soft='#7A8898')


def font(size, weight=500):
    result = ImageFont.truetype(str(ASSETS / 'Fonts/PretendardVariable.ttf'), size)
    result.set_variation_by_axes([weight])
    return result


def text(im, xy, value, size, weight=500, color='ink', anchor='lt'):
    if '\n' in value:
        for index, line in enumerate(value.split('\n')):
            text(im, (xy[0], xy[1]+round(index*size*1.3)), line, size, weight, color, anchor)
        return
    ImageDraw.Draw(im).text(xy, value, font=font(size, weight), fill=COLORS.get(color, color), anchor=anchor,
                           spacing=round(size * .22))


def paste(im, source, xy, width=None, pixel=False):
    source = source.convert('RGBA')
    if width:
        source = source.resize((width, round(source.height * width / source.width)),
                               Image.Resampling.NEAREST if pixel else Image.Resampling.LANCZOS)
    im.alpha_composite(source, tuple(map(round, xy)))
    return source.size


def panel(im, source, xy, width, radius=20, pixel=False):
    source = source.convert('RGBA')
    height = round(source.height * width / source.width)
    shadow = Image.new('RGBA', im.size)
    x, y = xy
    ImageDraw.Draw(shadow).rounded_rectangle((x-1, y+10, x+width+1, y+height+10), radius,
                                             fill=(38, 58, 95, 25))
    im.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(18)))
    paste(im, source, xy, width, pixel)


def base(index, category):
    im = Image.new('RGBA', SIZE, COLORS['paper'])
    paste(im, Image.open(ASSETS / 'Branding/logo.png'), (70, 43), 50)
    text(im, (134, 50), '메요일', 29, 740)
    text(im, (237, 58), 'MapleDay', 19, 500, 'muted')
    text(im, (1928, 58), f'{index:02d}  /  {category}', 21, 650, 'muted', anchor='rt')
    ImageDraw.Draw(im).line((72, 118, 1928, 118), fill=COLORS['line'], width=2)
    text(im, (72, 962), 'Data based on NEXON Open API', 17, 450, 'muted')
    text(im, (1928, 962), '실제 에셋 기반 화면 재구성 · 예시 데이터 · 닉네임 비공개', 17, 450, 'muted', anchor='rt')
    return im


def bullet(im, y, title, subtitle):
    ImageDraw.Draw(im).ellipse((76, y+9, 84, y+17), fill=COLORS['blue'])
    text(im, (103, y), title, 28, 700)
    text(im, (103, y+45), subtitle, 22, 450, 'muted')


def scheduler():
    im = base(1, '스케줄러')
    text(im, (72, 185), '오늘의 메이플,\n빠짐없이.', 70, 760)
    text(im, (76, 383), '일일·주간 퀘스트와 보스 진행도를\n캐릭터별로 모아 확인하세요.', 27, 450, 'muted')
    bullet(im, 523, '게임에서 보던 스케줄러', '익숙한 화면으로 확인하는 오늘 할 일')
    bullet(im, 649, '미완료 항목만 알림', 'Windows 알림과 앱 알림함으로 확인')
    bullet(im, 775, '내 일정까지 한곳에', '보스 일정·일반 일정·썬데이 메이플')
    panel(im, film.scheduler_board(), (884, 137), 1016, pixel=True)
    return im


def income():
    im = base(2, '수익 기록')
    text(im, (72, 185), '벌어들인 메소,\n기록으로 남기기.', 62, 760)
    text(im, (76, 371), '보스·사냥·물욕템 수익을\n날짜별 캘린더로 한눈에.', 28, 450, 'muted')
    bullet(im, 508, '아이템 분배까지 정산', '파티 인원·분배 비율·실제 수령액 기록')
    bullet(im, 629, '전체·보스·사냥 따로 보기', '이번 주·이번 달·누적 수익 확인')
    bullet(im, 750, '움직이는 수익 집계', '재생하고 영상·이미지로 내보내기')
    source = film.income_page().crop((235, 165, 1151, 721))
    panel(im, source, (790, 228), 1138)
    text(im, (794, 180), '날짜를 누르면 그날의 기록을 확인할 수 있어요.', 23, 550, 'muted')
    for i, name in enumerate(('1113098', '1113306', 'gold-0')):
        path = ASSETS / ('IncomeReplay/Meso/gold-0.png' if name == 'gold-0' else f'BossLoot/{name}.png')
        if path.is_file():
            source = Image.open(path).convert('RGBA')
            source.thumbnail((46, 46), Image.Resampling.NEAREST)
            paste(im, source, (140+i*67, 866), 42, pixel=True)
    return im


def growth():
    im = base(3, '경험치 · 알림')
    text(im, (72, 185), '쌓이는 경험치,\n보이는 성장.', 66, 760)
    text(im, (76, 381), '최근 7일의 경험치 흐름과\n예상 레벨업 시간을 확인하세요.', 27, 450, 'muted')
    bullet(im, 518, '경험치 히스토리', '일평균 획득량·오늘 올린 경험치 확인')
    bullet(im, 643, '놓치기 쉬운 할 일도', '초기화 전 미완료 퀘스트·보스 알림')
    source = film.level_page().crop((235, 149, 1151, 711))
    panel(im, source, (790, 208), 1138)
    # This source already uses the fixed masked nickname from the film compositor.
    notice = film.notice_card()
    panel(im, notice, (76, 782), 615, radius=15)
    return im


def cover():
    # Render at 2x before the exact recommended upload size to keep the type clean.
    scale = 2
    im = Image.new('RGBA', (502*scale, 267*scale), COLORS['pale'])
    d = ImageDraw.Draw(im)
    d.ellipse((712, -112, 1140, 316), fill='#DFE9FF')
    d.ellipse((800, -38, 1070, 233), fill='#D4E2FF')
    text(im, (66, 58), '메이플스토리 일정 관리 유틸리티', 23, 600, 'muted')
    text(im, (62, 116), '메요일', 114, 800)
    text(im, (68, 260), '할 일부터 수익까지,\n메이플을 한눈에.', 39, 600)
    paste(im, Image.open(ASSETS / 'Branding/logo.png'), (676, 139), 268)
    d.line((68, 430, 936, 430), fill='#C9D7ED', width=2)
    text(im, (68, 463), '스케줄러 · 수익 · 경험치 · 알림', 29, 650, 'blue')
    return im.resize((502, 267), Image.Resampling.LANCZOS)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    QA.mkdir(parents=True, exist_ok=True)
    items = [('00-cover.png', cover(), (502, 267), 1_000_000),
             ('01-scheduler.png', scheduler(), SIZE, 4_000_000),
             ('02-income.png', income(), SIZE, 4_000_000),
             ('03-growth-notifications.png', growth(), SIZE, 4_000_000)]
    metadata = []
    preview = Image.new('RGB', (1200, 2039), '#E9EDF4')
    for i, (name, im, size, limit) in enumerate(items):
        path = OUT / name
        im.convert('RGB').save(path, optimize=True)
        with Image.open(path) as exported:
            assert exported.size == size and exported.format == 'PNG'
        assert path.stat().st_size < limit
        metadata.append({'file': name, 'width': size[0], 'height': size[1], 'bytes': path.stat().st_size,
                         'limitBytes': limit, 'role': '대표 이미지' if i == 0 else '서비스 스크린샷'})
        if i == 0:
            thumb = im.convert('RGB')
            preview.paste(thumb, (349, 22))
        else:
            thumb = im.convert('RGB').resize((1160, 580), Image.Resampling.LANCZOS)
            preview.paste(thumb, (20, 299+(i-1)*580))
    preview.save(QA / 'preview.jpg', quality=93, subsampling=0)
    (QA / 'validation.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding='utf-8')
    bundle = QA / 'MapleDay-main-listing.zip'
    with zipfile.ZipFile(bundle, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, *_ in items: archive.write(OUT / name, name)
        for name in ('description-500.txt', 'registration.txt', 'README.md'):
            if (OUT / name).is_file(): archive.write(OUT / name, name)
    print(json.dumps(metadata, ensure_ascii=False, indent=2))
    print('Bundle:', bundle)


if __name__ == '__main__': main()
