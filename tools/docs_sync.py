"""Bounded documentation facts and explicit source-review gates (stdlib only).

No network, model, scheduler, Git writes or semantic acknowledgements. See
../docs/DOCUMENTATION.md for the reviewed manifest contract and closeout loop.
"""
import argparse
import hashlib
from html.parser import HTMLParser
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import tempfile
from urllib.parse import unquote, urlsplit

MANIFEST = 'docs/DOCUMENTATION.json'
STATUS = 'docs/SOURCE_STATUS.json'
OUTPUTS = ('README.md', 'docs/DOC_STATUS.md')
BEGIN = '<!-- docs-sync:begin facts -->'
END = '<!-- docs-sync:end facts -->'
EVIDENCE = {'docs/PREVIEW_QA.json', 'prototype/tests/results.json', 'prototype/tests/safety-results.json'}
HASH = re.compile(r'[0-9a-f]{64}\Z')


class Invalid(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise Invalid(message)


def keys(value, expected, name):
    require(isinstance(value, dict) and set(value) == set(expected), 'Invalid fields: ' + name)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode()


def relative(value):
    require(isinstance(value, str) and re.fullmatch(r'[A-Za-z0-9_./-]+', value), 'Invalid repository path')
    p = PurePosixPath(value)
    require(not p.is_absolute() and p.as_posix() == value and all(x not in ('.', '..') for x in p.parts), 'Invalid repository path: ' + value)
    return value


def safe(root, value, *, file=True):
    relative(value)
    path = root / value
    for part in [path, *path.parents]:
        if part == root:
            break
        require(not part.is_symlink(), 'symlink forbidden: ' + value)
    require(path.resolve().is_relative_to(root), 'Path escape: ' + value)
    require(path.is_file() if file else path.exists(), 'Missing repository path: ' + value)
    return path


def read(root, value, limit=16 * 1024 * 1024):
    path = safe(root, value)
    require(path.stat().st_size <= limit, 'Input too large: ' + value)
    return path.read_bytes()


def load(root, value):
    def unique(pairs):
        obj = {}
        for key, item in pairs:
            require(key not in obj, 'Duplicate JSON field: ' + key)
            obj[key] = item
        return obj
    try:
        limit = 512 * 1024 if value == MANIFEST else 64 * 1024 if value == STATUS else 16 * 1024 * 1024
        def reject_constant(value):
            raise Invalid('Nonfinite JSON number: ' + value)
        return json.loads(read(root, value, limit), object_pairs_hook=unique, parse_constant=reject_constant)
    except (UnicodeError, json.JSONDecodeError) as exc:
        raise Invalid('Invalid JSON: ' + value) from exc


def is_document(path):
    name = PurePosixPath(path).name.lower()
    return (name != 'requirements-dev.txt' and name.endswith(('.md', '.rst', '.txt'))) or name.startswith(('license', 'copying', 'notice'))


def inherently_protected(path):
    name = PurePosixPath(path).name.lower()
    return any(word in name for word in ('license', 'notice', 'trademark', 'changelog', '-ofl.'))


def listed(root):
    command = ['git', '-C', str(root)]
    top = subprocess.run(command + ['rev-parse', '--show-toplevel'], capture_output=True, check=True, timeout=10).stdout.decode().strip()
    require(Path(top).resolve() == root, 'Root must be the exact Git working tree')
    data = subprocess.run(command + ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], capture_output=True, check=True, timeout=10).stdout
    require(len(data) <= 4 * 1024 * 1024, 'Repository inventory exceeds documentation budget')
    paths = sorted(set(p.decode('utf-8') for p in data.split(b'\0') if p))
    require(len(paths) <= 10000, 'Repository file-count budget exceeded')
    # Tracked deletions, symlinks and unexpected paths fail before generation.
    for path in paths:
        safe(root, path)
    return paths


