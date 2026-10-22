"""Guard the authentic native product capture and keep browser QA separate."""
import hashlib
import importlib.util
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
PREVIEW = ROOT / 'docs/assets/relicade-native-linux.png'


class ReadmePreviewTests(unittest.TestCase):
    def test_product_section_uses_the_actual_native_capture(self):
        text = (ROOT / 'README.md').read_text(encoding='utf-8')
        section = text.split('### Current native application', 1)[1].split('## Explore locally', 1)[0]
        images = re.findall(r'!\[[^\]]*\]\(([^)]+)\)', section)
        self.assertEqual(images, ['docs/assets/relicade-native-linux.png'])
        self.assertIn('Captured from the actual native Linux development build', section)
        self.assertIn('empty local library', section)
        self.assertNotIn('relicade-interface-ocarina.png', text)
        self.assertNotIn('### Ocarina of Time\n\n![', text)
        self.assertNotRegex(text, r'!\[[^\]]*\]\(https://cdn2\.steamgriddb\.com/')

    def test_curated_preview_is_a_real_desktop_png(self):
        self.assertTrue(PREVIEW.is_file(), 'Actual native product capture is missing')
        raw = PREVIEW.read_bytes()
        self.assertEqual(hashlib.sha256(raw).hexdigest(),
                         '8cff472868bee415e31f2f4a63f532646fc4b05a17f8c8cda59a1e105bcee9a1',
                         'Product capture changed; verify a new authentic native capture before updating this guard')
        self.assertEqual(raw[:8], b'\x89PNG\r\n\x1a\n')
        width, height = struct.unpack('>II', raw[16:24])
        self.assertGreaterEqual(width, 1280)
        self.assertGreaterEqual(height, 720)

    @unittest.skipUnless(importlib.util.find_spec('playwright'), 'Optional browser QA dependency not installed')
    def test_browser_qa_exports_do_not_overwrite_curated_previews(self):
        before = hashlib.sha256(PREVIEW.read_bytes()).hexdigest() if PREVIEW.exists() else None
        with tempfile.TemporaryDirectory(prefix='relicade-qa-output-') as folder:
            output = Path(folder) / 'captures'
            env = dict(os.environ, RELICADE_QA_OUTPUT_DIR=str(output))
            done = subprocess.run([sys.executable, str(ROOT/'tools/qa_preview.py')], cwd=ROOT,
                                  env=env, capture_output=True, text=True, timeout=75)
            self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
            self.assertTrue((output/'relicade-launcher.png').is_file())
            self.assertTrue((output/'relicade-brand.png').is_file())
        if before:
            self.assertEqual(hashlib.sha256(PREVIEW.read_bytes()).hexdigest(), before)


if __name__ == '__main__':
    unittest.main()
