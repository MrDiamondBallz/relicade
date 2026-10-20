# Relicade native development foundation

Old legends. New life.

This is a buildable .NET 10 / Avalonia development foundation, not a packaged
release, full readiness, a proven launcher replacement or complete platform
acceptance. Intensive development, reference-behavior reconciliation and the
functional/platform gates in [the product requirements](../docs/PRODUCT_REQUIREMENTS.md)
remain necessary. The browser prototype and original brand masters are separate
and preserved.

## Source layout

- `Relicade.App.csproj`: shared UI, library, release, installation/launch, mods,
  controller/keyboard, kiosk, settings and recovery services.
- `lib/Relicade.Core/`: reusable release-provider and installation domain code.
- `Relicade.Desktop/`: Windows, Linux and macOS host; explicit target RIDs.
- `Relicade.Android/`: Android host, package adapters and resources. Android is not
  a desktop executable runner.
- `Relicade.Tests/`: retained baseline unit/headless tests and product-boundary,
  offline-library and artwork regressions. Test fixtures are synthetic.
- `tools/export_assets.py`: original identity/glyph exports; Pillow and
  `rsvg-convert` are needed only to regenerate committed PNG/ICO resources.

Exact source origin, license/copyright and asset boundaries are in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), [LICENSE](LICENSE), the
[Core license](lib/Relicade.Core/LICENSE) and the preserved source
[trademark notice](TRADEMARKS.md). Original names are not product branding.

## Build locally

Use the official .NET 10 SDK pinned by `global.json`, from
https://dotnet.microsoft.com/download/dotnet/10.0 . Verify the download's SHA-512
against Microsoft's official release metadata before extracting it to your own
project development directory. Do not commit SDKs, NuGet caches or outputs.

From this directory, with that SDK on `PATH`:

```sh
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1
export DOTNET_NOLOGO=1
export NUGET_PACKAGES="$HOME/.cache/relicade-dev/nuget"

dotnet publish Relicade.Desktop/Relicade.Desktop.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishTrimmed=false -m:1 -o artifacts/linux-x64

# Cross-publish only; this does not exercise Windows.
dotnet publish Relicade.Desktop/Relicade.Desktop.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishTrimmed=false -m:1 -o artifacts/win-x64
```

Restore/build concurrency is bounded by `Directory.Build.props`; keep `-m:1`.
Windows compile constants and Windows-only dependencies follow the target RID,
not a Linux cross-build host. Desktop builds do not require the Android workload;
do not build the entire solution unless the Android toolchain is installed.
Builds retain compiler/analyzer warnings; a successful publish is not a clean
security, dependency, accessibility or hardware audit.

For local tests, use an isolated `HOME`, `XDG_DATA_HOME`, `XDG_CONFIG_HOME`,
`XDG_CACHE_HOME` and `TMPDIR`. Never point a development test at an existing
library, tokens or game data. On Linux, run permission tests as an unprivileged
user or drop DAC-override capabilities; root otherwise bypasses their fixture
permissions. Headless tests need no existing desktop session.

```sh
dotnet test Relicade.Tests/Relicade.Tests.csproj -c Release -m:1 \
  --filter 'Category!=Slow&Category!=LiveArtwork'
```

The `Slow` publish-output test performs another real build and can be run
separately with `--filter Category=Slow`. OS/hardware-specific fixtures may skip
or return early on other platforms. Passing test totals do not certify those
platforms. `LiveArtwork` is an explicitly selected network smoke: set
`RELICADE_ARTWORK_SMOKE_URL` to the exact credited HTTPS source and optionally
`RELICADE_ARTWORK_SMOKE_CACHE` to private runtime storage, then run
`--filter Category=LiveArtwork`. Never commit that downloaded artwork. The
headless drawing backend uses dummy pixels; real image decoding/rendering must
also be checked with a real native renderer on an isolated display.

## Identity, data and network boundaries

Application/assembly/executable identity is Relicade. Android package identity
is `io.github.mrdiamondballz.relicade`; desktop shortcut bundles use that namespace.
Default desktop data roots are `%LOCALAPPDATA%/Relicade` on Windows,
`$XDG_DATA_HOME/Relicade` (or `~/.local/share/Relicade`) on Linux and
`~/Library/Application Support/Relicade` on macOS. Android uses its own package's
files directory. No executable/package sidecar is silently adopted, and no other
product's settings or tokens are migrated. Legacy explicit migration/recovery
helpers remain in source/tests but are not invoked by desktop startup.

Self-update, startup update hooks/auto-apply, telemetry, remote announcements and
automatic download-problem reporting are disabled. Manual self-update reports
unavailable, not up to date. No independent signed update service is configured.
No inherited telemetry token is embedded or read from the environment. Retained
update/telemetry algorithms are exercised only with explicit synthetic test
transports/hooks; those tests are not live product services.