def manifest(root, paths):
    data = load(root, MANIFEST)
    keys(data, ['version', 'source_groups', 'documents', 'protected_files'], MANIFEST)
    require(type(data['version']) is int and data['version'] == 1, 'Unknown manifest version')
    groups, docs = data['source_groups'], data['documents']
    require(isinstance(groups, dict) and 0 < len(groups) <= 32, 'Missing/excessive source groups')
    for name, roots in groups.items():
        require(isinstance(name, str) and re.fullmatch('[a-z][a-z0-9_-]*', name) and name != 'status', 'Invalid source group')
        require(isinstance(roots, list) and 0 < len(roots) <= 128 and len(roots) == len(set(roots)), 'Invalid source roots')
        for source in roots:
            safe(root, source, file=False)
            require(source != MANIFEST and source != STATUS and not is_document(source) and '.git' not in PurePosixPath(source).parts, 'Recursive/document/Git source root: ' + source)
    require(isinstance(docs, dict) and 0 < len(docs) <= 256, 'Missing/excessive document policies')
    for path, entry in docs.items():
        safe(root, path)
        require(is_document(path), 'Not a documentation path: ' + path)
        require(isinstance(entry, dict), 'Invalid document entry')
        policy = entry.get('policy')
        if policy == 'review':
            keys(entry, ['policy', 'sources', 'reviewed'], path)
            require(not inherently_protected(path), 'Legal/history must be protected: ' + path)
            sources = entry['sources']
            require(isinstance(sources, list) and sources and len(sources) == len(set(sources)) and all(s in groups for s in sources), 'Unknown document source: ' + path)
            require(isinstance(entry['reviewed'], dict) and set(entry['reviewed']) <= set(sources + ['status']), 'Invalid reviewed map: ' + path)
            require(all(isinstance(h, str) and HASH.fullmatch(h) for h in entry['reviewed'].values()), 'Invalid review digest: ' + path)
        elif policy == 'protected':
            keys(entry, ['policy', 'sha256'], path)
            require(isinstance(entry['sha256'], str) and HASH.fullmatch(entry['sha256']), 'Invalid protection digest: ' + path)
            require(digest(read(root, path)) == entry['sha256'], 'Protected content changed; explicit legal/history review required: ' + path)
        elif policy == 'generated':
            keys(entry, ['policy'], path)
            require(path == 'docs/DOC_STATUS.md', 'Unknown generated output: ' + path)
        else:
            raise Invalid('Unknown document policy: ' + path)
    require(docs.get('README.md', {}).get('policy') == 'review', 'README requires semantic review')
    require(docs.get('docs/DOC_STATUS.md', {}).get('policy') == 'generated', 'Missing status output')
    protected = data['protected_files']
    require(isinstance(protected, dict) and len(protected) <= 256, 'Invalid protected files')
    for path, sha in protected.items():
        require(path not in OUTPUTS and isinstance(sha, str) and HASH.fullmatch(sha), 'Invalid protected file')
        require(digest(read(root, path)) == sha, 'Protected asset/evidence changed: ' + path)
    return data


