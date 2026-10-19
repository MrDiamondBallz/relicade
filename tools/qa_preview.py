"""Exercise and screenshot the REAL restricted preview, including its CSP.

Optional QA dependencies: pip install playwright Pillow; playwright install chromium
Set CHROME_EXECUTABLE only when using an existing Chrome/Chromium installation.
"""
from pathlib import Path
import importlib.util
import json
import os
import shutil
import threading
from urllib.parse import urlsplit
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('preview', ROOT/'tools/preview.py')
assert spec and spec.loader
preview = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preview)
server = preview.make_server(ROOT, 0)
threading.Thread(target=server.serve_forever, daemon=True).start()
base = f'http://127.0.0.1:{server.server_port}'
assets = Path(os.environ.get('RELICADE_QA_OUTPUT_DIR', str(ROOT/'qa/local/preview'))).resolve()
assets.mkdir(parents=True, exist_ok=True)
errors, blocked, requests, checks = [], [], [], []
try:
    with sync_playwright() as p:
        executable = os.environ.get('CHROME_EXECUTABLE') or shutil.which('google-chrome') or shutil.which('chromium') or shutil.which('chromium-browser')
        launch = {'headless':True, 'args':['--no-sandbox']}
        if executable:
            launch['executable_path'] = executable
        browser = p.chromium.launch(**launch)
        page = browser.new_page(viewport={'width':1600,'height':1000})
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.on('console', lambda m: blocked.append(m.text) if 'Content Security Policy' in m.text or 'Refused to' in m.text else None)
        page.on('request', lambda r: requests.append(r.url))
        page.goto(base+'/prototype/')
        page.locator('.game-card').first.wait_for()
        page.wait_for_function('document.fonts.status === "loaded"')
        page.wait_for_function('Array.from(document.images).filter(i => i.getBoundingClientRect().top < innerHeight).every(i => i.complete && i.naturalWidth > 0)')
        count = page.locator('.game-card').count()
        assert count == 12
        page.screenshot(path=str(assets/'relicade-launcher.png'))
        checks.append('Real restricted preview loads 12 authentic project entries with all visible art/fonts')
        page.locator('#hero-detail').click()
        assert page.locator('#detail-title').is_visible()
        page.locator('#detail-save').click()
        assert page.locator('#detail-save').inner_text() == 'Remove from library'
        page.keyboard.press('Escape')
        assert page.locator('#hero-detail').evaluate('(e) => e === document.activeElement')
        page.keyboard.press('Control+k')
        assert page.locator('#command-dialog').is_visible()
        page.keyboard.press('Escape')
        checks.append('Details, save persistence, dialog focus and Ctrl+K under production preview CSP')
        for width,height in [(390,844),(1280,800),(1600,1000),(390,844),(1600,1000)]:
            page.set_viewport_size({'width':width,'height':height})
            assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
        checks.append('Fresh render and bidirectional responsive breakpoint transitions without horizontal overflow')
        page.goto(base+'/brand/')
        page.set_viewport_size({'width':1440,'height':1100})
        page.wait_for_function('document.fonts.status === "loaded"')
        assert page.locator('img').evaluate_all('(images) => images.every(i => i.complete && i.naturalWidth > 0)')
        page.screenshot(path=str(assets/'relicade-brand.png'))
        checks.append('Brand board and vector assets render under restricted preview CSP')
        assert not errors, errors
        assert not blocked, blocked
        origin = lambda value: (urlsplit(value).scheme, urlsplit(value).hostname, urlsplit(value).port)
        assert all(origin(u) == origin(base) for u in requests), 'Unexpected remote runtime requests'
        checks.append('Zero page errors/CSP violations or remote runtime requests')
        browser.close()
finally:
    server.shutdown()
    server.server_close()
report = {'passed':True,'checks':checks,'pageErrors':errors,'cspViolations':blocked,'nativeLauncherTested':False}
(ROOT/'docs/PREVIEW_QA.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
