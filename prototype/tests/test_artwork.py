"""Browser artwork contract. Real catalog URLs, explicitly synthetic image responses.
No fixture response is evidence that the production provider works.
"""
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import threading
import unittest
from urllib.parse import quote
from PIL import Image
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('art_preview', ROOT/'tools/preview.py')
assert SPEC and SPEC.loader
preview = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(preview)
OCARINA = 'https://cdn2.steamgriddb.com/hero/b63d7ec7840735552cb77df807d944ef.png'


class ArtworkTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = preview.make_server(ROOT, 0)
        threading.Thread(target=cls.server.serve_forever, daemon=True).start()
        cls.base = f'http://127.0.0.1:{cls.server.server_port}'
        cls.pw = sync_playwright().start()
        exe = os.environ.get('CHROME_EXECUTABLE') or shutil.which('google-chrome')
        cls.browser = cls.pw.chromium.launch(headless=True, args=['--no-sandbox'], executable_path=exe)
        out = io.BytesIO()
        Image.new('RGB', (96, 48), '#5d7b40').save(out, format='PNG')
        cls.fixture = out.getvalue()  # Synthetic green rectangle, not game imagery.

    @classmethod
    def tearDownClass(cls):
        cls.browser.close()
        cls.pw.stop()
        cls.server.shutdown()
        cls.server.server_close()

    def setUp(self):
        self.ctx = self.browser.new_context(viewport={'width':1280,'height':900})
        self.addCleanup(self.ctx.close)
        self.requests = []
        self.all_requests = []
        self.ctx.on('request',lambda r:self.all_requests.append(r.url))
        self.errors = []
        self.response = 'ok'
        self.held = []
        self.ctx.route('https://cdn2.steamgriddb.com/**', self.provider)
        self.page = self.ctx.new_page()
        self.page.on('pageerror', lambda e:self.errors.append(str(e)))
        self.addCleanup(lambda:self.assertEqual(self.errors, []))

    def tearDown(self):
        for route in self.held:
            try:
                route.abort()
            except Exception:
                pass  # Worker may already have cancelled this held fixture.

    def provider(self, route):
        self.requests.append(route.request)
        if self.response == 'ok':
            route.fulfill(status=200, content_type='image/png', body=self.fixture)
        elif self.response == '404':
            route.fulfill(status=404, content_type='text/plain', body='Fixture: missing image')
        elif self.response == 'corrupt':
            route.fulfill(status=200, content_type='image/png', body=b'Fixture: not PNG bytes')
        elif self.response == 'hang':
            self.held.append(route)  # Deliberately unresolved until worker aborts.
        elif self.response == 'redirect':
            route.fulfill(status=302, headers={'Location':self.base+'/brand/private-fixture'})
        elif self.response == 'foreign-redirect':
            route.fulfill(status=302, headers={'Location':'https://unapproved.invalid/artwork-fixture.png'})
        else:
            route.abort('internetdisconnected')

    def open(self, offline=False):
        self.page.goto(self.base+'/prototype/'+('?art=offline' if offline else ''))
        self.page.locator('.game-card').first.wait_for()
        self.page.wait_for_function('() => window.RelicadeArt && window.RelicadeArt.ready')

    def loaded(self):
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'loaded'")

    def test_real_selection_every_slot_and_credentialless_requests(self):
        self.ctx.add_cookies([{'name':'artwork-fixture-cookie','value':'must-not-forward','url':'https://cdn2.steamgriddb.com','secure':True,'sameSite':'None'}])
        self.open()
        self.loaded()
        self.assertEqual(self.page.locator('#hero-art').get_attribute('data-source'), OCARINA)
        self.page.locator('#art-settings-button').click()
        self.page.locator('#art-scan').click()
        self.page.wait_for_function("() => document.querySelectorAll('#art-diagnostics [data-state=loaded]').length === 36")
        self.assertEqual(self.page.locator('#art-diagnostics .art-slot').count(), 36)
        self.assertIn('36 / 36 real image slots decoded', self.page.locator('#art-summary').inner_text())
        self.assertTrue(self.requests)
        for req in self.requests:
            self.assertNotIn('referer', req.headers)
            self.assertNotIn('cookie', req.headers)
            self.assertNotIn('authorization', req.headers)
        self.assertTrue(self.page.locator('#art-diagnostics .art-slot img').evaluate_all('(xs)=>xs.every(x=>x.complete&&x.naturalWidth>0)'))
        self.page.wait_for_function("async () => (await (await caches.open('relicade-art-good-v1')).keys()).length === 36")

    def test_offline_mode_no_provider_requests_and_no_blank_slots(self):
        self.open(offline=True)
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'offline'")
        self.assertEqual(self.requests, [])
        self.assertTrue(all(url.startswith(self.base+'/') for url in self.all_requests),self.all_requests)
        self.assertIn('original tile', self.page.locator('#hero-art .art-status').inner_text())
        self.page.locator('#art-settings-button').click()
        self.page.locator('#art-scan').click()
        self.page.wait_for_function("() => document.querySelectorAll('#art-diagnostics [data-state=offline]').length === 36")
        self.assertTrue(self.page.locator('#art-diagnostics .art-fallback').evaluate_all('(xs)=>xs.every(x=>x.complete&&x.naturalWidth>0)'))
        self.assertEqual(self.requests, [])

    def test_failure_retry_is_bounded_and_keyboard_repair(self):
        self.response = '404'
        self.open()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'fallback'")
        self.assertIn('original tile', self.page.locator('#hero-art .art-status').inner_text())
        self.page.locator('#art-settings-button').focus()
        self.page.keyboard.press('Enter')
        retry = self.page.locator('#art-diagnostics .art-retry').filter(has_text='Retry').nth(9) # Ocarina hero (snapshot order).
        retry.focus()
        self.page.keyboard.press('Enter')
        self.page.wait_for_function("() => document.querySelectorAll('#art-diagnostics .art-slot')[9].dataset.state === 'fallback'")
        retry.click()
        self.page.wait_for_function("() => document.querySelectorAll('#art-diagnostics .art-slot')[9].dataset.attempts === '3'")
        self.page.wait_for_function("() => document.querySelectorAll('#art-diagnostics .art-retry')[9].disabled")
        count = len(self.requests)
        self.page.wait_for_timeout(300)
        self.assertEqual(len(self.requests), count)
        self.page.keyboard.press('Escape')
        self.assertTrue(self.page.locator('#art-settings-button').evaluate('(e)=>e===document.activeElement'))

    def test_corrupt_image_not_reported_loaded(self):
        self.response = 'corrupt'
        self.open()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'fallback'")
        self.assertNotEqual(self.page.locator('#hero-art').get_attribute('data-state'), 'loaded')
        self.assertTrue(self.page.locator('#hero-art .art-fallback').evaluate('(i)=>i.complete&&i.naturalWidth>0'))

    def test_cache_last_good_survives_reload_and_offline(self):
        self.open()
        self.loaded()
        self.page.wait_for_function('() => window.RelicadeArt.pending === 0')
        self.page.locator('#art-settings-button').click()
        self.page.locator('#art-offline').check()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'cached'")
        count = len(self.requests)
        self.page.reload()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'cached'")
        self.assertEqual(len(self.requests), count)
        self.assertIn('Cached', self.page.locator('#hero-art .art-status').inner_text())

    def test_timeout_loading_then_honest_fallback_without_layout_shift(self):
        self.response = 'hang'
        self.open()
        before = self.page.locator('#spotlight').bounding_box()
        self.assertIn('original tile', self.page.locator('#hero-art .art-status').inner_text())
        self.assertTrue(self.page.locator('#hero-art .art-fallback').is_visible())
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'fallback'", timeout=20000)
        self.assertEqual(self.page.locator('#spotlight').bounding_box(), before)
        self.assertIn('timed out', self.page.locator('#hero-art .art-status').inner_text())

    def test_untrusted_source_and_missing_local_art_keep_text_not_blank(self):
        data = json.loads((ROOT/'prototype/data/catalog.json').read_text())
        app = next(a for a in data['apps'] if a['projectName']=='Ship of Harkinian')
        app['referenceArtSources']['libraryArt']['hero'] = 'https://attacker.invalid/track.png'
        app['localArt']['hero'] = '../private.png'
        self.ctx.route('**/data/catalog.json', lambda r:r.fulfill(content_type='application/json',body=json.dumps(data)))
        self.open()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'denied'")
        self.assertTrue(self.page.locator('#hero-art .art-placeholder').is_visible())
        self.assertEqual(self.page.locator('#hero-art img').count(), 0)
        self.assertIn('title fallback',self.page.locator('#hero-art .art-status').inner_text())
        self.assertFalse(any('attacker.invalid' in url for url in self.all_requests))

    def test_corrupt_refresh_preserves_decoded_last_good(self):
        self.open()
        self.loaded()
        self.page.locator('#art-settings-button').click()
        self.page.wait_for_function("async () => (await caches.open('relicade-art-good-v1')).keys().then(xs=>xs.some(x=>x.url.includes('b63d7ec')))")
        self.response='corrupt'
        self.page.locator('#art-diagnostics .art-retry').nth(9).click()
        self.page.wait_for_function("() => document.querySelectorAll('#art-diagnostics .art-slot')[9].dataset.state === 'cached'")
        self.assertIn('last-good',self.page.locator('#art-diagnostics .art-status').nth(9).inner_text())

    def test_keyboard_approved_alternative_repairs_failed_slot(self):
        self.response='404'
        self.open()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'fallback'")
        self.page.locator('#art-settings-button').click()
        self.response='ok'
        select=self.page.locator('#art-diagnostics .art-alternative').nth(9)
        select.focus()
        self.page.keyboard.press('ArrowDown')
        self.page.keyboard.press('Enter')
        self.page.wait_for_function("() => document.querySelectorAll('#art-diagnostics .art-slot')[9].dataset.state === 'loaded'")
        self.assertIn('/grid/',self.page.locator('#hero-art').get_attribute('data-source') or '')

    def test_redirect_private_destination_blocked_before_request(self):
        self.response='redirect'
        forbidden=[]
        self.ctx.route(self.base+'/brand/private-fixture', lambda r:(forbidden.append(r.request.url),r.abort()))
        self.open()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'fallback'")
        self.assertEqual(forbidden,[])

    def test_redirect_unapproved_https_host_blocked_before_request(self):
        self.response='foreign-redirect'
        forbidden=[]
        self.ctx.route('https://unapproved.invalid/**',lambda r:(forbidden.append(r.request.url),r.abort()))
        self.open()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'fallback'")
        self.assertEqual(forbidden,[])

    def test_broken_original_fallback_keeps_truthful_title_and_card(self):
        self.response='404'
        self.ctx.route('**/assets/art/*-hero.webp',lambda r:r.fulfill(status=404,body='Fixture: original tile missing'))
        self.open()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'fallback'")
        self.assertTrue(self.page.locator('#hero-art .art-placeholder').is_visible())
        self.assertIn('title fallback',self.page.locator('#hero-art .art-status').inner_text())
        self.assertEqual(self.page.locator('#hero-art img').count(),0)

    def test_browser_offline_signal_uses_last_good_without_provider_traffic(self):
        self.open()
        self.loaded()
        self.page.wait_for_function('() => window.RelicadeArt.pending === 0')
        self.ctx.set_offline(True)
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'cached'")
        count=len(self.requests)
        self.page.wait_for_timeout(200)
        self.assertEqual(len(self.requests),count)

    def test_offline_switch_aborts_and_does_not_retry_queued_work(self):
        self.response='hang'
        self.open()
        self.page.locator('#art-settings-button').click()
        self.page.locator('#art-offline').check()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'offline'")
        count=len(self.requests)
        self.page.locator('#art-scan').click()
        self.page.wait_for_function("() => window.RelicadeArt.pending === 0")
        self.assertEqual(len(self.requests),count)

    def test_unsupported_worker_does_not_make_insecure_direct_request(self):
        self.ctx.add_init_script("Object.defineProperty(navigator,'serviceWorker',{value:undefined})")
        self.open()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'fallback'")
        self.assertEqual(self.requests,[])

    def test_missing_worker_csp_fails_closed_without_provider_contact(self):
        self.ctx.route('**/art-worker.js',lambda r:r.fulfill(content_type='text/javascript',body=(ROOT/'prototype/art-worker.js').read_text()))
        self.open()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'fallback'")
        self.assertEqual(self.requests,[])

    def cache_key(self, url=OCARINA):
        return self.base+'/prototype/art-cache/'+quote(url, safe="~()*!.'-")

    def fault_ocarina_put(self):
        self.ctx.service_workers[0].evaluate("""() => {
          self.failedPuts=0;
          const put=Cache.prototype.put;
          Cache.prototype.put=function(request,response) {
            if(String(request.url||request).includes('b63d7ec')) {
              self.failedPuts++;
              return Promise.reject(new DOMException('Synthetic quota failure','QuotaExceededError'));
            }
            return put.call(this,request,response);
          };
        }""")

    def wait_good_cache(self):
        self.page.wait_for_function("async key => Boolean(await (await caches.open('relicade-art-good-v1')).match(key))", arg=self.cache_key())

    def test_valid_refresh_failed_put_preserves_offline_last_good(self):
        self.open()
        self.loaded()
        self.wait_good_cache()
        self.fault_ocarina_put()
        replacement=io.BytesIO()
        Image.new('RGB', (120, 60), '#123456').save(replacement, format='PNG')
        self.fixture=replacement.getvalue()
        self.page.locator('#art-settings-button').click()
        self.page.locator('#art-diagnostics .art-retry').nth(9).click()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state==='loaded'")

        # Wait for the injected failure, not a fixed delay or merely a decoded image.
        worker=self.ctx.service_workers[0]
        for _ in range(100):
            if worker.evaluate('self.failedPuts'):
                break
            self.page.wait_for_timeout(20)
        self.assertEqual(worker.evaluate('self.failedPuts'), 1)
        self.page.wait_for_function("() => document.querySelector('#hero-art').artEntry.persisted===false")
        self.assertEqual(self.page.locator('#hero-art .art-remote').evaluate('(i)=>i.naturalWidth'),120)
        self.assertTrue(self.page.evaluate("async key => Boolean(await (await caches.open('relicade-art-good-v1')).match(key))", self.cache_key()))
        self.page.locator('#art-offline').check()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state==='cached'")
        self.assertTrue(self.page.locator('#hero-art .art-remote').evaluate('(i)=>i.complete&&i.naturalWidth>0'))
        self.assertEqual(self.page.locator('#hero-art .art-remote').evaluate('(i)=>i.naturalWidth'),96, 'Offline recovered the old decoded pixels, not the unpersisted refresh')

    def test_cached_confirmations_do_not_rewrite_or_charge_attempts(self):
        self.open()
        self.loaded()
        self.wait_good_cache()
        self.fault_ocarina_put()
        self.page.locator('#art-settings-button').click()
        count=sum(r.url==OCARINA for r in self.requests)
        for _ in range(3):
            self.page.locator('#art-offline').check()
            self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state==='cached'")
            self.page.locator('#art-offline').uncheck()
            self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state==='cached'")
        self.assertEqual(self.ctx.service_workers[0].evaluate('self.failedPuts'), 0)
        self.assertEqual(sum(r.url==OCARINA for r in self.requests), count)
        self.assertEqual(self.page.locator('#hero-art').get_attribute('data-attempts'), '1')
        self.wait_good_cache()

    def test_cache_count_bound_evicts_oldest_after_successful_insert(self):
        self.open()
        self.loaded()
        self.page.locator('#art-settings-button').click()
        self.page.locator('#art-scan').click()
        self.page.wait_for_function("() => window.RelicadeArt.pending===0 && document.querySelectorAll('#art-diagnostics [data-state=loaded]').length===36")
        self.page.wait_for_function("async () => (await (await caches.open('relicade-art-good-v1')).keys()).length===36")
        # Direct worker messages still require renderer decode before confirmation.
        result=self.page.evaluate("""async () => {
          const rpc=data=>new Promise(resolve=>{const c=new MessageChannel();c.port1.onmessage=e=>{if(e.data.phase!=='attempt')resolve(e.data);};navigator.serviceWorker.controller.postMessage(data,[c.port2]);});
          const cache=await caches.open('relicade-art-good-v1');
          const oldest=(await cache.keys()).slice(0,2).map(k=>k.url);
          const confirmations=await Promise.all(['00000000000000000000000000000000','00000000000000000000000000000001'].map(async hash=>{
            const url='https://cdn2.steamgriddb.com/hero/'+hash+'.png';
            const t=await rpc({type:'load',url,allowNetwork:true});
            const img=new Image();img.src=t.src;await img.decode();
            const confirmation=await rpc({type:'confirm',token:t.token});
            return {persisted:confirmation.persisted,width:img.naturalWidth};
          }));
          return {confirmations,count:(await cache.keys()).length,oldestPresent:await Promise.all(oldest.map(async key=>Boolean(await cache.match(key))))};
        }""")
        self.assertEqual(result, {'confirmations':[{'persisted':True,'width':96}]*2,'count':36,'oldestPresent':[False,False]})

    def stale_selection_budget(self, completion):
        normal=self.provider
        def held_hero(route):
            if route.request.url==OCARINA:
                self.requests.append(route.request)
                self.held.append(route)
            else:
                normal(route)
        self.ctx.unroute('https://cdn2.steamgriddb.com/**')
        self.ctx.route('https://cdn2.steamgriddb.com/**', held_hero)
        self.open()
        self.page.locator('#art-settings-button').click()
        select=self.page.locator('#art-diagnostics .art-alternative').nth(9)
        slot=self.page.locator('#art-diagnostics .art-slot').nth(9)
        header=select.locator('option').nth(1).get_attribute('value')
        for attempt in range(1,4):
            for _ in range(150):
                if self.held:
                    break
                self.page.wait_for_timeout(20)
            self.assertTrue(self.held, 'Expected actual held hero network attempt')
            self.assertEqual(slot.get_attribute('data-attempts'), str(attempt))
            # This is a real selection event, not direct mutation of internal state.
            select.select_option(header)
            self.page.wait_for_function("() => ['loaded','cached'].includes(document.querySelectorAll('#art-diagnostics .art-slot')[9].dataset.state)")
            before=slot.evaluate('(e)=>({source:e.dataset.source,state:e.dataset.state,src:e.querySelector(".art-remote").src})')
            for route in self.held:
                if completion=='success':
                    route.fulfill(status=200,content_type='image/png',body=self.fixture)
                else:
                    route.abort('failed')
            self.held.clear()
            self.page.wait_for_function('() => window.RelicadeArt.pending===0')
            self.assertEqual(slot.evaluate('(e)=>({source:e.dataset.source,state:e.dataset.state,src:e.querySelector(".art-remote").src})'), before)
            select.select_option(OCARINA)
            if attempt<3:
                self.page.wait_for_function("() => document.querySelectorAll('#art-diagnostics .art-slot')[9].dataset.state==='loading'")
        self.page.wait_for_function("() => document.querySelectorAll('#art-diagnostics .art-slot')[9].dataset.state==='fallback'")
        self.assertEqual(slot.get_attribute('data-attempts'), '3')
        self.assertEqual(sum(r.url==OCARINA for r in self.requests), 3)
        self.assertFalse(self.page.evaluate("async key => Boolean(await (await caches.open('relicade-art-good-v1')).match(key))", self.cache_key()), 'Stale successful pixels were never decoded/confirmed and must not persist')
        self.assertTrue(self.page.locator('#art-diagnostics .art-retry').nth(9).is_disabled())
        self.page.evaluate("window.dispatchEvent(new Event('online'))")
        self.page.wait_for_timeout(100)
        self.assertEqual(sum(r.url==OCARINA for r in self.requests), 3)

    def test_stale_success_selection_charges_source_budget_without_stale_pixels(self):
        self.stale_selection_budget('success')

    def test_stale_error_selection_charges_source_budget_without_stale_state(self):
        self.stale_selection_budget('error')

    def test_rapid_alternatives_do_not_overbook_pending_network_capacity(self):
        self.open(offline=True)
        self.page.wait_for_function("() => window.RelicadeArt.pending===0")
        self.page.locator('#art-settings-button').click()
        worker=self.ctx.service_workers[0]
        worker.evaluate("""() => {
          self.cacheWaiters=[];
          const match=Cache.prototype.match;
          Cache.prototype.match=async function(request,...args) {
            if(String(request.url||request).includes('b63d7ec'))
              await new Promise(resolve=>self.cacheWaiters.push(resolve));
            return match.call(this,request,...args);
          };
        }""")
        self.page.locator('#art-offline').uncheck()
        select=self.page.locator('#art-diagnostics .art-alternative').nth(9)
        header=select.locator('option').nth(1).get_attribute('value')
        # Delay cache lookup before fetch-start notifications. Merely counting
        # completed/started requests without reserving capacity would overbook.
        for count in range(1,6):
            for _ in range(100):
                if worker.evaluate('self.cacheWaiters.length')>=count:
                    break
                self.page.wait_for_timeout(20)
            self.assertEqual(worker.evaluate('self.cacheWaiters.length'), count)
            select.select_option(header)
            self.page.wait_for_function("() => ['loaded','cached'].includes(document.querySelectorAll('#art-diagnostics .art-slot')[9].dataset.state)")
            select.select_option(OCARINA)
        # Include the final selection, then let all delayed lookups proceed.
        for _ in range(100):
            if worker.evaluate('self.cacheWaiters.length')==6:
                break
            self.page.wait_for_timeout(20)
        worker.evaluate('self.cacheWaiters.splice(0).forEach(resolve=>resolve())')
        self.page.wait_for_function('() => window.RelicadeArt.pending===0')
        self.assertEqual(sum(r.url==OCARINA for r in self.requests), 3)
        self.assertEqual(self.page.locator('#hero-art').get_attribute('data-attempts'), '3')
        self.assertEqual(self.page.locator('#hero-art').get_attribute('data-state'), 'fallback')

    def test_denied_cache_does_not_fabricate_persistence(self):
        self.ctx.on('serviceworker', lambda w:w.evaluate("() => {caches.open=async()=>{throw Error('Fixture: cache denied')};}"))
        self.open()
        self.loaded()
        self.page.locator('#art-settings-button').click()
        self.page.locator('#art-offline').check()
        self.page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'offline'")
        self.assertIn('original tile',self.page.locator('#hero-art .art-status').inner_text())


if __name__ == '__main__':
    unittest.main(verbosity=2)
