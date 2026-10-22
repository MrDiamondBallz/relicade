# Runtime game artwork

## Product screenshots

Public product screenshots must come from the actual current native application.
Do not substitute browser design previews, generated interfaces, composited game
libraries or images that imply unimplemented features. Identify empty libraries,
synthetic test data and development builds in their captions. Never include
credentials, personal paths or private user data.

The README's native Linux capture shows a real empty-library development build.
Brand artwork and the historical browser prototype remain design assets, not
evidence of native features or a released product. Preserve actual interface
content and accurate upstream attribution rather than editing a screenshot to
make the product appear more complete.

## Catalog artwork

The browser reference prefers actual game imagery from the catalog's retained
`referenceArtSources.libraryArt` URLs. Original local tiles are bounded fallback,
not a substitute presented as a successfully loaded game image. Game images do
not establish an installed game, playable state, publisher approval or license.

## Player controls and image completeness

Run `python tools/preview.py` and open `/prototype/`. Online artwork contacts
SteamGridDB and exposes the viewer's IP/user agent to that provider. There is no
artwork account, token, analytics request, backend proxy or remote font request.
**Artwork & privacy** is available in the top bar at every supported width.

- Select **Offline artwork / privacy mode** to stop/abort provider requests and
  queued artwork work. Previously decoded last-good cache entries may display;
  otherwise original local art and an explicit offline label remain visible.
- To avoid provider contact on the very first visit, open
  `/prototype/?art=offline`. The checkbox preference uses
  `relicade.art.offline` in browser localStorage. If storage is unavailable the
  control is session-only; the explicit URL remains the reliable first-load
  choice. Clearing browser storage clears that preference and cached artwork.
- This browser mode still loads its local shell, fonts, snapshot and original
  tiles from the loopback server. It is **not** acceptance of the native
  application's stronger zero-application-request network-disabled mode.
- **Check all artwork slots** checks the three expected visual variants for all
  twelve snapshot projects: hero, header and capsule (36 slots). Spotlight and
  details use hero; discovery cards use header; the diagnostic exposes all three
  variants, including capsules. Optional icon/logo references are retained as
  source metadata, not requested/rendered visual slots in this reference.
- The diagnostic distinguishes decoded expected sources, selected alternatives,
  cache, fallback, and pending/not-checked slots. It shows exact source URLs and
  per-slot Retry/approved alternative controls. A local tile never counts as a
  decoded real source. Different-crop alternatives are explicitly labeled and
  selected only by the user, not silently substituted.
- Retry allows at most three network attempts per slot/source per page session,
  including the first attempt. Renders, search and saved-library changes do not
  reset that budget. Reconnection does not retry automatically. Change to a
  listed alternative or reload deliberately after the retry limit; there is no
  arbitrary URL input or infinite retry loop.

Actual fetch starts charge their slot/source budget even if the user changes
source or artwork mode before completion. Unresolved cache/queue lookups reserve
capacity without counting as attempts; cache-only replies release it. An unknown
transport outcome retains that reservation for the session rather than risk a
fourth fetch. Stale responses cannot change the selected image or its state and
their undecoded tickets are discarded, not committed to cache.

Visible states are not color-only: not checked, loading with a local replacement,
loaded game art, cached last-good, denied/missing source, offline original tile,
and failed/missing/corrupt/timed-out image with original tile. Even a missing or
broken local fallback retains the factual project title and explicit status.
Artwork dimensions are reserved; changing state does not resize cards or hero.
Native dialog keyboard behavior, Escape/focus return and labeled Retry/select
controls remain usable. Card accessible descriptions include the artwork state.

## Browser transport and security boundary

`art-policy.js` accepts only exact HTTPS `cdn2.steamgriddb.com` URLs with bounded
lowercase 32-hex image names in `/hero/` or `/grid/` and png/jpg/webp extensions.
Original spelling is validated before normalization: credentials, alternate
ports, private/IP/unapproved hosts, traversal, backslashes, whitespace, query,
fragment, unsafe protocols and other paths fail closed. Local fallbacks have a
separate bounded `assets/art/<lowercase-slug>.webp` policy. Metadata is never HTML,
commands or an arbitrary local path.

The provider currently serves these images without CORS permission. The browser
therefore uses a narrowly scoped local service worker, not credential-bearing
`img` requests to the provider and not an invented backend endpoint:

1. The client verifies the required worker response CSP before enabling online
   transport. Use the restricted preview server; a generic static server without
   those headers degrades honestly to original fallback. Unsupported/insecure
   service-worker environments also fall back, never bypass the privacy policy.
2. The worker validates the URL independently and fetches using `no-cors`,
   `credentials: omit`, empty/no-referrer policy and no HTTP cache reliance.
   No cookies or authorization/referrer values are forwarded. An empty Referer
   field may appear in browser instrumentation; it discloses no page URL.
3. Fetch requires redirect-follow for no-cors requests. The worker's HTTP CSP
   permits connections to **only** that exact HTTPS CDN host, including redirects;
   it does not allow even its own loopback origin as a redirect destination.
   The document permits only self and the exact CDN as image response origins
   (opaque responses retain their provider URL). There is no broad `https:` image
   allowlist. Tests cover private/loopback redirects before destination contact.
4. The worker serves opaque responses through internal same-origin image tickets.
   It never reads/unwraps opaque response bodies or invents HTTP status/headers.
   Actual renderer image decode is required before loaded state and cache commit.
   A 404/invalid body fails image decode and reports unavailable/corrupt; opaque
   HTTP status cannot honestly distinguish 404, other HTTP errors and bad bytes.
