"""Check API difficulty badges and the short film's scene schedule offline."""
import json
from PIL import ImageChops
import render_intro as film

assert film.DURATION == 13.5 and film.FPS == 60
assert film.SCENES == [3, 7, 2, 4, 5, 6]
assert film.STARTS == [0, 3, 6, 7.5, 9.5, 11.5]
assert ImageChops.difference(film.frame(0), film.income_hero(0).convert('RGB')).getbbox() is None
assert 'characters' not in film.SCREENS
# No real nickname may affect any of the composed screens, including moving notices.
original_names = [character['Name'] for character in film.CHARS]
screens = [film.level_page, film.notice_card, film.notifications_page, film.characters_page]
before = [screen() for screen in screens]
try:
    for index, character in enumerate(film.CHARS):
        character['Name'] = f'PRIVATE_NICKNAME_{index}_MUST_NOT_RENDER'
    for expected, screen in zip(before, screens):
        assert ImageChops.difference(expected, screen()).getbbox(alpha_only=False) is None
finally:
    for character, name in zip(film.CHARS, original_names):
        character['Name'] = name
assert ImageChops.difference(film.header('CONTENTS').crop((5, 5, 140, 31)),
                            film.ui('main_entity_back_contents').crop((5, 5, 140, 31))).getbbox(alpha_only=False) is None
assert ImageChops.difference(film.header('WEEKLY', weekly=True).crop((5, 5, 140, 31)),
                            film.ui('main_entity_back_weekly').crop((5, 5, 140, 31))).getbbox(alpha_only=False) is None
weekly = [entry for entry in film.CHARS[0]['Entries']
          if entry.get('Cycle') in ('bossWeekly', 'weekly')]
assert weekly and weekly[0]['Difficulty'] == 'extreme'
for entry in weekly:
    badge = film.asset('Scheduler/Difficulty/' + entry['Difficulty'] + '.png')
    expected = film.ui('main_entity_back_listBoss').copy()
    expected.alpha_composite(badge, (53, 9))
    actual = film.scheduler_row(dict(entry, Complete=False), boss=True)
    box = (53, 9, 53 + badge.width, 9 + badge.height)
    assert ImageChops.difference(actual.crop(box), expected.crop(box)).getbbox(alpha_only=False) is None
for korean, english in [('이지', 'easy'), ('노멀', 'normal'), ('하드', 'hard'),
                        ('카오스', 'chaos'), ('익스트림', 'extreme')]:
    actual = film.scheduler_row(dict(weekly[0], Difficulty=korean, Complete=False), True)
    expected = film.scheduler_row(dict(weekly[0], Difficulty=english, Complete=False), True)
    assert ImageChops.difference(actual, expected).getbbox(alpha_only=False) is None
try:
    film.scheduler_row(dict(weekly[0], Difficulty='unknown'), True)
except ValueError:
    pass
else:
    raise AssertionError('An unknown difficulty must not silently become normal.')
print('Verified: masked nicknames on all screens, every weekly API badge, five bilingual difficulties, 1.5-second scheduler, 3-second boss income, 3-second hunting income, 13.5 seconds at 60 fps.')

plan = json.loads((film.HERE / 'demo-income-plan.json').read_text(encoding='utf-8'))
market = json.loads((film.HERE / 'market-prices.json').read_text(encoding='utf-8'))
prices = {(str(row['itemId']), row['variant']): row['priceEok'] * 100000000
          for row in market['prices'] if row['region'] == 'normal' and row['priceEok'] > 0}
unit = {row['Name']: row for row in plan['unitPrices']}
for item in unit.values():
    assert item['unitMeso'] == prices[(item['Id'], item['variant'])]
loot_total = 0
seen = set()
for clear in plan['settlements']:
    for item in clear['Loot']:
        assert item['Meso'] == unit[item['Name']]['unitMeso']
        loot_total += item['Meso']
        seen.add(item['Name'])
assert seen == set(unit)
assert loot_total + plan['crystalIncome'] == plan['totalMeso']
assert film.income_timeline()[-1]['Total'] == plan['totalMeso']
print('Verified actual whole-eok unit prices, eligible loot quantities, and unadjusted total:', plan['totalMeso'])

hunt = json.loads((film.HERE / 'demo-hunting-plan.json').read_text(encoding='utf-8'))
fragment = next(row for row in json.loads((film.HERE / 'fragment-prices.json').read_text(encoding='utf-8')) if row['region'] == 'normal')
assert hunt['unitMeso'] == fragment['priceMillion'] * 1000000
assert hunt['hours'] == 1000 and hunt['hoursPerLimit'] == 5 and hunt['days'] == 200
assert hunt['level'] == 300 and hunt['bonusPercent'] == 280 and hunt['fragments'] == 30000
assert hunt['mesoIncome'] == 230000000 * 38 // 10 * 200
assert hunt['totalMeso'] == hunt['mesoIncome'] + hunt['unitMeso'] * 30000
assert film.income_timeline(True)[-1]['Total'] == hunt['totalMeso']
print('Verified 1,000 hours hunting, 300-level cap, +280% mesos and actual fragment unit price.')
