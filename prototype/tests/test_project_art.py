"""Offline tests for original editorial artwork; no network or game images."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('project_art', ROOT / 'project_art.py')


def load_renderer():
    if not SPEC or not SPEC.loader or not Path(SPEC.origin).exists():
        return None
    module = importlib.util.module_from_spec(SPEC)
    sys.modules['project_art'] = module
    SPEC.loader.exec_module(module)
    return module


art = load_renderer()


class OriginalArtTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(art, 'Original project-art renderer must exist')
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.app = {'slug': 'fixture-project', 'name': 'A Factual Game Title',
                    'projectName': 'Community Project', 'sourceProject': {'author': 'Original Maintainer'},
                    'artwork': 'https://example.invalid/icon.png',
                    'libraryArt': {'hero': 'https://cdn2.steamgriddb.com/hero/reference.png'},
                    'artProvenance': [{'kind': 'hero', 'sourceUrl': 'https://cdn2.steamgriddb.com/hero/reference.png',
                                      'localPath': 'assets/art/old.webp', 'sha256': 'old', 'rights': 'Third-party rights'}]}

    def test_reproducible_valid_art_and_truthful_provenance(self):
        result = art.apply_original_art(copy.deepcopy(self.app), root=self.root)
        first = {}
        for kind, size in art.SIZES.items():
            path = self.root / result['localArt'][kind]
            first[kind] = (path.read_bytes(), path.stat().st_mtime_ns)
            with Image.open(path) as image:
                self.assertEqual(image.size, size)
                self.assertEqual(image.format, 'WEBP')
                colors = image.getcolors(1000000)
                self.assertIsNotNone(colors)
                self.assertGreater(len(colors or []), 2 if kind == 'hero' else 10)
            provenance = next(p for p in result['artProvenance'] if p['kind'] == kind)
            self.assertEqual(provenance['localPath'], result['localArt'][kind])
            self.assertEqual(provenance['sha256'], hashlib.sha256(path.read_bytes()).hexdigest())
            self.assertEqual(provenance['sourceUrl'], 'https://github.com/MrDiamondBallz/relicade/blob/main/prototype/project_art.py')
            self.assertIn('Relicade original project-tile art', provenance['rights'])
            self.assertEqual(provenance['renderer']['sha256'], hashlib.sha256((ROOT / 'project_art.py').read_bytes()).hexdigest())
            self.assertEqual(len(provenance['fonts']), 2)
        again = art.apply_original_art(result, root=self.root)
        self.assertEqual(result, again)
        for kind in art.SIZES:
            path = self.root / again['localArt'][kind]
            self.assertEqual((path.read_bytes(), path.stat().st_mtime_ns), first[kind])
        second_root = self.root / 'independent'
        other = art.apply_original_art(copy.deepcopy(self.app), root=second_root)
        for kind in art.SIZES:
            self.assertEqual((second_root / other['localArt'][kind]).read_bytes(), first[kind][0])

    def test_reference_urls_preserved_without_local_or_hash_claims(self):
        result = art.apply_original_art(copy.deepcopy(self.app), root=self.root)
        refs = result['referenceArtSources']
        self.assertEqual(refs['artwork'], self.app['artwork'])
        self.assertEqual(refs['libraryArt'], self.app['libraryArt'])
        self.assertEqual(refs['provenance'][0]['rights'], 'Third-party rights')
        self.assertNotIn('localPath', json.dumps(refs))
        self.assertNotIn('sha256', json.dumps(refs))
        self.assertNotIn('libraryArt', {k: v for k, v in result.items() if k != 'referenceArtSources'})
        self.assertNotIn('artwork', result)
        self.assertEqual(result['sourceProject'], self.app['sourceProject'])

    def test_rejects_path_traversal_and_invalid_slug_before_writing(self):
        for slug in ['../escape', 'bad/name', '', 'bad\\name', 'CAPS']:
            with self.subTest(slug=slug), self.assertRaises(ValueError):
                art.apply_original_art(dict(self.app, slug=slug), root=self.root)
        self.assertEqual(list(self.root.iterdir()), [])

    def test_long_titles_fit_all_canvas_types(self):
        for kind in art.SIZES:
            image, layout = art.render_tile(dict(self.app, projectName='A very long community project title with several unusually elongated words'), kind)
            self.assertEqual(image.size, art.SIZES[kind])
            for x0, y0, x1, y1 in layout['textBounds']:
                self.assertGreaterEqual(x0, 0)
                self.assertGreaterEqual(y0, 0)
                self.assertLessEqual(x1, image.width)
                self.assertLessEqual(y1, image.height)

    def test_hero_uses_crop_safe_geometry_while_ui_supplies_accessible_title(self):
        # A single responsive hero is cover-cropped at many ratios and overlaid
        # with real game/project text by the UI. Embedded duplicate type clips.
        _, layout = art.render_tile(self.app, 'hero')
        self.assertEqual(layout['textBounds'], [])
        self.assertEqual(layout['titleBounds'], [])

    def test_header_type_survives_mobile_center_crop(self):
        # The header is 1.846:1; mobile cards crop it to 1.5:1.
        # Both side strips (source ~112px) must remain nonessential geometry.
        _, layout = art.render_tile(self.app, 'header')
        for x0, _, x1, _ in layout['textBounds']:
            self.assertGreaterEqual(x0, 135)
            self.assertLessEqual(x1, 1065)

    def test_header_titles_do_not_hide_under_mobile_save_button(self):
        apps = json.loads((ROOT / 'data/catalog.json').read_text())['apps']
        for app in apps:
            _, layout = art.render_tile(app, 'header')
            self.assertIn('titleBounds', layout)
            for _, y0, _, y1 in layout['titleBounds']:
                self.assertGreaterEqual(y0, 310)
                self.assertLessEqual(y1, 520)

    def test_snapshot_schema_and_all_36_original_tiles(self):
        snapshot = json.loads((ROOT / 'data/catalog.json').read_text())
        self.assertEqual(len(snapshot['apps']), 12)
        self.assertEqual(snapshot['fetchedAt'], '2026-10-18T22:36:20.040896+00:00')
        self.assertIn('SteamGridDB', snapshot['sourceAttribution'])
        paths = set()
        for app in snapshot['apps']:
            self.assertIn('referenceArtSources', app)
            self.assertEqual(set(app['localArt']), set(art.SIZES))
            self.assertEqual(len(app['artProvenance']), 3)
            for provenance in app['artProvenance']:
                path = ROOT / provenance['localPath']
                paths.add(path)
                self.assertRegex(path.name, r'^[a-z0-9-]+-(hero|header|capsule)\.webp$')
                self.assertEqual(provenance['sha256'], hashlib.sha256(path.read_bytes()).hexdigest())
                self.assertEqual(provenance['sourceUrl'], art.SOURCE_URL)
                with Image.open(path) as image:
                    self.assertEqual(image.size, art.SIZES[provenance['kind']])
        self.assertEqual(len(paths), 36)
        self.assertEqual(paths, set((ROOT / 'assets/art').glob('*.webp')))


if __name__ == '__main__':
    unittest.main()
