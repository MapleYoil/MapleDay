"""Check extracted game UI geometry and provenance without launching the app."""
from pathlib import Path
import hashlib
import json
import re
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'src/MapleDay/Assets/Equipment/UIEquip'
manifest = json.loads((ASSETS / 'manifest.json').read_text(encoding='utf-8'))
entries = {Path(row['file']).stem: row for row in manifest}
assert len(entries) == len(manifest), 'Duplicate asset names'
for row in manifest:
    assert re.fullmatch(r'[A-Za-z0-9_-]+\.png', row['file']), row['file']
    path = ASSETS / row['file']
    assert hashlib.sha256(path.read_bytes()).hexdigest() == row['sha256'], path
    with Image.open(path) as image:
        assert image.size == (row['width'], row['height']), path
    assert row['source'].startswith('UI/UIEquip.img/'), row['source']

tooltip_root = ASSETS.parent / 'UIToolTip_Item_Equip'
tooltip_rows = json.loads((tooltip_root / 'manifest.json').read_text(encoding='utf-8'))
tooltip_names = {Path(row['file']).stem for row in tooltip_rows}
for row in tooltip_rows:
    path = tooltip_root / row['file']
    assert re.fullmatch(r'[A-Za-z0-9_-]+\.png', row['file'])
    assert hashlib.sha256(path.read_bytes()).hexdigest() == row['sha256']
    with Image.open(path) as image:
        assert image.size == (row['width'], row['height'])
    assert row['source'].startswith('UI/UIToolTip.img/Item/Equip/')
for name in ['frame_common_top', 'frame_common_mid', 'frame_common_btm', 'frame_common_line', 'textIcon_starForce_enhanced', 'itemIcon_base', 'imgFont_atkPow_equipped']:
    assert name in tooltip_names
fonts = json.loads((tooltip_root / 'fonts.json').read_text(encoding='utf-8'))
assert fonts['common/itemName']['family'] == '돋움'
assert fonts['common/itemName']['size'] == 14 and fonts['common/itemName']['bold'] == 1
assert fonts['common/normal']['family'] == 'MD모리스9'
assert fonts['common/normal']['size'] == 12 and fonts['common/normal']['bold'] == 0
for entry in fonts.values():
    assert entry['source'].startswith('UI/UIToolTip.img/Item/Equip/font/')
for kind in ['potential', 'additionalPotential']:
    for grade in ['normal', 'rare', 'epic', 'unique', 'legendary']:
        assert f'textIcon_{kind}_title_{grade}' in tooltip_names

slots = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12', '13', '15', '16', '17', '21', '22', '23', '26', '27', '28', '29', '30', '31', 'title']
for slot in slots:
    for prefix in ['Equip_EquipTab_Slots_', 'Equip_EquipTab_SlotName_']:
        assert prefix + slot in entries, prefix + slot
    item = entries['Equip_EquipTab_Slots_' + slot]
    assert 0 <= -item['originX'] <= 366 - item['width']
    assert 0 <= -item['originY'] <= 444 - item['height']
for kind in ['ArcEquip', 'AutEquip', 'GrandAutEquip']:
    for name in ['backgrnd', 'box', 'disablebox', 'textUp_force', 'textDown_v']:
        assert f'Equip_Symbol_{kind}_{name}' in entries
for prefix in ['Equip_EquipTab', 'Deco_CoordiPreset']:
    for number in range(1, 4):
        for state in ['normal', 'checked']:
            assert f'{prefix}_button_preset{number}_{state}_0' in entries
for button in ['Equip_EquipTab_button_symbol', 'Equip_EquipTab_button_DecoUI', 'Deco_CoordiTab_button_EquipUI', 'Deco_CoordiTab_button_CoordiPreset']:
    normal = entries[f'{button}_normal_0']
    for state in ['mouseOver', 'pressed', 'disabled']:
        row = entries[f'{button}_{state}_0']
        assert (row['width'], row['height']) == (normal['width'], normal['height']), button
        # A pressed image can move by one pixel; the control preserves each original origin.
        assert abs(row['originX'] - normal['originX']) <= 2 and abs(row['originY'] - normal['originY']) <= 2, button
    assert entries[f'{button}_mouseOver_0']['sha256'] != normal['sha256'], button

# Static resource assembly for visual QA. No app process or computer interaction.
canvas = Image.new('RGBA', (610, 444), '#16222d')
def paste(name, x=None, y=None):
    row = entries[name]
    with Image.open(ASSETS / row['file']) as image:
        canvas.alpha_composite(image.convert('RGBA'), (x if x is not None else -row['originX'], y if y is not None else -row['originY']))
paste('Equip_main_backgrnd')
paste('Equip_main_tab_detailTab_selected_0')
paste('Equip_main_tab_detailTab_normal_1')
paste('Equip_EquipTab_canvas_equip')
canvas.alpha_composite(Image.new('RGBA', (132, 177), 'white'), (117, 100))
for slot in slots: paste('Equip_EquipTab_SlotName_' + slot, -entries['Equip_EquipTab_Slots_' + slot]['originX'], -entries['Equip_EquipTab_Slots_' + slot]['originY'])
for number in range(1, 4): paste(f'Equip_EquipTab_button_preset{number}_normal_0')
paste('Equip_EquipTab_button_DecoUI_normal_0')
paste('Equip_EquipTab_button_symbol_normal_0')
paste('Equip_Symbol_backgrnd', 374, 53)
paste('Equip_Symbol_tab_categoryTab_normal_0', 396, 65)
paste('Equip_Symbol_tab_categoryTab_selected_1', 488, 65)
paste('Equip_Symbol_GrandAutEquip_backgrnd', 396, 99)
for i in range(3): paste('Equip_Symbol_GrandAutEquip_disablebox', 416 + 50 * i, 247)
output = ROOT / 'artifacts/build/equipment-assets-preview.png'
output.parent.mkdir(parents=True, exist_ok=True)
canvas.save(output)
print(f'Validated {len(manifest)} game UI assets; {output}')
