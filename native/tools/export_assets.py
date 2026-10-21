"""Export original Relicade identity and simple UI glyphs (Pillow + rsvg-convert)."""
from io import BytesIO
from pathlib import Path
import subprocess

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
NATIVE = ROOT / 'native'
ASSETS = NATIVE / 'Assets'


def svg(name, width):
    data = subprocess.check_output(['rsvg-convert', '-w', str(width), str(ROOT / 'brand' / name)])
    return Image.open(BytesIO(data)).convert('RGBA')


def glyph(name, paths=(), rectangles=(), circles=()):
    image = Image.new('RGBA', (96, 96))
    draw = ImageDraw.Draw(image)
    for path in paths:
        draw.line(path, fill='#F4F0E6', width=6, joint='curve')
    for rect in rectangles:
        draw.rectangle(rect, outline='#F4F0E6', width=6)
    for circle in circles:
        draw.ellipse(circle, outline='#F4F0E6', width=6)
    target = ASSETS / name
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target)


def main():
    ASSETS.mkdir(exist_ok=True)
    icon = svg('app-icon.svg', 256)
    for name in ['app.png', 'relicade-icon.png']:
        icon.save(ASSETS / name)
    icon.save(NATIVE / 'icon.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    svg('wordmark.svg', 718).save(ASSETS / 'wordmark.png')
    svg('wordmark-tail.svg', 606).save(ASSETS / 'wordmark-tail.png')
    glyph('DefaultGame.png', rectangles=[(18, 24, 78, 72)], paths=[[(30, 48), (48, 48)], [(39, 39), (39, 57)]], circles=[(60, 39, 66, 45), (60, 54, 66, 60)])
    glyph('Continue.png', paths=[[(33, 20), (72, 48), (33, 76), (33, 20)]])
    glyph('AppCatalog.png', rectangles=[(12, 12, 39, 39), (57, 12, 84, 39), (12, 57, 39, 84), (57, 57, 84, 84)])
    glyph('Github.png', rectangles=[(12, 12, 84, 84)], paths=[[(36, 30), (22, 48), (36, 66)], [(60, 30), (74, 48), (60, 66)]])
    glyph('CheckForUpdates.png', paths=[[(24, 30), (48, 12), (72, 30)], [(48, 12), (48, 66)], [(18, 60), (18, 84), (78, 84), (78, 60)]])
    glyph('Settings.png', circles=[(18, 18, 78, 78), (36, 36, 60, 60)])
    glyph('Icons/button_minimize.png', paths=[[(20, 65), (76, 65)]])
    glyph('Icons/button_fullscreen.png', rectangles=[(20, 20, 76, 76)])
    glyph('Icons/button_close.png', paths=[[(20, 20), (76, 76)], [(20, 76), (76, 20)]])
    glyph('Icons/button_download.png', paths=[[(48, 12), (48, 66)], [(24, 42), (48, 66), (72, 42)], [(18, 72), (18, 84), (78, 84), (78, 72)]])
    glyph('Icons/button_launch.png', paths=[[(33, 20), (72, 48), (33, 76), (33, 20)]])
    glyph('Icons/button_update.png', paths=[[(24, 30), (48, 12), (72, 30)], [(48, 12), (48, 78)]])
    glyph('Icons/button_loading.png', circles=[(18, 18, 78, 78)], paths=[[(48, 24), (48, 48), (66, 48)]])
    for name in ['lin', 'win', 'mac']:
        # Generic device symbol, not a platform vendor's mark; adjacent text supplies the OS name.
        glyph(f'Icons/platform_{name}.png', rectangles=[(12, 18, 84, 66)], paths=[[(48, 66), (48, 84)], [(30, 84), (66, 84)]])
    for name, boxes in {'square': [(18, 18, 78, 78)], 'square_alt': [(18, 18, 78, 78)], 'portrait': [(30, 12, 66, 84)], 'landscape': [(12, 30, 84, 66)], 'list': [(12, 18, 84, 36), (12, 60, 84, 78)]}.items():
        glyph(f'Icons/preset_{name}.png', rectangles=boxes)
    for name in ['info', 'tip', 'important', 'warning', 'caution']:
        glyph(f'markdown_{name}.png', circles=[(12, 12, 84, 84)], paths=[[(48, 24), (48, 54)], [(48, 66), (48, 72)]])
    resources = NATIVE / 'Relicade.Android' / 'Resources'
    for dpi, size in [('mdpi', 48), ('hdpi', 72), ('xhdpi', 96), ('xxhdpi', 144), ('xxxhdpi', 192)]:
        target = resources / f'mipmap-{dpi}'
        target.mkdir(parents=True, exist_ok=True)
        for name in ['ic_launcher.png', 'ic_launcher_round.png']:
            icon.resize((size, size), Image.Resampling.LANCZOS).save(target / name)
    (resources / 'drawable').mkdir(exist_ok=True)
    icon.save(resources / 'drawable/ic_launcher_foreground.png')
    print('Exported original brand masters and original geometric UI glyphs.')


if __name__ == '__main__':
    main()
