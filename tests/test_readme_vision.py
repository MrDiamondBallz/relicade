"""Guard the scope, truthfulness and upkeep of the public project overview."""
from pathlib import Path
import re
import json
import hashlib
import unittest
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]

class ReadmeVisionTests(unittest.TestCase):
    def test_vision_includes_revivals_retro_and_emulation(self):
        text = (ROOT / 'README.md').read_text()
        self.assertIn('## The vision', text)
        self.assertIn('## Current progress', text)
        vision = text.split('## The vision', 1)[1].split('## Current progress', 1)[0].lower()
        for phrase in ['community-revived', 'retro games', 'emulation', 'offline', 'controller']:
            self.assertIn(phrase, vision)

    def test_progress_separates_merged_work_from_unfinished_features(self):
        text = (ROOT / 'README.md').read_text()
        current = text.split('## Current progress', 1)[1].split('## Build or preview', 1)[0]
        for phrase in ['Merged development source', 'In progress', 'pull/1', 'pull/3',
                       'integrated Windows gameplay is still pending', 'disabled Play action']:
            self.assertIn(phrase, current)
        self.assertIn('no packaged launcher release yet', text)
        self.assertIn('docs/DEVELOPMENT_MILESTONES.md', text)
        self.assertIn('not yet implemented', text)
        self.assertLess(text.index('### Current native application'), text.index('## Current progress'))

    def test_product_goals_keep_catalog_breadth_and_honest_visual_setup_scope(self):
        requirements = (ROOT / 'docs/PRODUCT_REQUIREMENTS.md').read_text()
        for phrase in ['complete provenance-qualified upstream catalog',
                       'not a selected', 'separate acceptance states',
                       'required player-supplied input', 'Emulation is not implemented',
                       'unauthorized sources', 'readable title and status overlays']:
            self.assertIn(phrase, requirements)
        artwork = (ROOT / 'docs/ARTWORK.md').read_text()
        self.assertIn('actual current native application', artwork)
        self.assertIn('future interface, not evidence of the current application', artwork)
        for path in ['README.md', 'docs/PRODUCT_REQUIREMENTS.md', 'docs/ROADMAP.md']:
            text = (ROOT / path).read_text().lower()
            for framing in ['reference launcher', 'competitor capability', 'full-parity acceptance']:
                self.assertNotIn(framing, text, path)

    def test_local_readme_links_exist(self):
        text = (ROOT / 'README.md').read_text()
        targets = re.findall(r'\]\(([^)]+)\)', text)
        for target in targets:
            url = urlsplit(target)
            if url.scheme or url.netloc or not url.path:
                continue
            with self.subTest(target=target):
                self.assertTrue((ROOT / unquote(url.path)).exists())

    def test_current_native_capture_has_immutable_provenance_and_honest_caption(self):
        text = (ROOT / 'README.md').read_text()
        current = text.split('### Current native application', 1)[1].split('## The vision', 1)[0]
        image = re.search(r'!\[[^\]]+\]\((docs/assets/[^)]+\.jpg)\)', current)
        self.assertIsNotNone(image)
        path = ROOT / image.group(1)
        metadata = json.loads(path.with_suffix('.json').read_text())
        self.assertEqual(metadata['file'], path.name)
        self.assertEqual(metadata['sha256'], hashlib.sha256(path.read_bytes()).hexdigest())
        self.assertEqual(metadata['platform'], 'Windows x64')
        self.assertRegex(metadata['sourceCommit'], r'^[0-9a-f]{40}$')
        self.assertEqual(metadata['dimensions'], [1920, 1032])
        self.assertFalse(metadata['imageEdited'])
        self.assertFalse(metadata['composited'])
        self.assertIn('not a mockup, a playable-game count or release acceptance', current)

    def test_previous_native_capture_is_retained_as_history_not_current(self):
        self.assertTrue((ROOT / 'docs/assets/relicade-native-linux.png').is_file())
        text = (ROOT / 'docs/ARTWORK.md').read_text()
        self.assertIn('retained unchanged as historical material', text)
        self.assertIn('old subtitle does not represent', text)
        self.assertIn('assets/relicade-native-linux.png', text)

    def test_readme_freshness_is_part_of_contribution_and_pr_workflow(self):
        contributing = (ROOT / 'CONTRIBUTING.md').read_text()
        template = ROOT / '.github/pull_request_template.md'
        self.assertIn('README freshness', contributing)
        self.assertIn('every meaningful source milestone', contributing)
        self.assertTrue(template.is_file())
        self.assertIn('README', template.read_text())
        self.assertIn('current progress', template.read_text())

if __name__ == '__main__':
    unittest.main()
