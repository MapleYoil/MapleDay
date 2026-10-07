"""Rebuild the dated price catalog from reviewed Nexon patch tables.

Run manually after reviewing the cited announcements; not part of publishing.
Only weekly/monthly crystals are included (the scheduler API starts 2026-06-25).
"""
import json
import re
import urllib.request
from urllib.parse import quote
from pathlib import Path
from bs4 import BeautifulSoup

DIFFICULTIES = {"이지": "easy", "노멀": "normal", "하드": "hard", "카오스": "chaos", "익스트림": "extreme"}
JUNE = "https://maplestory.nexon.com/News/Update/806"
SEPTEMBER = "https://maplestory.nexon.com/news/update/813"

def rows(url, marker):
    html = urllib.request.urlopen(url).read().decode("utf-8")
    soup = BeautifulSoup(html, "html.parser")
    table = next(t for t in soup.find_all("table") if marker in t.get_text())
    result = []
    for row in table.find_all("tr")[1:]:
        cells = [c.get_text(" ", strip=True) for c in row.find_all("td")]
        name, difficulty = re.fullmatch(r"(.+?)\s*\((.+?)\)", cells[0]).groups()
        result.append((name.strip(), DIFFICULTIES[difficulty], int(cells[1].replace(",", "")), int(cells[2].replace(",", ""))))
    return result

entries = {}
def add(name, difficulty, date, meso, source):
    entries[(name.replace(" ", ""), difficulty, date)] = dict(Name=name, Difficulty=difficulty, EffectiveFrom=date, Meso=meso, Source=source)

june = rows(JUNE, "665,000,000")
september = rows(SEPTEMBER, "4,040,000")
assert len(june) >= 45 and len(september) >= 40
daily = {("반레온", "hard"), ("아카이럼", "normal"), ("매그너스", "normal"), ("파풀라투스", "normal"), ("힐라", "hard"), ("핑크빈", "chaos"), ("시그너스", "normal")}
for name, difficulty, old, new in june:
    if (name.replace(" ", ""), difficulty) in daily:
        continue
    add(name, difficulty, "2026-06-18", old if name.replace(" ", "") == "검은마법사" else new, JUNE)
    if name.replace(" ", "") == "검은마법사":
        add(name, difficulty, "2026-07-01", new, JUNE)
for name, difficulty, old, new in september:
    key = (name.replace(" ", ""), difficulty)
    if not any((n, d) == key for n, d, date in entries):
        add(name, difficulty, "2026-08-20" if name == "벨로나" else "2026-06-18", old, SEPTEMBER)
    add(name, difficulty, "2026-10-01" if key[0] == "검은마법사" else "2026-09-17", new, SEPTEMBER)
# Unchanged Bellona hard price, cross-checked against the wiki mirror and current
# price table. Nexon's August announcement establishes its release date.
add("벨로나", "hard", "2026-08-20", 2_950_000_000,
    "https://namu.moe/w/" + quote("강렬한 힘의 결정"))
output = Path(__file__).resolve().parents[1] / "src/MapleDay.Core/CrystalPrices.json"
output.write_text(json.dumps(sorted(entries.values(), key=lambda e: (e["Name"], e["Difficulty"], e["EffectiveFrom"])), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Saved {len(entries)} dated prices")
