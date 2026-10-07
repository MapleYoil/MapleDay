"""Prepare premultiplied BGRA pixels for synchronous, reusable WinUI images."""
from pathlib import Path
import json
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/MapleDay/Assets"
OUTPUT = ASSETS / "Decoded"
manifest = {}
for folder in ["Scheduler", "Worlds", "Branding"]:
    for path in sorted((ASSETS / folder).rglob("*.png")):
        relative = path.relative_to(ASSETS)
        with Image.open(path) as source:
            image = source.convert("RGBA").convert("RGBa")
            rgba = image.tobytes()
            pixels = bytearray(rgba)
            pixels[0::4], pixels[2::4] = rgba[2::4], rgba[0::4]
            target = OUTPUT / relative.with_suffix(".bgra")
            target.parent.mkdir(parents=True, exist_ok=True)
            if not target.exists() or target.read_bytes() != pixels:
                target.write_bytes(pixels)
            manifest[relative.as_posix()] = {"width": image.width, "height": image.height,
                                             "file": relative.with_suffix(".bgra").as_posix()}
OUTPUT.mkdir(exist_ok=True)
text = json.dumps(manifest, indent=2)
target = OUTPUT / "manifest.json"
if not target.exists() or target.read_text(encoding="utf-8") != text:
    target.write_text(text, encoding="utf-8")
print(f"Prepared {len(manifest)} images for synchronous reuse.")
