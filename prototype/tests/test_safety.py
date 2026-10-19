"""Adversarial metadata, storage denial, modal keyboard and brand/DPI checks."""
import functools,http.server,json,os,pathlib,shutil,threading,xml.etree.ElementTree as ET
from playwright.sync_api import sync_playwright
ROOT=pathlib.Path(__file__).resolve().parents[2]
class Quiet(http.server.SimpleHTTPRequestHandler):
 def log_message(self,format,*args):pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Quiet,directory=str(ROOT)))
threading.Thread(target=server.serve_forever,daemon=True).start()
base=f'http://127.0.0.1:{server.server_port}'
results=[]
with sync_playwright() as p:
 executable=os.environ.get('CHROME_EXECUTABLE') or shutil.which('google-chrome') or shutil.which('chromium') or shutil.which('chromium-browser')
 launch={'headless':True,'args':['--no-sandbox']}
 if executable:launch['executable_path']=executable
 b=p.chromium.launch(**launch)
 page=b.new_page(viewport={'width':1280,'height':800});errors=[];requests=[]
 page.on('pageerror',lambda e:errors.append(str(e)));page.on('request',lambda req:requests.append(req.url))
 page.goto(base+'/prototype/?art=offline');page.locator('.game-card').first.wait_for()
 page.evaluate("() => { Storage.prototype.setItem=function(){throw new DOMException('Storage denied','SecurityError')}; }")
 page.locator('.save-card').first.click();assert 'storage is unavailable' in page.locator('#toast').inner_text();assert page.locator('#library-count').inner_text()=='0';assert page.locator('.save-card').first.get_attribute('aria-pressed')=='false'
 page.locator('#hero-detail').click();page.locator('#detail-dialog .dialog-close').focus();page.keyboard.press('Shift+Tab');assert page.locator('#detail-dialog').evaluate('(d)=>d.contains(document.activeElement)');page.keyboard.press('Tab');assert page.locator('#detail-dialog').evaluate('(d)=>d.contains(document.activeElement)');page.keyboard.press('Escape')
 payload=json.loads((ROOT/'prototype/data/catalog.json').read_text());bad=payload['apps'][0];bad.update({'name':'<img src=x onerror=alert(1)>','description':'<script>window.pwned=true</script>','catalogUrl':'javascript:alert(1)','sourceProject':{'provider':'github','repository':'../javascript:bad','website':'javascript:alert(1)'},'localArt':{'header':'https://attacker.invalid/track.png','hero':'javascript:alert(1)'}})
 page.route('**/data/catalog.json',lambda route:route.fulfill(content_type='application/json',body=json.dumps(payload)))
 page.reload();page.locator('.game-card').first.wait_for();assert page.locator('.game-title').first.text_content()=='<img src=x onerror=alert(1)>';page.locator('.game-open').first.click();assert page.locator('#detail-dialog script').count()==0;assert page.locator('#detail-dialog a').count()==0;assert page.evaluate('window.pwned===undefined');assert not any('attacker.invalid' in r for r in requests)
 assert all(r.startswith(base) for r in requests),requests
 page.close()
 for scale in [1,1.25,1.5,2]:
  page=b.new_page(viewport={'width':1280,'height':800},device_scale_factor=scale);page.goto(base+'/prototype/?art=offline');page.locator('.game-card').first.wait_for();assert page.evaluate('document.documentElement.scrollWidth<=innerWidth');results.append({'scale':scale,'horizontalOverflow':False});page.close()
 for width in [1440,390]:
  page=b.new_page(viewport={'width':width,'height':1000 if width==1440 else 844});page.goto(base+'/brand/');page.wait_for_function('document.fonts.status==="loaded"');assert page.evaluate('document.documentElement.scrollWidth<=innerWidth');assert page.locator('.lockup-grid img').count()==3;assert page.locator('img').evaluate_all('(images)=>images.every(i=>i.complete&&i.naturalWidth>0)');page.screenshot(path=str(ROOT/f'brand/qa-{width}.png'),full_page=True);page.close()
 for path in (ROOT/'brand').glob('*.svg'):ET.parse(path)
 b.close()
server.shutdown()
report={'passed':True,'checks':['localStorage denial does not show saved success','native dialog keyboard containment','untrusted catalog text cannot inject HTML/script','javascript URLs are omitted','untrusted artwork URLs rejected','all runtime asset requests local in explicit offline artwork mode','browser DPR 1/1.25/1.5/2 no overflow','brand variants and fonts rendered','brand desktop/mobile no overflow','all brand SVG XML valid'],'dpiEmulation':results,'pageErrors':errors,'nativeWindowsTested':False}
assert not errors,errors
(ROOT/'prototype/tests/safety-results.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
