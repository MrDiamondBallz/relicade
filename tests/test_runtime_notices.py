"""Offline notice inventory checks; all executable-looking fixture files are text."""
import base64
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('runtime_notices', ROOT / 'tools/check_runtime_notices.py')
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class RuntimeNoticeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.bundle = self.root / 'bundle'
        self.bundle.mkdir()
        self.payload = b'Synthetic notice fixture, not a third-party license.\n'
        (self.bundle / 'LICENSE').write_bytes(self.payload)
        blob = hashlib.sha1(b'blob ' + str(len(self.payload)).encode() + b'\0' + self.payload).hexdigest()
        self.canonical = base64.b64encode(b'c' * 64).decode()
        self.asset = b'synthetic library bytes; never executed'
        self.manifest = {
            'schema_version': 1, 'runtime_identifier': 'linux-x64',
            'target_framework': '.NETCoreApp,Version=v10.0', 'sdk_version': '10.0.402',
            'scope': 'Synthetic fixture',
            'notices': {'LICENSE': {'sha256': hashlib.sha256(self.payload).hexdigest(), 'origin': {
                'kind': 'git', 'repository': 'example/fixture', 'commit': 'a' * 40,
                'path': 'LICENSE', 'blob_sha1': blob,
                'url': 'https://github.com/example/fixture/blob/' + 'a' * 40 + '/LICENSE'}}},
            'packages': [{'identity': 'Example/1.0.0', 'kind': 'package',
                'declared_package_license': 'MIT', 'deps_sha512': 'sha512-' + self.canonical,
                'raw_archive_sha256': 'b' * 64, 'canonical_content_sha512': self.canonical,
                'notices': ['LICENSE'], 'assets': [{'deps_path': 'lib/net10.0/Example.dll',
                    'package_member': 'lib/net10.0/Example.dll', 'published_path': 'Example.dll',
                    'sha256': hashlib.sha256(self.asset).hexdigest(), 'kind': 'runtime'}]}],
            'additional_components': [], 'own_source_files': ['Relicade.Desktop.deps.json', 'Relicade.dll'],
            'own_metadata_identities': ['Relicade/0.1.0'], 'qualification': ['No acceptance']}
        self.save()

    def save(self):
        (self.bundle / 'manifest.json').write_text(json.dumps(self.manifest))

    def publish(self):
        root = self.root / 'publish'
        root.mkdir()
        (root / 'Example.dll').write_bytes(self.asset)
        (root / 'Relicade.dll').write_bytes(b'own-source synthetic bytes')
        target = '.NETCoreApp,Version=v10.0/linux-x64'
        deps = {'runtimeTarget': {'name': target}, 'libraries': {
            'Example/1.0.0': {'type': 'package', 'sha512': 'sha512-' + self.canonical},
            'Relicade/0.1.0': {'type': 'project', 'sha512': ''}},
            'targets': {target: {'Example/1.0.0': {'runtime': {'lib/net10.0/Example.dll': {}}}}}}
        (root / 'Relicade.Desktop.deps.json').write_text(json.dumps(deps))
        shutil.copytree(self.bundle, root / checker.BUNDLE_DESTINATION)
        return root

    def change_deps(self, root, edit):
        path = root / 'Relicade.Desktop.deps.json'
        value = json.loads(path.read_text())
        edit(value)
        path.write_text(json.dumps(value))

    def test_actual_source_bundle_is_scoped_and_unaccepted(self):
        result = checker.validate()
        self.assertEqual((25, 225, 25), (result['packages'], result['runtime_assets'], result['notice_files']))
        self.assertEqual('not_accepted', result['distribution_rights'])
        self.assertEqual('not_accepted', result['platform_support'])
        self.assertFalse(result['published_output_checked'])

    def test_vendor_document_classification_matches_the_actual_bundle_origins(self):
        registry = json.loads((ROOT / 'docs/DOCUMENTATION.json').read_text())
        bundle = json.loads((ROOT / 'native/licenses/linux-x64/manifest.json').read_text())
        found = []
        for path, entry in registry['documents'].items():
            if 'vendor_notice' not in entry:
                continue
            relative = path.removeprefix('native/licenses/linux-x64/')
            notice = bundle['notices'][relative]
            origin = notice['origin']
            declaration = entry['vendor_notice']
            self.assertEqual(notice['sha256'], entry['sha256'])
            self.assertEqual(origin['raw_archive_sha256'], declaration['archive_sha256'])
            self.assertEqual(origin['member'], declaration['member'])
            name, version = origin['identity'].lower().split('/')
            self.assertEqual(f'https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg',
                             declaration['archive_url'])
            found.append(path)
        self.assertEqual(2, len(found))

    def test_git_storage_preserves_every_original_notice_byte(self):
        bundle = ROOT / 'native/licenses/linux-x64'
        manifest = json.loads((bundle / 'manifest.json').read_text())
        for relative in manifest['notices']:
            with self.subTest(notice=relative):
                path = (bundle / relative).relative_to(ROOT).as_posix()
                raw = (ROOT / path).read_bytes()
                expected = hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest()
                actual = subprocess.run(['git', '-C', str(ROOT), 'hash-object', '--path', path, path],
                                        check=True, capture_output=True, text=True, timeout=10).stdout.strip()
                self.assertEqual(expected, actual)

    def test_valid_source_and_publish_keep_acceptance_separate(self):
        self.assertTrue(checker.validate(self.bundle)['ok'])
        result = checker.validate(self.bundle, self.publish())
        self.assertTrue(result['published_output_checked'])
        self.assertEqual('not_accepted', result['distribution_rights'])

    def test_changed_notice_is_rejected(self):
        (self.bundle / 'LICENSE').write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError, 'Changed notice'):
            checker.validate(self.bundle)

    def test_relabeling_changed_bytes_cannot_keep_original_git_provenance(self):
        (self.bundle / 'LICENSE').write_bytes(b'changed')
        self.manifest['notices']['LICENSE']['sha256'] = hashlib.sha256(b'changed').hexdigest()
        self.save()
        with self.assertRaisesRegex(ValueError, 'pinned Git blob'):
            checker.validate(self.bundle)

    def test_source_url_must_match_exact_public_commit(self):
        self.manifest['notices']['LICENSE']['origin']['url'] = 'https://user:secret@github.com/example/fixture'
        self.save()
        with self.assertRaisesRegex(ValueError, 'Upstream URL'):
            checker.validate(self.bundle)

    def test_duplicate_json_keys_are_rejected(self):
        path = self.bundle / 'manifest.json'
        path.write_text(path.read_text().replace('"schema_version": 1', '"schema_version": 1, "schema_version": 1'))
        with self.assertRaisesRegex(ValueError, 'Duplicate JSON'):
            checker.validate(self.bundle)

    def test_traversal_is_rejected_before_reading(self):
        self.manifest['notices']['../LICENSE'] = self.manifest['notices'].pop('LICENSE')
        self.save()
        with self.assertRaisesRegex(ValueError, 'relative inventory path'):
            checker.validate(self.bundle)

    def test_symlink_notice_is_rejected(self):
        path = self.bundle / 'LICENSE'
        path.rename(self.root / 'retained')
        path.symlink_to(self.root / 'retained')
        with self.assertRaisesRegex(ValueError, 'regular unlinked'):
            checker.validate(self.bundle)
        self.assertEqual(self.payload, (self.root / 'retained').read_bytes())

    def test_hardlinked_notice_is_rejected(self):
        os.link(self.bundle / 'LICENSE', self.root / 'retained')
        with self.assertRaisesRegex(ValueError, 'regular unlinked'):
            checker.validate(self.bundle)

    @unittest.skipUnless(hasattr(os, 'mkfifo'), 'Named-pipe fixture requires POSIX')
    def test_named_pipe_notice_is_rejected_before_open(self):
        (self.bundle / 'LICENSE').unlink()
        os.mkfifo(self.bundle / 'LICENSE')
        with self.assertRaisesRegex(ValueError, 'regular unlinked'):
            checker.validate(self.bundle)

    def test_linked_bundle_ancestor_is_rejected(self):
        alias = self.root / 'alias'
        alias.symlink_to(self.bundle, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, 'Linked path ancestor'):
            checker.validate(alias)

    def test_changed_published_runtime_asset_is_rejected(self):
        root = self.publish()
        (root / 'Example.dll').write_bytes(b'other bytes')
        with self.assertRaisesRegex(ValueError, 'runtime asset changed'):
            checker.validate(self.bundle, root)

    def test_changed_published_notice_is_rejected(self):
        root = self.publish()
        (root / checker.BUNDLE_DESTINATION / 'LICENSE').write_bytes(b'other notice')
        with self.assertRaisesRegex(ValueError, 'Published notice differs'):
            checker.validate(self.bundle, root)

    def test_missing_published_notice_is_not_a_pass(self):
        root = self.publish()
        (root / checker.BUNDLE_DESTINATION / 'LICENSE').unlink()
        with self.assertRaises(FileNotFoundError):
            checker.validate(self.bundle, root)

    def test_unknown_output_file_is_rejected(self):
        root = self.publish()
        (root / 'unmapped.dll').write_bytes(b'synthetic')
        with self.assertRaisesRegex(ValueError, 'unmapped files'):
            checker.validate(self.bundle, root)

    def test_new_dependency_requires_inventory_reconciliation(self):
        root = self.publish()
        self.change_deps(root, lambda d: d['libraries'].update({'New/1.0.0': {'type': 'package', 'sha512': ''}}))
        with self.assertRaisesRegex(ValueError, 'dependency identities'):
            checker.validate(self.bundle, root)

    def test_changed_canonical_dependency_hash_is_rejected(self):
        root = self.publish()
        self.change_deps(root, lambda d: d['libraries']['Example/1.0.0'].update(sha512='sha512-' + base64.b64encode(b'x' * 64).decode()))
        with self.assertRaisesRegex(ValueError, 'canonical dependency metadata'):
            checker.validate(self.bundle, root)

    def test_other_platform_is_not_certified_by_linux_inventory(self):
        root = self.publish()
        self.change_deps(root, lambda d: d['runtimeTarget'].update(name='.NETCoreApp,Version=v10.0/linux-arm64'))
        with self.assertRaisesRegex(ValueError, 'runtime target differs'):
            checker.validate(self.bundle, root)

    def test_changed_deps_asset_mapping_is_rejected(self):
        root = self.publish()
        target = '.NETCoreApp,Version=v10.0/linux-x64'
        self.change_deps(root, lambda d: d['targets'][target]['Example/1.0.0'].update(runtime={'elsewhere.dll': {}}))
        with self.assertRaisesRegex(ValueError, 'asset mapping changed'):
            checker.validate(self.bundle, root)

    def test_raw_archive_and_canonical_content_hashes_are_not_conflated(self):
        self.assertNotEqual(self.manifest['packages'][0]['raw_archive_sha256'], self.canonical)
        self.assertTrue(checker.validate(self.bundle, self.publish())['ok'])

    def test_absent_runtimepack_hash_remains_scoped_unknown(self):
        self.manifest['packages'][0].update(kind='runtimepack', deps_sha512='')
        self.save()
        result = checker.validate(self.bundle)
        self.assertEqual('not_accepted', result['distribution_rights'])

    def test_empty_package_hash_is_not_treated_like_runtimepack_metadata(self):
        self.manifest['packages'][0]['deps_sha512'] = ''
        self.save()
        with self.assertRaisesRegex(ValueError, 'Canonical hash mismatch'):
            checker.validate(self.bundle)

    def test_unbound_notice_reference_is_rejected(self):
        self.manifest['packages'][0]['notices'] = ['missing.txt']
        self.save()
        with self.assertRaisesRegex(ValueError, 'notice reference'):
            checker.validate(self.bundle)

    def test_foreign_reference_assembly_is_not_mistaken_for_own_alias(self):
        root = self.publish()
        self.change_deps(root, lambda d: d['libraries'].update({'External/1.0.0': {'type': 'reference', 'sha512': ''}}))
        with self.assertRaisesRegex(ValueError, 'dependency identities'):
            checker.validate(self.bundle, root)

    @unittest.skipUnless(hasattr(os, 'mkfifo'), 'Named-pipe fixture requires POSIX')
    def test_named_pipe_own_output_is_rejected_before_open(self):
        root = self.publish()
        (root / 'Relicade.dll').unlink()
        os.mkfifo(root / 'Relicade.dll')
        with self.assertRaisesRegex(ValueError, 'regular unlinked'):
            checker.validate(self.bundle, root)

    def test_sdk_apphost_bytes_are_checked_separately_from_own_code(self):
        host = b'synthetic SDK host; never executed'
        digest = hashlib.sha256(host).hexdigest()
        self.manifest['additional_components'] = [{
            'kind': 'sdk-apphost', 'identity': 'ExampleHost/1.0.0',
            'source_member': 'native/apphost', 'source_sha256': 'c' * 64,
            'published_paths': ['Relicade'], 'published_sha256': {'Relicade': digest},
            'notices': ['LICENSE']}]
        self.save()
        root = self.publish()
        (root / 'Relicade').write_bytes(host)
        self.assertTrue(checker.validate(self.bundle, root)['ok'])
        (root / 'Relicade').write_bytes(b'changed host')
        with self.assertRaisesRegex(ValueError, 'SDK apphost changed'):
            checker.validate(self.bundle, root)

    def test_notice_nuget_origin_must_match_package_provenance(self):
        self.manifest['notices']['LICENSE']['origin'] = {
            'kind': 'nuget', 'identity': 'Example/1.0.0', 'member': 'LICENSE',
            'raw_archive_sha256': 'b' * 64, 'canonical_content_sha512': self.canonical}
        self.save()
        self.assertTrue(checker.validate(self.bundle)['ok'])
        self.manifest['notices']['LICENSE']['origin']['raw_archive_sha256'] = 'd' * 64
        self.save()
        with self.assertRaisesRegex(ValueError, 'Notice package provenance'):
            checker.validate(self.bundle)

    def test_boolean_schema_version_is_not_integer_version_one(self):
        self.manifest['schema_version'] = True
        self.save()
        with self.assertRaisesRegex(ValueError, 'schema'):
            checker.validate(self.bundle)

    def test_missing_and_duplicate_package_notice_references_are_rejected(self):
        for references in [[], ['LICENSE', 'LICENSE']]:
            with self.subTest(references=references):
                self.manifest['packages'][0]['notices'] = references
                self.save()
                with self.assertRaisesRegex(ValueError, 'notice reference'):
                    checker.validate(self.bundle)

    def test_raw_archive_hash_cannot_replace_canonical_metadata(self):
        self.manifest['packages'][0]['deps_sha512'] = 'sha512-' + base64.b64encode(b'r' * 64).decode()
        self.save()
        with self.assertRaisesRegex(ValueError, 'Canonical hash mismatch'):
            checker.validate(self.bundle)

    def test_unmapped_source_notice_is_not_silently_copied(self):
        (self.bundle / 'unknown.txt').write_text('unreviewed notice')
        with self.assertRaisesRegex(ValueError, 'unmapped files'):
            checker.validate(self.bundle)

    def test_duplicate_own_source_inventory_is_not_collapsed(self):
        for field in ['own_source_files', 'own_metadata_identities']:
            with self.subTest(field=field):
                original = list(self.manifest[field])
                self.manifest[field].append(original[0])
                self.save()
                with self.assertRaisesRegex(ValueError, 'duplicate own-source'):
                    checker.validate(self.bundle)
                self.manifest[field] = original

    def test_linux_copy_items_preserve_existing_notice_destinations(self):
        project = ET.parse(ROOT / 'native/Directory.Build.props')
        groups = [g for g in project.findall('ItemGroup') if g.get('Condition') == "'$(RuntimeIdentifier)' == 'linux-x64'"]
        self.assertEqual(1, len(groups))
        item = groups[0].find('None')
        self.assertEqual('$(MSBuildThisFileDirectory)licenses/linux-x64/**/*', item.get('Include'))
        self.assertEqual('licenses/runtime/linux-x64/%(RecursiveDir)%(Filename)%(Extension)', item.get('Link'))
        self.assertEqual('PreserveNewest', item.get('CopyToPublishDirectory'))
        inherited = {n.get('Link') for n in project.findall('./ItemGroup/None') if n.get('Link') in {'licenses/Native-MIT.txt', 'licenses/Core-MIT.txt', 'licenses/THIRD_PARTY_NOTICES.md', 'licenses/Upstream-TRADEMARKS.md'}}
        self.assertEqual(4, len(inherited))


if __name__ == '__main__':
    unittest.main()