5. At most two transfers/decodes run concurrently; queues and ticket counts are
   bounded. Each active source has a 12-second network/body deadline; the renderer
   also bounds decode/transport waits. Queued slots remain visibly pending. All
   work is finite and offline changes cancel or invalidate stale completions.

The browser controls TLS, DNS, private-network enforcement, redirect processing,
image decoding and storage quota. JavaScript cannot pin DNS or inspect/limit the
bytes, MIME headers or decoded dimensions of an opaque response before decoding.
Count/concurrency/time bounds are not a hard byte-memory quota. Browser rendering
is **not** the native security gate; native adapters must add the controls below.
Do not generalize this transport to arbitrary hosts or payload download/install.

## Last-good cache

`relicade-art-good-v1` holds at most 36 opaque provider responses, keyed by exact
approved URL, persisted only after actual successful image load. New invalid
bytes do not replace a previous good entry. Failed refreshes preserve/recover the
last-good image with a cached/refresh-failure label; corrupt cached entries are
removed and replaced by truthful original fallback. Opaque responses may consume
padded quota; storage may be denied, evicted, cleared or unavailable. No durable
cache guarantee or HTTP-cache hit is fabricated. No native filesystem path is
exposed to the renderer. The browser does not know the cached source's current
freshness, license or authenticity; cached means previously decoded, not verified.

Replacement uses `Cache.put` without a destructive prior delete, so a rejected
refresh write does not itself remove the prior last-good response. Cache-only
decode confirmations do not rewrite storage. Successful insertions and oldest-
entry eviction are serialized to retain the 36-entry count bound under normal
storage operation. These application guarantees do not prevent browser-initiated
eviction, user clearing, storage denial or other browser storage failures.

## Provenance, refresh and rights

The exact Ocarina source is
<https://cdn2.steamgriddb.com/hero/b63d7ec7840735552cb77df807d944ef.png>.
Source metadata, attribution, acquisition time, external provenance and original
fallback/font hashes are retained. Historical transformation text in external
reference provenance describes earlier source records, not a new claim that the
runtime downloaded/encoded/bundled a local third-party image. Sources & artwork
exposes separate online source links and original-tile provenance.

`fetch_catalog.py` remains metadata-only plus original-tile rendering: it does
not fetch artwork or provide an arbitrary downloader. Original renderer, tile
bytes, font sources and OFL notices are unchanged. New runtime third-party image
bytes and unreviewed live QA screenshots must not be committed. Only explicitly
reviewed, privacy-safe captures of the actual native product belong in the public
showcase; automated browser QA must not overwrite them. Source links,
runtime availability and credit do not grant a license; see [NOTICE](../NOTICE.md).

## Verification commands

```sh
node --test prototype/tests/art_policy.test.cjs
node tools/check_artwork.cjs
python prototype/tests/test_artwork.py
python prototype/tests/test_project_art.py
python tools/qa_preview.py
# Explicit provider contact; real responses, private ignored screenshots/report:
python tools/qa_artwork_live.py --network
```

`check_artwork.cjs` is an actionable **offline metadata/fallback** diagnostic,
not live availability evidence. Deterministic browser tests use actual snapshot
URLs but explicitly synthetic PNG/error responses. The opt-in live tool uses no
interception and records actual decode outcomes per expected slot; failures are
reported, not converted into production success. Automated captures go under
ignored `qa/local/` by default, never over the curated documentation preview.
The live checker exits nonzero if any expected project/kind/source is missing,
duplicated, substituted, fallback-only or lacks positive decoded dimensions; its
private report records availability separately from row count.

## Documentation maintenance

This guide's transport/cache requirements remain source-sensitive and are
registered in the [documentation review map](DOC_STATUS.md). Regenerating
repository facts does not certify artwork rights, live-provider availability,
native byte-bound enforcement or emulation support. The broader retro/emulation
vision remains future work with separate licensing and user-data requirements.

## Native adapter contract — requirements, not implemented here

Align with coverage IDs `library.presentation`, `library.offline-start`,
`privacy.network-disabled`, `privacy.consent`, and requirements R6–R10. No native
coverage cell is accepted by these browser tests. Every native platform/package
still needs independent unit/package/runtime evidence at its exact build commit.

A native metadata/art adapter must expose stable project ID, expected slot kind,
exact approved source URL, source provider/credit/rights statement, metadata
acquisition and last-check timestamps, chosen user override and versioned source
identity. Return effective states separately: pending, source-loaded, cache,
last-good-on-refresh-failure, offline-fallback, denied, corrupt and unavailable;
include a bounded reason/retry capability, not success-shaped missing data.
Do not expose private filesystem paths in UI payloads or export secrets.

The native transport must validate scheme/host/path and every redirect before
contact, resolve/reject nonpublic addresses and pin validated connections while
preserving TLS hostname checks, omit credentials/referrers, and apply hard
compressed/expanded-byte, decode-dimension/pixel-count, concurrency and deadline
budgets. Cache only validated/decoded content into isolated app-owned storage;
use atomic writes, digest/source association, bounded byte/count retention,
corruption recovery, last-good preservation and explicit clear/repair operations.
Do not erase user custom art on metadata refresh or imply digest = publisher
license/authenticity. Tests must inject permission/quota/disk/rename/cancel/crash,
changed source, expired URL, corrupt cache and offline failures without losing
working art. Native network-disabled mode must suppress **all** application
traffic, not just provider art, while local library and launch remain usable.
No privileged native cache/downloader is implemented or duplicated in this lane.
