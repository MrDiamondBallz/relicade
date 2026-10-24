"""Pinned embedded metadata must survive Git's Windows line-ending checkout rules."""
from pathlib import Path
import hashlib
import os
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
RELATIVE = 'native/CatalogData/wikidata-games-20261024.jsonl'
EXPECTED = '5b9d6c569e2a29f15fe4e7ac1adb24611bedac0d566a02d427c8b262aba839fb'


class CatalogCheckoutTests(unittest.TestCase):
    def test_autocrlf_checkout_retains_exact_pinned_catalog_bytes(self):
        # Only a new disposable local repository is configured. No user's Git
        # settings, live checkout, hooks, credentials or remote refs are changed.
        with tempfile.TemporaryDirectory(prefix='relicade-catalog-checkout-') as directory:
            fixture = Path(directory) / 'repository'
            fixture.mkdir()
            (fixture / '.gitattributes').write_bytes((ROOT / '.gitattributes').read_bytes())
            target = fixture / RELATIVE
            target.parent.mkdir(parents=True)
            target.write_bytes((ROOT / RELATIVE).read_bytes())
            env = dict(os.environ, GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL=os.devnull)
            for key in ['GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_COMMON_DIR']:
                env.pop(key, None)
            def git(*args):
                return subprocess.run(['git', '-c', 'core.autocrlf=true', *args], cwd=fixture,
                                      env=env, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            git('init', '--quiet')
            git('add', '.gitattributes', RELATIVE)
            checkout = Path(directory) / 'fresh-checkout'
            checkout.mkdir()
            git('checkout-index', '--all', '--prefix=' + str(checkout) + os.sep)
            actual = (checkout / RELATIVE).read_bytes()
            self.assertEqual(hashlib.sha256(actual).hexdigest(), EXPECTED)
            self.assertNotIn(b'\r\n', actual)
            self.assertEqual(git('check-attr', 'text', '--', RELATIVE).stdout.decode().strip(),
                             RELATIVE + ': text: unset')


if __name__ == '__main__':
    unittest.main()
