"""Validate the version-1 coverage contract using only the Python standard library.

This checks evidence structure and consistency, not the truth of test attestations.
Independent review must inspect the recorded commit, commands and artifacts.
"""
import argparse
import json
from pathlib import Path, PurePosixPath
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
PLATFORMS = {
    'windows-x64': ('windows', 'x64', 'native-installer', 'desktop-executable'),
    'linux-x64-appimage': ('linux', 'x64', 'AppImage', 'desktop-executable'),
    'linux-arm64-appimage': ('linux', 'arm64', 'AppImage', 'desktop-executable'),
    'linux-x64-flatpak': ('linux', 'x64', 'Flatpak', 'desktop-executable'),
    'linux-arm64-flatpak': ('linux', 'arm64', 'Flatpak', 'desktop-executable'),
    'macos-x64': ('macos', 'x64', 'signed-app', 'desktop-executable'),
    'macos-arm64': ('macos', 'arm64', 'signed-app', 'desktop-executable'),
    'android-apk': ('android', 'adapter-defined', 'APK', 'android-package'),
}
CATEGORIES = set('accessibility catalog downloads export input installation library mods offline performance platform privacy profiles queue readiness recovery releases settings shell transactional trust updates'.split())
MANDATORY_FEATURES = set('''catalog.browse catalog.search-filter catalog.detail catalog.readme-reviews catalog.custom-list
library.manual-existing library.search-sort-tags library.presentation library.remove-uninstall library.durability library.offline-start
release.version-channel release.pin-skip release.rolling-build release.providers trust.verification trust.url-policy
download.stage-bytes download.cancel-retry download.queue download.resume download.cancel-latency
install.portable install.archive-containment install.transaction install.rollback install.user-data install.running-lock
recovery.journal recovery.import-export recovery.center recovery.diagnostics
mods.providers mods.dependencies mods.layout-remove mods.staged-update mods.profiles mods.conflicts
readiness.states readiness.import readiness.project-adapter input.keyboard input.gamepad input.rebinding input.osk
shell.fullscreen-kiosk shell.tray-lifetime shell.shortcuts shell.steam-shortcuts shell.cli
settings.customization settings.music-announcements privacy.consent privacy.network-disabled privacy.credentials
accessibility.semantics accessibility.dpi accessibility.preferences performance.library performance.render-idle performance.stability
platform.desktop-execution platform.windows-msi platform.linux-appimage platform.linux-flatpak platform.linux-runners
platform.macos platform.android-apk platform.data-roots platform.self-update platform.release-gates'''.split())
KINDS = {'unit', 'source_inspection', 'source_build', 'package', 'native_runtime', 'browser', 'fixture_definition'}
IMPLEMENTATIONS = {'planned', 'wip', 'supported', 'unsupported', 'not_applicable'}
VALIDATIONS = {'not_run', 'blocked', 'inconclusive', 'failed', 'accepted', 'not_applicable'}
FAMILIES = {'zip', 'path', 'link', 'library', 'operation', 'http', 'mods', 'readiness', 'privacy', 'input', 'platform', 'accessibility', 'performance', 'catalog', 'release', 'security'}
DESKTOP_ONLY = set('install.portable install.transaction install.rollback shell.tray-lifetime shell.shortcuts shell.cli platform.desktop-execution'.split())
CONDITIONAL = set('download.resume install.archive-containment mods.providers mods.dependencies mods.layout-remove mods.staged-update mods.profiles mods.conflicts readiness.import input.gamepad input.rebinding settings.music-announcements'.split())
OS_SPECIFIC = {
    'input.osk': set(p for p in PLATFORMS if p.startswith('linux-')),
    'shell.steam-shortcuts': set(p for p in PLATFORMS if p.startswith(('windows-', 'linux-'))),
    'platform.windows-msi': {'windows-x64'},
    'platform.linux-appimage': set(p for p in PLATFORMS if p.endswith('-appimage')),
    'platform.linux-flatpak': set(p for p in PLATFORMS if p.endswith('-flatpak')),
    'platform.linux-runners': set(p for p in PLATFORMS if p.startswith('linux-')),
    'platform.macos': {'macos-x64', 'macos-arm64'},
    'platform.android-apk': {'android-apk'},
}
ID = re.compile(r'[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*\Z')
COMMIT = re.compile(r'[0-9a-f]{40}\Z')


