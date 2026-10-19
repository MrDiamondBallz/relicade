"""Bounded public metadata snapshot with original, locally generated project art.

Remote game artwork is retained only as external reference metadata, never fetched.
"""
import concurrent.futures, datetime, http.client, importlib.util, ipaddress, json, pathlib, socket, sys, urllib.error, urllib.parse, urllib.request
ROOT = pathlib.Path(__file__).parent

# File-based consumers need not put prototype/ on sys.path to find the sibling.
if __package__:
    from . import project_art
else:
    _art_spec = importlib.util.spec_from_file_location('_relicade_project_art', ROOT / 'project_art.py')
    if _art_spec is None or _art_spec.loader is None:
        raise ImportError('Cannot load the original project artwork renderer')
    project_art = importlib.util.module_from_spec(_art_spec)
    sys.modules[_art_spec.name] = project_art
    _art_spec.loader.exec_module(project_art)

BASE = 'https://api.quiverlauncher.com/api/v1'
SLUGS = [
 'thelegendofzeldamajorasmaskandocarinaoftime-zelda64recompiled',
 'sonicunleashed-unleashedrecompiled','banjokazooie-banjorecompiled',
 'thelegendofzeldaocarinaoftime-shipofharkinian',
 'supermario64-supermario64coopdx','opengoal-launcher-opengoal-launcher',
 'perfectdark-perfectdarkpcport','sonic3knuckles-sonic3air',
 'mariokart64-mariokart64recompiled','doom-gzdoom',
 'papermario-papermariorecut','supermetroid-snesrevsupermetroidlauncher']

# Exact role-specific hosts in data/catalog.json, not a wildcard provider list.
METADATA_HOSTS = frozenset({'api.quiverlauncher.com'})
ART_HOSTS = frozenset({'cdn2.steamgriddb.com'})
METADATA_MAX_BYTES = 2 * 1024 * 1024
ART_MAX_BYTES = 16 * 1024 * 1024

MAX_REDIRECTS = 5


def public_addresses(url, role):
    hosts = {'metadata': METADATA_HOSTS, 'art': ART_HOSTS}.get(role)
    if hosts is None or not isinstance(url, str) or any(c.isspace() or ord(c) < 32 for c in url):
        raise ValueError('Invalid snapshot URL or role')
    parsed = urllib.parse.urlsplit(url)
    if (parsed.scheme != 'https' or parsed.hostname not in hosts
            or parsed.username is not None or parsed.password is not None
            or parsed.port not in (None, 443)):
        raise ValueError('Snapshot URL must use HTTPS and an approved role-specific host without credentials')
    answers = socket.getaddrinfo(parsed.hostname, 443, type=socket.SOCK_STREAM)
    addresses = list(dict.fromkeys(answer[4][0] for answer in answers))
    if not addresses:
        raise ValueError('Snapshot host has no public address')
    for address in addresses:
        ip = ipaddress.ip_address(address)
        if not ip.is_global or ip.is_multicast or ip.is_unspecified or ip.is_loopback or ip.is_link_local or ip.is_reserved:
            raise ValueError('Snapshot host resolved to a nonpublic address')
    return addresses


class PinnedHTTPSConnection(http.client.HTTPSConnection):
    """Connect only to prevalidated IPs; retain the original hostname for TLS."""
    def __init__(self, host, *, addresses, **kwargs):
        super().__init__(host, **kwargs)
        self._addresses = addresses
        self._create_connection = self._connect_public

    def _connect_public(self, address, timeout, source_address):
        last_error = None
        for ip in self._addresses:
            try:
                return socket.create_connection((ip, address[1]), timeout, source_address)
            except OSError as error:
                last_error = error
        raise last_error or OSError('No validated address')


class PinnedHTTPSHandler(urllib.request.HTTPSHandler):
    def __init__(self, addresses):
        super().__init__()
        self.addresses = addresses

    def https_open(self, req):
        def connection(host, **kwargs):
            return PinnedHTTPSConnection(host, addresses=self.addresses, **kwargs)
        return self.do_open(connection, req, context=self._context)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def request(url, *, role='metadata'):
    limit = {'metadata': METADATA_MAX_BYTES, 'art': ART_MAX_BYTES}.get(role)
    if limit is None:
        raise ValueError('Unknown snapshot request role')
    for redirects in range(MAX_REDIRECTS + 1):
        addresses = public_addresses(url, role)
        # Ignore environment proxies: they would bypass address pinning.
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(), PinnedHTTPSHandler(addresses))
        req = urllib.request.Request(url, headers={
            'User-Agent': 'Relicade-design-prototype/0.1 public-metadata-snapshot',
            'Accept-Encoding': 'identity'})
        try:
            response = opener.open(req, timeout=35)
        except urllib.error.HTTPError as error:
            if error.code not in (301, 302, 303, 307, 308):
                error.close()
                raise
            try:
                location = error.headers.get('Location')
                if not location or redirects == MAX_REDIRECTS:
                    raise ValueError('Missing redirect destination or too many redirects')
                url = urllib.parse.urljoin(url, location)
            finally:
                error.close()
            # Validate and re-resolve the destination before opening it; never read redirect bodies.
            continue
        with response:
            length = response.headers.get('Content-Length')
            if length is not None and (not length.isdecimal() or int(length) > limit):
                raise ValueError('Snapshot response exceeds size limit or has invalid length')
            raw = response.read(limit + 1)
            if len(raw) > limit:
                raise ValueError('Snapshot response exceeds size limit')
            return raw
    raise ValueError('Too many redirects')

def collect(slug):
    """Fetch metadata only; migrate remote references and render original art."""
    quoted_slug = urllib.parse.quote(slug, safe='')
    url = BASE + '/apps/' + quoted_slug
    data = json.loads(request(url))
    app = data['entry']
    app['sourceProject'] = data.get('project', {})
    app['catalogUrl'] = 'https://quiverlauncher.com/apps/' + quoted_slug
    app['metadataUrl'] = url
    return project_art.apply_original_art(app, root=ROOT)


def main():
    """Refresh public metadata and generate original art, without fetching art."""
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
        apps = list(executor.map(collect, SLUGS))
    out = {
        'schemaVersion': 1,
        'fetchedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'source': BASE,
        'scope': f'Editorial selection of {len(SLUGS)} public catalog projects. Not a full catalog. Metadata is a point-in-time third-party snapshot, not independently validated by Relicade.',
        'attribution': 'Public catalog metadata and project details: respective providers and project maintainers. Local artwork: original Relicade project artwork generated by prototype/project_art.py. External reference metadata only; source artwork is not downloaded or redistributed. External artwork rights remain with respective publishers, rights holders and SteamGridDB contributors. Source links and exact provenance are retained. Unaffiliated prototype; no ROMs or executables included.',
        'sourceAttribution': 'Public metadata: Quiver catalog and respective project maintainers. Game artwork: respective publishers, rights holders and SteamGridDB contributors. Unaffiliated prototype; no ROMs or executables included.',
        'apps': apps,
    }
    destination = ROOT / 'data/catalog.json'
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps([{'name': a['name'], 'project': a['projectName'],
                       'projectSource': a['sourceProject'], 'art': list(a['localArt'])}
                      for a in apps], indent=2))


if __name__ == '__main__':
    main()
