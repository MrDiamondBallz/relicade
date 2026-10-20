# Relicade browser preview

**Old legends. New life.**

An original game-first discovery and saved-library preview, not a native launcher. It is one design surface for the wider vision: community-revived classics, retro games and future emulation alongside native ports, recompilations and recreations. Emulation is not implemented here; no emulator/core or game/BIOS distribution is provided. Identity masters and the brand presentation live in [`../brand/`](../brand/).

## Run

From the repository root:

```sh
python tools/preview.py
```

Open <http://127.0.0.1:8766/prototype/> or <http://127.0.0.1:8766/brand/>. No npm/build step, account, remote fonts or runtime catalog API access is required. Online mode loads credited game imagery from SteamGridDB. For no artwork-provider contact from first load, use <http://127.0.0.1:8766/prototype/?art=offline>. The restricted server supplies required artwork security headers and does not expose the repository root. Direct `file://` opening may prevent JSON loading.

## Working paths

- Editorial discovery using an authentic, bounded twelve-project public metadata snapshot.
- Search across game/project names, descriptions and tags; platform/system/feature filters and sorting.
- Project details with source links, maintainer credit, reported platforms and point-in-time release metadata. Requirements uncertainty stays explicit.
- Browser-local saved-library bookmarks under `relicade.library`, persisted only after storage acknowledges a write. These are not installed games.
- Ctrl/Cmd K command palette, keyboard navigation, native dialogs, Escape and focus restoration.
- Source/attribution disclosure, real game artwork, decoded last-good cache where supported, explicit original-art fallback, per-slot repair and offline artwork/privacy controls.

There are no executable downloads, installs, game launches, fake progress, fabricated playtimes or verification verdicts. Disabled native functionality is not represented by successful-looking controls.

## Presentation and states

The game/project is the focal surface. Explore project is the primary action; source/requirements/release detail and advanced filters are disclosed on demand. The same original symbol, wordmark, fonts, graphite/ivory palette and orange action accent connect the brand and product.

States cover loading, ready, no matches, empty saved library, save/remove confirmation, denied/corrupt browser storage and unavailable metadata with retry. Browser preferences are not installed application state.

## Data, art and rights

`data/catalog.json` records actual acquisition time, source provider and per-project metadata URLs. It is a bounded selection, not the full catalog or a live release/security check. External project statements are not independently validated Relicade facts.

Bundled project tiles are original typographic/geometric presentation art, not gameplay, official game images or an endorsement. Original-art provenance contains actual local paths and hashes. Online mode prefers credited game images from retained external URLs; only fallback tiles are bundled in runtime assets. The metadata refresh workflow still never downloads game artwork. Public URLs and attribution do not grant a license. [Runtime artwork](../docs/ARTWORK.md) documents all 36 expected slots, failure/retry/privacy states, opaque-response cache limits and the native adapter requirements.

See [NOTICE.md](../NOTICE.md) for source credits, game-name rights and bundled font licenses. No ROMs, BIOS, game binaries or player files are included. Original source visibility is not an open-source license grant; trademark clearance has not been claimed.

## Verification

See [CONTRIBUTING.md](../CONTRIBUTING.md) for setup/test commands. Browser checks cover interactions, hostile metadata, malformed shapes, image/cache failures, responsive layouts and actual restricted-preview CSP. Offline regression captures use original art; live provider QA is separately opt-in and private. Static local preview traffic is distinct from native network-disabled acceptance.

Reduced motion removes transitions. Logos remain vector and layout uses CSS pixels. Browser DPR emulation does not establish native Windows/macOS/Linux/Android hardware or accessibility compatibility.

## Native release work

Packaging, real install/update/launch, transactional recovery, validated owned-data setup, compatibility/trust checks, mod profiles, controller input and native assistive-technology/DPI acceptance remain separate requirements. This preview is not a completed launcher.
