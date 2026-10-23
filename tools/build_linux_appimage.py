"""Build a private Linux x64 AppImage using explicitly supplied, pinned tools.

No tool downloads, signing, update service, desktop installation or game execution.
The runtime's additional distribution obligations remain a separate release gate.
"""
from __future__ import annotations
import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import platform
import signal
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import build_linux_appdir as appdir


PROCESS_MARKER = ".relicade-tool-active"


def _pins():
    return json.loads((appdir.ROOT / 'native/packaging/linux/appimage-toolchain.json').read_text())


def _copy_pinned(source, target, pin):
    source = Path(source).absolute()
    appdir._directory(source.parent)
    data = appdir.notices._bytes(source.parent, source.name, pin['size'])
    if len(data) != pin['size'] or hashlib.sha256(data).hexdigest() != pin['sha256']:
        raise ValueError('Build tool differs from its pinned official asset')
    target.write_bytes(data)


class UndrainedBuild(RuntimeError):
    """The caller must retain staging until the exact child is reconciled."""


@contextmanager
def _staging(parent):
    directory = Path(tempfile.mkdtemp(prefix='.relicade-image-', dir=parent))
    preserve = False
    try:
        yield directory
    except UndrainedBuild:
        preserve = True
        raise
    finally:
        if not preserve and not (directory / PROCESS_MARKER).exists():
            shutil.rmtree(directory)


def _wait_for_group(process, timeout):
    """Wait for actual group disappearance, not just the leader's exit status."""
    deadline = time.monotonic() + max(0, timeout)
    wake = threading.Event()
    while True:
        leader_done = process.poll() is not None  # Reap our direct child.
        try:
            os.killpg(process.pid, 0)
        except ProcessLookupError:
            if leader_done:
                return
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise subprocess.TimeoutExpired('build process group', timeout)
        # The condition is kernel group disappearance; elapsed time is never a pass.
        wake.wait(min(0.05, remaining))


def _signal_group(process, value):
    try:
        os.killpg(process.pid, value)
    except ProcessLookupError:
        pass


def _print_log(log):
    try:
        print(log.read_text(errors='replace'), file=sys.stderr, end='')
    except OSError as exc:
        # Diagnostic failure must not replace an uncertain-lifetime exception.
        print('Could not read build diagnostic: ' + str(exc), file=sys.stderr)


