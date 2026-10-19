"""Run with python prototype/tests/test_ui.py from project root. No npm/build step."""
import functools, http.server, json, os, pathlib, shutil, threading
from playwright.sync_api import sync_playwright
ROOT=pathlib.Path(__file__).resolve().parents[2]
class Quiet(http.server.SimpleHTTPRequestHandler):
 def log_message(self,format,*args):pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Quiet,directory=str(ROOT)))
threading.Thread(target=server.serve_forever,daemon=True).start()
url=f'http://127.0.0.1:{server.server_port}/prototype/?art=offline'
reports=[]
with sync_playwright() as p:
 executable=os.environ.get('CHROME_EXECUTABLE') or shutil.which('google-chrome') or shutil.which('chromium') or shutil.which('chromium-browser')
 launch={'headless':True,'args':['--no-sandbox']}
 if executable:launch['executable_path']=executable
 browser=p.chromium.launch(**launch)
 page=browser.new_page(viewport={'width':1600,'height':1000},device_scale_factor=1)
 errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto(url);page.locator('.game-card').first.wait_for();assert page.locator('.game-card').count()==12
 page.screenshot(path=str(ROOT/'prototype/tests/desktop.png'),full_page=True)
 page.get_by_role('button',name='Explore project',exact=False).click();assert page.locator('#detail-title').inner_text()=='The Legend of Zelda: Ocarina of Time'
 page.locator('#detail-save').click();assert json.loads(page.evaluate("localStorage.getItem('relicade.library')"))
 assert page.locator('#detail-save').inner_text()=='Remove from library'
 assert page.locator('#detail-dialog').get_by_role('link',name='Project & requirements').get_attribute('href')=='https://github.com/harbourmasters/shipwright'
 page.screenshot(path=str(ROOT/'prototype/tests/detail.png'),full_page=True)
 page.keyboard.press('Escape');assert page.locator('#hero-detail').evaluate('(e)=>e===document.activeElement')
 page.get_by_role('button',name='Library',exact=False).first.click();assert page.locator('.game-card').count()==1
 page.reload();page.locator('.game-card').first.wait_for();page.get_by_role('button',name='Library',exact=False).first.click();assert page.locator('.game-card').count()==1
 page.locator('.save-card').click();assert page.locator('#empty-state').is_visible();assert json.loads(page.evaluate("localStorage.getItem('relicade.library')"))==[]
 page.get_by_role('button',name='Explore Discover').click();assert page.locator('.game-card').count()==12
 page.locator('#search').fill('Unleashed');assert page.locator('.game-card').count()==1;assert page.locator('#spotlight').is_hidden()
 page.locator('#search').fill('<img src=x onerror=alert(1)>');assert page.locator('#empty-state').is_visible();assert page.locator('#active-filters img').count()==0
 page.get_by_role('button',name='Clear search & filters').click()
 page.get_by_role('button',name='Widescreen',exact=True).click();assert 0<page.locator('.game-card').count()<=12
 page.get_by_role('button',name='All projects',exact=True).click()
 page.locator('#filters-button').click();page.locator('#os-filter').select_option('android');page.locator('#filters-form').get_by_role('button',name='Show projects').click();assert page.locator('.game-card').count()==1
 page.get_by_role('button',name='Clear all',exact=True).click()
 page.keyboard.press('Control+k');assert page.locator('#command-dialog').is_visible();page.locator('#command-search').fill('Perfect Dark');page.keyboard.press('ArrowDown');page.keyboard.press('Enter');assert page.locator('#detail-title').inner_text()=='Perfect Dark';page.keyboard.press('Escape')
 page.wait_for_function("document.activeElement===document.querySelector('#command-button')")
 page.locator('#hero-tabs button').nth(1).click();assert page.locator('#hero-title').text_content()=='Sonic Unleashed.'
 page.locator('#credits-button').click();assert page.locator('#about-dialog').is_visible();assert 'quiver' not in page.locator('#about-dialog').inner_text().lower();page.keyboard.press('Escape')
 page.wait_for_function("document.activeElement===document.querySelector('#credits-button')")
 for w,h in [(1600,1000),(1280,800),(390,844)]:
  page.set_viewport_size({'width':w,'height':h});page.screenshot(path=str(ROOT/f'prototype/tests/viewport-{w}.png'),full_page=True)
  assert page.evaluate('document.documentElement.scrollWidth<=window.innerWidth'),f'horizontal overflow {w}'
  reports.append({'viewport':f'{w}x{h}','overflow':False,'cards':page.locator('.game-card').count()})
 page.emulate_media(reduced_motion='reduce');assert page.locator('.game-art img').first.evaluate('(e)=>getComputedStyle(e).transitionDuration')=='0s'
 page.reload();page.locator('.game-card').first.wait_for();page.evaluate("localStorage.setItem('relicade.library','{broken')");page.reload();page.locator('.game-card').first.wait_for();assert page.locator('#library-count').inner_text()=='0'
 page.route('**/data/catalog.json',lambda route:route.fulfill(status=503,body='Unavailable'));page.reload();page.locator('#empty-state').wait_for();assert page.get_by_text('The catalog is out of reach.').is_visible()
 assert not errors,errors
 browser.close()
server.shutdown()
result={'passed':True,'checks':['12 real projects','project detail and source URL','library save/remove/reload persistence','empty library','search','safe untrusted search rendering','feature filter','platform filter','Ctrl+K keyboard selection','dialog Escape and focus return','spotlight switching','source disclosure','responsive overflow','reduced motion','corrupt storage recovery','catalog unavailable state','no page exceptions'],'viewports':reports,'pageErrors':errors}
(ROOT/'prototype/tests/results.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result,indent=2))