def facts(root):
    status = load(root, STATUS)
    expected = {'schema_version': 1, 'source_stage': 'development', 'merged_source_pr': 1,
                'cleanup_pr': 3, 'cleanup_state': 'draft', 'packaged_release': False,
                'full_parity': False, 'native_platform_acceptance': False, 'emulation': 'planned'}
    keys(status, expected, STATUS)
    require(all(type(status[k]) is type(v) for k, v in expected.items()), 'Invalid source status types')
    require(all(status[k] == expected[k] for k in ('schema_version', 'source_stage', 'packaged_release', 'full_parity', 'native_platform_acceptance', 'emulation')), 'Unknown/unreviewed source status; release/support changes require schema review')
    require(all(0 < status[k] < 1000000 for k in ('merged_source_pr', 'cleanup_pr')), 'Invalid source PR identity')
    require(status['cleanup_state'] in ('draft', 'open', 'merged', 'closed'), 'Unknown cleanup source state')
    coverage = load(root, 'docs/FEATURE_COVERAGE.json')
    scenarios = load(root, 'tests/fixtures/scenarios.json')
    catalog = load(root, 'prototype/data/catalog.json')
    keys(coverage, ['schema_version', 'product', 'scope', 'categories', 'boundaries', 'platforms', 'evidence', 'features'], 'coverage facts')
    keys(scenarios, ['schema_version', 'scenarios'], 'scenario facts')
    keys(catalog, ['schemaVersion', 'fetchedAt', 'source', 'scope', 'attribution', 'sourceAttribution', 'apps'], 'catalog facts')
    require(type(coverage['schema_version']) is int and coverage['schema_version'] == 1
            and type(scenarios['schema_version']) is int and scenarios['schema_version'] == 1
            and type(catalog['schemaVersion']) is int and catalog['schemaVersion'] == 1, 'Unknown factual input version')
    def count_rows(value, key, identity):
        require(isinstance(value, dict) and isinstance(value.get(key), list) and 0 < len(value[key]) <= 10000, 'Invalid factual list: ' + key)
        rows = value[key]
        require(all(isinstance(row, dict) and isinstance(row.get(identity), str) and row[identity] for row in rows), 'Invalid factual row: ' + key)
        require(len({row[identity] for row in rows}) == len(rows), 'Duplicate factual identity: ' + key)
        return len(rows)
    return {'features': count_rows(coverage, 'features', 'id'),
            'platforms': count_rows(coverage, 'platforms', 'id'),
            'scenarios': count_rows(scenarios, 'scenarios', 'id'),
            'projects': count_rows(catalog, 'apps', 'id'), 'status': status}, digest(canonical(status))


def source_digests(root, paths, groups):
    result = {}
    for group, roots in groups.items():
        selected = [p for p in paths if any(p == r or p.startswith(r + '/') for r in roots)
                    and not is_document(p) and p not in EVIDENCE | {MANIFEST, STATUS}]
        require(selected, 'Source group has no eligible inputs: ' + group)
        require(sum(safe(root, p).stat().st_size for p in selected) <= 128 * 1024 * 1024, 'Source group byte budget exceeded: ' + group)
        result[group] = digest(canonical([[p, digest(read(root, p))] for p in selected]))
    return result


