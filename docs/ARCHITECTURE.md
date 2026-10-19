# Relicade architecture

## Current implementation

Relicade has two separate development surfaces: the original browser/brand preview and the .NET 10/Avalonia native foundation under `native/`. The native shared code retains real local persistence, installation/launch, release, mods, input, kiosk and recovery services. Successful compilation and synthetic/headless exercises do not establish complete reference-behavior reconciliation, full readiness, platform acceptance or a packaged release. Relicade does not operate a production catalog/account service.

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
- `tests/`, `prototype/tests/`, `tools/qa_preview.py`: HTTP containment, browser interaction/adversarial state and real restricted-preview/CSP checks. Browser DPI emulation is not native hardware acceptance.

### State ownership

The browser owns view/filter/dialog state and a set of saved project IDs under `relicade.library`. The snapshot owns public metadata. No DOM class, downloaded image or saved bookmark can imply an installed or playable game. Dialog close/focus restoration checks the active open state so an older close event cannot invalidate a newly opened dialog.

## Native foundation — implemented source, incomplete acceptance

`native/Relicade.App.csproj` supplies the shared Avalonia UI/services, `native/lib/Relicade.Core` the domain/provider/install layer, and the desktop/Android projects the platform hosts. The inherited capabilities and source origin are documented in technical/legal notices rather than product presentation. Local-library fixtures exercise persistence, snapshots, corrupt-store preservation, missing files and native shell composition. Linux self-contained startup uses an isolated virtual display; Windows cross-publish is not Windows runtime acceptance. [Build instructions and platform status](../native/README.md) identify the remaining gates.

The product's target architecture still requires the following completed boundaries; source reuse alone does not deliver or accept every item:

- **Library domain:** stable game/project/installation IDs; atomic/versioned metadata; per-install presentation and provenance; guided import/export/recovery.
- **Operation engine:** durable queue/journal, bounded transfers, trust/digest checks, contained staging and transactional activation. Publish readiness only from authoritative manifests and platform checks.
- **Project adapters:** owned-data requirements, executable/runtime discovery, launch invocation, save/config locations and mod compatibility. Unknown requirements remain unknown.
- **Mod domain:** provider adapters, dependency/file-ownership resolver, staged replacement and reproducible profiles. Separate file collisions from unsupported semantic compatibility claims.
- **Platform adapters:** Windows x64; Linux x64/ARM64 and AppImage/Flatpak; macOS x64/ARM64; Android APK/permissions. Desktop executables are not Android packages. Credential storage, shell integration, input, accessibility and updater behavior are platform-specific.
- **Catalog/trust:** lawful source/provenance, cached metadata, independently defined signing/review policy and explicit offline state. Relicade must not inherit the reference launcher's app/update/telemetry identity or claim to operate its backend.

Crossing from frontend to native execution must require typed operations, scoped filesystem roots, truthful effective state and safe cancellation. Remote metadata must never become arbitrary shell commands, HTML or destructive paths.

The foundation owns Relicade application/package/data/shortcut identities and does not adopt another executable's settings sidecar. Inherited self-update hooks/destinations, telemetry, announcement delivery and automatic download reports are disabled. External metadata/image endpoints retain their actual identity; their reported verdicts are not independent Relicade verification. Real game-art loading and caching remain primary, with explicit unavailable/offline fallbacks and no bundled unlicensed game pixels. The retained installation engine is not yet journaled/transactional; product-wide offline enforcement, vault-backed tokens and complete functional/platform reconciliation remain acceptance work.

## Acceptance

The [product requirements](PRODUCT_REQUIREMENTS.md) define feature, platform and improvement gates. Every native side effect requires unit/fault/security tests plus package and runtime evidence. A shared-code build alone cannot accept a platform.
