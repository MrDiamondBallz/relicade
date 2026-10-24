"""Guard the authentic native product capture and keep browser QA separate."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
PREVIEW = ROOT / 'docs/assets/relicade-native-empty-8a1796c-20261023.png'
HISTORICAL_PREVIEW = ROOT / 'docs/assets/relicade-native-linux.png'


class ReadmePreviewTests(unittest.TestCase):
    def test_product_section_uses_the_actual_native_capture(self):
        text = (ROOT / 'README.md').read_text(encoding='utf-8')
        section = text.split('### Current native application', 1)[1].split('## The vision', 1)[0]
        images = re.findall(r'!\[[^\]]*\]\(([^)]+)\)', section)
        self.assertEqual(images, ['docs/assets/relicade-windows-controller-catalog-d092f2f-20261024.jpg'])
        self.assertIn('Genuine native Windows development build', section)
        self.assertIn('not a mockup, a playable-game count or release acceptance', section)
        self.assertIn('isolated empty Library', section)
        self.assertNotIn('relicade-interface-ocarina.png', text)
        self.assertNotRegex(text, r'!\[[^\]]*\]\(https://cdn2\.steamgriddb\.com/')

    def test_historical_desktop_capture_remains_immutable(self):
        self.assertTrue(HISTORICAL_PREVIEW.is_file(), 'Actual native product capture is missing')
        raw = HISTORICAL_PREVIEW.read_bytes()
        self.assertEqual(hashlib.sha256(raw).hexdigest(),
                         '8cff472868bee415e31f2f4a63f532646fc4b05a17f8c8cda59a1e105bcee9a1',
                         'Product capture changed; verify a new authentic native capture before updating this guard')
        self.assertEqual(raw[:8], b'\x89PNG\r\n\x1a\n')
        width, height = struct.unpack('>II', raw[16:24])
        self.assertGreaterEqual(width, 1280)
        self.assertGreaterEqual(height, 720)

    def test_current_headless_capture_matches_reviewed_bytes_and_provenance(self):
        raw = PREVIEW.read_bytes()
        self.assertEqual(raw[:8], b'\x89PNG\r\n\x1a\n')
        self.assertEqual(struct.unpack('>II', raw[16:24]), (1100, 760))
        digest = '47be8795d969a74325bfe8ac5391ccbf9978db27efedb276c9395861db70fa91'
        self.assertEqual(hashlib.sha256(raw).hexdigest(), digest,
                         'Current capture changed; independently verify a new native capture before updating this guard')
        metadata = json.loads(PREVIEW.with_suffix('.json').read_text())
        self.assertEqual(metadata['image'], PREVIEW.name)
        self.assertEqual(metadata['sha256'], digest)
        self.assertEqual(metadata['native_source_commit'], '8a1796c3fd97a77ff55546a6958054ea8dafd3e0')
        self.assertEqual(metadata['fixture'], 'native/Relicade.Tests/RealRender/NativeTaglineRenderingTests.cs')
        self.assertEqual(metadata['fixture_sha256'], '405641aa78a85a39ad15bb85faeae7ec339d57f7c4ca9ecf61809cab1497cb59')
        self.assertEqual(metadata['capture_kind'], 'actual-native-headless')
        self.assertEqual((metadata['width'], metadata['height']), (1100, 760))

    @unittest.skipUnless(importlib.util.find_spec('playwright'), 'Optional browser QA dependency not installed')
    def test_browser_qa_exports_do_not_overwrite_curated_previews(self):
        before = {path: hashlib.sha256(path.read_bytes()).hexdigest()
                  for path in (PREVIEW, HISTORICAL_PREVIEW)}
        with tempfile.TemporaryDirectory(prefix='relicade-qa-output-') as folder:
            output = Path(folder) / 'captures'
            env = dict(os.environ, RELICADE_QA_OUTPUT_DIR=str(output))
            done = subprocess.run([sys.executable, str(ROOT/'tools/qa_preview.py')], cwd=ROOT,
                                  env=env, capture_output=True, text=True, timeout=75)
            self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
            self.assertTrue((output/'relicade-launcher.png').is_file())
            self.assertTrue((output/'relicade-brand.png').is_file())
        for path, digest in before.items():
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), digest, str(path))


if __name__ == '__main__':
    unittest.main()
