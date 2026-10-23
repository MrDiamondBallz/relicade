# Relicade native development foundation

Old legends. New life.

This is a buildable .NET 10 / Avalonia development foundation, not a packaged
release, full readiness, a complete player experience or complete platform
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
  `rsvg-convert` regenerate committed PNG/ICO resources and are also required by
  the Python native-branding regression that compares those exports to the SVG masters.

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
export AVALONIA_TELEMETRY_OPTOUT=1
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1
export DOTNET_NOLOGO=1
export NUGET_PACKAGES="$HOME/.cache/relicade-dev/nuget"

dotnet publish Relicade.Desktop/Relicade.Desktop.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishTrimmed=false -m:1 -o artifacts/linux-x64

# Cross-publish only; this does not exercise Windows.
dotnet publish Relicade.Desktop/Relicade.Desktop.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishTrimmed=false -m:1 -o artifacts/win-x64
```

The Linux x64 [runtime notice inventory](../docs/NATIVE_PACKAGE_CONTENTS.md)
adds version-bound vendor notices and a read-only publish-content check. It does
not establish distribution rights or platform support. Other targets retain
separate unresolved package/notice obligations.

Keep both opt-outs set before restore, build, test and publish. The official
Avalonia.BuildServices package documents `AVALONIA_TELEMETRY_OPTOUT=1` for its
independent build-time telemetry; the .NET variable alone does not disable it.
These are build-process controls, not a network-disabled runtime setting or
proof that earlier builds made no requests. Relicade's runtime privacy and
network-isolation requirements remain separate.

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

The unavailable-artwork UI fixture installs its fake loader before assigning an
image source and waits for the observed loading transition, without external
artwork traffic or a fixed timing delay. This is a synthetic UI check only.

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

Library cards can retain a separate catalog header/capsule reference, bound to
the exact catalog entry and original project icon. Custom local images and
player-edited icons stay in control. Add checks existing cache only; later
session-owned enrichment uses the shared image loader and falls back to the icon
when the cover is unavailable. All three layouts show source attribution in the
image tooltip. The initial static SteamGridDB URL subset is not full artwork
coverage, a rights grant or product-wide offline-network acceptance. See
[artwork boundaries](../docs/ARTWORK.md#native-library-cover-references).

## Native Discover presentation

Discover uses the established orange action accent, readable theme-aware text
and a cover-forward grid beneath “Find your next replay.” The wider cards retain
the existing catalog image binding, project/platform labels, release and feedback
status, Add/Open actions, source selection, search and every filter. Narrow
desktop windows use a single readable card column; short windows keep controls
inside the scrolling surface. Player theme preferences and the complete product
wordmark remain unchanged.

Keyboard/controller hints wrap in a reserved footer row instead of clipping at
short widths. Feedback verdicts retain their explicit wording and select a
readable colored tone against the card background, with a neutral fallback for
custom palettes. These checks do not establish full accessibility compliance.

This is a bounded native presentation step, not a finished visual system or proof
that every catalog image is available or licensed. Rendering fixtures use clearly
named synthetic projects and procedural test pixels, never invented game art.
The existing artwork source, cache and privacy boundaries still apply.

## Platform status and remaining acceptance

| Target | Foundation status | Still required |
| --- | --- | --- |
| Linux x64 | Self-contained publish; private AppDir/AppImage recipe; isolated virtual-X11 startup and local/headless fixture exercises | Released AppImage/Flatpak, real installs/launches, game-data requirements, recovery/security, controller/accessibility/hardware tests |
| Windows x64 | Self-contained cross-publish from Linux | Native Windows build/package/runtime, install/launch/recovery, signing, DPI/Narrator/controller acceptance |
| Linux ARM64 | Desktop RID and source retained; not built here | ARM64 build/package/runtime and hardware acceptance |
| macOS Intel/Apple Silicon | Both desktop RIDs and platform adapters retained; not built here | macOS build, signing/notarization, packaging/runtime and hardware acceptance |
| Android | Full host/package source/resources retained; workload not installed or built here | Android SDK/workload, signed APK build/package/install, permissions/input/runtime and hardware acceptance |

The local library really persists, snapshots and fails closed on corrupt JSON.
Synthetic fixtures exercise the native shell, missing/executable detection and
offline restart; detecting a path is not proving game-data readiness, compatibility
or a successful game launch. No downloaded game package is executed in this
milestone. Existing advanced services remain available in source rather than
being rewritten away, but source presence is not functional acceptance.

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

ZIP staging checks the complete declared expanded size against observed available
space before opening any payload output. Entry/expanded-byte safety budgets are
charged once, including nested extraction; insufficient space reports required
and observed available bytes. TAR and single-file staging retain their incremental
checks. This is a staging observation, not a disk reservation, complete
cache/backup/install-peak estimate or protection against concurrent external
writers. Actual write failures still use the existing transaction failure path;
physical disk-exhaustion and power-loss acceptance remain open.

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

## Catalog marker files

Catalog `filesToAdd` entries are requests for empty marker files, not game-data
or BIOS prerequisites. Preparation uses atomic create-new operations and never
truncates existing files. Linked install roots or marker paths are refused;
platform-ambiguous names and launcher-reserved metadata names are excluded.
This is not protection against a hostile process concurrently replacing parent
directories, and it does not establish game readiness.

Removing a name from metadata no longer deletes its existing file, even when it
is empty. Legacy metadata does not prove ownership. Best-effort preparation
returns the preserved names; strict entry preparation reports them before
creating any new markers, for both hosted and manually managed entry edits.
Manual folder preparation checks existing installation metadata first and performs
marker preflight before creating instructions. Instruction files are also created
without overwriting existing content. Review retained files before removing them yourself.
A failed later creation may leave earlier newly created empty markers in place;
preparation is not an all-or-nothing transaction. Automatic owned-marker cleanup
remains unimplemented until ownership and recovery can be established safely.

## Recent-play history

A failed ordinary desktop process-creation attempt leaves existing last-played
history unchanged and does not create a new recent-play record. History is
recorded after a non-null process is returned, while the initial portable writer
lease is still held and before lifetime tracking can release it. A started process is not evidence that a game reached its
menu or played successfully; game-specific readiness and native-platform checks
remain separate. The launch failure still reports an error, and this ordering
change does not relax recovery or process-lifetime authority.

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

After a confirmed restore, the same unchanged Library entry receives the observed
version, executable and package identity without a status reload or network check.
A separate nonrecovering, read-only generation check holds writer authority until
the Library update finishes; a newer generation, even with identical version
labels, prevents stale publication. This work stays off the UI thread.
Cached game-data readiness is cleared: use **Finish game setup** to revalidate
retained files before Play. An entry changed or replaced while restoration runs is
not overwritten, and closing the dialog prevents late updates. A committed disk
restore remains a success even when that later Library update is refused.

Hashing/copying runs off the UI thread. Cancel requests a safe stop, not an
immediate SLA; activation after durable Applying finishes noninterruptibly.
Closing a session revokes UI participation and drains work before dependencies
dispose. Success means an actual complete committed manifest was observed, even
if completion reporting raced an error/cancel. Otherwise the result is incomplete
or unknown and requires inspection. The existing transaction can attempt internal
recovery during the confirmed operation; errors do not certify unchanged state
or safe retry. Preview, result observation and reopening initiate no new recovery,
repair, retry or resume. This is included source, not a packaged/native-platform,
power-loss, hardware controller or accessibility acceptance claim. Opening the
Library maintenance menu does not prefetch releases; the explicit Change version
action still fetches releases when needed.

## Read-only preservation inspection — not managed uninstall

For a supported installed portable entry, **Inspect preservation…** in the
Library menu opens an explicit local assessment. **Refresh inspection** observes
again; **Previous** and **Next** expose the complete bounded result. Closing
cancels participation and drains pending observation. There is no delete, repair,
settings-save, download or recovery action in this journey. Managed uninstall
remains refused; removing an entry from the Library is a separate operation.

The observer binds the raw committed manifest to its exact committed journal,
then compares only its listed regular payload files with observed size and hash.
Changed, unlisted, linked, unreadable or uncertain files never become removal
candidates. Declared current data, transaction history and unknown files are
shown separately and remain in place. Original game files, saves, canonical
owned-data stores and mod state outside the inspected roots are not exhaustively
located or verified; absence from this result does not mean absence on disk.

Existing transaction authority is held through guarded result publication.
Generation/context changes refuse stale publication; bounded file/directory
identity rechecks mark detected changes uncertain. This is a timestamped
observation, not a freeze against external writers, deletion authorization or
reclaimable-space estimate. Metadata is capped at 2 MiB, traversal at 4,096
entries/64 levels, and payload hashing at 1 GiB. Reaching a limit reports partial
results and excludes unknown sizes from totals, rather than claiming a complete
inventory. Busy, conflicting or invalid metadata is preserved and refused, not
repaired. These resource limits do not define supported library size.

Synthetic tests cover known/changed/extra/data/history classifications, bounded
observations, detected changes, authority/cancellation, the actual menu/dialog
and unchanged explicit version fetching. They do not accept destructive
uninstall, hostile-writer safety, real saves, every filesystem or a platform
release.

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

## Managed installation entry edits

Entry edits coordinate with portable installation writers before changing library
metadata. Folder, source, management-mode and setup changes require conservative
unmanaged authority over both original and destination roots. Managed history,
unknown transaction state and any launch receipt, including uncertain or stale
receipts, prevent these changes. The check does not recover operations or remove
receipts. Enclosing managed roots and active ancestor writers are checked before
creating child history, including for linked paths inside another installation.

Display names, project labels, artwork and tags remain editable at the actual
installation root without marker or instruction preparation. This presentation-only
path retains the root's own recovery and launch records. Both authority scopes
span metadata commit and compensation; only setup edits may prepare files.

Managed migration remains unsupported and unaccepted. It needs a recoverable
transaction spanning root identities, manifests, retained versions/history, data,
settings and library metadata, with interruption and running-game coverage.
Hostile concurrent directory replacement and platform certification remain
separate obligations.

Linked installation paths use their actual root for presentation-edit authority.
Changing a linked entry’s folder name is refused rather than moving an unrelated
default-folder path, even when the explicit path currently equals that default.
Linked-root migration remains unsupported.

## Local game-data setup

The native library includes a package-bound **Finish game setup** flow for the
specified Perfect Dark US 1.1 Linux x64 build. Its first installation selects and
verifies the pinned US executable before activation, without overriding existing
launch selections on updates. It previews and confirms a local
copy of lawful user-supplied data, preserves originals and retained copies, and
revalidates data on the shared launch path. Installation and game-data readiness
are separate. Unknown package variants remain unconfirmed; this is not gameplay,
platform or complete catalog acceptance. See [supported input, provenance and
recovery limits](../docs/GAME_SETUP.md).

For these supported package-bound adapters, **Use retained game files…** provides
an explicit reuse preview after a compatible generation change. Separate lawful-use
confirmation adds only the current setup receipt; no original is needed, no game
bytes are copied, and earlier receipts are preserved. Missing or conflicting
writer/data evidence refuses reuse. Readiness publication remains guarded by both
installation and data-store authority; cancellation can retain a receipt without
marking the entry ready. The ordinary original-file chooser and launch checks
remain unchanged. See [retained-file reuse](../docs/GAME_SETUP.md#reuse-retained-game-files-after-a-compatible-generation-change).

A further compiled definition covers z2rs v0.6.0 Linux x64 with a lawful Zelda II
USA dump, headered or headerless. Its new-entry **Set up game** action first
offers the pinned engine installation with progress/cancel, then opens the
separately confirmed local-file import. No ROM download or automatic play is
performed, and an existing engine is not replaced. It preserves the whole original, validates the
upstream body hashes and binds the retained copy by full-file SHA-256 and length.
Launch disables Discord presence; the engine may remember the ROM path while
retaining its existing save locations. Linked/manual entries remain unchanged.
See [the exact package and input contract](../docs/GAME_SETUP.md#z2rs-zelda-ii-usa-setup).
This is source-stage setup, not gameplay or general emulation acceptance.

New dedicated Perfect Dark Library entries on Linux x64 can choose **Set up game**
to confirm the exact reviewed engine before the separate local `.z64` preview and
copy confirmation. The canonical and former repository names use the same pinned
package; a changed rolling release is refused rather than substituted. Other
hosts retain ordinary download controls. Engine installation alone is not
owned-data readiness, and this flow never autoplays. Existing files, versions and
pending work retain their first-install refusal; cancellation drains active work.
These are synthetic source checks, not official-package execution or gameplay.

## Portable launch-file choice

The desktop installer asks which staged launch file to use when a portable package
has several unresolved candidates. Relative paths distinguish the game from tools;
**Install** confirms the choice without running it. Cancellation preserves the
prior installation, and background/headless operations decline unresolved choices
without prompting. Existing valid selections and verified package defaults remain
in effect. Staged bytes are checked again before activation, and package-supplied
transaction lock entries are rejected without opening them. This is development
source with synthetic UI/transaction evidence, not game or platform acceptance.
See the [setup guide](../docs/GAME_SETUP.md) for the separate game-data step.

## From project details to your library

An app already in your local library has a separate **Open in Library** action
on its details page. Adding an app changes the primary action to that same route,
which selects the actual local entry so installation and setup remain in the
library. **Remove from library** stays a separate action; opening an entry does
not install or launch it. A missing or removed entry cannot use a stale navigation
action. If scope, tags or search hide the selected entry, a temporary reveal keeps
that one entry visible alongside normal results without saving different filters.
A visible notice explains the exception. Changing search/filters or switching to
Browse restores the filtered view. This route has local synthetic native
keyboard/controller coverage, not physical-device acceptance.


## Catalog pagination

Browse follows continuation cursors rather than assuming a short or empty page
ends the catalog. Cursor identity follows the value actually sent by the client,
including its boundary-whitespace trimming, without folding case or interior
content. A repeated or cyclic cursor stops further requests and leaves
already received cards visible with a reload diagnostic. Reload starts a fresh
query; ordinary network failures and cancellation retain the next-page retry.
There is no fixed catalog-size or page-count cutoff. This protects discovery
progress, not metadata authenticity, installation or gameplay acceptance.

### Release-history completeness

Release checks follow every declared history continuation before replacing the
last complete in-memory history. Empty or short pages do not end a history when
the server declares another page. History cursors retain their exact opaque value;
repeated or cyclic cursors, missing continuation/completion fields and malformed
responses produce an incomplete-read diagnostic rather than partial approval.
There is no fixed page-count cutoff. Reads have a two-minute overall deadline,
the existing per-request timeout, and a shared 64 MiB metadata-body budget; hitting
a bound refuses the incomplete read instead of silently truncating it.

Exact source tags, including case and `+build` labels, release IDs and existing
file metadata remain available. An exact requested tag takes precedence over a
legacy numeric or `v`-prefix alias. Conflicting records for one requested tag do
not lend it an arbitrary checksum or automatic approval, and known per-file flags
remain visible. Ambiguous aliases do not choose between distinct source tags.
Numeric release ordering remains unchanged. Explicit pin and cache-target binding
is documented under [explicit build-version pins](#explicit-build-version-pins).

A failed or canceled continuation retains the previous complete version-menu
history. Blocks actually observed in an incomplete read are kept separately as
denial-only evidence for that catalog app and version. They cannot supply an
approval, download or version-menu row. A valid complete refresh supersedes that
session evidence. Existing status-only offline checks still work when no history
page was received and no known block contradicts the status. This is not a durable
block database, authenticated metadata or a guarantee of current catalog status:
the normal ten-minute complete-history cache and thirty-minute status cache still
apply, and observed denial-only evidence does not survive an application restart.

## Selected download integrity

Catalog release approval does not replace a selected file's integrity check.
The native flow validates SHA-256 syntax, uses a valid publisher digest when the
catalog digest is absent or malformed, and asks before an interactive catalog
install without either digest. Automatic/headless flows decline that unverified
case. A mismatch preserves the previous installation; a malformed digest cannot
invent a same-tag update. See [download integrity](../docs/GAME_SETUP.md#download-integrity-and-catalog-approval)
for the boundaries. A matching digest is not a safety or gameplay certification.


Catalog identity is saved as part of Add, without waiting for enrichment. Missing
status metadata cannot silently turn that entry into an independent repository:
interactive installs explain unconfirmed identity, while automatic installs wait.
An absent explicit catalog ID cannot borrow a sibling entry's release approval.
Exact cached links still work; this is not a complete catalog migration or metadata
authentication system. A link learned in this session is retained across empty
refreshes and checked after failures, without silently migrating legacy saved IDs.

Background release-status and app-link refreshes detect repeated continuation
cursors using each endpoint's transmitted identity. A failed partial traversal
does not replace that feed's last complete cached metadata; the existing retry
backoff remains. Cancellation drains the operation and releases its refresh
lock, so a later valid read can proceed. The two feeds remain independent, not
an atomic combined snapshot. Short or empty pages with advancing cursors remain
valid; this is not metadata authentication or full catalog acceptance.

The optional **Play Freedoom Phase 2…** library action is a second package-bound
setup path, for UZDoom 5.0.3 Linux x64 and Freedoom 0.13.0 Phase 2 only. Players
can explicitly download the pinned official free-data archive or choose an
existing `.wad`, then review and confirm a separate copy before Play. Verified
original downloads and notices are retained and reusable; cancellation drains the
request without starting a game. Normal engine Launch and saves are unchanged.
New Linux x64 entries can first choose **Set up Freedoom Phase 2…** and confirm
the exact engine package. This guarded first installation never replaces an
existing version or retained data, never falls back to an unreviewed package, and
opens the separate data setup only after success. Its confirmation/progress window
can cancel just this setup and waits for owned transfer/install work to drain.
The supported guided-engine window reports measured received bytes and a transfer
percentage only when a usable total is known. Unknown totals stay indeterminate;
verification, preparation and safe activation are separate stages, not download
percentages. Receiving 100% does not mean verification or installation succeeded.
The window follows the launcher's light/dark theme and interface scale.
Late cancellation preserves a committed engine and reports that state; a pending
operation cannot be retried through the same window. No unattended download or
automatic Play is implied. See
[the exact package, data and rights boundaries](../docs/GAME_SETUP.md#optional-freedoom-phase-2).
This is development source, not tested gameplay or accepted platform support.

If a previously recorded imported copy changes, the setup dialog can show a
bounded repair preview. Explicit confirmation preserves the changed bytes in a
separate retained backup before replacing the active copy with verified data.
Missing or conflicting receipts do not authorize replacement; errors can leave
a committed backup or replacement and require a fresh inspection. See the
[recovery limits](../docs/GAME_SETUP.md#cancellation-recovery-and-limits).

Project details offer an explicit **Retry release notes** action after a transient
catalog or repository read failure. The current project stays open, duplicate
in-flight reads are suppressed, and cancelled or superseded requests cannot
publish old notes into a different page. Successfully loaded or empty histories
are retained; missing or removed repositories do not become retry loops. This
is a read-only recovery path, not a release download or installation action.

## Explicit build-version pins

An explicit version containing `+build` metadata is an exact source-tag target,
not just a numeric version. Different build labels, case or missing labels cannot
satisfy that pin or lend it cached download assets. The pin survives library
save/load, selected-release refresh and installation until the player uses the
existing **Back to latest updates** or **Back to verified updates** action.
Repository selection prefers an exact tag; an absent target cannot silently pick
another build, and an ambiguous legacy alias is not a selection. Warm-cache reuse
also checks that the cached version and attached release describe the same target;
inconsistent metadata is retained but fetched again rather than attached to the
card. Conditional endpoint responses reuse only their own cached payload.

**Reinstall** selects the known installed source tag, and **Install Unverified**
selects the displayed requested tag, using the same exact-first target rule.
A missing or ambiguous known target does not fall back to another release;
the player can use **Change Version** to choose another target explicitly.
Existing unknown-version sentinels retain the followed-release fallback only
when no exact or unique legacy release matches them. An explicit `+build` tag
remains authoritative even with a zero numeric base.
The selected release still undergoes its normal integrity and consent checks.

**Change Version** keeps source-catalog checks separate for exact, case-distinct
release tags, both when first loaded and when shown from the session cache.
Latest, Installed and Preferred markers use exact-first, unique-legacy-alias
selection; an ambiguous or absent target does not label a different release.
Conflicting history records remain conservatively marked, and these source
labels do not replace integrity checks or establish game readiness.
[Native-menu identity tests](Relicade.Tests/ExactVersionMenuTests.cs) exercise
fresh and cached states, conflicting history and exact/legacy marker selection
with synthetic metadata, without downloading or running a game.

Ordinary numeric and `v`-prefix aliases retain their existing behavior. Numeric
version ordering and general installed-version equivalence are unchanged; this
is target-selection and cache binding, not a complete update-ordering redesign or
runtime compatibility claim. Preference setters and cache validation do not
modify game files.

The version menu saves a new preference only after the installation service
confirms the chosen release completed, or validates that exact release is already
installed, while the library session is still open. Declines, cancellation and
unconfirmed failures retain the live preference in memory and in the saved library.
Temporary selection metadata does not retire an existing numeric pin; explicit
preference edits made during the operation are not restored to an older snapshot
on failure. Ordinary numeric-pin retirement outside selection is unchanged.
A failure after activation may
still leave the new generation installed: the library reconciles actual files
without interpreting a missing completion result as a rollback.

**Update Now** and automatic updates count a completed update and clear its old
skipped-version preference only after an acknowledged installation in the live
session. Refusals, failed transfers and cancellation keep the existing preferences;
late activation is reconciled without pretending it rolled back. A separate
preference edit made during a transfer is not replaced with the action's old state.
[Update outcome tests](Relicade.Tests/UpdateOutcomeTests.cs) exercise these paths
with synthetic packages, including successful installation, already-current
versions, missing digests and interruption after activation.

A rolling release can publish new package bytes under the same tag. **Update Now**
does not acknowledge that tag as already current when the retained installed
package digest and the matching asset's usable published SHA-256 identify a new
build. It continues through the normal download, integrity and activation checks.
Missing or malformed installed receipts and published digests do not establish a
rebuild; unknown receipt bytes are preserved. This does not change general version
equivalence or establish publisher authenticity or game readiness.
[Release verification tests](Relicade.Tests/ReleaseVerificationTests.cs) cover actual
interactive and automatic update callers, successful activation and cancellation,
while [digest tests](Relicade.Tests/PublishedAssetDigestTests.cs) cover malformed
and case-normalized hashes using synthetic inputs.

If another completed installation has already made that rolling package current,
the no-download result refreshes the cached receipt but does not count a new update
or clear saved version preferences. Explicit version selection can still acknowledge
an already-current target without downloading it again. MSI completion uses the
installer’s reported result: a retained same-tag receipt after cancellation is not
a completed installation and does not replace the recorded package checksum.

## Private Linux packaging recipe

A [Linux x64 development recipe](../docs/LINUX_APPIMAGE_DEVELOPMENT.md) now assembles
an AppDir and an unsigned AppImage from the checked directory publish, using
explicitly pinned local build tools. Existing output paths are not replaced.
This is separate from supported-platform and release acceptance: actual desktop,
FUSE, installation/upgrade, gameplay and AppImage-runtime rights checks remain
open. It does not provide Flatpak or ARM64 packaging acceptance.

### One reviewed local game portrait

Taisei Project's catalog card, app details header and saved library entry can display a bounded,
credited afensorm character portrait without an image-provider request. This is
real licensed game artwork, not a gameplay screenshot or proof the game is
installed. Custom player images retain priority. See [artwork provenance and
limitations](../docs/ARTWORK.md#reviewed-taisei-portrait). Other games continue to
use their eligible catalog imagery or honest icon fallback.

### Returning from Settings

Closing Settings with its sidebar button restores the main navigation zone and
keyboard/controller hints, just as the in-panel Close action does. Repeated
open/close interactions also ignore an opening focus callback that arrives after
the panel has closed. This source-level interaction check does not certify
physical controllers or platform accessibility APIs.

### Game-file guidance before installation

A project's Discover Overview now separates known original-game-file requirements,
optional free-data setup and a complete-game package from unreviewed requirements.
Known advice states its package/platform limits and offers an explicit Setup guide
link. It does not start a download, change launch eligibility or treat installation
as gameplay acceptance. See [Before you play](../docs/GAME_SETUP.md#before-you-play).

The guide interaction tests inject the host URL opener as well as HTTP transport.
They verify the actual keyboard/controller action and requested destination
without invoking a desktop browser. The normal host still uses the operating-system
URL launcher; headless interaction results do not certify that external handoff.

### Guided first-install intent

The compiled Linux x64 engine-setup paths retain exact attempt identity and stage
before payload transfer. Reopening the confirmation only inspects local history;
**Retry engine install** starts a separately confirmed fresh request after current
context/source checks. Unknown records and commit uncertainty are retained and
refused, not repaired or retried automatically. Engine completion is separate from
owned-data readiness. See [the setup contract](../docs/GAME_SETUP.md#interrupted-guided-engine-setup);
this is not a generic durable queue or packaged/runtime acceptance.

`GuidedInstallIntentProcessTests` verifies two separate-process boundaries using
only authored synthetic payloads and injected HTTP. Each parent case receives an
exact persisted-stage stdout handshake, terminates only its own worker, and drains
it before starting a separate inspector and explicit retry process. Inspection
makes zero HTTP requests and leaves application-data paths, bytes and modification
times unchanged; retry checks actual committed engine bytes and retained history.
Coordination receipts and fixture inputs are retained, not silently deleted.
These are Resolving/Downloading precommit checks, not power-loss certification or
whole-desktop startup acceptance. No acquired engine is executed.

The Library's saved-setup notice inspects supported currently loaded entries,
including entries hidden by filters. Its read-only review offers explicit bounded
continuation and **Show in Library**, not a transfer or recovery action. Unknown
records remain visible refusals; context changes or closure reject stale results
and pending reads drain. Neither a saved stage nor this notice grants readiness.
See [finding saved setup history](../docs/GAME_SETUP.md#finding-saved-setup-history).

## Loaded library metadata export

In **Library → Export metadata…**, review the disclosure, entry count and exact
JSON pages, then choose a **new** file. This exports the successfully loaded
saved-entry set, including entries hidden by search or filters. It is an
**export-only metadata list**, not a backup, importer or restore feature.

Version 1 records the provider/repository, logical folder key, base name/project,
advisory catalog ID, preferred/skipped release and asset filter where applicable.
Manual entries do not claim hosted releases and would need relinking. Multiple
instances may share a catalog ID; ambiguous folder or hosted repository/filter
selections and conflicting catalog origins refuse export rather than silently
merging records. All declared JSON fields are required, even when their value is
null. Unknown versions, fields and duplicate properties are rejected by the
format validator; this validator does not import anything.

The new format does not serialize settings, tokens, installation/prefix paths,
custom launch commands, artwork/mod URLs, custom labels/tags, automatic-update
preferences, game files, saves, play history or installation receipts. Included
names, repositories, logical keys and release labels can still reveal personal
information: inspect the exact preview before sharing it. This is an allowlisted
format, not a promise to identify every possible secret in free text. The older
raw persistence serializer remains unchanged and is **not** this metadata format.

Export requires a settled successful library load. A failed/in-flight load,
settings recovery, changed entry/root or closed session cannot authorize a stale
preview's save. The successful load retains its data/apps/settings context; a
root change before the first preview cannot relabel the earlier loaded entries. The legacy loader may already have discarded duplicate original
records; this preview cannot recover or validate those original records. The
10,000-entry and 8 MiB limits are resource safeguards and refuse oversized output
without truncation, not claims about total catalog support.

The writer rejects existing destinations, linked ancestors, explicit network or
device path spellings, and destinations inside current Relicade state/recovery
or game roots. Protected roots and their ancestors are inspected from the root
down without resolving aliases; an observed link refuses export even when the
chosen destination names its ordinary target. Attribute observations are repeated
at commit, with a 256-component depth safety limit, not a directory crawl or a
lock against external changes. It writes a new sibling temporary file and uses a no-replace
rename after rechecking the preview. A failed commit can have an uncertain
outcome; inspect the chosen destination rather than assuming nothing changed.
The path checks do not identify mount aliases or establish that a mounted filesystem is physically local,
or provide power-loss durability or protection against arbitrary external file
replacement. Save user game data and the library's recovery files separately.

Evidence is limited to authored synthetic offline library entries, an injected
file chooser, controlled cancellation/context changes, and headless native
presentation checks. Real OS picker behavior, screen readers, controllers,
other-platform filesystem behavior and complete R9 import/export/recovery
acceptance remain open.
