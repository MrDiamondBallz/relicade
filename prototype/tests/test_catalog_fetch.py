"""Offline refresh/security regressions; generated output uses temporary roots."""
import importlib.util
import io
import json
from pathlib import Path
import socket
import ssl
import sys
import tempfile
import unittest
from unittest.mock import ANY, Mock, patch
import urllib.error

SPEC = importlib.util.spec_from_file_location('catalog_fetch', Path(__file__).resolve().parents[1] / 'fetch_catalog.py')
assert SPEC and SPEC.loader
fetch = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(fetch)
REAL_BUILD_OPENER = fetch.urllib.request.build_opener
META = 'https://api.quiverlauncher.com/api/v1/apps/example'
ART = 'https://cdn2.steamgriddb.com/hero/example.png'
PUBLIC = '93.184.216.34'
SOURCE_ATTRIBUTION = 'Public metadata: Quiver catalog and respective project maintainers. Game artwork: respective publishers, rights holders and SteamGridDB contributors. Unaffiliated prototype; no ROMs or executables included.'


def fixture_metadata(library_art=None):
    return {'entry': {'slug': 'fixture', 'name': 'Fixture', 'projectName': 'Fixture Project',
                      'artwork': ART, 'libraryArt': library_art or {}},
            'project': {'name': 'Fixture Project', 'url': 'https://example.invalid/project'}}


class Response(io.BytesIO):
    def __init__(self, data=b'{}', headers=None):
        super().__init__(data)
        self.headers = headers or {}
        self.read_sizes = []

    def read(self, size=-1):
        self.read_sizes.append(size)
        return super().read(size)


