"""Offline product metadata contract; no desktop or software-center execution."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]

class AppStreamMetadataTests(unittest.TestCase):
    def metadata(self):
        return ET.parse(ROOT / 'native/packaging/linux/io.github.mrdiamondballz.relicade.appdata.xml').getroot()

    def test_license_grant_is_metadata_only_and_product_rights_remain_mixed(self):
        root = self.metadata()
        self.assertEqual('CC0-1.0', root.findtext('metadata_license'))
        self.assertEqual('MIT AND LicenseRef-proprietary', root.findtext('project_license'))
        text = (ROOT / 'native/packaging/linux/io.github.mrdiamondballz.relicade.appdata.xml').read_text()
        self.assertIn('only to this AppStream XML', text)
        self.assertIn('not application code, logos, artwork', text)

    def test_original_notice_is_preserved_before_the_narrow_exception(self):
        import hashlib
        text = (ROOT / 'NOTICE.md').read_bytes()
        marker = b'\n## AppStream metadata exception\n'
        self.assertEqual(1, text.count(marker))
        old, addition = text.split(marker)
        self.assertEqual('817c99a8816736a9534933b4d094d02fb66126e28af04cdf13a2031a694fbd67',
                         hashlib.sha256(old).hexdigest())
        self.assertIn(b'does not apply to application code, logos, artwork', addition)

    def test_identity_launchable_and_honest_development_description(self):
        root = self.metadata()
        self.assertEqual('desktop-application', root.attrib['type'])
        self.assertEqual('io.github.mrdiamondballz.relicade', root.findtext('id'))
        self.assertEqual('RELICADE', root.findtext('name'))
        self.assertEqual('io.github.mrdiamondballz.relicade.desktop', root.findtext('launchable'))
        text = ''.join(root.find('description').itertext())
        self.assertIn('development', text)
        self.assertIn('not a verified release', text)
        self.assertIsNone(root.find('releases'))
        self.assertIsNone(root.find('screenshots'))


class AppStreamPackagingTests(unittest.TestCase):
    def setUp(self):
        import sys
        sys.path.insert(0, str(ROOT / 'tools'))
        import build_linux_appdir, build_linux_appimage
        self.appdir = build_linux_appdir
        self.image = build_linux_appimage

    def test_known_runtime_notices_stay_partial_and_match_the_asset(self):
        import hashlib, json
        files = self.appdir.packaging_metadata()
        prefix = 'usr/share/doc/relicade/appimage-runtime/'
        manifest = json.loads(files[prefix + 'manifest.json'])
        self.assertEqual('not_accepted', manifest['distribution_rights'])
        self.assertEqual(3, len(manifest['notices']))
        self.assertEqual(3, len(manifest['unresolved']))
        for name, item in manifest['notices'].items():
            self.assertEqual(item['sha256'], hashlib.sha256(files[prefix + name]).hexdigest())
        self.assertIn('mimalloc', ' '.join(manifest['unresolved']))

    def test_corrupt_or_missing_notice_prevents_assembly_inputs(self):
        from unittest.mock import patch
        real = self.appdir.notices._bytes
        for bad in ['type2-runtime-LICENSE.txt', 'libfuse-LGPL2.txt', 'squashfuse-LICENSE.txt']:
            def changed(root, relative, *args):
                return b'changed' if relative == bad else real(root, relative, *args)
            with self.subTest(bad=bad), patch.object(self.appdir.notices, '_bytes', side_effect=changed):
                with self.assertRaises(ValueError): self.appdir.packaging_metadata()

    def test_wrong_runtime_or_promoted_rights_are_refused(self):
        import json
        from unittest.mock import patch
        real = self.appdir.notices._bytes
        for field, value in [('runtime_sha256', '0' * 64), ('runtime_source_commit', '0' * 40),
                             ('distribution_rights', 'accepted'), ('unresolved', [])]:
            def changed(root, relative, *args):
                data = real(root, relative, *args)
                if relative == 'manifest.json' and root.name == 'runtime-notices':
                    obj = json.loads(data); obj[field] = value; return json.dumps(obj).encode()
                return data
            with self.subTest(field=field), patch.object(self.appdir.notices, '_bytes', side_effect=changed):
                with self.assertRaises(ValueError): self.appdir.packaging_metadata()

    def test_validator_runs_without_network_flags_and_keeps_diagnostics(self):
        import tempfile, os, hashlib
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            validator = root / 'source-validator'
            validator.write_text('#!/bin/sh\n[ "$1" = validate-tree ] && [ "$2" = --no-net ] && [ "$3" = "$HOME/image" ]\n')
            os.chmod(validator, 0o755)
            with self.image._staging(root) as staging:
                result = self.image._validate_appstream(root / 'image', validator,
                    {'PATH': '/usr/bin:/bin', 'HOME': str(root)}, staging, 5)
                self.assertEqual(hashlib.sha256(validator.read_bytes()).hexdigest(), result)
                self.assertTrue((staging / 'metadata-validation.log').is_file())
                self.assertFalse((staging / self.image.PROCESS_MARKER).exists())

    def test_validator_missing_or_failed_cannot_be_reported_as_passed(self):
        import tempfile
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            with self.image._staging(root) as staging:
                with self.assertRaises(ValueError):
                    self.image._validate_appstream(root, None, {}, staging, 5)
            validator = root / 'reject'
            validator.write_text('#!/bin/sh\necho invalid-metadata\nexit 7\n')
            with self.image._staging(root) as staging:
                with self.assertRaises(ValueError):
                    self.image._validate_appstream(root, validator, {'PATH': '/usr/bin:/bin'}, staging, 5)
                self.assertIn('invalid-metadata', (staging / 'tool.log').read_text())
                self.assertFalse((staging / self.image.PROCESS_MARKER).exists())

if __name__ == '__main__': unittest.main()