Analytics-off is not network-off: online catalog, release-provider, package/mod
and image requests are operational traffic. The external catalog endpoints retain
their true identity. `RELICADE_CATALOG_API` can explicitly select a catalog API;
there is no fictitious Relicade backend. A catalog's verification/scan claims are
external reports, not independent Relicade authentication or a safety guarantee.
Provider tokens are optional and are now handled by OS-vault credential adapters
(Windows Credential Manager, macOS Keychain and Linux Secret Service, subject to
platform availability). There is no general-settings plaintext fallback. A
locked/unavailable vault is not reported as a missing credential or a successful
save. These adapters and synthetic fault fixtures do not certify every platform's
credential deployment; do not use sensitive tokens during development.

Legacy plaintext token fields require explicit preview and confirmation before
vault migration; source conflicts, stale previews and failed readback preserve
an error/recovery state. No silent startup import is performed. Sensitive copies
in backups need separate cleanup: the backup-cleanup draft is not merged here.

Real game imagery remains the normal path: catalog/project artwork URLs are
loaded and cached at runtime, including original-source fallback when a resized
copy fails. Genuine cached bytes survive restart/offline use. An unavailable
image gets an explicit missing-art label or local missing-art icon, not invented
game artwork or a replacement brand tile. Relicade identity/glyph resources are
original; no third-party game pixels are bundled. Image credits or public URLs
do not grant an artwork redistribution license. Browsing images can disclose an
IP to their providers. This milestone does not implement a complete product-wide
network-disabled setting; offline smoke uses external network isolation.

## Platform status and remaining acceptance

| Target | Foundation status | Still required |
| --- | --- | --- |
| Linux x64 | Self-contained publish; isolated virtual-X11 startup and local/headless fixture exercises | Packaged AppImage/Flatpak, real installs/launches, game-data requirements, recovery/security, controller/accessibility/hardware tests |
| Windows x64 | Self-contained cross-publish from Linux | Native Windows build/package/runtime, install/launch/recovery, signing, DPI/Narrator/controller acceptance |
| Linux ARM64 | Desktop RID and source retained; not built here | ARM64 build/package/runtime and hardware acceptance |
| macOS Intel/Apple Silicon | Both desktop RIDs and platform adapters retained; not built here | macOS build, signing/notarization, packaging/runtime and hardware acceptance |
| Android | Full host/package source/resources retained; workload not installed or built here | Android SDK/workload, signed APK build/package/install, permissions/input/runtime and hardware acceptance |

The local library really persists, snapshots and fails closed on corrupt JSON.
Synthetic fixtures exercise the native shell, missing/executable detection and
offline restart; detecting a path is not proving game-data readiness, compatibility
or a successful game launch. No downloaded game package is executed in this
milestone. Existing advanced services remain available in source rather than
being rewritten away, but source presence is not functional parity acceptance.

## Merged safety milestones and remaining work

Contained portable staging, persisted manifests, journaled generation replacement
and restart recovery are implemented in development source. Activation uses two
renames, not one atomic pair; power-loss guarantees remain platform-specific and
unaccepted beyond the tested boundary. Recovery refuses unknown ownership or
external changes rather than deleting them. Managed instance shortcuts can use
local metadata offline and preserve launch lifetime/child-tree authority; this is
not blanket game compatibility. The managed instance command currently restricts
supported lifetime ownership to native Linux ELF targets and refuses scripts,
AppImage runtimes, compatibility runners, unknown formats and other platforms;
this does not establish portable shortcut/runtime acceptance.

Removal guards compose explicit authority across unmanaged targets and refuse
managed generations, retained recovery history and uncertain launch receipts.
“Remove from library” keeps files; these safeguards are not managed uninstall.
Mod writes into managed portable generations are currently refused pending an
explicit compatible data/mod policy. Source-level staged mod replacement does
not establish complete game/mod rollback integration or all provider journeys.

Known release work includes owned-game-data prerequisites, complete queue and
cancellation journeys, guided retained-version recovery, compatible mod profiles,
credential/cleanup deployment checks, contained filesystem fault/security
acceptance, independent update trust, complete behavior reconciliation and every
platform/package gate. The backup-cleanup PR is separate draft work, not merged.
Do not treat this source as a production-safe installer or full readiness.

## Broader vision and documentation

Relicade aims to unite community-revived classics, retro games and emulation,
including native ports, recompilations and recreations, in coherent discovery,
setup, library, versions/mods/recovery and controller/keyboard journeys. Local
and offline use must remain account-optional. Emulation is future scope, not
implemented: emulator/core licensing, compatibility and user-supplied game/BIOS
requirements need explicit adapters and evidence. No unlicensed ROMs, BIOS or
keys are supplied, and existing native acceptance gates are unchanged.

Source-sensitive guides are subject to [documentation maintenance](../docs/DOCUMENTATION.md)
and independent review. Generated repository counts are not runtime acceptance.
