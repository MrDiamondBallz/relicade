# Relicade roadmap

Progress is accepted through working behaviour and recorded tests, not screenshots alone. The goal is a complete, gamer-first launcher across the target platforms, with improvements added rather than features cut for appearance.

## 1. Product foundation — available as a development preview

- Original symbol, wordmark, app icon and lockup masters.
- Brand system, local fonts and responsive presentation.
- Real-metadata discovery, search/filter, details and saved-library bookmarks.
- Original project-tile artwork, with no bundled third-party game images.
- Runtime real-game artwork from credited source URLs, including the exact Ocarina hero; explicit loading/cache/failure/offline fallback and 36-slot repair diagnostics. Browser opaque-cache limitations and native art-cache/privacy gates remain explicit in [ARTWORK.md](ARTWORK.md).
- Restricted local preview and regression suites.
- Product requirements, architecture and CI template.

Hosted CI remains pending activation. No native launcher release is implied.

## 2. Native foundation — in intensive development, not full readiness

A buildable .NET 10/Avalonia source foundation now retains the shared native capability baseline, with independent application/data identities, local-library persistence/headless fixtures, real runtime artwork loading/cache and disabled inherited self-update/telemetry/reporting channels. Linux self-contained startup and Windows cross-publish are development checks, not complete native/platform acceptance; see [native status](../native/README.md).

Next gates remain complete catalog and behavior coverage, project requirements/owned-data setup, truthful Ready/Needs setup/Repair states, complete install/launch/recovery journeys and independent functional/platform acceptance. Local launch must not require a catalog account or network round trip. Keep real available game imagery, including Ocarina, with accurate credits and explicit offline/error fallbacks; do not bundle unlicensed third-party pixels or replace normal available artwork with generic brand tiles.

A first package-bound local game-data setup flow is included in the development
source, with explicit copy confirmation and shared launch revalidation. It covers
one exact Perfect Dark Linux x64 package, with a separate optional Freedoom
Phase 2 flow bound to UZDoom 5.0.3 Linux x64. Neither establishes complete catalog
setup or tested gameplay. [The setup guide](GAME_SETUP.md) lists the supported input and limits.

## 3. Installation, credentials and recovery — bounded source progress

Merged source includes staged portable installation, manifests/journaled generation replacement and recovery; explicit vault-backed credential migration; offline per-instance shortcuts/lifetime handling; and managed-removal loss-prevention safeguards. These are reviewed development-source milestones, not complete packaged journeys or universal crash/power-loss safety. Managed uninstall and game/mod integration remain limited; see the [native guide](../native/README.md). [Redundant-backup payload cleanup](https://github.com/MrDiamondBallz/relicade/pull/3) adds explicit preview/hash-confirmation and interruption/resume for older committed duplicate immutable files only. All previous generations, data snapshots and the current operation's backup remain; this is not credential erasure, general retention or managed uninstall, which remain open work.

This source includes the native library's **Clean redundant backups…** journey:
complete preview, explicit displayed-hash confirmation, cancel, invocation-local
results and explicit same-hash resume. The UI is not packaged or native-platform
accepted; it does not complete general retention or managed uninstall.

Complete durable queues/cancellation, retained-version recovery journeys, provider trust and independent fault/security/platform acceptance still need work. Preserve original game data, saves and configuration. Test interrupted transfers, crashes, locked files and disk exhaustion.

The source now includes **Restore previous portable version…** in the native
library: exact immediate-retained-payload preview, explicit displayed-hash
confirmation, cancel/drain and observed-commit results. CURRENT declared data is
retained, not older saves; compatibility and external data remain unsupported
without project adapters. Preview/reopen starts no repair, resume or retry; the
existing confirmed transaction can attempt internal recovery on failure. No
download or general history restore is implied. Complete R3 and native-platform/package acceptance remain open.

## 4. Full functionality and platform coverage — mandatory, pending

Versions/pinning, manual entries/import/export, mods/dependencies/profiles, controller/keyboard navigation, kiosk/fullscreen, storage/recovery and platform package adapters remain required.

- Windows x64.
- Linux x64/ARM64, including AppImage and Flatpak packaging.
- macOS Intel/Apple Silicon.
- Android APK with its own permissions and application-launch boundaries.

Each platform needs actual package/runtime/accessibility/recovery evidence. Unsupported hardware is an open test requirement, not an assumed pass.

The [machine-readable coverage contract](FEATURE_COVERAGE.json) and [developer guide](FEATURE_COVERAGE.md) track 71 required feature rows, eight platform/package targets and 23 synthetic fixture scenarios. The validator and safe fixture generator are executable development tools, not native acceptance evidence; the native acceptance ledger remains empty.

## 5. Measurable quality improvements — pending

Clearer setup, unified operations, staged mod replacement, reproducible profiles, stable offline-first library performance and accessible controller-first workflows. Performance and reliability improvements need reproducible before/after task evidence with equivalent inputs and clearly stated limits.

## 6. Broader discovery, retro games and emulation — future scope

The north star is one coherent home for community-revived classics, retro games and emulation, including native ports, recompilations and recreations. Discovery should explain the project, compatibility and setup; library, versions, mods, recovery and controller/keyboard play should remain understandable and local/offline-account-optional.

The intended setup is a clear, player-approved download/configuration flow for authorized emulator/core/runtime assets, with progress, cancellation, recovery and a verified Play action. Required personal game data remains a guided local import, not an unauthorized background download.

Emulation is not implemented. Future adapters need emulator/core license review, explicit compatibility and lawfully user-supplied game data/BIOS; no unlicensed ROM/BIOS/key distribution. This staged direction does not add or waive existing native release gates or imply a hosted service.

## Documentation upkeep

[Generated facts and the source-review map](DOC_STATUS.md) are maintained with [the documentation tool](DOCUMENTATION.md) through contribution-time checks and periodic maintainer reconciliation. Narrative progress needs explicit source-diff review and independent review before adoption, not an automatically bumped date; the tool does not approve prose. Cloning the repository does not install a timer or service.

See [PRODUCT_REQUIREMENTS.md](PRODUCT_REQUIREMENTS.md) for detailed acceptance criteria. Public source availability is not an installable release, production service or universal compatibility claim.
