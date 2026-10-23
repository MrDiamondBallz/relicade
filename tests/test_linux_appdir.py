"""Offline AppDir assembly and own-source launcher fixtures; no game execution."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
from unittest.mock import patch
import test_runtime_notices

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import build_linux_appdir as builder


class LinuxAppDirTests(unittest.TestCase):
    def setUp(self):
        self.fixture = test_runtime_notices.RuntimeNoticeTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.root
        self.fixture.manifest['own_source_files'].append('Relicade.Desktop')
        self.fixture.save()
        self.publish = self.fixture.publish()
        # This shell is our fixture, not an acquired game or reference binary.
        (self.publish / 'Relicade.Desktop').write_text('#!/bin/sh\nprintf "%s\\n" "$PWD" "$@"\n')
        os.chmod(self.publish / 'Relicade.Desktop', 0o744)
        self.output = self.root / 'RELICADE fixture.AppDir'

    def assemble(self, **kwargs):
        return builder.assemble(self.publish, self.output, bundle=self.fixture.bundle, **kwargs)

    def test_complete_layout_copies_every_input_and_existing_brand(self):
        report = self.assemble(epoch=123)
        payload = self.output / 'usr/lib/relicade'
        for path in self.publish.rglob('*'):
            if path.is_file():
                self.assertEqual(path.read_bytes(), (payload / path.relative_to(self.publish)).read_bytes())
        self.assertEqual((ROOT / 'native/Assets/relicade-icon.png').read_bytes(), (self.output / '.DirIcon').read_bytes())
        self.assertEqual((self.output / '.DirIcon').read_bytes(), (self.output / 'relicade.png').read_bytes())
        self.assertFalse(report['packaged_release'])
        self.assertEqual('not_accepted', report['platform_support'])
        self.assertEqual('not_accepted', report['distribution_rights'])
        self.assertEqual(0o755, (payload / 'Relicade.Desktop').stat().st_mode & 0o777)
        for relative, item in report['files'].items():
            path = self.output / relative
            self.assertEqual(item['sha256'], hashlib.sha256(path.read_bytes()).hexdigest())
            self.assertEqual(123, int(path.stat().st_mtime))
        self.assertEqual(report, json.loads((self.output / 'relicade-build.json').read_text()))

    def test_repeated_assembly_is_byte_mode_and_time_identical(self):
        self.assemble(epoch=456)
        original = self.output
        self.output = self.root / 'second.AppDir'
        self.assemble(epoch=456)
        def snapshot(root):
            return {p.relative_to(root).as_posix(): (p.read_bytes() if p.is_file() else None, p.stat().st_mode & 0o777, int(p.stat().st_mtime)) for p in root.rglob('*')}
        self.assertEqual(snapshot(original), snapshot(self.output))

    def test_launcher_preserves_cwd_and_literal_arguments_for_both_entry_points(self):
        self.assemble()
        working = self.root / 'caller'; working.mkdir()
        for entry in ['AppRun', 'usr/bin/relicade']:
            with self.subTest(entry=entry):
                result = subprocess.run([str(self.output / entry), 'one two', ';echo not-a-command', '--help'], cwd=working, capture_output=True, text=True, timeout=5)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual([str(working), 'one two', ';echo not-a-command', '--help'], result.stdout.splitlines())

    def test_direct_launcher_symlink_resolves_own_payload(self):
        self.assemble()
        link = self.root / 'shortcut'; link.symlink_to(self.output / 'AppRun')
        result = subprocess.run([str(link), 'literal'], cwd=self.root, capture_output=True, text=True, timeout=5)
        self.assertEqual(0, result.returncode)
        self.assertEqual([str(self.root), 'literal'], result.stdout.splitlines())

    def test_existing_output_bytes_remain_untouched(self):
        self.output.mkdir(); (self.output / 'keep').write_bytes(b'existing')
        with self.assertRaises(FileExistsError): self.assemble()
        self.assertEqual(b'existing', (self.output / 'keep').read_bytes())

    def test_empty_output_is_not_replaced(self):
        self.output.mkdir(); before = self.output.stat().st_ino
        with self.assertRaises(FileExistsError): self.assemble()
        self.assertEqual(before, self.output.stat().st_ino)

    def test_concurrent_empty_output_is_not_replaced_at_publication(self):
        publish_new = builder._publish_new
        def insert(staging, destination):
            destination.mkdir(); (destination / 'keep').write_bytes(b'new owner')
            return publish_new(staging, destination)
        with patch.object(builder, '_publish_new', insert), self.assertRaises(FileExistsError): self.assemble()
        self.assertEqual(b'new owner', (self.output / 'keep').read_bytes())
        self.assertEqual([], list(self.root.glob('.relicade-appdir-*')))

    def test_linked_output_parent_is_refused(self):
        link = self.root / 'linked'; link.symlink_to(self.root, target_is_directory=True)
        self.output = link / 'output'
        with self.assertRaises(ValueError): self.assemble()
        self.assertFalse((self.root / 'output').exists())

    def test_linked_input_is_refused(self):
        link = self.root / 'linked'; link.symlink_to(self.publish, target_is_directory=True)
        self.publish = link
        with self.assertRaises(ValueError): self.assemble()

    def test_corrupt_notice_or_asset_is_refused_before_output_creation(self):
        for relative in ['Example.dll', 'licenses/runtime/linux-x64/LICENSE']:
            with self.subTest(relative=relative):
                path = self.publish / relative; original = path.read_bytes(); path.write_bytes(b'changed')
                with self.assertRaises(ValueError): self.assemble()
                self.assertFalse(self.output.exists()); path.write_bytes(original)

    def test_special_or_linked_input_never_opens_or_copies(self):
        path = self.publish / 'Relicade.dll'
        path.unlink(); os.mkfifo(path)
        with self.assertRaises(ValueError): self.assemble()
        path.unlink(); path.symlink_to(self.publish / 'Example.dll')
        with self.assertRaises(ValueError): self.assemble()
        path.unlink(); os.link(self.publish / 'Example.dll', path)
        with self.assertRaises(ValueError): self.assemble()
        self.assertFalse(self.output.exists())

    def test_write_or_special_permission_bits_are_refused(self):
        for mode in [0o766, 0o4744, 0o2744, 0o1744]:
            with self.subTest(mode=mode):
                os.chmod(self.publish / 'Relicade.Desktop', mode)
                with self.assertRaises(ValueError): self.assemble()
                self.assertFalse(self.output.exists())

    def test_nonexecutable_host_is_refused(self):
        os.chmod(self.publish / 'Relicade.Desktop', 0o644)
        with self.assertRaises(ValueError): self.assemble()

    def test_missing_host_is_refused_even_if_manifest_omits_it(self):
        (self.publish / 'Relicade.Desktop').unlink()
        self.fixture.manifest['own_source_files'].remove('Relicade.Desktop'); self.fixture.save()
        shutil.copyfile(self.fixture.bundle / 'manifest.json', self.publish / 'licenses/runtime/linux-x64/manifest.json')
        with self.assertRaises(ValueError): self.assemble()

    def test_invalid_epoch_is_refused(self):
        for epoch in [-1, 2147483648, True, '123']:
            with self.subTest(epoch=epoch), self.assertRaises(ValueError): self.assemble(epoch=epoch)

    def test_output_inside_input_is_refused(self):
        self.output = self.publish / 'output'
        with self.assertRaises(ValueError): self.assemble()

    def test_copy_change_refuses_publication_and_cleans_only_owned_staging(self):
        original_hash = builder.notices._hash
        changed = False
        def observe(root, relative, *args):
            nonlocal changed
            result = original_hash(root, relative, *args)
            if root == self.publish and relative == 'Relicade.dll' and not changed:
                (root / relative).write_bytes(b'changed after snapshot'); changed = True
            return result
        with patch.object(builder.notices, '_hash', observe), self.assertRaises(ValueError): self.assemble()
        self.assertFalse(self.output.exists())
        self.assertEqual([], list(self.root.glob('.relicade-appdir-*')))
        self.assertEqual(b'changed after snapshot', (self.publish / 'Relicade.dll').read_bytes())

    def test_oversized_input_is_refused_without_reading_sparse_bytes(self):
        with (self.publish / 'Relicade.dll').open('wb') as stream: stream.truncate(513 * 1024 * 1024)
        with self.assertRaisesRegex(ValueError, 'assembly budget'): self.assemble()
        self.assertFalse(self.output.exists())

    def test_desktop_entry_has_no_unimplemented_file_associations(self):
        self.assemble()
        text = (self.output / 'io.github.mrdiamondballz.relicade.desktop').read_text()
        self.assertIn('Exec=relicade\n', text)
        self.assertIn('Icon=relicade\n', text)
        self.assertNotIn('%U', text)
        self.assertNotIn('MimeType=', text)
        self.assertEqual(1, len(list(self.output.glob('*.desktop'))))

    def test_concurrent_empty_directory_is_not_replaced(self):
        publish_new = builder._publish_new
        inode = []
        def insert(staging, destination):
            destination.mkdir(); inode.append(destination.stat().st_ino)
            return publish_new(staging, destination)
        with patch.object(builder, '_publish_new', insert), self.assertRaises(FileExistsError): self.assemble()
        self.assertEqual(inode[0], self.output.stat().st_ino)
        self.assertEqual([], list(self.output.iterdir()))
        self.assertEqual([], list(self.root.glob('.relicade-appdir-*')))

    def test_missing_atomic_publication_support_has_no_overwrite_fallback(self):
        with patch.object(builder.ctypes, 'CDLL', return_value=object()), self.assertRaisesRegex(ValueError, 'no-replace'):
            self.assemble()
        self.assertFalse(self.output.exists())
        self.assertEqual([], list(self.root.glob('.relicade-appdir-*')))

    def test_control_character_paths_are_refused(self):
        for name in ['line\nbreak.AppDir', 'tab\tname.AppDir']:
            self.output = self.root / name
            with self.subTest(name=name), self.assertRaises(ValueError): self.assemble()
            self.assertFalse(self.output.exists())