def validate_contract(contract, fixture_document):
    """Return actionable errors for malformed input; never certify native behavior."""
    errors = []

    def error(where, message):
        errors.append(f'{where}: {message}')

    def obj(value, keys, where):
        if not isinstance(value, dict):
            error(where, 'expected object')
            return False
        for key in sorted(set(value) - set(keys)):
            error(where, f'unknown field {key}')
        for key in sorted(set(keys) - set(value)):
            error(where, f'missing field {key}')
        return set(value) == set(keys)

    def text(value, where):
        if not isinstance(value, str) or not value.strip():
            error(where, 'expected nonempty text')
            return False
        return True

    def strings(value, where, nonempty=True):
        if not isinstance(value, list):
            error(where, 'expected list')
            return False
        if nonempty and not value:
            error(where, 'expected nonempty list')
        good = all(text(v, f'{where}[{i}]') for i, v in enumerate(value))
        if good and len(set(value)) != len(value):
            error(where, 'duplicate value')
        return good and (bool(value) or not nonempty)

    def choice(value, allowed, where):
        if not isinstance(value, str) or value not in allowed:
            error(where, 'invalid value')
            return False
        return True

    def records(value, where):
        if not isinstance(value, list) or not value:
            error(where, 'expected nonempty list')
            return {}
        output = {}
        for index, row in enumerate(value):
            loc = f'{where}[{index}]'
            if not isinstance(row, dict):
                error(loc, 'expected object')
                continue
            key = row.get('id')
            if not isinstance(key, str) or not ID.fullmatch(key):
                error(loc, 'invalid id')
            elif key in output:
                error(loc, f'duplicate id {key}')
            else:
                output[key] = row
        return output

    if not obj(contract, {'schema_version', 'product', 'scope', 'categories', 'boundaries', 'platforms', 'evidence', 'features'}, 'contract'):
        return errors
    if type(contract['schema_version']) is not int or contract['schema_version'] != 1:
        error('schema_version', 'expected integer 1')
    if contract['product'] != 'Relicade':
        error('product', 'expected Relicade')
    text(contract['scope'], 'scope')
    if strings(contract['categories'], 'categories') and not CATEGORIES <= set(contract['categories']):
        error('categories', 'missing mandatory categories')
    if obj(contract['boundaries'], {'inspected_sources', 'search_scope', 'unknowns'}, 'boundaries'):
        for key, value in contract['boundaries'].items():
            strings(value, f'boundaries.{key}')

    if not obj(fixture_document, {'schema_version', 'scenarios'}, 'fixtures'):
        return errors
    if type(fixture_document['schema_version']) is not int or fixture_document['schema_version'] != 1:
        error('fixtures.schema_version', 'expected integer 1')
    scenarios = records(fixture_document['scenarios'], 'scenarios')
    families = set()
    for sid, row in scenarios.items():
        loc = f'scenarios.{sid}'
        if not obj(row, {'id', 'kind', 'mode', 'inputs', 'expected', 'artifacts'}, loc):
            continue
        if choice(row['kind'], FAMILIES, loc + '.kind'):
            families.add(row['kind'])
        choice(row['mode'], {'generated', 'definition'}, loc + '.mode')
        text(row['inputs'], loc + '.inputs')
        strings(row['expected'], loc + '.expected')
        if strings(row['artifacts'], loc + '.artifacts', nonempty=row['mode'] == 'generated'):
            if row['mode'] == 'definition' and row['artifacts']:
                error(loc + '.artifacts', 'definition-only scenario has no generated artifacts')
            for name in row['artifacts']:
                if '/' in name or '\\' in name or '..' in name or ':' in name:
                    error(loc + '.artifacts', 'expected safe top-level artifact names')
    if not FAMILIES <= families:
        error('scenarios', 'missing mandatory fixture families')

    platforms = records(contract['platforms'], 'platforms')
    features = records(contract['features'], 'features')
    if set(platforms) != set(PLATFORMS):
        error('platforms', 'mandatory platform set differs')
    if not MANDATORY_FEATURES <= set(features):
        error('features', 'missing mandatory feature IDs')
    if not isinstance(contract['evidence'], list):
        error('evidence', 'expected list')
        evidence = {}
    else:
        evidence = records(contract['evidence'], 'evidence') if contract['evidence'] else {}
    for eid, record in evidence.items():
        loc = f'evidence.{eid}'
        if not obj(record, {'id', 'platform', 'commit', 'fixture', 'command', 'result', 'output', 'artifact', 'hardware', 'feature', 'kind'}, loc):
            continue
        choice(record['kind'], KINDS, loc + '.kind')
        choice(record['platform'], PLATFORMS, loc + '.platform')
        choice(record['feature'], features, loc + '.feature')
        choice(record['fixture'], scenarios, loc + '.fixture')
        choice(record['result'], {'pass', 'fail', 'blocked', 'inconclusive'}, loc + '.result')
        if not isinstance(record['commit'], str) or not COMMIT.fullmatch(record['commit']):
            error(loc + '.commit', 'expected immutable lowercase 40-digit Git SHA')
        for key in ['command', 'output', 'hardware']:
            text(record[key], loc + '.' + key)
        artifact = record['artifact']
        if text(artifact, loc + '.artifact'):
            path = PurePosixPath(artifact)
            if path.is_absolute() or '..' in path.parts or '\\' in artifact or ':' in artifact:
                error(loc + '.artifact', 'expected shareable relative artifact locator, not a private path')

    if errors:
        return errors

    def refs(value, where, pid, fid=None):
        selected = []
        if strings(value, where, nonempty=False):
            for eid in value:
                record = evidence.get(eid)
                if record is None:
                    error(where, f'unknown evidence {eid}')
                elif record.get('platform') != pid or (fid and record.get('feature') != fid):
                    error(where, f'evidence {eid} platform/feature mismatch')
                else:
                    selected.append(record)
        return selected

    def accept(records_used, required_kinds, where):
        passed = [r for r in records_used if r.get('result') == 'pass']
        if not required_kinds <= {r.get('kind') for r in passed}:
            error(where, 'accepted requires passing ' + ', '.join(sorted(required_kinds)) + ' evidence')
        if len({r.get('commit') for r in passed}) != 1:
            error(where, 'accepted evidence must use the same commit')

    for pid, row in platforms.items():
        loc = f'platforms.{pid}'
        if not obj(row, {'id', 'os', 'architecture', 'package', 'payload_kind', 'implementation', 'checks'}, loc):
            continue
        if pid in PLATFORMS:
            for key, expected in zip(['os', 'architecture', 'package', 'payload_kind'], PLATFORMS[pid]):
                if row[key] != expected:
                    error(loc + '.' + key, f'expected {expected}')
        choice(row['implementation'], IMPLEMENTATIONS - {'not_applicable'}, loc + '.implementation')
        if not obj(row['checks'], {'build', 'package', 'runtime'}, loc + '.checks'):
            continue
        all_records = []
        for key, kind in [('build', 'source_build'), ('package', 'package'), ('runtime', 'native_runtime')]:
            check = row['checks'][key]
            cloc = loc + '.checks.' + key
            if obj(check, {'status', 'evidence'}, cloc):
                choice(check['status'], VALIDATIONS - {'not_applicable'}, cloc + '.status')
                chosen = refs(check['evidence'], cloc + '.evidence', pid)
                all_records.extend(chosen)
                if check['status'] == 'accepted':
                    accept(chosen, {kind}, cloc)
        if row['implementation'] == 'supported':
            if any(not isinstance(c, dict) or c.get('status') != 'accepted' for c in row['checks'].values()):
                error(loc, 'supported requires accepted build, package and runtime')
            accept(all_records, {'source_build', 'package', 'native_runtime'}, loc)

    used_categories = set()
    for fid, row in features.items():
        loc = f'features.{fid}'
        if not obj(row, {'id', 'category', 'origin', 'priority', 'requirement', 'acceptance', 'fixtures', 'targets'}, loc):
            continue
        if choice(row['category'], CATEGORIES, loc + '.category'):
            used_categories.add(row['category'])
        choice(row['origin'], {'baseline', 'additive'}, loc + '.origin')
        choice(row['priority'], {'P0', 'P1'}, loc + '.priority')
        if not isinstance(row['requirement'], str) or not re.fullmatch(r'docs/PRODUCT_REQUIREMENTS\.md § (?:[1-6]|R(?:10|[1-9]))', row['requirement']):
            error(loc + '.requirement', 'expected normative document and section reference')
        strings(row['acceptance'], loc + '.acceptance')
        fixture_ok = strings(row['fixtures'], loc + '.fixtures')
        if fixture_ok:
            for fixture in row['fixtures']:
                if fixture not in scenarios:
                    error(loc, f'unknown fixture {fixture}')
        if not isinstance(row['targets'], dict) or set(row['targets']) != set(PLATFORMS):
            error(loc + '.targets', 'target set must contain every mandatory platform')
            continue
        for pid, target in row['targets'].items():
            tloc = loc + '.targets.' + pid
            if not obj(target, {'applicability', 'reason', 'implementation', 'validation', 'evidence'}, tloc):
                continue
            choice(target['applicability'], {'required', 'conditional', 'not_applicable'}, tloc + '.applicability')
            if fid in MANDATORY_FEATURES:
                required_targets = OS_SPECIFIC.get(fid, set(PLATFORMS) - {'android-apk'} if fid in DESKTOP_ONLY else set(PLATFORMS))
                allowed = {'required', 'conditional'} if fid in CONDITIONAL else {'required'}
                if pid not in required_targets:
                    allowed = {'not_applicable'}
                if not isinstance(target['applicability'], str) or target['applicability'] not in allowed:
                    error(tloc, 'mandatory applicability does not match OS/package/feature contract')
            text(target['reason'], tloc + '.reason')
            choice(target['implementation'], IMPLEMENTATIONS, tloc + '.implementation')
            choice(target['validation'], VALIDATIONS, tloc + '.validation')
            chosen = refs(target['evidence'], tloc + '.evidence', pid, fid)
            for record in chosen:
                if fixture_ok and record.get('fixture') not in row['fixtures']:
                    error(tloc, 'evidence fixture must belong to this feature')
            na = target['applicability'] == 'not_applicable'
            if na:
                if target['implementation'] != 'not_applicable' or target['validation'] != 'not_applicable' or target['evidence']:
                    error(tloc, 'not_applicable requires matching statuses and no acceptance evidence')
            elif 'not_applicable' in (target['implementation'], target['validation']):
                error(tloc, 'applicable feature cannot have not_applicable status')
            if target['validation'] == 'accepted':
                accept(chosen, {'unit', 'package', 'native_runtime'}, tloc)
                if target['implementation'] != 'supported':
                    error(tloc, 'accepted requires supported implementation')
            if target['implementation'] == 'supported' and target['validation'] != 'accepted':
                error(tloc, 'supported requires accepted validation')
    if not CATEGORIES <= used_categories:
        error('features', 'missing mandatory category coverage')
    return errors


def load_document(path):
    """Reject ambiguous object keys and JSON's nonstandard numeric constants."""
    def unique_keys(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f'duplicate JSON key {key}')
            result[key] = value
        return result

    def reject_constant(value):
        raise ValueError(f'nonfinite JSON number {value}')

    return json.loads(path.read_text(encoding='utf-8'), object_pairs_hook=unique_keys,
                      parse_constant=reject_constant)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--contract', type=Path, default=ROOT / 'docs/FEATURE_COVERAGE.json')
    parser.add_argument('--fixtures', type=Path, default=ROOT / 'tests/fixtures/scenarios.json')
    args = parser.parse_args(argv)
    try:
        contract = load_document(args.contract)
        fixtures = load_document(args.fixtures)
        errors = validate_contract(contract, fixtures)
    except (OSError, ValueError) as exc:
        print(f'coverage validation failed: {exc}', file=sys.stderr)
        return 1
    if errors:
        print('\n'.join(errors), file=sys.stderr)
        return 1
    print(f"Valid: {len(contract['features'])} features, {len(contract['platforms'])} targets, "
          f"{len(fixtures['scenarios'])} scenarios; native acceptance is not implied.")
    return 0


if __name__ == '__main__':
    sys.exit(main())
