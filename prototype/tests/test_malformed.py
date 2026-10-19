"""Browser regressions for malformed nested snapshot metadata.

Run from the project root: python prototype/tests/test_malformed.py -v
All snapshot changes are intercepted in the browser; no catalog/art files are written.
"""
import copy
import functools
import http.server
import json
import os
import pathlib
import shutil
import threading
import unittest
from urllib.parse import urlsplit

from playwright.sync_api import sync_playwright


ROOT = pathlib.Path(__file__).resolve().parents[2]


class Quiet(http.server.SimpleHTTPRequestHandler):
    def log_message(self, format, *args):
        pass


class MalformedSnapshotTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.snapshot = json.loads((ROOT / 'prototype/data/catalog.json').read_text())
        cls.server = http.server.ThreadingHTTPServer(
            ('127.0.0.1', 0), functools.partial(Quiet, directory=str(ROOT)))
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.base = f'http://127.0.0.1:{cls.server.server_port}'
        cls.playwright = sync_playwright().start()
        executable = (os.environ.get('CHROME_EXECUTABLE') or shutil.which('google-chrome')
                      or shutil.which('chromium') or shutil.which('chromium-browser'))
        launch = {'headless': True, 'args': ['--no-sandbox']}
        if executable:
            launch['executable_path'] = executable
        cls.browser = cls.playwright.chromium.launch(**launch)

    @classmethod
    def tearDownClass(cls):
        cls.browser.close()
        cls.playwright.stop()
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join()

    def setUp(self):
        self.page = self.browser.new_page()
        self.addCleanup(self.page.close)
        self.page.set_default_timeout(5000)
        self.errors = []
        self.requests = []
        self.page.on('pageerror', lambda error: self.errors.append(str(error)))
        self.page.on('request', lambda request: self.requests.append(request.url))
        self.payload = copy.deepcopy(self.snapshot)
        self.payload['apps'] = self.payload['apps'][:2]
        for index, app in enumerate(self.payload['apps']):
            app['games'] = [{'title': f'Regression alias {index}'}]
            app['artProvenance'] = [{
                'kind': 'reference', 'sourceUrl': f'https://example.org/source-{index}'}]

    def load_snapshot(self):
        self.page.route('**/data/catalog.json', lambda route: route.fulfill(
            content_type='application/json', body=json.dumps(self.payload)))
        self.page.goto(self.base + '/prototype/')
        self.page.locator('.game-card').first.wait_for()
        self.assertEqual(self.page.locator('.game-card').count(), 2)

    def search(self, query, expected_names):
        self.page.locator('#search').fill(query)
        self.assertEqual(self.errors, [], 'Search must not throw on malformed games')
        self.assertEqual(self.page.locator('.game-title').all_text_contents(), expected_names)

    def check_sources(self, expected_first):
        self.page.locator('#search').fill('')
        self.page.locator('#credits-button').click()
        self.assertEqual(self.errors, [], 'Sources must not throw on malformed provenance')
        self.assertTrue(self.page.locator('#about-dialog').is_visible())
        self.page.get_by_text('Individual artwork sources', exact=True).click()
        rows = self.page.locator('.source-list li')
        self.assertEqual(rows.count(), 2)
        self.assertEqual(rows.locator('strong').all_text_contents(), [
            app['projectName'] + ' — ' for app in self.payload['apps']])
        self.assertEqual(rows.nth(0).locator('a').evaluate_all(
            '(links) => links.map(a => [a.textContent, a.href])'), expected_first)
        self.assertEqual(rows.nth(1).locator('a').get_attribute('href'),
                         'https://example.org/source-1')
        self.assertEqual(self.page.locator('#about-content img, #about-content script').count(), 0)
        for anchor in self.page.locator('#about-content a[href]').all():
            url = urlsplit(anchor.get_attribute('href'))
            self.assertEqual(url.scheme, 'https')
            self.assertIsNone(url.username)
            self.assertIsNone(url.password)
            self.assertEqual(anchor.get_attribute('target'), '_blank')
            self.assertEqual(set((anchor.get_attribute('rel') or '').split()), {'noopener', 'noreferrer'})
        self.assertTrue(all(url.startswith(self.base + '/') for url in self.requests),
                        self.requests)
        self.assertEqual(self.errors, [])

    def check_games(self, malformed, retained_titles=()):
        self.payload['apps'][0]['games'] = malformed
        self.load_snapshot()
        self.search('Regression alias 1', [self.payload['apps'][1]['name']])
        for title in retained_titles:
            self.search(title, [self.payload['apps'][0]['name']])
        self.search('No such regression title', [])
        self.check_sources([['reference', 'https://example.org/source-0']])

    def test_games_object(self):
        self.check_games({})

    def test_games_null_entry(self):
        self.check_games([None])

    def test_games_mixed_entries_retain_valid_titles(self):
        self.check_games([
            None, {}, [], 'invalid', 7, False, {'title': None}, {'title': 42},
            {'title': 'Retained game alpha'}, {'title': 'Retained game beta'}],
            ['Retained game alpha', 'Retained game beta'])

    def check_provenance(self, malformed, expected=()):
        self.payload['apps'][0]['artProvenance'] = malformed
        self.load_snapshot()
        self.search('Regression alias 0', [self.payload['apps'][0]['name']])
        self.search('Regression alias 1', [self.payload['apps'][1]['name']])
        self.check_sources(list(expected))

    def test_provenance_object(self):
        self.check_provenance({})

    def test_provenance_null_entry(self):
        self.check_provenance([None])

    def test_provenance_mixed_entries_retain_safe_sources(self):
        label = '<img src=x onerror=alert(1)>'
        self.check_provenance([
            None, {}, [], 'invalid', 7, False,
            {'kind': None, 'sourceUrl': 'https://example.org/invalid-kind'},
            {'kind': 42, 'sourceUrl': 'https://example.org/invalid-number-kind'},
            {'kind': 'missing URL'}, {'kind': 'null URL', 'sourceUrl': None},
            {'kind': 'number URL', 'sourceUrl': 42},
            {'kind': 'script', 'sourceUrl': 'javascript:alert(1)'},
            {'kind': 'plain HTTP', 'sourceUrl': 'http://example.org/insecure'},
            {'kind': 'data', 'sourceUrl': 'data:text/html,<script>alert(1)</script>'},
            {'kind': 'credentials', 'sourceUrl': 'https://user:secret@example.org/private'},
            {'kind': label, 'sourceUrl': 'https://example.org/retained-hero'},
            {'kind': 'header', 'sourceUrl': 'https://example.org/retained-header'}],
            [[label, 'https://example.org/retained-hero'],
             ['header', 'https://example.org/retained-header']])


if __name__ == '__main__':
    unittest.main()
