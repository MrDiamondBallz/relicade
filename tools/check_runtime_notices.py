"""Check a pinned Linux runtime notice bundle without fetching or running software."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import stat

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_BUNDLE = ROOT / 'native/licenses/linux-x64'
BUNDLE_DESTINATION = 'licenses/runtime/linux-x64'


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON key: ' + key)
        result[key] = value
    return result


def _relative(value):
    if not isinstance(value, str) or not value or not all(
        re.fullmatch(r'[A-Za-z0-9_.-]+', part) and part not in {'.', '..'}
        for part in value.split('/')
    ):
        raise ValueError('Invalid relative inventory path')
    return value


def _file(root, relative, limit):
    path = root.absolute().joinpath(*_relative(relative).split('/'))
    for parent in path.parents:
        if parent.is_symlink():
            raise ValueError('Linked path ancestor: ' + relative)
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size > limit:
        raise ValueError('Expected a bounded regular unlinked file: ' + relative)
    fd = os.open(path, os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0) | getattr(os, 'O_NONBLOCK', 0))
    try:
        opened = os.fstat(fd)
        if (opened.st_dev, opened.st_ino, opened.st_size) != (info.st_dev, info.st_ino, info.st_size) or opened.st_nlink != 1 or not stat.S_ISREG(opened.st_mode):
            raise ValueError('File changed before reading: ' + relative)
        return os.fdopen(fd, 'rb')
    except BaseException:
        os.close(fd)
        raise


def _bytes(root, relative, limit=2 * 1024 * 1024):
    with _file(root, relative, limit) as stream:
        value = stream.read(limit + 1)
    if len(value) > limit:
        raise ValueError('File grew beyond inventory budget: ' + relative)
    return value


def _hash(root, relative, maximum=256 * 1024 * 1024):
    result = hashlib.sha256()
    size = 0
    with _file(root, relative, maximum) as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            size += len(chunk)
            if size > maximum:
                raise ValueError('File grew beyond hashing budget: ' + relative)
            result.update(chunk)
    return result.hexdigest()


def _keys(value, expected):
    if not isinstance(value, dict) or set(value) != set(expected):
        raise ValueError('Unexpected inventory fields')


def _sha256(value):
    if not isinstance(value, str) or not re.fullmatch(r'[a-f0-9]{64}', value):
        raise ValueError('Invalid SHA256 value')


def _sha512(value):
    if not isinstance(value, str) or len(base64.b64decode(value, validate=True)) != 64:
        raise ValueError('Invalid canonical content SHA512 value')


def _origin(origin):
    if not isinstance(origin, dict):
        raise ValueError('Invalid notice origin')
    kind = origin.get('kind')
    if kind == 'git':
        _keys(origin, ['kind', 'repository', 'commit', 'path', 'blob_sha1', 'url'])
        if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', origin['repository']):
            raise ValueError('Invalid upstream repository')
        if not all(re.fullmatch(r'[a-f0-9]{40}', origin[k]) for k in ['commit', 'blob_sha1']):
            raise ValueError('Unpinned upstream source')
        path = _relative(origin['path'])
        expected = 'https://github.com/' + origin['repository'] + '/blob/' + origin['commit'] + '/' + path
        if origin['url'] != expected:
            raise ValueError('Upstream URL does not match its pinned source')
    elif kind == 'nuget':
        _keys(origin, ['kind', 'identity', 'member', 'raw_archive_sha256', 'canonical_content_sha512'])
        _relative(origin['member'])
        _sha256(origin['raw_archive_sha256'])
        _sha512(origin['canonical_content_sha512'])
    elif kind == 'dotnet-sdk':
        _keys(origin, ['kind', 'version', 'member', 'scope'])
        _relative(origin['member'])
        if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', origin['version']):
            raise ValueError('Unpinned SDK source')
    else:
        raise ValueError('Unknown notice origin')


def _inventory(root):
    observed = set()
    entries = 0
    for directory, subdirs, files in os.walk(root, followlinks=False):
        if len(Path(directory).relative_to(root).parts) > 32:
            raise ValueError('Inventory directory depth exceeds budget')
        entries += len(subdirs) + len(files)
        if entries > 4096:
            raise ValueError('Inventory exceeds entry budget')
        if any((Path(directory) / d).is_symlink() for d in subdirs):
            raise ValueError('Linked directory in inventory')
        for name in files:
            relative = (Path(directory) / name).relative_to(root).as_posix()
            observed.add(_relative(relative))
    return observed


def validate(bundle=DEFAULT_BUNDLE, publish=None):
    bundle = Path(bundle)
    data = _bytes(bundle, 'manifest.json')
    manifest = json.loads(data, object_pairs_hook=_pairs)
    _keys(manifest, ['schema_version', 'runtime_identifier', 'target_framework', 'sdk_version', 'scope', 'notices', 'packages', 'additional_components', 'own_source_files', 'own_metadata_identities', 'qualification'])
    if type(manifest['schema_version']) is not int or manifest['schema_version'] != 1 or manifest['runtime_identifier'] != 'linux-x64' or manifest['target_framework'] != '.NETCoreApp,Version=v10.0':
        raise ValueError('Unsupported inventory target or schema')
    notices = manifest['notices']
    if not isinstance(notices, dict) or not 1 <= len(notices) <= 256:
        raise ValueError('Invalid notice inventory size')
    for relative, notice in notices.items():
        _relative(relative)
        _keys(notice, ['sha256', 'origin'])
        _sha256(notice['sha256'])
        _origin(notice['origin'])
        if _hash(bundle, relative, 2 * 1024 * 1024) != notice['sha256']:
            raise ValueError('Changed notice bytes: ' + relative)
        if notice['origin']['kind'] == 'git':
            content = _bytes(bundle, relative)
            blob = hashlib.sha1(b'blob ' + str(len(content)).encode() + b'\0' + content).hexdigest()
            if blob != notice['origin']['blob_sha1']:
                raise ValueError('Notice differs from pinned Git blob: ' + relative)
    if _inventory(bundle) != set(notices) | {'manifest.json'}:
        raise ValueError('Source notice bundle has missing or unmapped files')
    packages = manifest['packages']
    if not isinstance(packages, list) or not 1 <= len(packages) <= 256:
        raise ValueError('Invalid package inventory size')
    identities = set()
    assets = {}
    def notice_refs(refs):
        if not isinstance(refs, list) or not refs or len(refs) != len(set(refs)) or any(r not in notices for r in refs):
            raise ValueError('Missing or duplicate applicable notice reference')
    for package in packages:
        _keys(package, ['identity', 'kind', 'declared_package_license', 'deps_sha512', 'raw_archive_sha256', 'canonical_content_sha512', 'notices', 'assets'])
        identity = package['identity']
        if not isinstance(identity, str) or not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.+-]+', identity) or identity in identities:
            raise ValueError('Invalid or duplicate package identity')
        identities.add(identity)
        if package['kind'] not in ['package', 'runtimepack']:
            raise ValueError('Unexpected package kind')
        _sha256(package['raw_archive_sha256'])
        _sha512(package['canonical_content_sha512'])
        expected = 'sha512-' + package['canonical_content_sha512']
        if package['deps_sha512'] != expected and not (package['kind'] == 'runtimepack' and package['deps_sha512'] == ''):
            raise ValueError('Canonical hash mismatch in inventory')
        notice_refs(package['notices'])
        if not isinstance(package['assets'], list) or not package['assets']:
            raise ValueError('Package has no mapped shipped assets')
        for asset in package['assets']:
            _keys(asset, ['deps_path', 'package_member', 'published_path', 'sha256', 'kind'])
            for key in ['deps_path', 'package_member', 'published_path']:
                _relative(asset[key])
            _sha256(asset['sha256'])
            if asset['kind'] not in ['runtime', 'native', 'resources', 'runtimeTargets']:
                raise ValueError('Unknown dependency asset kind')
            if asset['published_path'] in assets:
                raise ValueError('Ambiguous package asset owner')
            assets[asset['published_path']] = asset
    package_map = {p['identity']: p for p in packages}
    for notice in notices.values():
        origin = notice['origin']
        if origin['kind'] == 'nuget':
            package = package_map.get(origin['identity'])
            if not package or origin['raw_archive_sha256'] != package['raw_archive_sha256'] or origin['canonical_content_sha512'] != package['canonical_content_sha512']:
                raise ValueError('Notice package provenance differs from runtime inventory')
        elif origin['kind'] == 'dotnet-sdk' and origin['version'] != manifest['sdk_version']:
            raise ValueError('SDK notice version differs from inventory')
    hosts = set()
    host_hashes = {}
    for component in manifest['additional_components']:
        notice_refs(component['notices'])
        if component['kind'] == 'sdk-apphost':
            _keys(component, ['kind', 'identity', 'source_member', 'source_sha256', 'published_paths', 'published_sha256', 'notices'])
            _relative(component['source_member'])
            _sha256(component['source_sha256'])
            paths = component['published_paths']
            if not paths or len(paths) != len(set(paths)) or set(paths) != set(component['published_sha256']):
                raise ValueError('Invalid SDK apphost mapping')
            for path in paths:
                _relative(path)
                _sha256(component['published_sha256'][path])
                if path in hosts:
                    raise ValueError('Duplicate SDK apphost mapping')
                hosts.add(path)
                host_hashes[path] = component['published_sha256'][path]
        elif component['kind'] in ['embedded-font', 'embedded-native']:
            fields = ['kind', 'identity', 'assembly', 'source_commit', 'binding', 'notices']
            if component['kind'] == 'embedded-font':
                fields.append('source_font_blob')
                if not re.fullmatch(r'[a-f0-9]{40}', component['source_font_blob']):
                    raise ValueError('Unpinned font source')
            _keys(component, fields)
            if component['assembly'] not in assets or not re.fullmatch(r'[a-f0-9]{40}', component['source_commit']):
                raise ValueError('Unbound embedded component')
        else:
            raise ValueError('Unknown additional component')
    for field in ['own_source_files', 'own_metadata_identities']:
        values = manifest[field]
        if not isinstance(values, list) or len(values) > 256 or not all(isinstance(v, str) for v in values) or len(values) != len(set(values)):
            raise ValueError('Invalid or duplicate own-source inventory')
    own = {_relative(p) for p in manifest['own_source_files']}
    if own & set(assets) or hosts & (own | set(assets)):
        raise ValueError('Ambiguous source or SDK asset owner')
    if publish is not None:
        publish = Path(publish)
        deps = json.loads(_bytes(publish, 'Relicade.Desktop.deps.json', 16 * 1024 * 1024), object_pairs_hook=_pairs)
        target = manifest['target_framework'] + '/' + manifest['runtime_identifier']
        if deps['runtimeTarget']['name'] != target:
            raise ValueError('Published runtime target differs from inventory')
        libraries = deps['libraries']
        actual = {k for k, v in libraries.items() if v['type'] in ['package', 'runtimepack']}
        if actual != identities or set(libraries) - actual != set(manifest['own_metadata_identities']):
            raise ValueError('Published dependency identities differ from inventory')
        for package in packages:
            metadata = libraries[package['identity']]
            if metadata['type'] != package['kind'] or metadata.get('sha512') != package['deps_sha512']:
                raise ValueError('Published canonical dependency metadata changed')
            declared = deps['targets'][target][package['identity']]
            bindings = {(kind, path) for kind in ['runtime', 'native', 'resources', 'runtimeTargets'] for path in declared.get(kind, {})}
            if bindings != {(a['kind'], a['deps_path']) for a in package['assets']}:
                raise ValueError('Published dependency asset mapping changed')
            for asset in package['assets']:
                if _hash(publish, asset['published_path']) != asset['sha256']:
                    raise ValueError('Published runtime asset changed: ' + asset['published_path'])
        for relative, notice in notices.items():
            if _hash(publish, BUNDLE_DESTINATION + '/' + relative, 2 * 1024 * 1024) != notice['sha256']:
                raise ValueError('Published notice differs: ' + relative)
        if _bytes(publish, BUNDLE_DESTINATION + '/manifest.json') != data:
            raise ValueError('Published notice inventory differs')
        expected = own | hosts | set(assets) | {BUNDLE_DESTINATION + '/' + n for n in notices} | {BUNDLE_DESTINATION + '/manifest.json'}
        observed = _inventory(publish)
        if observed != expected:
            raise ValueError('Published output has missing or unmapped files')
        for relative, digest in host_hashes.items():
            if _hash(publish, relative) != digest:
                raise ValueError('Published SDK apphost changed: ' + relative)
        for relative in own | hosts:
            with _file(publish, relative, 256 * 1024 * 1024):
                pass
    return {'ok': True, 'runtime_identifier': 'linux-x64', 'packages': len(packages), 'runtime_assets': len(assets), 'notice_files': len(notices), 'published_output_checked': publish is not None, 'distribution_rights': 'not_accepted', 'platform_support': 'not_accepted'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish', type=Path, help='Inspect an existing Linux x64 publish directory; never builds or runs it.')
    args = parser.parse_args()
    try:
        result = validate(publish=args.publish)
    except (ValueError, OSError, KeyError, TypeError) as exc:
        print(json.dumps({'ok': False, 'error': str(exc)}, sort_keys=True))
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
