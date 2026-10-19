/* Credentialless artwork transport, NOT a general proxy or application cache.
 * No third-party bytes are in Git. Responses without CORS remain opaque.
 */
'use strict';
importScripts('art-policy.js');
const CACHE = 'relicade-art-good-v1', MAX_GOOD = 36, MAX_TICKETS = 48;
const tickets = new Map(), modes = new Map(), transfers = new Map();
let active = 0;
const queue = [];
let persistence = Promise.resolve();
function persist(t) {
  // Serialize insert + eviction. Cache.put replaces atomically: never delete a
  // last-good entry first, and never rewrite a decoded cache-only confirmation.
  const write = persistence.then(async () => {
    const cache = await caches.open(CACHE);
    await cache.put(key(t.url), t.response.clone());
    const keys = await cache.keys();
    for (const old of keys.slice(0, Math.max(0, keys.length - MAX_GOOD))) await cache.delete(old);
    return true;
  });
  persistence = write.catch(() => false);
  return persistence;
}
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
const runtime = (url, token) => new URL('art-runtime/' + encodeURIComponent(url) + '?ticket=' + token, self.registration.scope).href;
const key = url => new URL('art-cache/' + encodeURIComponent(url), self.registration.scope).href;
async function cached(url) { try { return await (await caches.open(CACHE)).match(key(url)); } catch (_) { return null; } }
async function forget(url) { try { await (await caches.open(CACHE)).delete(key(url)); } catch (_) { /* cache may be denied */ } }
function trimTickets() { for (const [id, t] of tickets) if (Date.now() - t.created > 60000) {t.finish();tickets.delete(id);} }
function ticket(client, url, response, finish=null) {
  trimTickets();
  if (tickets.size >= MAX_TICKETS) { const oldest=tickets.keys().next().value;tickets.get(oldest).finish();tickets.delete(oldest); }
  const id = crypto.randomUUID();
  tickets.set(id, {client, url, response, fromCache:finish===null, finish:finish||(()=>{}), created:Date.now()});
  return {token:id, src:runtime(url,id)};
}
async function transfer(client, url, started) {
  // Bound concurrency and waiting work; no timer-based automatic retries.
  if (queue.length >= 72) throw new Error('Artwork queue full');
  if (active >= 2) await new Promise(resolve => queue.push(resolve));
  else active++;
  const abort = new AbortController();
  const id = crypto.randomUUID();
  transfers.set(id, {client, abort});
  let done=false;
  const finish=()=>{if(done)return;done=true;clearTimeout(timer);transfers.delete(id);if(queue.length)queue.shift()();else active--;};
  const timer = setTimeout(() => {abort.abort();finish();}, 12000);
  try {
    if (modes.get(client)?.online !== true) throw new Error('Artwork offline');
    // Fetch forbids no-cors with redirect:error/manual. The worker's response
    // CSP therefore restricts EVERY redirect to the same approved HTTPS host.
    // Credentials/referrer stay omitted across permitted redirects.
    started(); // Charge only a real fetch, before its possibly stale completion.
    const response = await fetch(url, {mode:'no-cors', credentials:'omit', redirect:'follow',
      referrerPolicy:'no-referrer', referrer:'', cache:'no-store', signal:abort.signal});
    return {response,finish}; // Keep concurrency + timeout until decode/reject.
  } catch(error) {finish();throw error;}
}
async function message(event) {
  const port = event.ports[0], client = event.source;
  if (!port || !client?.id || !client.url?.startsWith(self.registration.scope)) return;
  const data = event.data || {};
  try {
    if (data.type === 'mode') {
      const live=new Set((await self.clients.matchAll()).map(c=>c.id));
      for(const id of modes.keys())if(!live.has(id))modes.delete(id);
      if(!Number.isSafeInteger(data.sequence) || data.sequence<1)throw new Error('Invalid mode sequence');
      if(data.sequence>(modes.get(client.id)?.sequence||0)){
        modes.set(client.id, {online:data.online===true,sequence:data.sequence});
        if (data.online !== true) for (const t of transfers.values()) if (t.client === client.id) t.abort.abort();
      }
      port.postMessage({ok:true}); return;
    }
    if (data.type === 'confirm' || data.type === 'reject' || data.type === 'discard') {
      const t = tickets.get(data.token);
      if (!t || t.client !== client.id) throw new Error('Expired artwork ticket');
      t.finish();
      if (data.type === 'confirm') {
        let persisted = false;
        try {
          persisted = t.fromCache ? Boolean(await cached(t.url)) : await persist(t);
        } catch (_) { /* Decoded pixels remain usable, but persistence is not promised. */ }
        tickets.delete(data.token);
        port.postMessage({ok:true, persisted});
      } else {
        tickets.delete(data.token);
        // A stale renderer never decoded this response. Release its transfer
        // without committing, invalidating last-good, or creating a new ticket.
        if(data.type==='discard'){port.postMessage({ok:true});return;}
        if(t.fromCache) await forget(t.url);
        const old = t.fromCache ? null : await cached(t.url);
        port.postMessage(old ? {...ticket(client.id,t.url,old),state:'cached',reason:'Refresh image invalid — cached last-good'} : {ok:true});
      }
      return;
    }
    if (data.type !== 'load') throw new Error('Unknown artwork request');
    const url = self.RelicadeArtPolicy.approved(data.url);
    if (!url) throw new Error('Artwork source denied');
    const good = await cached(url);
    if (good && !data.force) { port.postMessage({...ticket(client.id,url,good), state:'cached'}); return; }
    if (modes.get(client.id)?.online !== true || data.allowNetwork !== true) {
      port.postMessage(good ? {...ticket(client.id,url,good), state:'cached'} : data.allowNetwork!==true ? {state:'fallback',reason:'Retry limit reached — original tile'} : {state:'offline', reason:'Offline — original tile'}); return;
    }
    try {
      const {response,finish} = await transfer(client.id,url,()=>port.postMessage({phase:'attempt'}));
      // Opaque responses deliberately expose no status or headers. Decode in the
      // renderer is the only success evidence; a 404/corrupt response won't pass.
      if (response.type !== 'opaque' && !response.ok) {finish();throw new Error('Image HTTP error');}
      port.postMessage({...ticket(client.id,url,response,finish), state:'loaded'});
    } catch (error) {
      port.postMessage(good ? {...ticket(client.id,url,good), state:'cached', reason:'Refresh failed; last-good image'} : {state:'fallback', reason:'Source failed or timed out — original tile'});
    }
  } catch (_) { port.postMessage({state:'fallback', reason:'Artwork transport/cache unavailable — original tile'}); }
}
self.addEventListener('message', event => event.waitUntil(message(event)));
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  const prefix = new URL('art-runtime/', self.registration.scope).href;
  if (!url.href.startsWith(prefix)) return; // Local shell/catalog delivery is untouched.
  event.respondWith((async () => {
    trimTickets();
    let source;
    try {source=self.RelicadeArtPolicy.approved(decodeURIComponent(url.pathname.slice(new URL(prefix).pathname.length)));}catch(_){return new Response('',{status:400});}
    const t = tickets.get(url.searchParams.get('ticket'));
    if (!source || event.request.destination !== 'image') return new Response('',{status:404});
    if (t && t.client === event.clientId && t.url === source) return t.response.clone();
    const client = await self.clients.get(event.clientId);
    if (!client?.url.startsWith(self.registration.scope)) return new Response('',{status:404});
    return (await cached(source)) || new Response('',{status:404});
  })());
});