def _run(command, environment, directory, timeout):
    log = directory / 'tool.log'
    with log.open('xb') as output:
        marker = directory / PROCESS_MARKER
        # Retain staging even if a later diagnostic or repeated interruption masks
        # the original exception. Only confirmed group disappearance clears this.
        with marker.open('x') as pending:
            pending.write('Build process group is not yet confirmed drained.\n')
        process = subprocess.Popen(command, cwd=directory, env=environment,
                                   stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        deadline = time.monotonic() + timeout
        try:
            result = process.wait(timeout=timeout)
            _wait_for_group(process, deadline - time.monotonic())
            marker.unlink()
        except BaseException:
            try:
                # A reaped leader does not prove its descendants stopped using staging.
                _signal_group(process, signal.SIGTERM)
                try:
                    _wait_for_group(process, 5)
                except subprocess.TimeoutExpired:
                    _signal_group(process, signal.SIGKILL)
                    _wait_for_group(process, 5)
            except BaseException as exc:
                raise UndrainedBuild('Build process group ' + str(process.pid) +
                                     ' did not drain; retain staging at ' + str(directory)) from exc
            else:
                marker.unlink()
            finally:
                _print_log(log)
            raise
    _print_log(log)
    if result != 0:
        raise ValueError('AppImage build tool failed with exit ' + str(result))


def _validate_appstream(image_root, validator, environment, directory, timeout):
    if validator is None:
        raise ValueError('Supply a verified official appstreamcli with --appstreamcli')
    validator = Path(validator).absolute()
    appdir._directory(validator.parent)
    data = appdir.notices._bytes(validator.parent, validator.name, 32 * 1024 * 1024)
    checked = directory / 'appstreamcli'
    checked.write_bytes(data)
    os.chmod(checked, 0o755)
    _run([str(checked), 'validate-tree', '--no-net', str(image_root)], environment, directory, timeout)
    (directory / 'tool.log').rename(directory / 'metadata-validation.log')
    return hashlib.sha256(data).hexdigest()


def package(publish, output, tool, runtime, epoch=0, timeout=120, appstreamcli=None):
    if sys.platform != 'linux' or platform.machine().lower() not in {'x86_64', 'amd64'}:
        raise ValueError('This recipe requires a Linux x64 build host')
    if type(timeout) is not int or not 1 <= timeout <= 300:
        raise ValueError('Build deadline must be between 1 and 300 seconds')
    output = Path(output).absolute()
    appdir._directory(output.parent)
    if any(ord(c) < 32 or ord(c) == 127 for c in output.name):
        raise ValueError('Control characters are not supported in output names')
    if output.exists() or output.is_symlink():
        raise FileExistsError('Output must be a new file')
    if output.is_relative_to(Path(publish).absolute()):
        raise ValueError('Output must be outside the publish input')
    pins = _pins()
    tool_pin = next(p for p in pins if p['repository'] == 'AppImage/appimagetool')
    runtime_pin = next(p for p in pins if p['repository'] == 'AppImage/type2-runtime')
    with _staging(output.parent) as directory:
        checked_tool, checked_runtime = directory / 'appimagetool', directory / 'runtime'
        _copy_pinned(tool, checked_tool, tool_pin)
        _copy_pinned(runtime, checked_runtime, runtime_pin)
        os.chmod(checked_tool, 0o755)
        image_root = directory / 'RELICADE.AppDir'
        appdir.assemble(publish, image_root, epoch)
        for child in ['home', 'tmp']:
            (directory / child).mkdir()
        environment = {'PATH': '/usr/bin:/bin', 'LANG': 'C', 'LC_ALL': 'C', 'TZ': 'UTC',
                       'HOME': str(directory / 'home'), 'TMPDIR': str(directory / 'tmp'),
                       'ARCH': 'x86_64', 'SOURCE_DATE_EPOCH': str(epoch),
                       'APPIMAGE_EXTRACT_AND_RUN': '1'}
        validator_hash = _validate_appstream(image_root, appstreamcli, environment, directory, timeout)
        staged = directory / 'RELICADE.AppImage'
        command = [str(checked_tool), '--runtime-file', str(checked_runtime),
                   '--mksquashfs-opt=-processors', '--mksquashfs-opt=1', str(image_root), str(staged)]
        _run(command, environment, directory, timeout)
        with appdir.notices._file(directory, staged.name, 768 * 1024 * 1024) as stream:
            header = stream.read(12)
        if header[:4] != b'\x7fELF' or header[8:11] != b'AI\x02' or staged.stat().st_size <= runtime_pin['size']:
            raise ValueError('Build did not produce the expected type-2 AppImage container')
        digest = appdir.notices._hash(directory, staged.name, 768 * 1024 * 1024)
        os.chmod(staged, 0o755)
        report = {'sha256': digest, 'bytes': staged.stat().st_size, 'runtime_identifier': 'linux-x64',
                  'tool_sha256': tool_pin['sha256'], 'runtime_sha256': runtime_pin['sha256'],
                  'packaged_release': False, 'distribution_rights': 'not_accepted',
                  'platform_support': 'not_accepted', 'appstream_metadata': 'validator_passed',
                  'appstream_validator_sha256': validator_hash, 'runtime_notices': 'partial_not_accepted'}
        # link() is exclusive even if another owner creates the final path during the build.
        # The temporary link is removed with staging before this result is returned.
        os.link(staged, output, follow_symlinks=False)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['publish', 'output', 'appimagetool', 'runtime', 'appstreamcli']:
        parser.add_argument('--' + name, required=True, type=Path)
    parser.add_argument('--epoch', type=int, default=0)
    parser.add_argument('--timeout', type=int, default=120)
    args = parser.parse_args()
    try:
        report = package(args.publish, args.output, args.appimagetool, args.runtime, args.epoch, args.timeout, args.appstreamcli)
    except (OSError, ValueError, KeyError, TypeError, subprocess.TimeoutExpired, UndrainedBuild) as exc:
        print(json.dumps({'ok': False, 'error': str(exc)}))
        return 1
    print(json.dumps({'ok': True, **report}))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
