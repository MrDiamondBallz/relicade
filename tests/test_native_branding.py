"""The native name is the established orange R followed by the original ELICADE paths."""
from io import BytesIO
from pathlib import Path
import importlib.util
import subprocess
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SVG = '{http://www.w3.org/2000/svg}'
XAML = '{https://github.com/avaloniaui}'
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
LETTERS = ['letter-e-1', 'letter-l', 'letter-i', 'letter-c', 'letter-a', 'letter-d', 'letter-e-2']


class NativeBrandingTests(unittest.TestCase):
    def test_native_name_has_no_duplicate_initial_r(self):
        root = ET.parse(ROOT / 'native/MainView.axaml').getroot()
        panel = next(n for n in root.iter() if n.get(X + 'Name') == 'SidebarLogoPanel')
        images = list(panel.iter(XAML + 'Image'))
        self.assertEqual([i.get('Source') for i in images], [
            'avares://Relicade/Assets/app.png', 'avares://Relicade/Assets/wordmark-tail.png'])
        self.assertEqual(images[1].get('AutomationProperties.Name'), 'Relicade')
        self.assertEqual(images[1].get('HorizontalAlignment'), 'Left')
        self.assertEqual(panel.get('Spacing'), '0')
        suffix_stack = images[1] in list(panel)[1].iter()
        self.assertTrue(suffix_stack)
        self.assertEqual(list(panel)[1].get('Margin'), '0,16,0,0')

    def test_sidebar_uses_the_established_product_tagline(self):
        root = ET.parse(ROOT / 'native/MainView.axaml').getroot()
        panel = next(n for n in root.iter() if n.get(X + 'Name') == 'SidebarLogoPanel')
        labels = list(panel.iter(XAML + 'TextBlock'))
        self.assertEqual([label.get('Text') for label in labels], ['Old legends. New life.'])

    def test_suffix_preserves_exact_original_glyph_geometry(self):
        master = ET.parse(ROOT / 'brand/wordmark.svg').getroot()
        tail = ET.parse(ROOT / 'brand/wordmark-tail.svg').getroot()
        self.assertEqual(tail.get('viewBox'), '112 0 606 104')
        self.assertEqual(tail.get('fill'), '#F4F0E6')
        self.assertEqual(tail.findtext(SVG + 'title'), 'ELICADE suffix')
        paths = list(tail.iter(SVG + 'path'))
        self.assertEqual([p.get('id') for p in paths], LETTERS)
        self.assertEqual([p.attrib for p in paths], [p.attrib for p in master.iter(SVG + 'path')][1:])
        self.assertFalse(list(tail.iter(SVG + 'text')))
        self.assertEqual(len(list(master.iter(SVG + 'path'))), 8)

    @unittest.skipUnless(importlib.util.find_spec('PIL'), 'Optional image QA dependency not installed')
    def test_native_exports_match_orange_master_and_suffix(self):
        from PIL import Image
        # Exercise the trusted rasterizer, not an assumed dominant-color heuristic.
        icon = Image.open(BytesIO(subprocess.check_output([
            'rsvg-convert', '-w', '256', str(ROOT / 'brand/app-icon.svg')]))).convert('RGBA')
        self.assertIn((255, 102, 61, 255), icon.getdata())
        for name in ['app.png', 'relicade-icon.png']:
            self.assertEqual(Image.open(ROOT / 'native/Assets' / name).convert('RGBA').tobytes(), icon.tobytes())
        ico = Image.open(ROOT / 'native/icon.ico')
        sizes = {(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)}
        self.assertEqual(ico.ico.sizes(), sizes)
        for size in sizes:
            expected = icon if size == (256, 256) else icon.resize(size, Image.Resampling.LANCZOS)
            self.assertEqual(ico.ico.getimage(size).convert('RGBA').tobytes(), expected.tobytes(), size)
        resources = ROOT / 'native/Relicade.Android/Resources'
        for dpi, size in [('mdpi', 48), ('hdpi', 72), ('xhdpi', 96), ('xxhdpi', 144), ('xxxhdpi', 192)]:
            for name in ['ic_launcher.png', 'ic_launcher_round.png']:
                actual = Image.open(resources / f'mipmap-{dpi}' / name).convert('RGBA')
                self.assertEqual(actual.tobytes(), icon.resize((size, size), Image.Resampling.LANCZOS).tobytes())
        self.assertEqual(Image.open(resources / 'drawable/ic_launcher_foreground.png').convert('RGBA').tobytes(), icon.tobytes())
        for name in ['ic_launcher.xml', 'ic_launcher_round.xml']:
            adaptive = ET.parse(resources / 'mipmap-anydpi-v26' / name).getroot()
            self.assertEqual(adaptive.find('foreground/inset').get('{http://schemas.android.com/apk/res/android}drawable'), '@drawable/ic_launcher_foreground')
        tail = Image.open(ROOT / 'native/Assets/wordmark-tail.png').convert('RGBA')
        expected = Image.open(BytesIO(subprocess.check_output([
            'rsvg-convert', '-w', '606', str(ROOT / 'brand/wordmark-tail.svg')]))).convert('RGBA')
        self.assertEqual(tail.size, (606, 104))
        self.assertEqual(tail.tobytes(), expected.tobytes())
        # A complete suffix has seven separated opaque glyph-column runs, never eight.
        active = [any(tail.getpixel((x, y))[3] > 128 for y in range(tail.height)) for x in range(tail.width)]
        self.assertEqual(sum(on and (i == 0 or not active[i-1]) for i, on in enumerate(active)), 7)

    def test_active_icon_bindings_retain_product_identity(self):
        app = (ROOT / 'native/App.axaml.cs').read_text()
        self.assertIn('private static WindowIcon CreateTrayWindowIcon()', app)
        self.assertIn('new WindowIcon(icoPath)', app)
        self.assertIn('avares://Relicade/Assets/app.png', app)
        self.assertIn('avares://Relicade/Assets/app.png', (ROOT / 'native/App.MacMenu.cs').read_text())
        self.assertIn('icon.ico</ApplicationIcon>', (ROOT / 'native/Relicade.Desktop/Relicade.Desktop.csproj').read_text())
        self.assertIn('Title="Relicade"', (ROOT / 'native/MainWindow.axaml').read_text())


if __name__ == '__main__':
    unittest.main()
