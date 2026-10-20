"""Keep the repository masthead a single accessible Relicade wordmark."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SVG = '{http://www.w3.org/2000/svg}'
LETTERS = ['letter-r', 'letter-e-1', 'letter-l', 'letter-i', 'letter-c', 'letter-a', 'letter-d', 'letter-e-2']

class ReadmeBrandingTests(unittest.TestCase):
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

    def test_interface_preview_and_tagline_remain(self):
        readme = (ROOT / 'README.md').read_text()
        self.assertIn('### Old legends. New life.', readme)
        self.assertIn('docs/assets/relicade-interface-ocarina.png', readme)

if __name__ == '__main__':
    unittest.main()
