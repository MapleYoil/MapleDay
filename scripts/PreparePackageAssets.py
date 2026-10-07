"""Resize the existing MapleDay logo for Windows package assets; no new artwork."""
from pathlib import Path
import sys
from PIL import Image

root = Path(__file__).resolve().parents[1]
destination = Path(sys.argv[1]).resolve()
destination.mkdir(parents=True, exist_ok=True)
with Image.open(root / "src/MapleDay/Assets/Branding/logo.png") as source:
    logo = source.convert("RGBA")
    for name, size in [("StoreLogo", 50), ("Square44x44Logo", 44), ("Square150x150Logo", 150)]:
        logo.resize((size, size), Image.Resampling.LANCZOS).save(destination / f"{name}.png")
