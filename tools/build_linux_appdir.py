"""Assemble a new private Linux AppDir from an already checked directory publish.

No downloads, compiler invocation, desktop registration, signing or application
execution. AppImage filesystem/runtime creation is a separate explicit step.
"""
from __future__ import annotations
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import tempfile
import check_runtime_notices as notices

ROOT = Path(__file__).resolve().parents[1]


def _directory(path):
    path = Path(path).absolute()
    if any(ord(c) < 32 or ord(c) == 127 for c in str(path)):
        raise ValueError('Control characters are not supported in build paths')
    for item in [path, *path.parents]:
        if item.is_symlink() or not item.is_dir():
            raise ValueError('Expected an existing unlinked directory')
    return path


def _publish_new(staging, destination):
    # Linux renameat2 prevents replacing even an empty destination created after
    # preflight. No fallback to overwrite-capable rename is appropriate here.
    library = ctypes.CDLL(None, use_errno=True)
    rename = getattr(library, 'renameat2', None)
    if rename is None:
        raise ValueError('This build host lacks no-replace directory publication')
    rename.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    rename.restype = ctypes.c_int
    parent = os.open(destination.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        if rename(parent, os.fsencode(staging.name), parent, os.fsencode(destination.name), 1) != 0:
            code = ctypes.get_errno()
            raise OSError(code, os.strerror(code), str(destination))
    finally:
        os.close(parent)


def assemble(publish, destination, epoch=0, bundle=notices.DEFAULT_BUNDLE):
    if type(epoch) is not int or not 0 <= epoch <= 2147483647:
        raise ValueError('Invalid reproducible timestamp')
    publish = _directory(publish)
    destination = Path(destination).absolute()
    parent = _directory(destination.parent)
    if any(ord(c) < 32 or ord(c) == 127 for c in destination.name):
        raise ValueError('Control characters are not supported in output names')
    if destination.exists() or destination.is_symlink():
        raise FileExistsError('Output must be a new directory')
    if destination.is_relative_to(publish) or publish.is_relative_to(destination):
        raise ValueError('Input and output trees must be separate')
    names = notices._inventory(publish)
    if sum((publish / name).lstat().st_size for name in names) > 512 * 1024 * 1024:
        raise ValueError('Publish input exceeds the 512 MiB assembly budget')
    notices.validate(bundle, publish)
    snapshot = {}
    for relative in sorted(notices._inventory(publish)):
        source = publish / relative
        mode = source.lstat().st_mode
        if mode & (stat.S_ISUID | stat.S_ISGID | stat.S_ISVTX | stat.S_IWGRP | stat.S_IWOTH):
            raise ValueError('Unsafe permission bits in publish input')
        snapshot[relative] = {'sha256': notices._hash(publish, relative),
                              'mode': 0o755 if mode & 0o111 else 0o644, 'bytes': source.stat().st_size}
    if 'Relicade.Desktop' not in snapshot or snapshot['Relicade.Desktop']['mode'] != 0o755:
        raise ValueError('Publish input lacks the executable desktop host')
    staging = Path(tempfile.mkdtemp(prefix='.relicade-appdir-', dir=parent))
    try:
        payload = staging / 'usr/lib/relicade'
        payload.mkdir(parents=True)
        for relative, expected in snapshot.items():
            target = payload / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            digest = hashlib.sha256()
            copied = 0
            with notices._file(publish, relative, 256 * 1024 * 1024) as source, target.open('xb') as output:
                while chunk := source.read(1024 * 1024):
                    copied += len(chunk)
                    if copied > expected['bytes'] or copied > 256 * 1024 * 1024:
                        raise ValueError('Publish file grew beyond assembly budget')
                    digest.update(chunk)
                    output.write(chunk)
            if digest.hexdigest() != expected['sha256']:
                raise ValueError('Publish input changed while copying')
            os.chmod(target, expected['mode'])
        if notices._inventory(publish) != set(snapshot):
            raise ValueError('Publish file set changed while copying')
        notices.validate(bundle, payload)
        for relative, expected in snapshot.items():
            if notices._hash(publish, relative) != expected['sha256']:
                raise ValueError('Publish input changed during assembly')
        templates = ROOT / 'native/packaging/linux'
        for name in ['AppRun', 'relicade.desktop']:
            (staging / name).write_bytes(notices._bytes(templates, name))
        os.chmod(staging / 'AppRun', 0o755)
        icon = notices._bytes(ROOT, 'native/Assets/relicade-icon.png')
        (staging / 'relicade.png').write_bytes(icon)
        (staging / '.DirIcon').write_bytes(icon)
        # The desktop entry resolves through an AppDir-relative executable.
        bin_dir = staging / 'usr/bin'
        bin_dir.mkdir()
        launcher = '#!/bin/sh\nset -eu\nself=$(readlink -f -- "$0")\nroot=$(CDPATH= cd -- "$(dirname -- "$self")/../.." && pwd)\nexec "$root/AppRun" "$@"\n'
        (bin_dir / 'relicade').write_text(launcher)
        os.chmod(bin_dir / 'relicade', 0o755)
        report = {'schema_version': 1, 'runtime_identifier': 'linux-x64',
                  'development_build': True, 'packaged_release': False,
                  'distribution_rights': 'not_accepted', 'platform_support': 'not_accepted',
                  'notice_manifest_sha256': notices._hash(payload, notices.BUNDLE_DESTINATION + '/manifest.json'),
                  'files': {}}
        for relative in sorted(notices._inventory(staging)):
            path = staging / relative
            mode = 0o755 if path.stat().st_mode & 0o111 else 0o644
            os.chmod(path, mode)
            report['files'][relative] = {'sha256': notices._hash(staging, relative),
                                         'bytes': path.stat().st_size, 'mode': mode}
        (staging / 'relicade-build.json').write_text(json.dumps(report, indent=2, sort_keys=True) + '\n')
        for path in staging.rglob('*'):
            if path.is_dir():
                os.chmod(path, 0o755)
            os.utime(path, (epoch, epoch), follow_symlinks=False)
        os.chmod(staging, 0o755)
        os.utime(staging, (epoch, epoch))
        _publish_new(staging, destination)
        return report
    finally:
        # Only the uniquely created, unpublished staging tree is disposable.
        if staging.exists():
            shutil.rmtree(staging)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--epoch', type=int, default=0)
    args = parser.parse_args()
    try:
        report = assemble(args.publish, args.output, args.epoch)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(json.dumps({'ok': False, 'error': str(exc)}))
        return 1
    print(json.dumps({'ok': True, 'files': len(report['files']) + 1,
                      'packaged_release': False, 'platform_support': 'not_accepted'}))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
