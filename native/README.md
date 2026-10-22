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

The Avalonia family is pinned consistently to the official **12.1.4** patch
release in the shared, desktop, Android and both test projects. In the published
`Avalonia.Headless` package, `HeadlessUnitTestSession.StartNew` constructs and
publishes its dispatch task before scheduling it. This fixes the 12.1.1 race
that could leave the session's readonly task null and fail ordinary async disposal.
The NuGet package identifies upstream commit
`2d79ba55fae1b1e71c69f93a668080c9029e6a31` in
[AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia/tree/2d79ba55fae1b1e71c69f93a668080c9029e6a31).
The read-only publication/completion regression checks real session dispatch and
disposal; the separate renderer must produce current-version frames rather than
inherit pixel approval from an older dependency. This patch is not Android,
physical-platform, dependency-security, accessibility or release acceptance.

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
parent test project's headless drawing backend uses dummy pixels. The separate
[CPU rendering smoke project](Relicade.Tests/RealRender/README.md) exercises actual
Skia pixels with synthetic state in its own process; never load both test assemblies
in one process. That controlled headless check does not replace real native
rendering, image decoding and hardware acceptance on an isolated display.

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
in old originals, backups, rescue files and filesystem/OS remnants remain
sensitive: redundant portable payload cleanup does not scrub credentials or
provide secure erasure. Their separate privacy/retention work remains open.

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
platform/package gate. Redundant payload cleanup does not close those gates.
Do not treat this source as a production-safe installer or full readiness.

## Restore previous portable version — bounded development source

Open a library tile's context menu in list, grid or compact grid and choose
**Restore previous portable version…**. The dialog captures the exact instance,
shows current and retained version/operation identities, target executable,
CURRENT declared data paths, limits and full plan SHA-256. Cancel is the initial
and default answer. Use keyboard/controller inspection targets to reach all text;
Confirm on an inspection target is inert. **Restore displayed plan** is a separate
explicit action; stale state needs a fresh preview AND new confirmation.

This works offline only for the immediate complete validated previous portable
generation. Payload only; current saves/data retained; compatibility not guaranteed;
external/undeclared data not covered. Previous saves are never restored. No
download, arbitrary-version fallback, package rollback, compatible mod activation,
uninstall or general retention is provided. Unknown/pending/corrupt/linked/shared
or uncertain launch/mod history refuses without repair. Settings/library evidence
is read without Load/Save, credential initialization or live object replacement.

Hashing/copying runs off the UI thread. Cancel requests a safe stop, not an
immediate SLA; activation after durable Applying finishes noninterruptibly.
Closing a session revokes UI participation and drains work before dependencies
dispose. Success means an actual complete committed manifest was observed, even
if completion reporting raced an error/cancel. Otherwise the result is incomplete
or unknown and requires inspection. The existing transaction can attempt internal
recovery during the confirmed operation; errors do not certify unchanged state
or safe retry. Preview, result observation and reopening initiate no new recovery,
repair, retry or resume. This is included source, not a packaged/native-platform,
power-loss, hardware controller or accessibility acceptance claim. Existing
library-menu prefetch is separate from the rollback operation's network silence.

## Explicit redundant-backup payload cleanup — bounded development source

The source includes opt-in cleanup of duplicate regular immutable payload files
from older terminal committed portable operations' `backup/` directories, only
after verifying identical retained files in the same operations' `previous/`
generations. All previous generations, data snapshots, journals and the current
operation's entire backup remain. Declared data, saves/configuration, metadata
and unknown/changed evidence are not cleanup candidates. This is not managed
uninstall, deletion of previous generations, general retention or credential
erasure, and does not certify any native platform.

### Native library preview and confirmation

In this source, open a library tile's context menu in list, grid or compact grid
and choose **Clean redundant backups…**. This UI is included in this source, not
packaged or native-platform accepted. It captures that exact instance, hashes off the UI thread, and shows
every eligible relative backup/retained path, file length, total bytes and full
plan SHA-256. Cancel has initial/default focus; an empty plan cannot be applied.
Use keyboard/controller navigation to inspect every file row, including long
plans. Long file descriptions have separately focusable continuation parts;
Up/Down exposes each part even when the complete path exceeds one viewport.
No path characters are omitted or replaced with ellipsis. Confirming an
inspection part is inert: it neither cleans nor closes.
Only **Clean displayed plan** confirms that displayed hash. A changed plan,
library, settings or authority requires **Preview again** and a fresh confirmation.

While applying, Cancel requests a stop at a safe boundary; it is not rollback
or an immediate hashing/cancellation guarantee. An interrupted result displays
actual files/bytes removed **this invocation**, not planned totals. Resolve the
cause, then explicitly choose **Resume approved plan** for the same hash; resume
revalidates recovery, kiosk, library and filesystem authority. Nothing resumes
automatically. Closing or replacing the session revokes UI participation while
registered work drains before dependencies are released.

Locked kiosk and settings-recovery sessions cannot start cleanup; the service
also checks the manager's own injected recovery state, not only menu visibility.
Unsupported/manual/package/ambiguous instances show refusal, not uninstall.
Persisted settings are read into a detached nonsecret object without replacing
live settings or initializing credentials. An uninitialized store or custom store
without safe persisted-evidence support refuses cleanup; no mutating Load fallback
is attempted. Effective values and persisted recovery/kiosk policy remain distinct.
Cleanup adds no catalog/update/save/network operation. The existing library-menu
release prefetch is separate and may already contact a provider: this is not a
product-wide no-network claim. Desktop/headless tests do not accept Android,
real controllers, native accessibility or other platform/runtime behavior.

### Local command-line route

Use the exact case-sensitive library `InstanceKey`, not a display name or
repository name. With your own development build (not an available release):

```sh
dotnet Relicade.Desktop/bin/Release/net10.0/Relicade.Desktop.dll --preview-portable-backups <InstanceKey>
dotnet Relicade.Desktop/bin/Release/net10.0/Relicade.Desktop.dll --cleanup-portable-backups <InstanceKey> --confirm <planSha256>
```

Replace the placeholders; `planSha256` is the exact lower-case SHA-256 printed by
the preview, not an authentication signature. Inspect its relative file list and
reclaimable bytes before confirming. A stale preview, ambiguous root, changed
library/settings, pending recovery, running/unknown launch evidence, unavailable
writer authority or invalid file evidence refuses cleanup rather than recovering
or guessing. The cleanup-only route bypasses normal header/catalog/update/status
initialization; duplicate, mixed or extra arguments are errors.

A durable local approval receipt precedes deletion. After interruption, resolve
the reported problem and explicitly resume the same approved plan; there is no
automatic startup cleanup. Completion requires preserved-state readback and all
approved targets absent. Results count actual files/bytes removed this invocation,
not previously absent copies. Deleted redundant copies are not rolled back.
Arbitrary hostile OS writers, physical power loss/disk exhaustion, native Windows
console/handle behavior and other platform/runtime guarantees remain unaccepted;
Linux/headless fixtures and injected console tests do not replace those checks.

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
