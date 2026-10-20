"""Real repository fixtures for bounded generation and nonautomatic semantic review."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
TOOL = ROOT / 'tools/docs_sync.py'
BEGIN = '<!-- docs-sync:begin facts -->'
END = '<!-- docs-sync:end facts -->'


class DocsSyncTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='docs-sync-')
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        subprocess.run(['git', 'init', '-q', str(self.root)], check=True)
        for path in ['docs/FEATURE_COVERAGE.json', 'tests/fixtures/scenarios.json',
                     'prototype/data/catalog.json', 'docs/SOURCE_STATUS.json']:
            target = self.root / path
            target.parent.mkdir(parents=True, exist_ok=True)
            if (ROOT / path).exists():
                shutil.copyfile(ROOT / path, target)
        (self.root / 'src').mkdir()
        (self.root / 'src/app.py').write_text('value = 1\n')
        (self.root / 'README.md').write_text('# Manual introduction\n\n' + BEGIN + '\nold\n' + END + '\n\nManual footer.\n')
        (self.root / 'docs/DOC_STATUS.md').write_text('# Documentation status\n\n' + BEGIN + '\nold\n' + END + '\n')
        (self.root / 'NOTICE.md').write_text('Legal text, not generated.\n')
        (self.root / 'CHANGELOG.md').write_text('Historical entry, not generated.\n')
        sha = lambda p: hashlib.sha256((self.root / p).read_bytes()).hexdigest()
        self.manifest = {
            'version': 1,
            'source_groups': {'app': ['src'], 'facts': ['docs/FEATURE_COVERAGE.json', 'tests/fixtures', 'prototype/data']},
            'documents': {
                'README.md': {'policy': 'review', 'sources': ['app', 'facts'], 'reviewed': {}},
                'docs/DOC_STATUS.md': {'policy': 'generated'},
                'NOTICE.md': {'policy': 'protected', 'sha256': sha('NOTICE.md')},
                'CHANGELOG.md': {'policy': 'protected', 'sha256': sha('CHANGELOG.md')},
            },
            'protected_files': {},
        }
        self.save()

    def save(self):
        (self.root / 'docs/DOCUMENTATION.json').write_text(json.dumps(self.manifest, indent=2) + '\n')

    def run_tool(self, mode):
        result = subprocess.run([sys.executable, str(TOOL), '--root', str(self.root), mode], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 2, result.stdout + result.stderr)
        return result, json.loads(result.stdout)

    def acknowledge(self):
        _, report = self.run_tool('--audit')
        for entry in self.manifest['documents'].values():
            if entry['policy'] == 'review':
                entry['reviewed'] = {group: report['source_digests'][group] for group in entry['sources']}
                entry['reviewed']['status'] = report['status_digest']
        self.save()

    def green(self):
        self.acknowledge()
        result, _ = self.run_tool('--write')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def snapshot(self):
        return {p.relative_to(self.root).as_posix(): p.read_bytes() for p in self.root.rglob('*') if p.is_file() and '.git' not in p.parts}

    def test_real_source_change_stale_write_does_not_ack_then_explicit_review_passes(self):
        self.green()
        (self.root / 'src/app.py').write_text('value = 2\n')
        before = self.snapshot()
        result, report = self.run_tool('--check')
        self.assertEqual(result.returncode, 1)
        self.assertIn('README.md', report['review_required'])
        self.assertEqual(before, self.snapshot(), '--check mutated files')
        manifest = (self.root / 'docs/DOCUMENTATION.json').read_bytes()
        result, _ = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertEqual(manifest, (self.root / 'docs/DOCUMENTATION.json').read_bytes())
        self.acknowledge()  # Deliberate reviewer action, never an updater side effect.
        result, _ = self.run_tool('--check')
        self.assertEqual(result.returncode, 0, result.stdout)

    def test_fact_change_refresh_then_review(self):
        self.green()
        path = self.root / 'prototype/data/catalog.json'
        data = json.loads(path.read_text())
        data['apps'].pop()
        path.write_text(json.dumps(data))
        result, report = self.run_tool('--check')
        self.assertEqual(result.returncode, 1)
        self.assertIn('README.md', report['generated_stale'])
        self.assertIn('README.md', report['review_required'])
        self.run_tool('--write')
        self.acknowledge()
        result, _ = self.run_tool('--check')
        self.assertEqual(result.returncode, 0, result.stdout)

    def test_idempotent_manual_protected_and_history_preserved(self):
        self.green()
        before = self.snapshot()
        result, _ = self.run_tool('--write')
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(before, self.snapshot())
        self.assertTrue((self.root / 'README.md').read_text().startswith('# Manual introduction\n'))
        self.assertTrue((self.root / 'README.md').read_text().endswith('Manual footer.\n'))
        self.assertEqual((self.root / 'NOTICE.md').read_text(), 'Legal text, not generated.\n')
        self.assertEqual((self.root / 'CHANGELOG.md').read_text(), 'Historical entry, not generated.\n')
        result, _ = self.run_tool('--audit')
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(before, self.snapshot(), '--audit mutated files')

    def test_invalid_unknown_manifest_no_mutation(self):
        self.green()
        self.manifest['unknown'] = True
        self.save()
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(report['errors'])
        self.assertEqual(before, self.snapshot())

    def test_reviewed_status_input_changes_facts_but_never_acks_prose(self):
        self.green()
        path = self.root / 'docs/SOURCE_STATUS.json'
        data = json.loads(path.read_text())
        data['cleanup_state'] = 'merged'
        path.write_text(json.dumps(data))
        result, report = self.run_tool('--check')
        self.assertEqual(result.returncode, 1)
        self.assertIn('README.md', report['review_required'])
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertIn('recorded as merged development source, not release acceptance', (self.root / 'README.md').read_text())
        self.acknowledge()
        result, _ = self.run_tool('--check')
        self.assertEqual(result.returncode, 0, result.stdout)

    def test_unknown_fact_fields_fail_without_writes(self):
        self.green()
        path = self.root / 'prototype/data/catalog.json'
        data = json.loads(path.read_text())
        data['unknown_facts'] = 'untrusted'
        path.write_text(json.dumps(data))
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(report['errors'])
        self.assertEqual(before, self.snapshot())

    def test_unknown_status_no_mutation(self):
        self.green()
        path = self.root / 'docs/SOURCE_STATUS.json'
        data = json.loads(path.read_text())
        data['emulation'] = 'accepted'
        path.write_text(json.dumps(data))
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(report['errors'])
        self.assertEqual(before, self.snapshot())

    def test_discovery_requires_classification_and_references_exist(self):
        self.green()
        (self.root / 'docs/new.md').write_text('# New guide\n[missing](missing.md)\n')
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertIn('docs/new.md', report['unregistered'])
        self.assertEqual(before, self.snapshot())
        self.manifest['documents']['docs/new.md'] = {'policy': 'review', 'sources': ['app'], 'reviewed': {}}
        self.save()
        result, report = self.run_tool('--audit')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(any('missing.md' in error for error in report['errors']))
        (self.root / 'docs/new.md').write_text('# New guide\n[Overview](../README.md)\n')
        self.acknowledge()
        self.run_tool('--write')
        result, _ = self.run_tool('--check')
        self.assertEqual(result.returncode, 0, result.stdout)

    def test_symlink_and_escape_fail_before_any_write(self):
        self.green()
        target = self.root / 'src/link.py'
        target.symlink_to(TOOL)
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(any('symlink' in error for error in report['errors']))
        self.assertEqual(before, self.snapshot())
        target.unlink()
        self.manifest['source_groups']['app'] = ['../outside']
        self.save()
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(report['errors'])
        self.assertEqual(before, self.snapshot())

    def test_output_symlink_malformed_markers_and_protected_tampering(self):
        self.green()
        for mutation in ['marker', 'protected', 'symlink']:
            with self.subTest(mutation=mutation):
                path = self.root / ('NOTICE.md' if mutation == 'protected' else 'README.md')
                original = path.read_bytes()
                if mutation == 'symlink':
                    path.unlink()
                    path.symlink_to(self.root / 'NOTICE.md')
                elif mutation == 'marker':
                    path.write_text(path.read_text() + BEGIN)
                else:
                    path.write_text('Changed legal text\n')
                before = self.snapshot()
                result, report = self.run_tool('--write')
                self.assertEqual(result.returncode, 1)
                self.assertTrue(report['errors'])
                self.assertEqual(before, self.snapshot())
                path.unlink()
                path.write_bytes(original)

    def test_nonfinite_and_deeply_nested_json_fail_without_mutation(self):
        self.green()
        path = self.root / 'docs/SOURCE_STATUS.json'
        original = path.read_text()
        for invalid in [original.replace('"schema_version": 1', '"schema_version": NaN'), '[' * 1500 + '0' + ']' * 1500]:
            with self.subTest(kind='nonfinite' if 'NaN' in invalid else 'deep'):
                path.write_text(invalid)
                before = self.snapshot()
                result, report = self.run_tool('--write')
                self.assertEqual(result.returncode, 1)
                self.assertTrue(report['errors'])
                self.assertEqual(before, self.snapshot())
        path.write_text(original)

    def test_resource_budget_rejects_without_mutation(self):
        self.green()
        self.manifest['source_groups'] = {'group'+str(n): ['src'] for n in range(33)}
        self.save()
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(any('excessive source groups' in error for error in report['errors']))
        self.assertEqual(before, self.snapshot())

    def test_new_rst_document_links_and_policy(self):
        self.green()
        path = self.root / 'docs/guide.rst'
        path.write_text('Guide\n=====\n\n`Overview <../README.md>`_\n')
        self.manifest['documents']['docs/guide.rst'] = {'policy': 'review', 'sources': ['app'], 'reviewed': {}}
        self.save()
        self.acknowledge()
        result, _ = self.run_tool('--write')
        self.assertEqual(result.returncode, 0, result.stdout)
        path.write_text('Guide\n=====\n\n.. _bad: missing.md\n')
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(any('missing.md' in error for error in report['errors']))
        self.assertEqual(before, self.snapshot())

    def test_unmapped_source_rejected_and_new_mapped_source_stales_review(self):
        self.green()
        (self.root / 'unexpected.py').write_text('new = True\n')
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(any('Unmapped source' in error for error in report['errors']))
        self.assertEqual(before, self.snapshot())
        (self.root / 'unexpected.py').rename(self.root / 'src/new.py')
        result, report = self.run_tool('--check')
        self.assertEqual(result.returncode, 1)
        self.assertIn('README.md', report['review_required'])

    def test_local_reference_anchor_html_and_escape_validation(self):
        self.green()
        path = self.root / 'README.md'
        original = path.read_text()
        for link in ['[bad](#missing)', '[bad][ref]\n\n[ref]: missing.md',
                     '<img src="missing.png">', '[bad](../../outside.md)',
                     '[bad](docs/%2e%2e/%2e%2e/outside.md)']:
            with self.subTest(link=link):
                path.write_text(original + '\n' + link + '\n')
                before = self.snapshot()
                result, report = self.run_tool('--write')
                self.assertEqual(result.returncode, 1)
                self.assertTrue(report['errors'])
                self.assertEqual(before, self.snapshot())
        path.write_text(original + '\n[valid](#manual-introduction)\n')
        result, _ = self.run_tool('--check')
        self.assertEqual(result.returncode, 0, result.stdout)

    def test_duplicate_json_keys_and_hardlinked_outputs_rejected(self):
        self.green()
        path = self.root / 'docs/DOCUMENTATION.json'
        original = path.read_text()
        path.write_text(original.replace('"version": 1,', '"version": 1, "version": 1,'))
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(any('Duplicate JSON' in error for error in report['errors']))
        self.assertEqual(before, self.snapshot())
        path.write_text(original)
        import os
        os.link(self.root / 'README.md', self.root / 'linked.md')
        self.manifest['documents']['linked.md'] = {'policy': 'review', 'sources': ['app'], 'reviewed': {}}
        self.save()
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(any('Hardlinked' in error for error in report['errors']))
        self.assertEqual(before, self.snapshot())

    def test_late_validation_failure_never_partially_writes(self):
        self.green()
        readme = self.root / 'README.md'
        readme.write_text(readme.read_text().replace('Repository facts', 'Stale facts'))
        status = self.root / 'docs/DOC_STATUS.md'
        status.write_text(status.read_text().replace(END, ''))
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(any('markers' in error for error in report['errors']))
        self.assertEqual(before, self.snapshot())

    def test_ordinary_second_replace_failure_rolls_back_all_outputs(self):
        import importlib.util
        from unittest.mock import patch
        spec = importlib.util.spec_from_file_location('docs_sync_fixture', TOOL)
        assert spec and spec.loader
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.green()
        before = self.snapshot()
        replace = module.os.replace
        calls = []
        def fail_second(source, target):
            calls.append(str(target))
            if len(calls) == 2:
                raise OSError('Synthetic second replacement failure')
            return replace(source, target)
        with patch.object(module.os, 'replace', side_effect=fail_second):
            with self.assertRaises(OSError):
                module.atomic_batch(self.root, {'README.md': b'new first\n', 'docs/DOC_STATUS.md': b'new second\n'})
        self.assertEqual(before, self.snapshot())
        self.assertFalse(list(self.root.rglob('.docs-sync-*')))

    def test_build_git_and_doc_edits_not_recursive_source_drift(self):
        self.green()
        before_result, before = self.run_tool('--audit')
        self.assertEqual(before_result.returncode, 0)
        (self.root / 'src/bin').mkdir()
        (self.root / 'src/bin/output.py').write_text('build output\n')
        (self.root / 'README.md').write_text((self.root / 'README.md').read_text() + '\nManual clarification.\n')
        subprocess.run(['git', '-C', str(self.root), 'config', 'test.example', 'changed'], check=True)
        result, report = self.run_tool('--check')
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(before['source_digests'], report['source_digests'])


if __name__ == '__main__':
    unittest.main()
