"""The opt-in live checker must not pass row-count-only or fallback evidence."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('qa_artwork_live', ROOT/'tools/qa_artwork_live.py')
assert SPEC and SPEC.loader
live = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(live)


class LiveArtworkValidationTests(unittest.TestCase):
    def setUp(self):
        self.catalog = json.loads((ROOT/'prototype/data/catalog.json').read_text())
        # Validation fixtures only, never production-availability evidence.
        self.slots = [dict(project=app['id'], kind=kind,
                           source=app['referenceArtSources']['libraryArt'][kind],
                           state='loaded', width=96, height=48)
                      for app in self.catalog['apps'] for kind in ('hero', 'header', 'capsule')]

    def test_exact_expected_decoded_sources_pass_validation(self):
        self.assertEqual(live.availability_errors(self.catalog, self.slots), [])

    def test_non_ocarina_fallback_or_zero_dimensions_fail(self):
        for change in ({'state':'fallback', 'width':0, 'height':0}, {'width':0}, {'height':0}):
            with self.subTest(change=change):
                slots = copy.deepcopy(self.slots)
                slots[0].update(change)
                self.assertTrue(live.availability_errors(self.catalog, slots))

    def test_alternative_source_does_not_pass_as_expected_source(self):
        self.slots[0]['source'] = self.slots[1]['source']
        self.assertTrue(live.availability_errors(self.catalog, self.slots))

    def test_duplicate_missing_or_unexpected_slot_fails(self):
        duplicate = copy.deepcopy(self.slots)
        duplicate[0] = duplicate[1]
        unexpected = copy.deepcopy(self.slots)
        unexpected[0]['project'] = 'unexpected-fixture-project'
        for slots in (duplicate, unexpected, self.slots[:-1]):
            with self.subTest(slots=slots[:1]):
                self.assertTrue(live.availability_errors(self.catalog, slots))


if __name__ == '__main__':
    unittest.main(verbosity=2)
