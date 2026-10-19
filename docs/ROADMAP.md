# Relicade roadmap

Progress is accepted through working behaviour and recorded tests, not screenshots alone. The goal is a complete, gamer-first launcher across the target platforms, with improvements added rather than features cut for appearance.

## 1. Product foundation — available as a development preview

- Original symbol, wordmark, app icon and lockup masters.
- Brand system, local fonts and responsive presentation.
- Real-metadata discovery, search/filter, details and saved-library bookmarks.
- Original project-tile artwork, with no bundled third-party game images.
- Restricted local preview and regression suites.
- Product requirements, architecture and CI template.

Hosted CI remains pending activation. No native launcher release is implied.

## 2. Native library and readiness — pending

Implement durable local-library state, project requirements, owned-data setup, executable/runtime discovery and truthful Ready/Needs setup/Repair states. Local launch must not require a catalog account or network round trip.

## 3. Installation, downloads and recovery — pending

Implement verified release ingestion, a durable cancellable queue, contained extraction, recoverable activation and retained-payload rollback. Preserve original game data, saves and configuration. Test interrupted transfers, crashes, locked files and disk exhaustion.

## 4. Full functionality and platform coverage — mandatory, pending

Versions/pinning, manual entries/import/export, mods/dependencies/profiles, controller/keyboard navigation, kiosk/fullscreen, storage/recovery and platform package adapters remain required.

- Windows x64.
- Linux x64/ARM64, including AppImage and Flatpak packaging.
- macOS Intel/Apple Silicon.
- Android APK with its own permissions and application-launch boundaries.

Each platform needs actual package/runtime/accessibility/recovery evidence. Unsupported hardware is an open test requirement, not an assumed pass.

## 5. Measurable quality improvements — pending

Clearer setup, unified operations, staged mod replacement, reproducible profiles, stable offline-first library performance and accessible controller-first workflows. Claims of better performance or reliability need comparative task evidence.

See [PRODUCT_REQUIREMENTS.md](PRODUCT_REQUIREMENTS.md) for detailed acceptance criteria. Public source availability is not an installable release, production service or universal compatibility claim.
