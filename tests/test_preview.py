"""Real HTTP regression tests for the private, loopback-only design preview."""
import http.client
import importlib.util
from pathlib import Path
import tempfile
import threading
import unittest

MODULE = Path(__file__).resolve().parents[1] / 'tools' / 'preview.py'


class PreviewTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(MODULE.is_file(), 'Preview server has not been implemented')
        spec = importlib.util.spec_from_file_location('relicade_preview', MODULE)
        assert spec is not None and spec.loader is not None
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        self.tmp = tempfile.TemporaryDirectory(prefix='relicade-preview-test-')
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        for folder in ['prototype', 'brand', 'research/local-artifacts']:
            (self.root / folder).mkdir(parents=True)
        (self.root/'prototype/index.html').write_text('<h1>Relicade</h1>')
        (self.root/'brand/index.html').write_text('<h1>Brand</h1>')
        (self.root/'research/local-artifacts/private.json').write_text('not served')
        self.server = self.module.make_server(self.root, 0)
        self.addCleanup(self.server.server_close)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.server.shutdown)

    def request(self, path, method='GET', host=None):
        c = http.client.HTTPConnection('127.0.0.1', self.server.server_port, timeout=5)
        try:
            headers = {} if host is None else {'Host': host}
            c.request(method, path, headers=headers)
            response = c.getresponse()
            return response.status, dict(response.getheaders()), response.read()
        finally:
            c.close()

    def test_root_opens_prototype(self):
        status, headers, _ = self.request('/')
        self.assertEqual(status, 302)
        self.assertEqual(headers['Location'], '/prototype/')

    def test_serves_actual_prototype(self):
        status, headers, body = self.request('/prototype/')
        self.assertEqual(status, 200)
        self.assertIn(b'Relicade', body)
        self.assertEqual(headers['X-Content-Type-Options'], 'nosniff')
        self.assertIn("object-src 'none'", headers['Content-Security-Policy'])

    def test_brand_is_reachable(self):
        self.assertEqual(self.request('/brand/')[0], 200)

    def test_research_is_not_reachable(self):
        self.assertEqual(self.request('/research/local-artifacts/private.json')[0], 404)

    def test_traversal_is_rejected(self):
        for path in ['/prototype/../research/local-artifacts/private.json', '/prototype/%2e%2e/research/local-artifacts/private.json', '/prototype/%2e%2e%2fresearch/local-artifacts/private.json']:
            with self.subTest(path=path):
                self.assertEqual(self.request(path)[0], 404)

    def test_symlink_escape_is_rejected(self):
        (self.root/'prototype/escape').symlink_to(self.root/'research', target_is_directory=True)
        self.assertEqual(self.request('/prototype/escape/local-artifacts/private.json')[0], 404)

    def test_symlinked_asset_root_is_rejected(self):
        import shutil
        shutil.rmtree(self.root/'prototype')
        (self.root/'prototype').symlink_to(self.root/'research', target_is_directory=True)
        self.assertEqual(self.request('/prototype/local-artifacts/private.json')[0], 404)

    def test_implicit_index_symlink_escape_is_rejected(self):
        (self.root/'prototype/linked').mkdir()
        (self.root/'prototype/linked/index.html').symlink_to(self.root/'research/local-artifacts/private.json')
        self.assertEqual(self.request('/prototype/linked/')[0], 404)

    def test_directory_listing_is_disabled(self):
        (self.root/'prototype/empty').mkdir()
        self.assertEqual(self.request('/prototype/empty/')[0], 404)

    def test_foreign_host_is_rejected(self):
        self.assertEqual(self.request('/prototype/', host='untrusted.invalid')[0], 403)

    def test_write_request_is_rejected(self):
        self.assertEqual(self.request('/prototype/', method='POST')[0], 405)

    def test_head_has_no_body(self):
        status, _, body = self.request('/prototype/', method='HEAD')
        self.assertEqual(status, 200)
        self.assertEqual(body, b'')


if __name__ == '__main__':
    unittest.main()
