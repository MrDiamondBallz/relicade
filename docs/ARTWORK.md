# Runtime game artwork

## Product screenshots

Public product screenshots must come from the actual current native application.
Do not substitute browser design previews, generated interfaces, composited game
libraries or images that imply unimplemented features. Identify empty libraries,
synthetic test data and development builds in their captions. Never include
credentials, personal paths or private user data.

The README's current capture shows the actual native interface with an empty
library in a headless Linux development render, not a desktop-host session.
Brand artwork and the historical browser prototype remain design assets, not
evidence of native features or a released product. Preserve actual interface
content and accurate upstream attribution rather than editing a screenshot to
make the product appear more complete.

## Native capture provenance

The [current empty-library capture](assets/relicade-native-empty-8a1796c-20261023.png) renders native source
[`8a1796c`](https://github.com/MrDiamondBallz/relicade/tree/8a1796c3fd97a77ff55546a6958054ea8dafd3e0)
on 2026-10-23 at 1100×760 pixels. [Its immutable metadata](assets/relicade-native-empty-8a1796c-20261023.json)
records the PNG hash, fixture binding and limitations. It uses isolated test
settings, a rejecting network transport and the actual native view. No personal
files, installed games, game artwork or image edits are involved. It does not
establish a working desktop display, platform accessibility, controllers or gameplay.

The [earlier desktop development capture](assets/relicade-native-linux.png) is
retained unchanged as historical material. Its old subtitle does not represent
the current interface and it is no longer the README's current product image.

## Interface direction

The intended native card layout puts real game imagery first, with readable game
names, setup/play status and focus indicators over the art. Retain the established
dark palette, orange cut-R and complete RELICADE wordmark. Artwork needs recorded
source and rights/provenance review; an attractive card never proves install or
play support. Missing or offline art must keep a useful labeled fallback. Reveal
concepts describe a future interface, not evidence of the current application.

## Catalog artwork

The browser reference prefers actual game imagery from the catalog's retained
`referenceArtSources.libraryArt` URLs. Original local tiles are bounded fallback,
not a substitute presented as a successfully loaded game image. Game images do
not establish an installed game, playable state, publisher approval or license.

## Native Discover cards

The Discover presentation gives existing catalog header/capsule imagery more
space and keeps readable project titles and explicit Add/Open controls. It does
not replace unavailable images with generated game scenes, change providers,
remove upstream credits, or grant reuse rights. The native visual regression
fixtures bind local procedural pixels to clearly labeled synthetic entries;
those captures prove layout and image binding, not real-game artwork coverage.

## Native library cover references

Library entries retain a separate catalog cover record with the exact entry ID,
source URL, header/capsule variant and the catalog icon value used when it was
selected. The initial supported references are static SteamGridDB grid URLs;
other hosts or URL shapes keep the existing project icon. This is a bounded
mapping, not complete artwork-source coverage or a rights grant. Borrowed game
icons remain excluded from the project-icon field.

All three native library layouts use an eligible resolved cover while preserving
the player's custom local image and edited project icon. A changed entry ID or
icon binding invalidates the cover. The image tooltip retains its provider,
variant and original URL. These fields survive library save/load and catalog
refresh; they are not silently substituted with another project's art.

Add checks only existing cache files before inserting the library entry. Later
session-owned enrichment may use the existing shared image loader; its resizing
service at `quiverlauncher.com` and original SteamGridDB source can receive image
requests. Cancellation prevents late results from changing the entry, but does
not promise to cancel a shared image request already used by other views. This
is not the product-wide network-disabled mode required below.

Cache-only checks refuse linked/nonregular files, files larger than 16 MiB,
unsupported headers and undecodable images without deleting or overwriting the
retained entry. If revalidation rejects a previously displayed cache, its resolved
path and source label are revoked; a valid alternate cache alias still takes
priority. Valid cached covers remain usable offline; missing/failed covers fall
back to the project icon. The encoded-file limit is not a decoded-pixel or
memory budget. The broader transport, redirect, DNS, pixel, atomic-cache and
rights-review obligations below remain open; this change does not certify them.
Synthetic image fixtures exercise remote-cover mapping, persistence, fallback
and native layouts without requesting third-party game pixels. The separately
reviewed local portrait below has its own exact provenance and rights scope.

## Reviewed Taisei portrait

The exact Taisei Project catalog entry has one bundled, credited portrait by
**afensorm**, commissioned for that game and identified under CC BY 4.0 in the
[pinned upstream credits](https://github.com/taisei-project/taisei/blob/5668c41b41f7227d21fc80cf56997d4d30356ce7/COPYING.txt).
The [artwork notice](../native/Assets/GameArt/taisei-portrait-NOTICE.txt) records
the original Krita source, artist, license, crop/resize and lack of endorsement.
The [provenance record](../native/Assets/GameArt/taisei-portrait-provenance.json)
binds the original and derivative hashes. This is the artist's character
portrait, not a gameplay screenshot or generated game scene. Its license does
not change the rights in Relicade's original material.

The shipped representation is 960 × 540 pixels and 236,501 encoded bytes. Its
exact hash, PNG header and dimensions are checked before decoding. Only the
compiled resource and exact catalog ID/slug select it in Discover. Library use
also requires the matching GitHub project and unchanged icon binding. A remote
catalog field cannot opt another game into this bundled image.

Discover Add retains that reference through the ordinary library save/load
path. The shared local loader can resolve it while offline, without an image
provider request or another disk cache. The player's custom image still wins;
changed identity, a rejected resource or a cancelled late result keeps the
existing icon fallback. Both views retain the artist, license, original source
and transformation credit in the image tooltip. This one reviewed portrait is
not full catalog artwork coverage, installation evidence or gameplay evidence.
Other remote cover references retain the limitations described above.

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
