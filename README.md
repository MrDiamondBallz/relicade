<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="brand/readme-lockup-dark.svg">
  <img src="brand/readme-lockup-light.svg" alt="Relicade" width="420">
</picture>

### Old legends. New life.

**A new home for games that deserve another lifetime.**

[Preview](#explore-locally) · [Product direction](docs/PRODUCT_REQUIREMENTS.md) · [Roadmap](docs/ROADMAP.md) · [Contributing](CONTRIBUTING.md)

</div>

---

## The vision

Relicade is becoming one coherent home for **community-revived classics, retro games and emulation**—including native ports, recompilations and recreations. Discover the community project behind a favourite game, understand its setup and compatibility, and keep discovery, library and play together.

The goal is a clear path from “What can I play?” to the right version, game files, mods and recovery options. Controller-first and keyboard-complete journeys should make power features accessible, not remove them. Local and offline library use must remain possible without a required account.

Emulation is **future scope, not yet implemented**. Each emulator/core needs its own licensing and compatibility review; players must supply any required game data or BIOS lawfully. Relicade will not distribute unlicensed ROMs, BIOS or keys. This direction does not waive or silently expand the existing native release gates.

## Current progress

**No packaged launcher release.** This is development source and a browser design preview, not full functional parity or accepted native platform support.

- **Merged development source:** reviewed staged installation and recovery, explicit vault-backed credential migration, offline instance shortcuts and process lifetime handling, and managed-removal safeguards. These are bounded source milestones, not a production-safe installer or a promise of compatibility with every game. See [the merged source milestone](https://github.com/MrDiamondBallz/relicade/pull/1) and the [native guide](native/README.md).
- **In progress:** [backup-cleanup work](https://github.com/MrDiamondBallz/relicade/pull/3) is a separate draft, not merged functionality. Full behavior reconciliation, packaged installation, security/rights checks and real platform/controller/accessibility acceptance remain open.
- **Longer term:** the broader retro-game and emulation experience is not yet implemented. Versions, mods, owned-data setup and recovery need complete tested player journeys, not just service code.

The browser preview supports a bounded selection of real public project metadata, search, filters, project details, keyboard navigation and browser-local saved-library bookmarks. Saved projects are not installed games. It does not install, update, verify or launch executables.

The [native development source](native/README.md) uses .NET 10/Avalonia. Linux x64 self-contained publish and isolated startup, plus Windows x64 cross-publish, are development checks only. The [coverage contract](docs/FEATURE_COVERAGE.md) keeps native/package acceptance separate.

<!-- docs-sync:begin facts -->
Repository facts (not acceptance results): **12 catalog projects**, **71 required feature rows**, **8 platform/package targets** and **23 synthetic fixture scenarios**. Counts describe repository inputs, not working games or passing tests.

Source status: merged development source includes [reviewed safety work](https://github.com/MrDiamondBallz/relicade/pull/1); [backup cleanup](https://github.com/MrDiamondBallz/relicade/pull/3) remains a draft, not merged functionality. No packaged release, full-parity acceptance or native-platform acceptance is recorded here. Emulation is planned, not implemented.
<!-- docs-sync:end facts -->

[How documentation stays current](docs/DOCUMENTATION.md): contribution-time checks and periodic maintainer reconciliation keep generated facts and source-sensitive prose under review. The tool checks facts mechanically; it does not approve prose. Cloning the repository does not install a timer or service.

### Interface preview

![Relicade interface preview featuring Ocarina of Time artwork](docs/assets/relicade-interface-ocarina.png?v=single-r)

*Browser-rendered interface design preview with Ocarina of Time artwork inside the hero. This illustrates the visual design, not a native launcher release or installed-game state. Third-party artwork remains with its respective rights holders; see [NOTICE.md](NOTICE.md).*

[Identity system](brand/BRAND.md) · [Brand presentation](docs/assets/relicade-brand.png) · [Architecture](docs/ARCHITECTURE.md)

## Explore locally

Requires **Python 3.10+**. The preview itself needs no package installation, account or build step.

```sh
python tools/preview.py
```

- Product preview: <http://127.0.0.1:8766/prototype/>
- Brand presentation: <http://127.0.0.1:8766/brand/>

The server binds only to loopback, serves only the preview/brand directories, rejects directory listings and applies a restrictive content security policy. Stop it with **Ctrl+C**. Clearing browser site storage removes saved preview bookmarks.

Run the server regressions:

```sh
python -m unittest discover -s tests -v
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full developer suite and pinned dependencies. Hosted CI is not active yet; the [workflow template](docs/ci/prototype-quality.yml) is supplied without a misleading passing-build badge.

## Platform targets

These are **release requirements, not available downloads**:

- **Windows:** x64.
- **Linux:** x64 and ARM64; AppImage and Flatpak packaging.
- **macOS:** Intel x64 and Apple Silicon ARM64.
- **Android:** APK, with mobile-specific permissions, input and application launching.

Every platform needs its own package and runtime evidence. Shared-code compilation or browser emulation does not establish native compatibility. Desktop executable launching is not an Android capability.

## What comes next

The [roadmap](docs/ROADMAP.md) prioritizes real native library and readiness flows, durable downloads, recoverable upgrades, reproducible mod profiles and controller-first navigation. These remain requirements until implementation and failure-path tests pass.

## Project structure

- `brand/` — original identity masters, usage guide, tokens and licensed fonts.
- `prototype/` — browser preview, public metadata snapshot and original project tiles.
- `native/` — shared native code, desktop/Android hosts, retained license notices and native tests; see its build instructions and limitations.
- `docs/` — product requirements, architecture, roadmap and CI template.
- `tools/` — local preview and browser QA utilities.
- `tests/` — repeatable regression checks.

## Credits and rights

Community projects, game names and external metadata belong to their respective creators and rights holders. Source links and exact provenance remain available; a catalog listing is not a download license or safety endorsement. This repository contains no ROMs, BIOS, commercial game data, user credentials or copied installed executables.

See [NOTICE.md](NOTICE.md) for third-party credits and font licenses. Inherited native source retains its applicable MIT notices. No open-source license has been selected for separately owned original Relicade material; source visibility alone is not a reuse grant.
