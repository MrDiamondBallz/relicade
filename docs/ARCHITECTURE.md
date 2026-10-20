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
- `prototype/art-policy.js`, `art.js`, `art-worker.js`: allowlisted credentialless runtime artwork, decoded-only last-good browser cache, truthful fallback states, offline artwork controls and per-slot diagnostic/repair. The restricted server supplies required worker redirect CSP; ordinary static servers fail closed to fallback. This is not a native asset downloader or zero-application-request offline mode. See [ARTWORK.md](ARTWORK.md) for opaque-response limitations and native requirements.
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

The foundation owns Relicade application/package/data/shortcut identities and does not silently adopt another executable's settings sidecar. Inherited self-update hooks/destinations, telemetry, announcement delivery and automatic download reports are disabled. External metadata/image endpoints retain their actual identity; their reported verdicts are not independent Relicade verification. Real game-art loading and caching remain primary, with explicit unavailable/offline fallbacks and no bundled unlicensed game pixels.

Merged source now includes contained portable staging, manifests and journaled generation replacement/recovery. The two activation renames are not an atomic pair; platform-specific power-loss guarantees are not certified. Credential sessions use OS-vault adapters with no plaintext fallback; explicit legacy credential migration requires a preview and confirmation. Managed offline instance shortcuts enter the recovery/lifetime gate before launch. Removal composes authority for unmanaged targets and refuses managed/recovery trees; it is not managed uninstall. Mod writes into managed portable generations are currently refused, not advertised as integrated game/mod rollback. See the [native guide](../native/README.md). Explicit redundant-backup payload cleanup previews and hash-confirms only older committed operations' duplicate immutable backup files, preserving all previous generations, data snapshots, journals and current-operation backups. It acquires writer/mod/ancestor authority, binds library/settings and retained-file evidence, writes a durable local approval before deletion and requires explicit resume after interruption. This is not credential erasure, general history pruning or managed uninstall; hostile OS writers and physical power-loss/native-filesystem guarantees remain unaccepted. Product-wide network-disabled enforcement, complete functional reconciliation and all package/runtime platform gates remain open.

The cleanup-only typed `PortableBackupCleanupService` now composes that same
Core authority and canonical context for CLI and the native library dialog. The
UI addition in this branch is not yet merged or packaged. A session-owned flow
captures the exact InstanceKey, prevents duplicate dialogs, hashes/applies off
the dispatcher and drains cancellation before dependencies are released. The
dialog confirms only its displayed hash; stale errors require a new preview and
confirmation, and interrupted results require explicit same-hash resume. It
reads persisted identities without replacing the live Games collection. Its
cleanup-only manager check reads the injected settings recovery flag without
loading/saving that store; it does not establish arbitrary cross-store atomicity.
Existing library-menu release prefetch remains separate from these network-silent
cleanup operations. No Core deletion, receipt or lease algorithm is changed.

## Staged broader architecture — not implemented

The long-term discovery/library model spans community-revived classics, retro games and emulation as well as native ports, recompilations and recreations. Future project types need explicit execution kind, emulator/core identity and license, compatible versions, user-supplied game/BIOS prerequisites, controller/keyboard setup and recoverable version/mod state. No emulator host/core manager or ROM/BIOS/key distribution is implemented or authorized by this architecture. Local/offline use remains account-optional. This design direction does not add or waive current native acceptance cells.

## Documentation boundary

[Documentation maintenance](DOCUMENTATION.md) maps source responsibilities to each active document. Generated facts do not infer semantic truth; architecture claims require review of the actual source diff and independent review before adoption.

## Acceptance

The [product requirements](PRODUCT_REQUIREMENTS.md) define feature, platform and improvement gates. Every native side effect requires unit/fault/security tests plus package and runtime evidence. A shared-code build alone cannot accept a platform.
