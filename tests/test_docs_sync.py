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

    def vendor_fixture(self):
        path = 'native/licenses/vendor/NOTICE.txt'
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text('Preserved synthetic notice: [original](src/original.c)\n')
        entry = {'policy': 'protected', 'sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
                 'vendor_notice': {'archive_url': 'https://api.nuget.org/v3-flatcontainer/example/1.0.0/example.1.0.0.nupkg',
                                   'archive_sha256': 'a' * 64, 'member': 'NOTICE.txt',
                                   'upstream_relative_references': ['src/original.c']}}
        self.manifest['documents'][path] = entry
        self.save()
        return path, target, entry

    def test_vendor_citations_are_explicit_unchecked_and_do_not_rewrite_notice(self):
        path, target, entry = self.vendor_fixture()
        before = target.read_bytes()
        self.green()
        result, report = self.run_tool('--check')
        self.assertEqual(0, result.returncode, report)
        self.assertEqual(before, target.read_bytes())
        self.assertEqual([{'document': path, 'reference': 'src/original.c',
                          'archive_url': entry['vendor_notice']['archive_url'],
                          'target_availability': 'not_checked'}], report['vendor_upstream_references'])

    def test_vendor_classification_does_not_excuse_broken_owned_document_links(self):
        self.vendor_fixture()
        self.green()
        with (self.root / 'README.md').open('a') as stream:
            stream.write('\n[broken](missing.md)\n')
        result, report = self.run_tool('--check')
        self.assertEqual(1, result.returncode)
        self.assertTrue(any('README.md: missing.md' in error for error in report['errors']), report)

    def test_vendor_new_and_stale_references_still_fail(self):
        path, target, entry = self.vendor_fixture()
        target.write_text(target.read_text() + '[other](missing.c)\n')
        entry['sha256'] = hashlib.sha256(target.read_bytes()).hexdigest()
        self.save()
        _, report = self.run_tool('--audit')
        self.assertTrue(any('missing.c (Missing local link)' in e for e in report['errors']), report)
        entry['vendor_notice']['upstream_relative_references'].append('never-mentioned.c')
        self.save()
        _, report = self.run_tool('--audit')
        self.assertTrue(any('stale vendor reference' in e for e in report['errors']), report)

    def test_vendor_notice_must_be_registered_and_unchanged(self):
        path, target, entry = self.vendor_fixture()
        del self.manifest['documents'][path]
        self.save()
        _, report = self.run_tool('--audit')
        self.assertIn(path, report['unregistered'])
        self.manifest['documents'][path] = entry
        self.save()
        target.write_text('Changed notice')
        _, report = self.run_tool('--audit')
        self.assertTrue(any('Protected content changed' in e for e in report['errors']), report)

    def test_vendor_reference_paths_cannot_escape_or_hide_arbitrary_links(self):
        _, _, entry = self.vendor_fixture()
        for value in ['../secret', '/absolute', 'a/../../x', 'a//b', 'a/./b', '%2e%2e/x',
                      'https://example.com/x', 'a\\b', '*', 'a?query', 'a#fragment']:
            with self.subTest(value=value):
                entry['vendor_notice']['upstream_relative_references'] = [value]
                self.save()
                _, report = self.run_tool('--audit')
                self.assertTrue(any('normalized and relative' in e for e in report['errors']), report)

    def test_vendor_archive_provenance_rejects_credentials_unknown_hosts_and_members(self):
        _, _, entry = self.vendor_fixture()
        for value in ['https://user:secret@api.nuget.org/x', 'https://example.com/x',
                      'http://api.nuget.org/x', 'https://api.nuget.org/v3-flatcontainer/example/1.0.0/wrong.nupkg', None]:
            with self.subTest(value=value):
                entry['vendor_notice']['archive_url'] = value
                self.save()
                _, report = self.run_tool('--audit')
                self.assertTrue(report['errors'], report)
        entry['vendor_notice']['archive_url'] = 'https://api.nuget.org/v3-flatcontainer/example/1.0.0/example.1.0.0.nupkg'
        entry['vendor_notice']['member'] = '../NOTICE.txt'
        self.save()
        _, report = self.run_tool('--audit')
        self.assertTrue(any('normalized and relative' in e for e in report['errors']), report)

    def test_vendor_metadata_cannot_be_added_to_an_owned_review_document(self):
        _, _, entry = self.vendor_fixture()
        self.manifest['documents']['README.md']['vendor_notice'] = entry['vendor_notice']
        self.save()
        _, report = self.run_tool('--audit')
        self.assertTrue(report['errors'], report)

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
        path = self.root / 'docs/SOURCE_STATUS.json'
        data = json.loads(path.read_text())
        # This fixture must exercise a real draft -> merged transition even when
        # the repository's reviewed source-stage input already records merged.
        data['cleanup_state'] = 'draft'
        path.write_text(json.dumps(data))
        self.green()
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

    def test_registered_guide_rename_and_removal_refresh_generated_links(self):
        self.green()
        old, new = 'docs/old-guide.md', 'docs/new-guide.md'
        (self.root / old).write_text('# Guide\n[Overview](../README.md)\n')
        self.manifest['documents'][old] = {'policy': 'review', 'sources': ['app'], 'reviewed': {}}
        self.save()
        self.green()
        self.assertIn('old-guide.md', (self.root / 'docs/DOC_STATUS.md').read_text())
        subprocess.run(['git', '-C', str(self.root), 'add', '.'], check=True)
        subprocess.run(['git', '-C', str(self.root), 'mv', old, new], check=True)
        self.manifest['documents'][new] = self.manifest['documents'].pop(old)
        self.save()
        for action in ['rename', 'remove']:
            with self.subTest(action=action):
                if action == 'remove':
                    subprocess.run(['git', '-C', str(self.root), 'rm', '-q', '-f', new], check=True)
                    del self.manifest['documents'][new]
                    self.save()
                before = self.snapshot()
                for mode in ['--check', '--audit']:
                    result, report = self.run_tool(mode)
                    self.assertEqual(result.returncode, 1, result.stdout)
                    self.assertIn('docs/DOC_STATUS.md', report['generated_stale'])
                    self.assertEqual(before, self.snapshot(), mode + ' mutated files')
                result, report = self.run_tool('--write')
                self.assertEqual(result.returncode, 0, result.stdout)
                self.assertEqual(report['written'], ['docs/DOC_STATUS.md'])
                text = (self.root / 'docs/DOC_STATUS.md').read_text()
                self.assertNotIn('old-guide.md', text)
                self.assertEqual('new-guide.md' in text, action == 'rename')
                after = self.snapshot()
                for mode in ['--write', '--check', '--audit']:
                    result, _ = self.run_tool(mode)
                    self.assertEqual(result.returncode, 0, result.stdout)
                    self.assertEqual(after, self.snapshot())

    def test_rename_does_not_hide_broken_manual_links_or_write_any_output(self):
        self.green()
        old, new = 'docs/old-guide.md', 'docs/new-guide.md'
        (self.root / old).write_text('# Guide\n')
        self.manifest['documents'][old] = {'policy': 'review', 'sources': ['app'], 'reviewed': {}}
        self.save()
        self.green()
        readme = self.root / 'README.md'
        readme.write_text(readme.read_text() + '\n[Manual guide](docs/old-guide.md)\n')
        (self.root / old).rename(self.root / new)
        self.manifest['documents'][new] = self.manifest['documents'].pop(old)
        self.save()
        before = self.snapshot()
        result, report = self.run_tool('--write')
        self.assertEqual(result.returncode, 1)
        self.assertTrue(any('README.md: docs/old-guide.md' in error for error in report['errors']))
        self.assertEqual(before, self.snapshot())

    def test_manual_links_validate_prospective_generated_anchors_before_writes(self):
        self.green()
        status = self.root / 'docs/DOC_STATUS.md'
        status.write_text(status.read_text().replace(BEGIN, BEGIN + '\n## Obsolete generated heading\n'))
        readme = self.root / 'README.md'
        readme.write_text(readme.read_text() + '\n[Old anchor](docs/DOC_STATUS.md#obsolete-generated-heading)\n')
        before = self.snapshot()
        for mode in ['--check', '--audit', '--write']:
            result, report = self.run_tool(mode)
            self.assertEqual(result.returncode, 1, result.stdout)
            self.assertTrue(any('Missing local anchor' in error for error in report['errors']))
            self.assertEqual(before, self.snapshot())

    def test_output_named_directories_do_not_hide_authored_documentation(self):
        for tracked in [False, True]:
            for directory in ['qa', 'bin', 'obj', 'artifacts', 'node_modules', '.venv', 'venv', '__pycache__']:
                with self.subTest(tracked=tracked, directory=directory):
                    name = 'docs/' + directory + '/new-guide.md'
                    path = self.root / name
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_text('# QA guide\n[Broken](missing.md)\n')
                    if tracked:
                        subprocess.run(['git', '-C', str(self.root), 'add', '-f', name], check=True)
                    before = self.snapshot()
                    for mode in ['--audit', '--check', '--write']:
                        result, report = self.run_tool(mode)
                        self.assertEqual(result.returncode, 1, result.stdout)
                        self.assertIn(name, report['unregistered'])
                        self.assertEqual(before, self.snapshot())
                    self.manifest['documents'][name] = {'policy': 'review', 'sources': ['app'], 'reviewed': {}}
                    self.save()
                    before = self.snapshot()
                    result, report = self.run_tool('--write')
                    self.assertEqual(result.returncode, 1)
                    self.assertTrue(any(name + ': missing.md' in error for error in report['errors']))
                    self.assertEqual(before, self.snapshot())
                    if tracked:
                        subprocess.run(['git', '-C', str(self.root), 'rm', '-q', '-f', name], check=True)
                    else:
                        path.unlink()
                    del self.manifest['documents'][name]
                    self.save()

    def test_output_named_directories_do_not_hide_nonignored_source_changes(self):
        self.green()
        for directory in ['qa', 'bin', 'obj', 'artifacts', 'node_modules', '.venv', 'venv', '__pycache__']:
            with self.subTest(directory=directory):
                path = self.root / 'src' / directory / 'implementation.py'
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('value = 1\n')
                before = self.snapshot()
                result, report = self.run_tool('--check')
                self.assertEqual(result.returncode, 1)
                self.assertIn('README.md', report['review_required'])
                self.assertEqual(before, self.snapshot())
                self.acknowledge()
                result, _ = self.run_tool('--check')
                self.assertEqual(result.returncode, 0, result.stdout)
                path.write_text('value = 2\n')
                result, report = self.run_tool('--check')
                self.assertEqual(result.returncode, 1)
                self.assertIn('README.md', report['review_required'])
                self.acknowledge()

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
        # Real generated outputs are ignored by Git, not guessed by basename.
        (self.root / '.gitignore').write_text('src/bin/\n')
        self.manifest['source_groups']['facts'].append('.gitignore')
        self.save()
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
        # Force-tracking a previously ignored output makes it a real input.
        subprocess.run(['git', '-C', str(self.root), 'add', '-f', 'src/bin/output.py'], check=True)
        result, report = self.run_tool('--check')
        self.assertEqual(result.returncode, 1)
        self.assertIn('README.md', report['review_required'])


if __name__ == '__main__':
    unittest.main()
