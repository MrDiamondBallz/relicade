"""Pinned-tool orchestration with synthetic containers, never acquired games."""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import unittest
from unittest.mock import Mock, patch
import test_linux_appdir

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
import build_linux_appimage as builder


class LinuxAppImageTests(unittest.TestCase):
    def setUp(self):
        self.fixture = test_linux_appdir.LinuxAppDirTests()
        self.fixture.setUp(); self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.root
        self.tool = self.root / 'tool'; self.tool.write_bytes(b'non-executable synthetic tool')
        self.runtime = self.root / 'runtime'; self.runtime.write_bytes(b'synthetic runtime')
        self.output = self.root / 'output.AppImage'
        self.pins = [{'repository': name, 'size': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()} for name, path in [('AppImage/appimagetool', self.tool), ('AppImage/type2-runtime', self.runtime)]]
        self.container = b'\x7fELF' + b'\0' * 4 + b'AI\x02' + b'\0' * 90
        actual_assemble = builder.appdir.assemble
        def assemble(source, destination, epoch):
            return actual_assemble(source, destination, epoch, bundle=self.fixture.fixture.bundle)
        self.stack = contextlib.ExitStack(); self.addCleanup(self.stack.close)
        self.stack.enter_context(patch.object(builder, '_pins', return_value=self.pins))
        self.stack.enter_context(patch.object(builder.appdir, 'assemble', side_effect=assemble))
        self.run = self.stack.enter_context(patch.object(builder, '_run', side_effect=self.complete))

    def complete(self, command, environment, directory, timeout):
        self.assertEqual(['--no-appstream', '--runtime-file'], command[1:3])
        self.assertIn('--mksquashfs-opt=-processors', command)
        self.assertIn('--mksquashfs-opt=1', command)
        self.assertEqual(str(directory / 'runtime'), command[3])
        self.assertNotIn('GH_TOKEN', environment)
        self.assertNotIn('VERSION', environment)
        self.assertNotIn('--guess', command)
        self.assertNotIn('--sign', command)
        self.assertEqual('1', environment['APPIMAGE_EXTRACT_AND_RUN'])
        self.assertEqual('0', environment['SOURCE_DATE_EPOCH'])
        Path(command[-1]).write_bytes(self.container)

    def package(self, **kwargs):
        return builder.package(self.fixture.publish, self.output, self.tool, self.runtime, **kwargs)

    def test_pinned_local_tools_clean_environment_and_exclusive_output(self):
        with patch.dict(os.environ, {'GH_TOKEN': 'synthetic-do-not-forward', 'VERSION': 'unexpected'}):
            result = self.package()
        self.assertEqual(self.container, self.output.read_bytes())
        self.assertEqual(1, self.output.stat().st_nlink)
        self.assertEqual(0o755, self.output.stat().st_mode & 0o777)
        self.assertEqual(hashlib.sha256(self.container).hexdigest(), result['sha256'])
        self.assertFalse(result['packaged_release'])
        self.assertEqual('not_accepted', result['distribution_rights'])
        self.assertEqual('not_accepted', result['platform_support'])
        self.assertEqual([], list(self.root.glob('.relicade-image-*')))

    def test_changed_tool_and_runtime_refuse_before_execution(self):
        for path in [self.tool, self.runtime]:
            with self.subTest(path=path.name):
                original = path.read_bytes(); path.write_bytes(b'changed')
                with self.assertRaises(ValueError): self.package()
                path.write_bytes(original)
        self.run.assert_not_called(); self.assertFalse(self.output.exists())

    def test_linked_tool_refuses_before_execution(self):
        self.tool.unlink(); self.tool.symlink_to(self.runtime)
        with self.assertRaises(ValueError): self.package()
        self.run.assert_not_called()

    def test_existing_output_is_preserved(self):
        self.output.write_bytes(b'keep')
        with self.assertRaises(FileExistsError): self.package()
        self.run.assert_not_called(); self.assertEqual(b'keep', self.output.read_bytes())

    def test_output_created_during_build_is_preserved(self):
        def race(*args):
            self.complete(*args); self.output.write_bytes(b'other owner')
        self.run.side_effect = race
        with self.assertRaises(FileExistsError): self.package()
        self.assertEqual(b'other owner', self.output.read_bytes())
        self.assertEqual([], list(self.root.glob('.relicade-image-*')))

    def test_wrong_container_is_not_published(self):
        self.container = b'not an AppImage' * 10
        with self.assertRaises(ValueError): self.package()
        self.assertFalse(self.output.exists())

    def test_linked_container_is_not_published(self):
        def linked(command, *args): Path(command[-1]).symlink_to(self.tool)
        self.run.side_effect = linked
        with self.assertRaises(ValueError): self.package()
        self.assertFalse(self.output.exists())

    def test_failure_keeps_no_final_image(self):
        self.run.side_effect = ValueError('synthetic tool failure')
        with self.assertRaises(ValueError): self.package()
        self.assertFalse(self.output.exists())
        self.assertEqual([], list(self.root.glob('.relicade-image-*')))

    def test_undrained_child_retains_staging_without_final_image(self):
        self.run.side_effect = builder.UndrainedBuild('synthetic pending child')
        with self.assertRaises(builder.UndrainedBuild): self.package()
        self.assertFalse(self.output.exists())
        self.assertEqual(1, len(list(self.root.glob('.relicade-image-*'))))

    def test_output_inside_publish_is_refused_before_build(self):
        self.output = self.fixture.publish / 'nested.AppImage'
        with self.assertRaises(ValueError): self.package()
        self.run.assert_not_called()
        self.assertEqual([], list(self.fixture.publish.glob('.relicade-image-*')))

    def test_timeout_is_bounded(self):
        for value in [0, 301, True]:
            with self.subTest(value=value), self.assertRaises(ValueError): self.package(timeout=value)
        self.run.assert_not_called()


