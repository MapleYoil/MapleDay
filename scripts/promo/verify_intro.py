"""Check API difficulty badges and the short film's scene schedule offline."""
from PIL import ImageChops
import render_intro as film

assert film.DURATION == 12 and film.FPS == 60
assert film.SCENES == [2, 3, 4, 5, 6]
assert film.STARTS == [0, 3, 6, 8, 10]
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
print('Verified: masked nicknames on all screens, every weekly API badge, five bilingual difficulties, no opening/character scenes, 12 seconds at 60 fps.')
