# Relicade architecture

## Current implementation

Relicade currently consists of an original brand system and a browser interaction preview. There is no privileged native backend, account service, production catalog, executable installer or game-launch API yet.

```text
Locally cached public catalog + artwork provenance
                    |
                    v
 prototype/index.html + styles.css + app.js
      |                            |
      v                            v
 Search/filter/details      relicade.library
 and external source links  browser-local saved project IDs
                    |
                    v
 tools/preview.py — loopback-only static delivery
                    |
            Brand assets/tokens/fonts
```

### Boundaries

- `brand/`: source-of-truth SVG identity masters, local licensed fonts, shared tokens and responsive presentation. No runtime network dependency.
- `prototype/data/catalog.json`: bounded real third-party metadata snapshot, with acquisition time and per-artwork origin/hash. This is not the full catalog or independent release verification.
- `prototype/app.js`: DOM construction with text nodes, validated external HTTPS URLs, local artwork-path policy, filters/details and saved-library interactions. The library changes only after browser storage acknowledges the write. Saved is not installed.
- `tools/preview.py`: read-only, loopback-only static server. Only the brand/prototype roots are accessible; traversal, external symlink roots/indices, directory listings, foreign Hosts and mutation methods are rejected. CSP, no-referrer and no-store headers are applied. It is a local design tool, not an internet-facing service.
- `NOTICE.md`: external-source credits, original-art provenance and third-party license boundaries. Sensitive diagnostics and copied executable evidence are not included in the source repository.
- `prototype/art-policy.js`, `art.js`, `art-worker.js`: allowlisted credentialless runtime artwork, decoded-only last-good browser cache, truthful fallback states, offline artwork controls and per-slot diagnostic/repair. The restricted server supplies required worker redirect CSP; ordinary static servers fail closed to fallback. This is not a native asset downloader or zero-application-request offline mode. See [ARTWORK.md](ARTWORK.md) for opaque-response limitations and native requirements.
- `tests/`, `prototype/tests/`, `tools/qa_preview.py`: HTTP containment, browser interaction/adversarial state and real restricted-preview/CSP checks. Browser DPI emulation is not native hardware acceptance.

### State ownership

The browser owns view/filter/dialog state and a set of saved project IDs under `relicade.library`. The snapshot owns public metadata. No DOM class, downloaded image or saved bookmark can imply an installed or playable game. Dialog close/focus restoration checks the active open state so an older close event cannot invalidate a newly opened dialog.

## Native target architecture — not implemented

A suitable native implementation needs a shared domain core and platform-specific adapters, with separately testable frontends. The exact production UI/packaging stack is an implementation decision; a static browser prototype does not select or deliver it.

- **Library domain:** stable game/project/installation IDs; atomic/versioned metadata; per-install presentation and provenance; guided import/export/recovery.
- **Operation engine:** durable queue/journal, bounded transfers, trust/digest checks, contained staging and transactional activation. Publish readiness only from authoritative manifests and platform checks.
- **Project adapters:** owned-data requirements, executable/runtime discovery, launch invocation, save/config locations and mod compatibility. Unknown requirements remain unknown.
- **Mod domain:** provider adapters, dependency/file-ownership resolver, staged replacement and reproducible profiles. Separate file collisions from unsupported semantic compatibility claims.
- **Platform adapters:** Windows x64; Linux x64/ARM64 and AppImage/Flatpak; macOS x64/ARM64; Android APK/permissions. Desktop executables are not Android packages. Credential storage, shell integration, input, accessibility and updater behavior are platform-specific.
- **Catalog/trust:** lawful source/provenance, cached metadata, independently defined signing/review policy and explicit offline state. Relicade must not inherit the reference launcher's app/update/telemetry identity or claim to operate its backend.

Crossing from frontend to native execution must require typed operations, scoped filesystem roots, truthful effective state and safe cancellation. Remote metadata must never become arbitrary shell commands, HTML or destructive paths.

## Acceptance

The [product requirements](PRODUCT_REQUIREMENTS.md) define feature, platform and improvement gates. Every native side effect requires unit/fault/security tests plus package and runtime evidence. A shared-code build alone cannot accept a platform.
