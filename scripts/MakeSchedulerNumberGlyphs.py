"""Render the extracted MDmorris9 face with the game's 12px/fontBold=1 settings.

GDI pixels are saved at their native size so WinUI does not rasterize this pixel
font differently. Requires Windows and Pillow; never opens a window.
"""
from pathlib import Path
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
FONT = ROOT / "src/MapleDay/Assets/Fonts/Morris9.ttf"
OUTPUT = ROOT / "src/MapleDay/Assets/Scheduler/Numbers"
CHARACTERS = "0123456789., /STAGE층점인"
COLORS = {"number": (59, 84, 110), "unit": (116, 134, 150), "white": (255, 255, 255)}
gdi = c.WinDLL("gdi32", use_last_error=True)


def api(name, result, arguments):
    function = getattr(gdi, name)
    function.restype = result
    function.argtypes = arguments
    return function


add_font = api("AddFontResourceExW", c.c_int, [w.LPCWSTR, w.DWORD, c.c_void_p])
remove_font = api("RemoveFontResourceExW", w.BOOL, [w.LPCWSTR, w.DWORD, c.c_void_p])
create_dc = api("CreateCompatibleDC", w.HDC, [w.HDC])
delete_dc = api("DeleteDC", w.BOOL, [w.HDC])
select = api("SelectObject", w.HANDLE, [w.HDC, w.HANDLE])
delete = api("DeleteObject", w.BOOL, [w.HANDLE])
create_font = api("CreateFontW", w.HANDLE, [c.c_int] * 5 + [w.DWORD] * 8 + [w.LPCWSTR])
create_dib = api("CreateDIBSection", w.HBITMAP, [w.HDC, c.c_void_p, w.UINT, c.POINTER(c.c_void_p), w.HANDLE, w.DWORD])
set_color = api("SetTextColor", w.DWORD, [w.HDC, w.DWORD])
set_background = api("SetBkColor", w.DWORD, [w.HDC, w.DWORD])
text_out = api("TextOutW", w.BOOL, [w.HDC, c.c_int, c.c_int, w.LPCWSTR, c.c_int])
get_size = api("GetTextExtentPoint32W", w.BOOL, [w.HDC, w.LPCWSTR, c.c_int, c.c_void_p])
get_face = api("GetTextFaceW", c.c_int, [w.HDC, c.c_int, w.LPWSTR])
flush = api("GdiFlush", w.BOOL, [])


class BitmapInfo(c.Structure):
    _fields_ = [("size", w.DWORD), ("width", w.LONG), ("height", w.LONG),
                ("planes", w.WORD), ("bits", w.WORD), ("compression", w.DWORD),
                ("image", w.DWORD), ("x", w.LONG), ("y", w.LONG),
                ("used", w.DWORD), ("important", w.DWORD)]


OUTPUT.mkdir(parents=True, exist_ok=True)
if not add_font(str(FONT), 0x10, None):
    raise OSError("Could not load extracted game font")
dc = create_dc(None)
font = create_font(-12, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 3, 0, "MDmorris9")
previous_font = select(dc, font)
try:
    family = c.create_unicode_buffer(64)
    get_face(dc, 64, family)
    assert family.value in {"MDmorris9", "MD모리스9"}, family.value
    widths = {}
    for character in CHARACTERS:
        size = (w.LONG * 2)()
        if not get_size(dc, character, 1, size):
            raise OSError("GetTextExtentPoint32W failed")
        width, height = size[0], 18
        widths[f"{ord(character):04x}"] = width
        info = BitmapInfo(40, width, -height, 1, 32, 0, 0, 0, 0, 0, 0)
        bits = c.c_void_p()
        bitmap = create_dib(dc, c.byref(info), 0, c.byref(bits), None, 0)
        if not bitmap:
            raise OSError("CreateDIBSection failed")
        previous_bitmap = select(dc, bitmap)
        try:
            c.memset(bits, 0, width * height * 4)
            set_color(dc, 0xffffff)
            set_background(dc, 0)
            if not text_out(dc, 0, 0, character, 1):
                raise OSError("TextOutW failed")
            flush()
            mask = Image.frombytes("RGB", (width, height), c.string_at(bits, width * height * 4), "raw", "BGRX").getchannel("R")
            for color_name, color in COLORS.items():
                image = Image.new("RGBA", (width, height), (*color, 0))
                image.putalpha(mask)
                image.save(OUTPUT / f"{color_name}_{ord(character):04x}.png")
        finally:
            select(dc, previous_bitmap)
            delete(bitmap)
    manifest = {"source": "Etc/MORIS9.img/FONT_DATA", "fontSha256": hashlib.sha256(FONT.read_bytes()).hexdigest(),
                "family": family.value, "fontSize": 12, "fontBold": 1, "height": 18, "widths": widths}
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Rendered {len(widths)} original game glyphs in three colors, without changing the font file.")
finally:
    select(dc, previous_font)
    delete(font)
    delete_dc(dc)
    remove_font(str(FONT), 0x10, None)
