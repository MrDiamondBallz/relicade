"""Guard the scope, truthfulness and upkeep of the public project overview."""
from pathlib import Path
import re
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
        self.assertIn('## Current progress', text)
        current = text.split('## Current progress', 1)[1].split('### Interface preview', 1)[0]
        for phrase in ['Merged development source', 'In progress', 'No packaged launcher release',
                       'not yet implemented', 'pull/1', 'pull/3']:
            self.assertIn(phrase, current)

    def test_local_readme_links_exist(self):
        text = (ROOT / 'README.md').read_text()
        targets = re.findall(r'\]\(([^)]+)\)', text)
        for target in targets:
            url = urlsplit(target)
            if url.scheme or url.netloc or not url.path:
                continue
            with self.subTest(target=target):
                self.assertTrue((ROOT / unquote(url.path)).exists())

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