class CatalogFetchTests(unittest.TestCase):
    def setUp(self):
        self.response = Response()
        self.opener = Mock()
        self.opener.open.return_value = self.response
        self.dns = self.enterContext(patch.object(fetch.socket if hasattr(fetch, 'socket') else socket,
                                                 'getaddrinfo', return_value=[(socket.AF_INET, socket.SOCK_STREAM, 6, '', (PUBLIC, 443))]))
        self.build = self.enterContext(patch.object(fetch.urllib.request, 'build_opener', return_value=self.opener))
        # Intercept the vulnerable implementation too; a RED run never touches the network/files.
        self.legacy = self.enterContext(patch.object(fetch.urllib.request, 'urlopen', return_value=self.response))

    def assert_denied(self, url, **kwargs):
        with self.assertRaises(ValueError):
            fetch.request(url, **kwargs)
        self.build.assert_not_called()
        self.legacy.assert_not_called()
        self.assertEqual(self.response.read_sizes, [])

    def test_rejects_file_scheme_without_open_or_read(self):
        self.assert_denied('file:///not-a-real-fixture/no-secret')

    def test_rejects_http_loopback_without_open_or_read(self):
        self.assert_denied('http://127.0.0.1:12345/private-service')

    def test_rejects_private_and_unapproved_hosts_before_dns(self):
        for url in ['https://127.0.0.1/', 'https://[::1]/', 'https://10.0.0.1/',
                    'https://169.254.169.254/', 'https://unapproved.invalid/',
                    'https://api.quiverlauncher.com.unapproved.invalid/',
                    'https://api.quiverlauncher.com:444/', 'https://api.quiverlauncher.com./']:
            with self.subTest(url=url):
                self.assert_denied(url)
        self.dns.assert_not_called()

    def test_rejects_credentials_before_dns(self):
        self.assert_denied('https://user:password@api.quiverlauncher.com/api/v1/apps/example')
        self.dns.assert_not_called()

    def test_role_allowlists_match_snapshot_hosts(self):
        snapshot = json.loads((Path(__file__).resolve().parents[1] / 'data/catalog.json').read_text())
        from urllib.parse import urlsplit
        self.assertEqual(fetch.METADATA_HOSTS, {urlsplit(a['metadataUrl']).hostname for a in snapshot['apps']})
        self.assertEqual(fetch.ART_HOSTS, {urlsplit(url).hostname for a in snapshot['apps']
                                         for url in a['referenceArtSources']['libraryArt'].values() if url})

    def test_rejects_cross_role_hosts_and_unknown_role(self):
        for url, role in [(ART, 'metadata'), (META, 'art'), (META, 'unknown')]:
            with self.subTest(url=url, role=role):
                self.assert_denied(url, role=role)
        self.dns.assert_not_called()

    def test_rejects_nonpublic_dns_answers_before_open(self):
        for address in ['127.0.0.1', '10.0.0.1', '169.254.169.254', '0.0.0.0',
                        '224.0.0.1', '192.0.2.1', '::1', 'fe80::1', 'fc00::1', '::ffff:127.0.0.1']:
            with self.subTest(address=address):
                self.dns.return_value = [(socket.AF_INET6 if ':' in address else socket.AF_INET,
                                          socket.SOCK_STREAM, 6, '', (address, 443))]
                self.assert_denied(META)

    def test_rejects_mixed_or_empty_dns_answers(self):
        for answers in [[], [(socket.AF_INET, socket.SOCK_STREAM, 6, '', (PUBLIC, 443)),
                             (socket.AF_INET, socket.SOCK_STREAM, 6, '', ('127.0.0.1', 443))]]:
            with self.subTest(answers=answers):
                self.dns.return_value = answers
                self.assert_denied(META)

    def test_valid_metadata_preserves_timeout_and_bounded_read(self):
        self.assertEqual(fetch.request(META), b'{}')
        self.opener.open.assert_called_once()
        self.assertEqual(self.opener.open.call_args.kwargs['timeout'], 35)
        self.assertEqual(self.response.read_sizes, [fetch.METADATA_MAX_BYTES + 1])
        self.assertTrue(self.response.closed)
        self.legacy.assert_not_called()

    def test_valid_art_uses_separate_limit(self):
        self.assertEqual(fetch.request(ART, role='art'), b'{}')
        self.assertEqual(self.response.read_sizes, [fetch.ART_MAX_BYTES + 1])

    def test_rejects_declared_oversize_without_read(self):
        self.response.headers = {'Content-Length': str(100 * 1024 * 1024)}
        with self.assertRaises(ValueError):
            fetch.request(META)
        self.assertEqual(self.response.read_sizes, [])
        self.assertTrue(self.response.closed)

    def test_rejects_oversize_even_without_content_length(self):
        self.response = Response(b'x' * (2 * 1024 * 1024 + 1))
        self.opener.open.return_value = self.response
        self.legacy.return_value = self.response
        with self.assertRaises(ValueError):
            fetch.request(META)
        self.assertEqual(self.response.read_sizes, [fetch.METADATA_MAX_BYTES + 1])
        self.assertTrue(self.response.closed)

    def test_rejects_oversize_art(self):
        self.response = Response(b'x' * (16 * 1024 * 1024 + 1))
        self.opener.open.return_value = self.response
        with self.assertRaises(ValueError):
            fetch.request(ART, role='art')
        self.assertEqual(self.response.read_sizes, [fetch.ART_MAX_BYTES + 1])

    def redirect(self, destination):
        body = Response(b'must not read redirect body')
        error = urllib.error.HTTPError(META, 302, 'Found', {'Location': destination}, body)
        self.addCleanup(error.close)
        self.opener.open.side_effect = [error, self.response]
        return body

    def test_unsafe_redirects_do_not_open_destination_or_read(self):
        for destination in ['file:///no-real-file', 'http://127.0.0.1/private',
                            'https://10.0.0.1/private', 'https://unapproved.invalid/art',
                            'https://user:password@api.quiverlauncher.com/private', ART]:
            with self.subTest(destination=destination):
                self.opener.reset_mock()
                self.build.reset_mock()
                body = self.redirect(destination)
                with self.assertRaises(ValueError):
                    fetch.request(META)
                self.assertEqual(self.opener.open.call_count, 1)
                self.assertEqual(self.build.call_count, 1)
                self.assertEqual(body.read_sizes, [])
                self.assertTrue(body.closed)
                self.assertEqual(self.response.read_sizes, [])

    def test_redirect_dns_is_revalidated_before_destination_open(self):
        body = self.redirect('/api/v1/apps/moved')
        self.dns.side_effect = [[(socket.AF_INET, socket.SOCK_STREAM, 6, '', (PUBLIC, 443))],
                                [(socket.AF_INET, socket.SOCK_STREAM, 6, '', ('127.0.0.1', 443))]]
        with self.assertRaises(ValueError):
            fetch.request(META)
        self.assertEqual(self.opener.open.call_count, 1)
        self.assertEqual(body.read_sizes, [])
        self.assertEqual(self.response.read_sizes, [])

    def test_safe_relative_redirect_revalidated_and_followed(self):
        body = self.redirect('/api/v1/apps/moved')
        self.assertEqual(fetch.request(META), b'{}')
        self.assertEqual(self.dns.call_count, 2)
        self.assertEqual(self.opener.open.call_args.args[0].full_url,
                         'https://api.quiverlauncher.com/api/v1/apps/moved')
        self.assertEqual(body.read_sizes, [])
        self.assertTrue(body.closed)

    def test_redirect_loop_is_bounded_without_body_reads(self):
        bodies = [Response() for _ in range(10)]
        errors = [urllib.error.HTTPError(META, 302, 'Found', {'Location': META}, b) for b in bodies]
        for error in errors:
            self.addCleanup(error.close)
        self.opener.open.side_effect = errors
        with self.assertRaises(ValueError):
            fetch.request(META)
        self.assertLessEqual(self.opener.open.call_count, 6)
        self.assertTrue(all(not b.read_sizes for b in bodies))

    def test_redirect_handler_disables_automatic_following(self):
        handler = fetch.NoRedirect()
        self.assertIsNone(handler.redirect_request(Mock(), Mock(), 302, 'Found', {}, ART))

    def test_connections_pin_validated_ip_with_original_tls_hostname(self):
        # Exercise actual HTTPSConnection -> TLS path entirely through socket mocks.
        connection = fetch.PinnedHTTPSConnection('api.quiverlauncher.com', addresses=[PUBLIC], timeout=35)
        sock = Mock()
        with patch.object(socket, 'create_connection', return_value=sock) as connect:
            with patch.object(connection._context, 'wrap_socket', return_value=Mock()) as tls:
                connection.connect()
        self.assertEqual(connect.call_args.args[0], (PUBLIC, 443))
        self.assertEqual(tls.call_args.kwargs['server_hostname'], 'api.quiverlauncher.com')
        self.dns.assert_not_called()

    def test_real_opener_and_https_handler_use_pinned_connection(self):
        self.build.side_effect = REAL_BUILD_OPENER
        sock = Mock()
        sock.makefile.return_value = io.BytesIO(b'HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\n{}')
        with patch.object(socket, 'create_connection', return_value=sock) as connect:
            with patch.object(ssl.SSLContext, 'wrap_socket', return_value=sock) as tls:
                self.assertEqual(fetch.request(META), b'{}')
        self.assertEqual(connect.call_args.args[0], (PUBLIC, 443))
        self.assertEqual(connect.call_args.args[1], 35)
        self.assertEqual(tls.call_args.kwargs['server_hostname'], 'api.quiverlauncher.com')
        self.assertEqual(self.dns.call_count, 1)
        self.legacy.assert_not_called()

    def test_real_opener_rejects_redirect_without_reading_body(self):
        self.build.side_effect = REAL_BUILD_OPENER
        sock = Mock()
        body = Response(b'HTTP/1.1 302 Found\r\nLocation: https://127.0.0.1/private\r\nContent-Length: 4\r\n\r\nSTOP')
        sock.makefile.return_value = body
        with patch.object(socket, 'create_connection', return_value=sock) as connect:
            with patch.object(ssl.SSLContext, 'wrap_socket', return_value=sock):
                with self.assertRaises(ValueError):
                    fetch.request(META)
        self.assertEqual(connect.call_count, 1)
        self.assertEqual(body.read_sizes, [])
        self.assertTrue(body.closed)
        self.assertEqual(self.build.call_count, 1)

    def test_collect_keeps_unsafe_upstream_art_as_reference_without_open(self):
        metadata = fixture_metadata({'hero': 'file:///no-real-file'})
        self.response = Response(json.dumps(metadata).encode())
        self.opener.open.return_value = self.response
        with patch.object(fetch, 'project_art', create=True) as art:
            art.apply_original_art.side_effect = lambda app, **kwargs: app
            app = fetch.collect('fixture')
            art.apply_original_art.assert_called_once_with(app, root=fetch.ROOT)
        self.assertEqual(self.opener.open.call_count, 1)
        self.legacy.assert_not_called()
        self.assertEqual(self.opener.open.call_args.args[0].full_url,
                         fetch.BASE + '/apps/fixture')

    def test_collect_only_requests_metadata_even_for_valid_https_art(self):
        metadata = fixture_metadata({kind: ART for kind in ('hero', 'header', 'capsule')})
        with patch.object(fetch, 'request', return_value=json.dumps(metadata).encode()) as request:
            with patch.object(fetch, 'project_art', create=True) as art:
                art.apply_original_art.side_effect = lambda app, **kwargs: app
                app = fetch.collect('fixture')
                art.apply_original_art.assert_called_once_with(app, root=fetch.ROOT)
        request.assert_called_once_with(fetch.BASE + '/apps/fixture')
        self.assertEqual(app['sourceProject'], metadata['project'])
        self.assertEqual(app['catalogUrl'], 'https://quiverlauncher.com/apps/fixture')
        self.assertEqual(app['metadataUrl'], fetch.BASE + '/apps/fixture')

    def test_collect_renders_original_art_in_temporary_root(self):
        metadata = fixture_metadata({'hero': ART})
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch.object(fetch, 'ROOT', root):
                with patch.object(fetch, 'request', return_value=json.dumps(metadata).encode()) as request:
                    app = fetch.collect('fixture')
            request.assert_called_once_with(fetch.BASE + '/apps/fixture')
            self.assertNotIn('artwork', app)
            self.assertNotIn('libraryArt', app)
            self.assertEqual(app['referenceArtSources']['artwork'], ART)
            self.assertEqual(app['referenceArtSources']['libraryArt'], {'hero': ART})
            self.assertTrue(app['localArt'])
            self.assertTrue(app['artProvenance'])
            for path in app['localArt'].values():
                self.assertTrue((root / path).is_file())

    def test_importlib_load_finds_sibling_art_module_without_sys_path_entry(self):
        assert fetch.__file__
        source = Path(fetch.__file__).resolve()
        search_path = [p for p in sys.path if Path(p).resolve() != source.parent]
        with patch.object(sys, 'path', search_path):
            spec = importlib.util.spec_from_file_location('isolated_catalog_fetch', source)
            assert spec and spec.loader
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
        self.assertEqual(Path(module.project_art.__file__).resolve(), source.with_name('project_art.py'))
        self.assertTrue(callable(module.project_art.apply_original_art))

    def test_main_collects_metadata_and_writes_truthful_original_art_attribution(self):
        metadata = fixture_metadata({kind: ART for kind in ('hero', 'header', 'capsule')})
        def render(app, *, root):
            app['localArt'] = {'hero': 'assets/art/fixture-hero.webp'}
            app['artProvenance'] = []
            return app
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch.object(fetch, 'ROOT', root), patch.object(fetch, 'SLUGS', ['fixture']):
                with patch.object(fetch, 'request', return_value=json.dumps(metadata).encode()) as request:
                    with patch.object(fetch, 'project_art', create=True) as art:
                        art.apply_original_art.side_effect = render
                        with patch('sys.stdout', new_callable=io.StringIO):
                            fetch.main()
                        art.apply_original_art.assert_called_once_with(ANY, root=root)
            snapshot = json.loads((root / 'data/catalog.json').read_text())
        request.assert_called_once_with(fetch.BASE + '/apps/fixture')
        self.assertEqual(snapshot['sourceAttribution'], SOURCE_ATTRIBUTION)
        self.assertEqual(snapshot['source'], fetch.BASE)
        self.assertEqual(len(snapshot['apps']), 1)
        self.assertTrue(snapshot['fetchedAt'])
        self.assertIn('original', snapshot['attribution'].lower())
        self.assertIn('external reference', snapshot['attribution'].lower())
        self.assertIn('not downloaded or redistributed', snapshot['attribution'])


if __name__ == '__main__':
    unittest.main()
