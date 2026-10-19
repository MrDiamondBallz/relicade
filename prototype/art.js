/* Artwork presentation/state only. Native cache requirements: docs/ARTWORK.md. */
'use strict';
(() => {
  const P = window.RelicadeArtPolicy, entries = new Map();
  const observed=new Set();
  let diagnosticListener=null;
  let worker = null, pending = 0, ready = false, epoch = 0, slotID = 0, modeSequence=0;
  let activeLoads=0;
  const waiting=[];
  async function acquire(){
    if(activeLoads>=2)await new Promise(resolve=>waiting.push(resolve));
    else activeLoads++;
    let released=false;
    const release=()=>{if(released)return;released=true;clearTimeout(timer);if(waiting.length)waiting.shift()();else activeLoads--;};
    const timer=setTimeout(release,16000);
    return release;
  }
  let offline = new URLSearchParams(location.search).get('art') === 'offline';
  try { offline ||= localStorage.getItem('relicade.art.offline') === 'true'; } catch (_) { /* session mode still works */ }
  const node = (tag, cls, content) => { const n = document.createElement(tag); n.className = cls; if (content) n.textContent = content; return n; };
  function rpc(data, started=()=>{}) {
    if(data.type==='mode')data={...data,sequence:++modeSequence};
    return new Promise((resolve, reject) => {
      if (!worker) { reject(new Error('Safe artwork transport unavailable')); return; }
      const channel = new MessageChannel();
      const timer = setTimeout(() => { channel.port1.close(); reject(new Error('Artwork transport timed out')); }, 16000);
      channel.port1.onmessage = event => {
        if(event.data.phase==='attempt'){started();return;}
        clearTimeout(timer); channel.port1.close(); resolve(event.data);
      };
      worker.postMessage(data, [channel.port2]);
    });
  }
  const initialized = (async () => {
    try {
      if (!('serviceWorker' in navigator) || !window.isSecureContext) throw new Error('Unsupported browser');
      await Promise.race([
        (async () => {
          const policy=await fetch('art-worker.js',{cache:'no-store',credentials:'omit',redirect:'error'});
          if(!policy.ok || policy.headers.get('Content-Security-Policy')!=="default-src 'none'; script-src 'self'; connect-src https://cdn2.steamgriddb.com")throw new Error('Required artwork redirect policy unavailable');
          await navigator.serviceWorker.register('art-worker.js', {scope:'./'});
          await navigator.serviceWorker.ready;
          if (!navigator.serviceWorker.controller) await new Promise(resolve => navigator.serviceWorker.addEventListener('controllerchange', resolve, {once:true}));
          worker = navigator.serviceWorker.controller;
          await rpc({type:'mode',online:!offline && navigator.onLine});
        })(),
        new Promise((_,reject) => setTimeout(() => reject(new Error('Artwork worker unavailable')),5000))
      ]);
    } catch (_) { worker = null; }
    ready = true;
  })();
  function emit(entry) {
    for (const [element, update] of entry.views) {
      if (element.isConnected) update();
      else entry.views.delete(element);
    }
    document.dispatchEvent(new Event('artworkstate'));
  }
  function entryFor(app, kind) {
    const key = app.id + ':' + kind;
    if (!entries.has(key)) entries.set(key, {app,kind,source:P.source(app,kind),state:'idle',reason:'Not checked — original tile',src:null,attempts:new Map(),reserved:new Map(),views:new Map(),generation:0,decoded:false});
    return entries.get(key);
  }
  async function start(entry, force=false) {
    if (entry.state === 'loading' || (entry.src && !entry.decoded)) return;
    const url = entry.source;
    const attempts = entry.attempts.get(url) || 0;
    if (force && attempts >= 3) return;
    const generation = ++entry.generation, modeEpoch = epoch;
    entry.release?.();
    let release=null;
    entry.state = 'loading'; entry.reason = 'Loading game art — original tile underneath'; entry.src = null; entry.decoded = false; emit(entry);
    pending++;
    try {
      await initialized;
      if (modeEpoch !== epoch) return;
      if (!url) { entry.state='denied'; entry.reason='Missing or unapproved source — original tile'; return; }
      if (!worker) { entry.state=offline?'offline':'fallback'; entry.reason=offline?'Offline — original tile':'Safe artwork transport unavailable — original tile'; return; }
      release=await acquire();
      if(modeEpoch!==epoch || generation!==entry.generation)return;
      entry.release=release;
      await rpc({type:'mode',online:!offline && navigator.onLine});
      if(modeEpoch!==epoch || generation!==entry.generation)return;
      // Reserve possible network capacity while a cache lookup/worker queue is
      // unresolved, so rapid source switches cannot overbook the same budget.
      // Only the worker's actual fetch-start notification charges an attempt;
      // cache-only results and offline/queue rejection never charge it.
      const allowNetwork=(entry.attempts.get(url)||0)+(entry.reserved.get(url)||0)<3;
      let reserved=allowNetwork;
      if(reserved)entry.reserved.set(url,(entry.reserved.get(url)||0)+1);
      const unreserve=()=>{if(reserved){entry.reserved.set(url,entry.reserved.get(url)-1);reserved=false;}};
      // Do not unreserve on an unknown RPC outcome: fail closed rather than
      // granting another possibly fourth fetch after a transport timeout.
      const result = await rpc({type:'load', url, force, allowNetwork},()=>{
        unreserve();
        entry.attempts.set(url,(entry.attempts.get(url)||0)+1);
        if(entry.source===url)emit(entry);
      });
      unreserve();
      if (modeEpoch !== epoch || generation !== entry.generation) {
        if(result.token)await rpc({type:'discard',token:result.token});
        return;
      }
      entry.state = result.state; entry.reason = result.reason || (result.state==='cached'?'Cached game art — last-good':'Game art · SteamGridDB / respective rights holders');
      if(url!==P.source(entry.app,entry.kind))entry.reason+=' · approved alternative (different crop)';
      entry.src = result.src || null; entry.token = result.token;
    } catch (_) {
      if (generation === entry.generation && modeEpoch === epoch) { entry.state='fallback'; entry.reason='Artwork transport timed out — original tile'; }
    } finally {
      pending--;
      if(!entry.src || modeEpoch!==epoch || generation!==entry.generation)release?.();
      if (generation === entry.generation && modeEpoch === epoch) emit(entry);
    }
  }
  const observer = 'IntersectionObserver' in window ? new IntersectionObserver(items => {
    for (const item of items) if (!item.target.isConnected || item.isIntersecting) { observer.unobserve(item.target);observed.delete(item.target); const entry = item.target.artEntry; if (item.target.isConnected && entry.state==='idle') start(entry); }
  }, {rootMargin:'200px'}) : null;
  function mount(app, kind, cls='', eager=false) {
    const entry = entryFor(app,kind), box = node('div','art-slot '+cls);
    for(const previous of entry.views.keys())if(!previous.isConnected)entry.views.delete(previous);
    for(const previous of observed)if(!previous.isConnected){observer?.unobserve(previous);observed.delete(previous);}
    box.dataset.kind=kind; box.dataset.project=app.id;box.setAttribute('role','img');
    // Text and local art remain in place during loading/failure, even when a
    // malformed or missing fallback cannot decode. No image is hidden on error
    // without an honest visible replacement and state label.
    const title = node('span','art-placeholder',app.name), fallback = node('img','art-fallback');
    const local = P.local(app,kind);
    if (local) fallback.src = local;
    fallback.alt=''; fallback.decoding='async'; fallback.addEventListener('error',()=>{fallback.remove();update();});
    const remote = node('img','art-remote'); remote.alt=''; remote.referrerPolicy='no-referrer';
    const status = node('span','art-status');status.id='art-status-'+(++slotID);
    box.append(title);if(local)box.append(fallback);box.append(status);
    const reason=()=>fallback.parentNode===box?entry.reason:entry.reason.replaceAll('original tile','title fallback');
    let displayed = '', failed = '', decodeTimer, decoded=false;
    const update = () => {
      const count=entry.attempts.get(entry.source)||0;
      box.dataset.attempts=String(count); box.dataset.source=entry.source||'';
      box.dataset.state=entry.src?'loading':entry.state;
      status.textContent=entry.src?(fallback.parentNode===box?'Loading game art — original tile':'Loading game art — title fallback'):reason();
      box.setAttribute('aria-label',app.name+' · '+kind+' · '+status.textContent);
      if (!entry.src) { clearTimeout(decodeTimer);remote.removeAttribute('src');remote.remove(); remote.classList.remove('is-loaded'); displayed=''; return; }
      if (displayed===entry.src) {
        if (decoded && remote.complete && remote.naturalWidth>0 && failed!==entry.src) showLoaded();
        return;
      }
      displayed=entry.src; failed='';decoded=false; remote.classList.remove('is-loaded');
      clearTimeout(decodeTimer);decodeTimer=setTimeout(()=>remote.onerror(),14000);
      box.insertBefore(remote,status);
      remote.src=entry.src;
    };
    const showLoaded = () => {
      remote.classList.add('is-loaded'); box.dataset.state=entry.state;
      status.textContent=entry.reason; box.setAttribute('aria-label',app.name+' · '+kind+' · '+entry.reason);
    };
    remote.onload = async () => {
      if (remote.getAttribute('src')!==entry.src || !remote.naturalWidth) return;
      const source=entry.src;
      try{await remote.decode();}catch(_){if(source===entry.src)remote.onerror();return;}
      if(source!==entry.src)return;
      clearTimeout(decodeTimer);
      decoded=true;
      const release=entry.release;
      showLoaded(); entry.decoded=true;
      const token=entry.token;
      if (token && entry.confirmed!==token) {
        entry.confirmed=token;
        try { const result=await rpc({type:'confirm',token}); entry.persisted=result.persisted===true; } catch (_) { entry.persisted=false; }
      }
      release?.();
      emit(entry);
    };
    remote.onerror = async () => {
      if (!entry.src || displayed!==entry.src || failed===entry.src) return;
      clearTimeout(decodeTimer);
      failed=entry.src; const token=entry.token;
      decoded=false;
      const generation=entry.generation, modeEpoch=epoch;
      const release=entry.release;
      entry.src=null; entry.decoded=false; entry.state='fallback'; entry.reason='Image missing, corrupt or unavailable — original tile';
      remote.classList.remove('is-loaded'); remote.removeAttribute('src');
      emit(entry);
      if(token)try {
        const result=await rpc({type:'reject',token});
        if(result.src && generation===entry.generation && modeEpoch===epoch){entry.src=result.src;entry.token=result.token;entry.state=result.state;entry.reason=result.reason;emit(entry);}
      }catch(_){ /* no last-good recovery available */ }
      if(!entry.src)release?.();
    };
    entry.views.set(box,update);
    box.artEntry=entry;
    // First synchronous paint cannot depend on provider/worker/network results.
    box.dataset.state=entry.state; status.textContent=reason();
    box.setAttribute('aria-label',app.name+' · '+kind+' · '+reason());
    queueMicrotask(() => { if (box.isConnected) { update(); if (entry.state==='idle') { if(eager || !observer) start(entry); else {observed.add(box);observer.observe(box);} } } });
    return box;
  }
  async function setOffline(value) {
    offline=value===true; epoch++;
    let persisted=true;
    try { localStorage.setItem('relicade.art.offline',String(offline)); } catch (_) { persisted=false; }
    await initialized;
    try { await rpc({type:'mode',online:!offline && navigator.onLine}); } catch (_) { /* explicit fallback remains */ }
    for (const entry of entries.values()) { entry.generation++; entry.state='idle'; entry.src=null; if ([...entry.views.keys()].some(v=>v.isConnected)) start(entry); }
    return persisted;
  }
  function diagnostic(apps, target, summary) {
    if(diagnosticListener)document.removeEventListener('artworkstate',diagnosticListener);
    target.replaceChildren();
    for (const app of apps) for (const kind of P.KINDS) {
      const entry=entryFor(app,kind), row=node('section','art-diagnostic');
      row.append(node('h3','',app.name+' · '+kind),mount(app,kind,'diagnostic-art'));
      const source=node('p','art-source',entry.source||'No approved source');
      const retry=node('button','art-retry secondary-button','Retry'), select=node('select','art-alternative');
      retry.type='button'; retry.setAttribute('aria-label','Retry '+app.name+' '+kind+' artwork');
      retry.onclick=()=>start(entry,true);
      select.setAttribute('aria-label','Approved artwork alternative for '+app.name+' '+kind);
      for (const candidate of P.KINDS) { const url=P.source(app,candidate); if(!url)continue;const option=node('option','',candidate===kind?'Expected '+kind:'Alternative '+candidate+' (different crop)');option.value=url;select.append(option); }
      select.value=entry.source||'';
      select.onchange=()=>{entry.generation++;entry.source=select.value;entry.state='idle';entry.src=null;start(entry);source.textContent=entry.source;};
      row.append(source,retry,select); target.append(row);
      const update=()=>{retry.disabled=entry.state==='loading'||Boolean(entry.src&&!entry.decoded)||(entry.attempts.get(entry.source)||0)>=3;retry.textContent=(entry.attempts.get(entry.source)||0)>=3?'Retry limit reached':'Retry';};
      entry.views.set(row,update); update();
    }
    const updateSummary=()=>{
      const total=apps.length*P.KINDS.length;
      let loaded=0,cached=0,fallback=0,unchecked=0,alternatives=0;
      for(const app of apps)for(const kind of P.KINDS){const e=entryFor(app,kind);if(e.decoded){e.state==='cached'?cached++:loaded++;if(e.source!==P.source(app,kind))alternatives++;}else if(['fallback','offline','denied'].includes(e.state))fallback++;else unchecked++;}
      summary.textContent=`${loaded+cached} / ${total} real image slots decoded (${cached} cached, ${alternatives} alternatives); ${loaded+cached-alternatives} expected sources decoded; ${fallback} fallback; ${unchecked} pending/not checked. Retry failed slots or choose a credited alternative. A local tile is not a loaded game image.`;
    };
    const listener=()=>{if(!target.isConnected){document.removeEventListener('artworkstate',listener);return;}updateSummary();};
    diagnosticListener=listener;document.addEventListener('artworkstate',listener);updateSummary();
  }
  window.addEventListener('offline',()=>setOffline(offline));
  window.addEventListener('online',()=>{ /* No automatic network retry on reconnection. Use Retry or change artwork mode. */ });
  window.RelicadeArt = {mount,diagnostic,setOffline,scan:apps=>{for(const app of apps)for(const kind of P.KINDS){const e=entryFor(app,kind);if(e.state==='idle')start(e);}},
    get ready(){return ready;},get pending(){return pending;},get offline(){return offline;}};
})();
