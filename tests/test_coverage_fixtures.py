"""Exercise safe synthetic fixture construction, not extraction or native operations."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]


class FixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = ROOT / 'tools/coverage_fixtures.py'
        if not path.is_file():
            raise AssertionError('Synthetic fixture generator has not been implemented')
        spec = importlib.util.spec_from_file_location('coverage_fixtures', path)
        assert spec and spec.loader
        cls.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.module)

    def test_deterministic_bounded_bytes_only(self):
        first, second = self.module.build_fixtures(), self.module.build_fixtures()
        self.assertEqual(first, second)
        self.assertTrue(all(isinstance(value, bytes) for value in first.values()))
        self.assertLess(sum(map(len, first.values())), 2_000_000)
        for name in first:
            self.assertNotIn('/', name)
            self.assertNotIn('\\', name)
            self.assertNotIn('..', name)
            self.assertNotIn(Path(name).suffix, ['.exe', '.dll', '.apk', '.rom'])

    def test_valid_zip_and_traversal_names_are_data_only(self):
        files = self.module.build_fixtures()
        with zipfile.ZipFile(io.BytesIO(files['valid.zip'])) as archive:
            self.assertEqual(archive.namelist(), ['payload/readme.txt', 'payload/data.txt'])
        with zipfile.ZipFile(io.BytesIO(files['unsafe-paths.zip'])) as archive:
            self.assertIn('../escape.txt', archive.namelist())
            self.assertIn('C:/escape.txt', archive.namelist())
            self.assertIn('payload/readme.txt:stream', archive.namelist())
        self.assertEqual(zipfile.ZipFile(io.BytesIO(files['unsafe-paths.zip'])).testzip(), None)

    def test_symlink_stored_in_zip_not_created_on_host(self):
        files = self.module.build_fixtures()
        with zipfile.ZipFile(io.BytesIO(files['links.zip'])) as archive:
            entry = archive.getinfo('payload/link')
            self.assertTrue(stat.S_ISLNK(entry.external_attr >> 16))
            self.assertEqual(archive.read(entry), b'../../outside')
        links = json.loads(files['link-cases.json'])
        self.assertIn('preexisting-junction', [case['id'] for case in links])

    def test_corruption_and_resource_budgets_without_a_real_bomb(self):
        files = self.module.build_fixtures()
        with self.assertRaises(zipfile.BadZipFile):
            zipfile.ZipFile(io.BytesIO(files['truncated.zip']))
        with zipfile.ZipFile(io.BytesIO(files['bad-crc.zip'])) as archive:
            self.assertIsNotNone(archive.testzip())
        with zipfile.ZipFile(io.BytesIO(files['budget.zip'])) as archive:
            self.assertGreater(sum(x.file_size for x in archive.infolist()), 1024)
            self.assertLess(sum(x.file_size for x in archive.infolist()), 100_000)

    def test_library_corruption_and_future_version_are_distinct(self):
        files = self.module.build_fixtures()
        with self.assertRaises(json.JSONDecodeError):
            json.loads(files['library-corrupt.json'])
        self.assertEqual(json.loads(files['library-future.json'])['schema_version'], 999)
        self.assertEqual(len(json.loads(files['library-duplicate.json'])['entries']), 2)

    def test_failure_schedule_has_each_journal_boundary_and_preservation_oracle(self):
        schedule = json.loads(self.module.build_fixtures()['operation-failures.json'])
        self.assertEqual(set(schedule['boundaries']), {'download', 'verify', 'extract', 'journal', 'activate', 'metadata', 'cleanup'})
        self.assertTrue({'disk-full', 'access-denied', 'cancel', 'terminate'} <= set(schedule['faults']))
        self.assertIn('user-data-unchanged', schedule['invariants'])
        self.assertFalse(schedule['native_operations_executed'])

    def test_emit_never_extracts_links_or_overwrites_output(self):
        with tempfile.TemporaryDirectory(prefix='relicade-fixtures-') as temp:
            dest = Path(temp) / 'fixtures'
            self.module.emit(dest)
            self.assertFalse(any(p.is_symlink() for p in dest.iterdir()))
            self.assertEqual({p.name for p in dest.iterdir()}, set(self.module.build_fixtures()) | {'manifest.json'})
            manifest = json.loads((dest / 'manifest.json').read_text())
            for name, data in self.module.build_fixtures().items():
                self.assertEqual(manifest['files'][name], hashlib.sha256(data).hexdigest())
            with self.assertRaises(FileExistsError):
                self.module.emit(dest)
            self.assertEqual(list(Path(temp).iterdir()), [dest])

    def test_generated_scenario_artifacts_exist(self):
        scenarios = json.loads((ROOT / 'tests/fixtures/scenarios.json').read_text())['scenarios']
        files = self.module.build_fixtures()
        for scenario in scenarios:
            if scenario['mode'] == 'generated':
                self.assertTrue(scenario['artifacts'], scenario['id'])
            else:
                self.assertEqual(scenario['artifacts'], [])
            for name in scenario['artifacts']:
                self.assertIn(name, files, scenario['id'])

    def test_cli_exercises_generator(self):
        with tempfile.TemporaryDirectory(prefix='relicade-fixture-cli-') as temp:
            dest = Path(temp) / 'fixtures'
            result = subprocess.run([sys.executable, 'tools/coverage_fixtures.py', '--output', str(dest)],
                                    cwd=ROOT, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue((dest / 'manifest.json').is_file())


if __name__ == '__main__':
    unittest.main()