class References(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []
    def handle_starttag(self, tag, attrs):
        for key, value in attrs:
            if value and key in ('src', 'href'):
                self.links.append(value)
            if value and key == 'srcset':
                self.links.extend(item.strip().split()[0] for item in value.split(','))


def clean_markup(text):
    return re.sub(r'(?ms)^\s*(`{3,}|~{3,})[^\n]*\n.*?^\s*\1\s*$', '', text)


def anchors(text):
    seen, found = {}, set(re.findall(r'\bid=["\']([^"\']+)', text))
    for heading in re.findall(r'(?m)^#{1,6}\s+(.+?)\s*#*$', clean_markup(text)):
        slug = re.sub(r'[^\w\- ]', '', re.sub('<[^>]+>', '', heading).lower()).replace(' ', '-')
        n = seen.get(slug, 0)
        found.add(slug + ('-' + str(n) if n else ''))
        seen[slug] = n + 1
    return found


def validate_links(root, docs, outputs=None):
    # Validate the prospective document set, including unchanged manual wrappers.
    # Obsolete generated links may disappear in this refresh; links to anchors
    # removed by a refresh must not be validated against old on-disk content.
    outputs = outputs or {}
    def content(path):
        return outputs[path] if path in outputs else read(root, path, 2 * 1024 * 1024)
    errors = []
    for path in docs:
        text = clean_markup(content(path).decode('utf-8'))
        html = References()
        html.feed(text)
        # Inline Markdown, reference-style definitions, autolinks and raw HTML.
        links = re.findall(r'!?\[[^\]\n]*\]\(\s*(<[^>]+>|[^\s)]+)(?:\s+[^)]*)?\)', text)
        links += re.findall(r'(?m)^\s*\[[^\]]+\]:\s*(\S+)', text)
        links += re.findall(r'<((?:https?://|mailto:)[^>]+)>', text)
        links += html.links
        links += re.findall(r'`[^`\n]*<([^>]+)>`_', text)
        links += re.findall(r'(?m)^\s*\.\. _[^:]+:\s*(\S+)', text)
        for link in links:
            link = link.strip('<>')
            try:
                url = urlsplit(link)
                if url.scheme or url.netloc:
                    require(url.scheme in ('http', 'https', 'mailto') and not url.username and not url.password, 'Unsupported/credential-bearing link')
                    continue
                decoded = unquote(url.path)
                require(not decoded.startswith(('/', '\\')) and '\\' not in decoded, 'Absolute local link')
                target = (root / path).parent / decoded if decoded else root / path
                require(target.resolve().is_relative_to(root), 'Local link escape')
                rel = target.resolve().relative_to(root).as_posix()
                # Inspect original components too, not just the symlink-resolved path.
                cursor = target
                while cursor != root and cursor != cursor.parent:
                    require(not cursor.is_symlink(), 'symlink local link')
                    cursor = cursor.parent
                require(target.exists(), 'Missing local link')
                if url.fragment and target.is_file() and target.suffix.lower() == '.md':
                    require(unquote(url.fragment) in anchors(content(rel).decode('utf-8')), 'Missing local anchor')
            except (Invalid, ValueError, OSError) as exc:
                errors.append(path + ': ' + link + ' (' + str(exc) + ')')
    return errors


def replace_block(raw, content, path):
    require(len(raw) <= 2 * 1024 * 1024 and len(content.encode('utf-8')) <= 512 * 1024, 'Generated output budget exceeded: ' + path)
    text = raw.decode('utf-8')
    require(text.count(BEGIN) == 1 and text.count(END) == 1 and text.index(BEGIN) < text.index(END), 'Invalid generated markers: ' + path)
    start = text.index(BEGIN) + len(BEGIN)
    end = text.index(END)
    output = (text[:start] + '\n' + content.rstrip() + '\n' + text[end:]).encode('utf-8')
    require(len(output) <= 2 * 1024 * 1024, 'Generated output byte budget exceeded: ' + path)
    return output


def generated(root, data, counts):
    status = counts['status']
    cleanup = {'draft': 'remains a draft, not merged functionality',
               'open': 'is open review work, not merged functionality',
               'merged': 'is recorded as merged development source, not release acceptance',
               'closed': 'is closed without a merged-functionality claim'}[status['cleanup_state']]
    summary = ('Repository facts (not acceptance results): **{projects} catalog projects**, '
               '**{features} required feature rows**, **{platforms} platform/package targets** and '
               '**{scenarios} synthetic fixture scenarios**. Counts describe repository inputs, not working games or passing tests.\n\n'
               'Source status: merged development source includes [reviewed safety work](https://github.com/MrDiamondBallz/relicade/pull/{merged}); '
               '[backup cleanup](https://github.com/MrDiamondBallz/relicade/pull/{cleanup_pr}) {cleanup}. '
               'No packaged release, full-parity acceptance or native-platform acceptance is recorded here. Emulation is planned, not implemented.').format(
                   **counts, merged=status['merged_source_pr'], cleanup_pr=status['cleanup_pr'], cleanup=cleanup)
    inventory = ['This inventory is generated from the explicit maintenance registry. Semantic review is a separate gate, never inferred from these counts.',
                 '', '| Document | Policy | Source responsibilities |', '| --- | --- | --- |']
    for path, entry in sorted(data['documents'].items()):
        inventory.append('| [' + path + '](' + os.path.relpath(path, 'docs').replace(os.sep, '/') + ') | ' + entry['policy'] + ' | ' + ', '.join(entry.get('sources', [])) + ' |')
    inventory.extend(['', summary, '', 'Source responsibilities:', ''])
    inventory.extend('- `' + group + '`: ' + ', '.join('`' + p + '`' for p in roots) for group, roots in sorted(data['source_groups'].items()))
    bodies = {'README.md': summary, 'docs/DOC_STATUS.md': '\n'.join(inventory)}
    return {path: replace_block(read(root, path), body, path) for path, body in bodies.items()}


def atomic_batch(root, outputs):
    """Prestage every file; rollback any replaced output on ordinary I/O failure.

    A process/power failure can leave a subset refreshed. A subsequent check
    detects that state; it never updates the semantic acknowledgement manifest.
    """
    staged, replaced, originals, modes = {}, [], {}, {}
    try:
        for path, value in outputs.items():
            target = safe(root, path)
            require(target.stat().st_nlink == 1, 'Hardlinked output forbidden: ' + path)
            originals[path] = target.read_bytes()
            modes[path] = stat.S_IMODE(target.stat().st_mode)
            if originals[path] == value:
                continue
            fd, tmp = tempfile.mkstemp(prefix='.docs-sync-', dir=target.parent)
            staged[path] = Path(tmp)
            with os.fdopen(fd, 'wb') as stream:
                stream.write(value)
                stream.flush()
                os.fsync(stream.fileno())
            os.chmod(tmp, stat.S_IMODE(target.stat().st_mode))
        for path, tmp in staged.items():
            safe(root, path)
            os.replace(tmp, root / path)
            replaced.append(path)
    except BaseException:
        for path in reversed(replaced):
            # No in-place truncation of an output (or possible hardlink target).
            fd, tmp = tempfile.mkstemp(prefix='.docs-sync-rollback-', dir=(root / path).parent)
            with os.fdopen(fd, 'wb') as stream:
                stream.write(originals[path])
            os.chmod(tmp, modes[path])
            os.replace(tmp, root / path)
        raise
    finally:
        for tmp in staged.values():
            tmp.unlink(missing_ok=True)


def run(root, mode):
    report = {'ok': False, 'errors': [], 'unregistered': [], 'generated_stale': [],
              'review_required': [], 'source_digests': {}, 'status_digest': None, 'written': []}
    try:
        require(root.exists() and not root.is_symlink() and root.absolute() == root.resolve(), 'Root/ancestors must not be symlinks')
        paths = listed(root)
        data = manifest(root, paths)
        # Git omits ignored untracked outputs, never tracked/nonignored authored
        # inputs. A directory's basename is not proof that its contents are junk.
        discovered = {p for p in paths if is_document(p)}
        report['unregistered'] = sorted(discovered - data['documents'].keys())
        require(not report['unregistered'], 'Unregistered documentation requires a maintenance policy')
        counts, report['status_digest'] = facts(root)
        eligible = [p for p in paths if not is_document(p) and p not in EVIDENCE | {MANIFEST, STATUS}]
        roots = [r for group in data['source_groups'].values() for r in group]
        unassigned = [p for p in eligible if not any(p == r or p.startswith(r + '/') for r in roots)]
        require(not unassigned, 'Unmapped source inputs require responsibility classification: ' + ', '.join(unassigned))
        report['source_digests'] = source_digests(root, paths, data['source_groups'])
        outputs = generated(root, data, counts)
        report['errors'].extend(validate_links(root, data['documents'], outputs))
        # Validate every output inode before allowing any write. Generated links
        # are constructed only from the already validated registry paths.
        for path in OUTPUTS:
            require(safe(root, path).stat().st_nlink == 1, 'Hardlinked output forbidden: ' + path)
        for path, entry in data['documents'].items():
            if entry['policy'] == 'review':
                wanted = {s: report['source_digests'][s] for s in entry['sources']}
                wanted['status'] = report['status_digest']
                if entry['reviewed'] != wanted:
                    report['review_required'].append(path)
        report['generated_stale'] = [p for p, value in outputs.items() if read(root, p) != value]
        if mode == '--write' and not report['errors']:
            atomic_batch(root, outputs)
            report['written'] = report['generated_stale'][:]
            report['generated_stale'] = []
        report['ok'] = not (report['errors'] or report['unregistered'] or report['generated_stale'] or report['review_required'])
    except (Invalid, OSError, ValueError, TypeError, KeyError, RecursionError, subprocess.SubprocessError, UnicodeError) as exc:
        report['errors'].append(str(exc))
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    for flag in ('--write', '--check', '--audit'):
        mode.add_argument(flag, action='store_true')
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1], help='Exact Git worktree (default: repository containing this tool)')
    args = parser.parse_args()
    selected = '--write' if args.write else '--audit' if args.audit else '--check'
    report = run(args.root.absolute(), selected)
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0 if report['ok'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
