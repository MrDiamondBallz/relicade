"""Bounded shipped portrait identity and attribution; no network or app execution."""
import hashlib
import json
from pathlib import Path
import struct
import unittest

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'native/Assets/GameArt'

class ReviewedArtworkTests(unittest.TestCase):
    def test_exact_small_representation(self):
        metadata = json.loads((ART / 'taisei-portrait-provenance.json').read_text())
        data = (ART / 'taisei-elly-afensorm-cover.png').read_bytes()
        self.assertEqual(236501, len(data))
        self.assertEqual(metadata['derivative_bytes'], len(data))
        self.assertEqual(metadata['derivative_sha256'], hashlib.sha256(data).hexdigest())
        self.assertEqual(b'\x89PNG\r\n\x1a\n', data[:8])
        self.assertEqual((960, 540), struct.unpack('>II', data[16:24]))
        self.assertEqual([960, 540], metadata['derivative_pixels'])
        self.assertFalse(metadata['transformation']['generated_content'])
        self.assertEqual('k177mxt12q61yv9fsvf6wvh1pn8f4n2n', metadata['catalog_entry_id'])
    def test_exact_source_and_credit_remain_shipped(self):
        metadata = json.loads((ART / 'taisei-portrait-provenance.json').read_text())
        notice = (ART / metadata['notice']).read_text()
        for text in ['afensorm', 'https://creativecommons.org/licenses/by/4.0/',
                     metadata['source_commit'], metadata['source_path'], metadata['license_source_commit'],
                     'cropped', 'resized', 'not Relicade', 'Copyright (c) 2011']:
            self.assertIn(text, notice)
        self.assertEqual([0, 0, 2500, 1406], metadata['transformation']['crop_rectangle'])
        self.assertIn(metadata['derivative_sha256'], (ROOT / 'native/Services/ReviewedGameArtwork.cs').read_text())
