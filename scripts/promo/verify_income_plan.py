"""Check the demonstration against the authenticated price snapshot before publishing."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
source = root / 'artifacts/Promo/source'
catalog = json.loads((root / 'src/MapleDay.Core/BossLootItems.json').read_text(encoding='utf-8-sig'))
market = json.loads((source / 'market-prices.json').read_text(encoding='utf-8-sig'))
plan = json.loads((source / 'demo-income-plan.json').read_text(encoding='utf-8-sig'))
items = {item['Name']: item for item in catalog}
priced = {price['itemId'] for price in market['prices'] if price['region'] == 'normal' and price['priceEok'] > 0}
clears = plan['settlements']
assert len(clears) == plan['totalKills'] == 24
assert len({clear['Boss'] for clear in clears}) == 12
assert plan['targetMeso'] == sum(clear['Meso'] for clear in clears) == 1_000_000_000_000
assert plan['crystalIncome'] + sum(drop['Meso'] for clear in clears for drop in clear['Loot']) == plan['targetMeso']
for clear in clears:
    assert clear['Loot']
    for drop in clear['Loot']:
        item = items[drop['Name']]
        assert item['Id'] in priced, 'Demonstration contains an item without a positive price.'
        assert item['Icon'] == drop['Icon'] and drop['Meso'] >= 0
print('Verified: every demo drop has a positive price; 12 weekly bosses, 24 clears, crystals and loot total exactly 1T.')
