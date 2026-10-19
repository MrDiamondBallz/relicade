"""Opt-in live source visual QA. Private ignored captures; never a native gate.
No intercepted image responses: the actual retained provider URLs are exercised.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import threading
from playwright.sync_api import sync_playwright

ROOT=Path(__file__).resolve().parents[1]

def availability_errors(catalog, slots):
    """Require each expected source to decode, not merely 36 DOM row objects."""
    expected={(app['id'],kind):app['referenceArtSources']['libraryArt'][kind]
              for app in catalog['apps'] for kind in ('hero','header','capsule')}
    seen=set()
    errors=[]
    for slot in slots:
        key=(slot['project'],slot['kind'])
        if key in seen:
            errors.append(f'Duplicate slot: {key}')
        seen.add(key)
        if key not in expected or slot['source']!=expected[key]:
            errors.append(f'Unexpected slot/source: {key}')
        if slot['state'] not in ('loaded','cached') or slot['width']<=0 or slot['height']<=0:
            errors.append(f'Expected image did not decode: {key} ({slot["state"]})')
    errors.extend(f'Missing slot: {key}' for key in sorted(expected.keys()-seen))
    return errors

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--network',action='store_true',help='Explicitly permit contact with credited public artwork provider')
    args=parser.parse_args()
    if not args.network:
        parser.error('--network is required; use offline QA otherwise')
    output=ROOT/'qa/local/artwork-live'
    output.mkdir(parents=True,exist_ok=True)
    spec=importlib.util.spec_from_file_location('live_art_preview',ROOT/'tools/preview.py')
    assert spec and spec.loader
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    server=module.make_server(ROOT,0)
    threading.Thread(target=server.serve_forever,daemon=True).start()
    report={'scope':'Real browser/provider artwork only, not native acceptance','commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'pageErrors':[],'cspViolations':[],'providerRequests':[],'slots':[]}
    try:
        with sync_playwright() as pw:
            browser=pw.chromium.launch(headless=True,args=['--no-sandbox'],executable_path=os.environ.get('CHROME_EXECUTABLE') or shutil.which('google-chrome'))
            report['browserVersion']=browser.version
            context=browser.new_context(viewport={'width':1600,'height':1000})
            context.on('request',lambda r:report['providerRequests'].append({'url':r.url,'referrerPresent':bool(r.headers.get('referer')),'cookiePresent':bool(r.headers.get('cookie')),'authorizationPresent':bool(r.headers.get('authorization'))}) if r.url.startswith('https://cdn2.steamgriddb.com/') else None)
            page=context.new_page()
            page.on('pageerror',lambda e:report['pageErrors'].append(str(e)))
            context.on('console',lambda m:report['cspViolations'].append(m.text) if 'violates the following Content Security Policy' in m.text else None)
            page.goto(f'http://127.0.0.1:{server.server_port}/prototype/')
            page.locator('.game-card').first.wait_for()
            page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'loaded'",timeout=45000)
            report['ocarina']=page.locator('#hero-art').evaluate('(e)=>({source:e.dataset.source,state:e.dataset.state,width:e.querySelector(".art-remote").naturalWidth,height:e.querySelector(".art-remote").naturalHeight})')
            page.wait_for_function('() => document.fonts.status === "loaded"')
            page.screenshot(path=str(output/'ocarina-desktop.png'))
            page.locator('#art-settings-button').click()
            page.locator('#art-scan').click()
            page.wait_for_function("() => window.RelicadeArt.pending === 0 && ![...document.querySelectorAll('#art-diagnostics .art-slot')].some(e=>e.dataset.state==='loading')",timeout=300000)
            report['summary']=page.locator('#art-summary').inner_text()
            report['slots']=page.locator('#art-diagnostics .art-slot').evaluate_all('(xs)=>xs.map(e=>({project:e.dataset.project,kind:e.dataset.kind,source:e.dataset.source,state:e.dataset.state,status:e.querySelector(".art-status").textContent,width:e.querySelector(".art-remote")?.naturalWidth||0,height:e.querySelector(".art-remote")?.naturalHeight||0}))')
            report['availabilityErrors']=availability_errors(json.loads((ROOT/'prototype/data/catalog.json').read_text()),report['slots'])
            report['availabilityPassed']=not report['availabilityErrors']
            page.keyboard.press('Escape')
            for width,height in [(390,844),(1280,800),(1600,1000)]:
                page.set_viewport_size({'width':width,'height':height})
                assert page.evaluate('document.documentElement.scrollWidth <= innerWidth')
                page.screenshot(path=str(output/f'live-{width}.png'))
            page.locator('#art-settings-button').click()
            page.locator('#art-offline').check()
            page.wait_for_function("() => document.querySelector('#hero-art').dataset.state === 'cached'")
            page.keyboard.press('Escape')
            report['offlineLastGood']=page.locator('#hero-art').get_attribute('data-state')
            page.screenshot(path=str(output/'offline-last-good.png'))
            browser.close()
    finally:
        server.shutdown()
        server.server_close()
        (output/'report.json').write_text(json.dumps(report,indent=2)+'\n')
    assert len(report['slots'])==36
    assert report['availabilityPassed'],report['availabilityErrors']
    assert report['ocarina']['source']=='https://cdn2.steamgriddb.com/hero/b63d7ec7840735552cb77df807d944ef.png'
    assert not report['pageErrors'] and not report['cspViolations'],report
    assert all(not any(r[k] for k in ('referrerPresent','cookiePresent','authorizationPresent')) for r in report['providerRequests'])
    print(json.dumps({k:v for k,v in report.items() if k not in ('providerRequests','slots')},indent=2))
    print('Private ignored captures: qa/local/artwork-live/')

if __name__=='__main__':
    main()
