"""Generate bounded text-only adversarial fixtures; NEVER extract these archives.

The output directory must not exist. Links and OS faults are archive metadata or
JSON definitions, not host symlinks, native operations or simulated test results.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import stat
import sys
import zipfile


def encoded(value):
    return (json.dumps(value, indent=2, ensure_ascii=True) + '\n').encode('utf-8')


def archive(entries):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', compression=zipfile.ZIP_DEFLATED) as zf:
        for name, content, symlink in entries:
            info = zipfile.ZipInfo(name, date_time=(2020, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = ((stat.S_IFLNK | 0o777) if symlink else (stat.S_IFREG | 0o644)) << 16
            zf.writestr(info, content)
    return stream.getvalue()


def build_fixtures():
    """Return deterministic filename -> bytes; no filesystem/network/process actions."""
    valid = archive([('payload/readme.txt', b'Relicade synthetic payload only.\n', False),
                     ('payload/data.txt', b'synthetic-data-v1\n', False)])
    unsafe = ['../escape.txt', 'payload/../../escape.txt', 'C:/escape.txt', 'C:relative.txt',
              '/absolute.txt', '//server/share/file.txt', '\\\\?\\C:\\device.txt',
              'payload\\..\\..\\escape.txt', 'payload/readme.txt:stream', 'payload/CON',
              'payload/Readme.txt', 'payload/readme.txt', 'payload/trailing. ',
              'payload/caf\u00e9.txt', 'payload/cafe\u0301.txt']
    bad_crc = bytearray(valid)
    central = bad_crc.index(b'PK\x01\x02')
    bad_crc[central + 16] ^= 0xff
    entries = [dict(id=f'synthetic-{i:05d}', name=f'Synthetic project {i:05d}',
                    tags=['even' if i % 2 == 0 else 'odd'], readiness='not_installed') for i in range(10000)]
    data = {
        'valid.zip': valid,
        'unsafe-paths.zip': archive([(name, b'not an executable\n', False) for name in unsafe]),
        'links.zip': archive([('payload/link', b'../../outside', True),
                              ('payload/link/child.txt', b'ancestor-link-test\n', False)]),
        'truncated.zip': valid[:20],
        'bad-crc.zip': bytes(bad_crc),
        'budget.zip': archive([('payload/low-budget.txt', b'X' * 4096, False)]),
        'link-cases.json': encoded([
            dict(id='preexisting-symlink', setup='Adapter creates a symlink ancestor inside an isolated test root only.', expected='Reject escape before writes.'),
            dict(id='preexisting-junction', setup='Windows adapter creates an isolated junction to a sibling sentinel directory.', expected='Reject escape; sentinel hashes unchanged.'),
            dict(id='tar-hardlink', setup='Adapter constructs supported tar hardlink/special-file variants in isolation.', expected='Declared safe link policy enforced before writes.')]),
        'library-valid.json': encoded(dict(schema_version=1, entries=[entries[0]])),
        'library-corrupt.json': b'{"schema_version":1,"entries":[',
        'library-duplicate.json': encoded(dict(schema_version=1, entries=[entries[0], entries[0]])),
        'library-future.json': encoded(dict(schema_version=999, entries=[])),
        'library-legacy.json': encoded(dict(apps=[dict(name='Synthetic legacy', folderName='synthetic-only')])),
        'library-1000.json': encoded(dict(schema_version=1, entries=entries[:1000])),
        'library-10000.json': encoded(dict(schema_version=1, entries=entries)),
        'operation-failures.json': encoded(dict(
            boundaries=['download', 'verify', 'extract', 'journal', 'activate', 'metadata', 'cleanup'],
            faults=['disk-full', 'access-denied', 'cancel', 'terminate'],
            injection='Native adapter injects each fault at each boundary, restarts and inspects actual manifests/bytes.',
            invariants=['old-or-new-complete-manifest', 'user-data-unchanged', 'no-active-payload-cleanup', 'no-outside-root-writes'],
            native_operations_executed=False)),
        'mod-cases.json': encoded(dict(
            chain={'A': ['B@1'], 'B@1': ['C@1'], 'C@1': []},
            version_conflict={'A': ['B@1'], 'D': ['B@2']}, cycle={'A': ['B'], 'B': ['A']},
            collisions={'A': ['mods/shared.txt'], 'D': ['mods/shared.txt']},
            layouts=['flat', 'folder-per-mod'], profiles=['Vanilla', 'Custom'],
            failure='Replacement download fails before activation; old bytes and records remain.')),
        'readiness-cases.json': encoded(dict(
            files={'match.txt': 'synthetic-region-A\n', 'wrong-region.txt': 'synthetic-region-B\n',
                   'invalid.txt': 'invalid\n', 'truncated.txt': 'synthetic-'},
            adapter_cases=['missing', 'renamed', 'locked', 'linked-source-changed', 'cancel-each-step'],
            invariant='Original bytes never changed; unknown/missing prerequisite cannot be Ready.')),
        'privacy-canaries.json': encoded(dict(
            token='SYNTHETIC_SECRET_DO_NOT_EXPORT', local_path='SYNTHETIC_LOCAL_PATH_DO_NOT_EXPORT',
            protected_data='SYNTHETIC_PROTECTED_BYTES_DO_NOT_UPLOAD',
            capture_states=['first-run', 'decline', 'opt-in', 'revoke', 'offline'],
            invariant='No protected canaries leave in requests/exports/diagnostics; offline emits zero application requests.')),
        'sentinel-data.json': encoded(dict(original='synthetic-original-unchanged', save='synthetic-save-unchanged', config='synthetic-config-unchanged')),
    }
    return data


def emit(destination) -> dict:
    """Write fixed top-level filenames exclusively, never archive entry destinations."""
    destination = Path(destination)
    destination.mkdir(parents=False, exist_ok=False)
    files = build_fixtures()
    for name, payload in files.items():
        with (destination / name).open('xb') as handle:
            handle.write(payload)
    manifest = dict(schema_version=1, synthetic_only=True, native_operations_executed=False,
                    files={name: hashlib.sha256(payload).hexdigest() for name, payload in sorted(files.items())})
    with (destination / 'manifest.json').open('xb') as handle:
        handle.write(encoded(manifest))
    return manifest


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True, help='New directory under an isolated test workspace')
    args = parser.parse_args(argv)
    try:
        manifest = emit(args.output)
    except OSError as exc:
        print(f'Fixture generation failed: {exc}', file=sys.stderr)
        return 1
    print(f"Generated {len(manifest['files'])} synthetic fixture files; no native operations executed.")
    return 0


if __name__ == '__main__':
    sys.exit(main())
