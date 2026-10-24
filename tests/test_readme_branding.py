"""Keep the repository masthead a single accessible Relicade wordmark."""
from pathlib import Path
import importlib.util
import re
import json
import hashlib
from typing import cast
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SVG = '{http://www.w3.org/2000/svg}'
LETTERS = ['letter-r', 'letter-e-1', 'letter-l', 'letter-i', 'letter-c', 'letter-a', 'letter-d', 'letter-e-2']

class ReadmeBrandingTests(unittest.TestCase):
    def test_current_catalog_photo_retains_verified_native_capture_bytes_and_provenance(self):
        name = 'relicade-windows-wide-catalog-9cfa7fc-20261024'
        image = ROOT / 'docs/assets' / (name + '.jpg')
        metadata = json.loads((ROOT / 'docs/assets' / (name + '.json')).read_text())
        self.assertIn('docs/assets/' + name + '.jpg', (ROOT / 'README.md').read_text())
        self.assertEqual(hashlib.sha256(image.read_bytes()).hexdigest(), metadata['sha256'])
        self.assertEqual(metadata['gitBlob'], 'd93b761f122eb21b09d262ecf24842628c78ac63')
        self.assertEqual(metadata['sourceCommit'], '9cfa7fccd5200bb1a8f61c34c67c455b89bbf465')
        self.assertEqual(metadata['platform'], 'Windows x64')
        self.assertFalse(metadata['imageEdited'])
        self.assertFalse(metadata['composited'])
        self.assertFalse(metadata['gameplayAccepted'])
        self.assertFalse(metadata['releaseAccepted'])

    def assets(self):
        readme = (ROOT / 'README.md').read_text()
        picture = re.search(r'<picture>(.*?)</picture>', readme, re.S)
        self.assertIsNotNone(picture)
        assert picture is not None
        paths = re.findall(r'(?:srcset|src)="([^"]+\.svg)"', picture.group(1))
        self.assertEqual(len(paths), 2)
        return [ET.parse(ROOT / path).getroot() for path in paths]

    def test_no_duplicate_standalone_product_heading(self):
        self.assertNotRegex((ROOT / 'README.md').read_text(), r'(?m)^#\s+RELICADE\s*$')

    def test_each_theme_spells_name_once_without_an_extra_symbol(self):
        for root in self.assets():
            with self.subTest(description=root.findtext(SVG + 'desc')):
                paths = list(root.iter(SVG + 'path'))
                self.assertEqual([path.get('id') for path in paths], LETTERS)
                self.assertEqual(len(paths), 8)
                self.assertEqual(root.findtext(SVG + 'title'), 'Relicade')

    def test_orange_r_is_the_initial_not_a_separate_icon(self):
        for root in self.assets():
            paths = list(root.iter(SVG + 'path'))
            self.assertEqual(paths[0].get('id'), 'letter-r')
            self.assertEqual(paths[0].get('fill'), '#FF663D')
            self.assertFalse(any('http' in value for path in paths for value in path.attrib.values()))

    def test_prototype_uses_one_complete_wordmark(self):
        html = (ROOT / 'prototype/index.html').read_text()
        brand = re.search(r'<a class="brand"[^>]*>(.*?)</a>', html, re.S)
        self.assertIsNotNone(brand)
        assert brand is not None
        images = re.findall(r'<img\b[^>]*src="([^"]+)"', brand.group(1))
        self.assertEqual(images, ['../brand/readme-lockup-dark.svg'])

    @unittest.skipUnless(importlib.util.find_spec('PIL'),
                         'Optional image QA dependency not installed')
    def test_historical_native_capture_uses_the_same_single_r(self):
        from PIL import Image
        image = Image.open(ROOT / 'docs/assets/relicade-native-linux.png').convert('RGB')
        # The orange initial replaces R; exactly seven light-coloured letters follow.
        # Bounds are the suffix in the actual native capture, above its subtitle.
        columns = [any(min(cast(tuple[int, int, int], image.getpixel((x, y)))) > 180 for y in range(64, 92))
                   for x in range(80, 215)]
        runs = sum(active and (i == 0 or not columns[i - 1]) for i, active in enumerate(columns))
        self.assertEqual(runs, 7, 'Preview still contains an extra light-coloured R')

    def test_interface_preview_and_tagline_remain(self):
        readme = (ROOT / 'README.md').read_text()
        self.assertIn('### Old legends. New life.', readme)
        self.assertIn('docs/assets/relicade-native-empty-8a1796c-20261023.png', readme)

if __name__ == '__main__':
    unittest.main()