class LinuxAppImageProcessTests(unittest.TestCase):
    def test_own_source_failure_preserves_diagnostic_and_exit(self):
        import tempfile
        with tempfile.TemporaryDirectory() as root, contextlib.redirect_stderr(io.StringIO()) as diagnostic:
            with self.assertRaisesRegex(ValueError, 'exit 7'):
                builder._run([sys.executable, '-c', 'print("controlled failure", flush=True); raise SystemExit(7)'], {'PATH': '/usr/bin:/bin'}, Path(root), 5)
            self.assertIn('controlled failure', diagnostic.getvalue())
            self.assertIn('controlled failure', (Path(root) / 'tool.log').read_text())

    def test_own_source_blocking_child_is_cancelled_and_drained(self):
        import tempfile
        with tempfile.TemporaryDirectory() as root, contextlib.redirect_stderr(io.StringIO()) as diagnostic:
            with self.assertRaises(subprocess.TimeoutExpired):
                builder._run([sys.executable, '-c', 'import signal; print("waiting for signal", flush=True); signal.pause()'], {'PATH': '/usr/bin:/bin'}, Path(root), 1)
            self.assertIn('waiting for signal', diagnostic.getvalue())

    def test_unconfirmed_drain_is_reported_not_treated_as_safe_cleanup(self):
        import tempfile
        child = Mock(pid=12345)
        child.wait.side_effect = [subprocess.TimeoutExpired('fixture', 1), subprocess.TimeoutExpired('fixture', 5), subprocess.TimeoutExpired('fixture', 5)]
        child.poll.return_value = None
        with tempfile.TemporaryDirectory() as root, patch.object(builder.subprocess, 'Popen', return_value=child), patch.object(builder.os, 'killpg') as kill, patch.object(builder, '_wait_for_group', side_effect=subprocess.TimeoutExpired('fixture group', 5)), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(builder.UndrainedBuild): builder._run(['fixture'], {}, Path(root), 1)
            self.assertEqual([(12345, signal.SIGTERM), (12345, signal.SIGKILL)], [c.args for c in kill.call_args_list])

    def test_official_metadata_is_explicit_and_version_bound(self):
        pins = builder._pins()
        self.assertEqual(['1.9.1', '20251108'], [p['tag'] for p in pins])
        self.assertEqual('ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0', pins[0]['sha256'])
        self.assertEqual('2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d', pins[1]['sha256'])
        for pin in pins:
            self.assertEqual(40, len(pin['source_commit']))
            self.assertEqual('https://github.com/' + pin['repository'] + '/releases/download/' + pin['tag'] + '/' + pin['name'], pin['url'])


@unittest.skipUnless(sys.platform == 'linux', 'Linux process-group build host required')
class LinuxAppImageGroupLifetimeTests(unittest.TestCase):
    def exercise(self, mode):
        import ctypes
        import tempfile
        library = ctypes.CDLL(None, use_errno=True)
        previous = ctypes.c_int()
        self.assertEqual(0, library.prctl(37, ctypes.byref(previous), 0, 0, 0))
        self.assertEqual(0, library.prctl(36, 1, 0, 0, 0))
        child = None
        code = '''import os,signal,sys
read,write=os.pipe()
pid=os.fork()
if pid==0:
 os.close(read)
 signal.signal(signal.SIGTERM,signal.SIG_IGN)
 with open(sys.argv[1],"w") as f: f.write(str(os.getpid()))
 os.write(write,b"r");os.close(write)
 if sys.argv[2]=="joined": os._exit(0)
 while True: signal.pause()
os.close(write);os.read(read,1);os.close(read)
if sys.argv[2]=="joined": os.waitpid(pid,0);os._exit(0)
if sys.argv[2]=="success": os._exit(0)
while True: signal.pause()
'''
        try:
            with tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                pidfile = root / 'descendant.pid'
                outcome = 'returned'
                try:
                    try:
                        with contextlib.redirect_stderr(io.StringIO()), builder._staging(root) as staging:
                            builder._run([sys.executable, '-c', code, str(pidfile), mode],
                                         {'PATH': '/usr/bin:/bin'}, staging, 1)
                    except (subprocess.TimeoutExpired, builder.UndrainedBuild) as exc:
                        outcome = type(exc).__name__
                    self.assertTrue(pidfile.exists(), 'Owned descendant did not signal readiness')
                    child = int(pidfile.read_text())
                    proc = Path('/proc') / str(child) / 'stat'
                    live = proc.exists() and proc.read_text().rsplit(') ', 1)[1].split()[0] != 'Z'
                    self.assertFalse(live, 'Owned descendant survived bounded group shutdown')
                    if mode == 'joined':
                        self.assertEqual('returned', outcome)
                        self.assertFalse(staging.exists())
                    else:
                        # This test adopts but intentionally has not yet reaped the child.
                        # A group still present in the kernel is not confirmed drained.
                        self.assertEqual('UndrainedBuild', outcome)
                        self.assertTrue(staging.exists())
                finally:
                    if child is None and pidfile.exists():
                        child = int(pidfile.read_text())
                    if child is not None:
                        try: os.kill(child, signal.SIGKILL)
                        except ProcessLookupError: pass
                        try: os.waitpid(child, 0)
                        except ChildProcessError: pass
                        self.assertFalse((Path('/proc') / str(child)).exists())
        finally:
            self.assertEqual(0, library.prctl(36, previous.value, 0, 0, 0))

    def test_timeout_does_not_clean_staging_while_descendant_group_remains(self):
        self.exercise('timeout')

    def test_successful_leader_does_not_prove_descendant_group_is_finished(self):
        self.exercise('success')

    def test_normally_joined_descendant_group_allows_success_and_cleanup(self):
        self.exercise('joined')

    def test_interrupted_shutdown_and_unreadable_diagnostic_retain_staging(self):
        import tempfile
        child = Mock(pid=12345)
        child.wait.side_effect = subprocess.TimeoutExpired('fixture', 1)
        with tempfile.TemporaryDirectory() as folder:
            with patch.object(builder.subprocess, 'Popen', return_value=child), patch.object(builder.os, 'killpg'), patch.object(builder, '_wait_for_group', side_effect=KeyboardInterrupt), patch.object(Path, 'read_text', side_effect=OSError('synthetic diagnostic failure')), contextlib.redirect_stderr(io.StringIO()) as diagnostic:
                with self.assertRaises(builder.UndrainedBuild), builder._staging(Path(folder)) as staging:
                    builder._run(['fixture'], {}, staging, 1)
                self.assertTrue(staging.exists())
                self.assertIn('Could not read build diagnostic', diagnostic.getvalue())


class LinuxAppImageRetentionTests(unittest.TestCase):
    def test_active_marker_survives_a_masked_lifetime_error(self):
        import tempfile
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaisesRegex(OSError, 'diagnostic'), builder._staging(Path(folder)) as staging:
                (staging / builder.PROCESS_MARKER).write_text('pending own child')
                raise OSError('diagnostic failed after an uncertain child')
            self.assertTrue(staging.exists())
            self.assertTrue((staging / builder.PROCESS_MARKER).exists())

    def test_failed_process_creation_retains_pending_evidence(self):
        import tempfile
        with tempfile.TemporaryDirectory() as folder:
            with patch.object(builder.subprocess, 'Popen', side_effect=OSError('synthetic creation error')):
                with self.assertRaises(OSError), builder._staging(Path(folder)) as staging:
                    builder._run(['fixture'], {}, staging, 1)
            self.assertTrue(staging.exists())
            self.assertTrue((staging / builder.PROCESS_MARKER).exists())
