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
- **Catalog/trust:** lawful source/provenance, cached metadata, independently defined signing/review policy and explicit offline state. Relicade must use its own application, update and telemetry identities and accurately identify externally operated catalog services.

Crossing from frontend to native execution must require typed operations, scoped filesystem roots, truthful effective state and safe cancellation. Remote metadata must never become arbitrary shell commands, HTML or destructive paths.

The foundation owns Relicade application/package/data/shortcut identities and does not silently adopt another executable's settings sidecar. Inherited self-update hooks/destinations, telemetry, announcement delivery and automatic download reports are disabled. External metadata/image endpoints retain their actual identity; their reported verdicts are not independent Relicade verification. Real game-art loading and caching remain primary, with explicit unavailable/offline fallbacks and no bundled unlicensed game pixels.

Merged source now includes contained portable staging, manifests and journaled generation replacement/recovery. The two activation renames are not an atomic pair; platform-specific power-loss guarantees are not certified. Credential sessions use OS-vault adapters with no plaintext fallback; explicit legacy credential migration requires a preview and confirmation. Managed offline instance shortcuts enter the recovery/lifetime gate before launch. Removal composes authority for unmanaged targets and refuses managed/recovery trees; it is not managed uninstall. Mod writes into managed portable generations are currently refused, not advertised as integrated game/mod rollback. See the [native guide](../native/README.md). Explicit redundant-backup payload cleanup previews and hash-confirms only older committed operations' duplicate immutable backup files, preserving all previous generations, data snapshots, journals and current-operation backups. It acquires writer/mod/ancestor authority, binds library/settings and retained-file evidence, writes a durable local approval before deletion and requires explicit resume after interruption. This is not credential erasure, general history pruning or managed uninstall; hostile OS writers and physical power-loss/native-filesystem guarantees remain unaccepted. Product-wide network-disabled enforcement, complete functional reconciliation and all package/runtime platform gates remain open.

The cleanup-only typed `PortableBackupCleanupService` now composes that same
Core authority and canonical context for CLI and the native library dialog. The
UI is included in this source, not packaged or native-platform accepted. A session-owned flow
captures the exact InstanceKey, prevents duplicate dialogs, hashes/applies off
the dispatcher and drains cancellation before dependencies are released. The
dialog confirms only its displayed hash; stale errors require a new preview and
confirmation, and interrupted results require explicit same-hash resume. It
reads persisted identities without replacing the live Games collection. Its
cleanup-only manager check reads the injected settings recovery flag without
loading/saving that store. Detached bounded strict settings evidence never
replaces Current, mutates recovery flags or initializes credentials. A cleanup-only
optional evidence capability fails closed for uninitialized/unsupported stores;
the provider query itself is noninitializing. Live and persisted policy stay
distinct; these reads do not establish arbitrary cross-store atomicity. Inert
focusable inspection rows expose the entire plan through existing modal navigation.
Long descriptions are divided losslessly into bounded continuation subtargets,
so navigation can expose the middle of a path taller than the viewport, not just
its first/last edge. Confirm on an inspection target is inert; general modal
algorithms are unchanged.
Existing library-menu release prefetch remains separate from these network-silent
cleanup operations. No Core deletion, receipt or lease algorithm is changed.

## Recent-play recording boundary

Ordinary desktop launch records last-played time only after process creation
returns a non-null process, before transferring the initial writer lease to
lifetime tracking. This keeps the history write inside writer authority even
when the child has already exited. A missing interpreter or other process-creation failure cannot create or overwrite
recent-play history. Null returns retain the existing error path. The record
means a process started, not that gameplay or project prerequisites were verified;
no receipt, child-tree, recovery or launch-event policy changes with this ordering.

## Portable launch completion identity

A short-lived source-owned process may exit after its live check and tracking
handle is opened but before its identity is read. Only a missing identity, or
Linux's exact ESRCH IOException (HResult 3) from identity acquisition, together with
positive exit evidence from the original owned Process permits the existing
completed-lifetime cleanup. A missing PID alone, an unknown/live original process,
permission errors, other IO errors and receipt write/flush/sync failures do not
authorize success. Internal scheduling callbacks cannot authorize this exception.
Cleanup failures propagate. This narrow Attach boundary changes no receipt schema,
child-tree policy, journal/rollback algorithm or native-platform acceptance.

## Artwork dispatcher ownership

A view's display-scale callback checks that view's existing instance dispatcher,
not the global lazy UI dispatcher. Pending artwork decoding can finish after its
host has closed; off-owner completion still uses the last UI-read scale without
reading windows/screens or claiming another application's dispatcher. This narrow
ownership boundary does not change artwork loading, cancellation or cache policy,
and does not establish general application-lifetime or native-platform acceptance.

## Text-decoration ownership

Banner-link styles and Markdown link runs create their underline decorations on
their owning UI context rather than sharing a thread-affine static instance
across application lifetimes. The underline's visual properties, link targets
and keyboard/controller editing behavior are unchanged. This narrow resource
boundary does not change application isolation or certify general UI lifetime,
native input hardware or platform acceptance.

## Library selection layout

A queued library selection is bound to the latest request and the exact card at
its captured index. Superseded, filtered/reordered, overlay-blocked or closed-session
requests do not scroll. Destination lazy content can change measured heights from
an earlier library's cached placeholders: selection first materializes the destination
viewport, completes that layout, then aligns the actual card. This bounded operation
adds no timer, recurring scroll subscription or change to general navigation/modal
algorithms; it is not native hardware, accessibility or performance acceptance.

## Explicit retained portable-payload rollback — source-stage journey

The additive `PortableRollbackService` composes a distinct versioned rollback
context from the exact library instance/root, persisted library digest, live
effective settings and detached strict persisted evidence. It does not load or
replace live settings/library objects, initialize credentials or reuse cleanup
consent. `PreviewRollback` and `RollbackConfirmed` independently derive the same
versioned, ordinally ordered SHA-256 under owned composed authority. The immediate
complete `previous/` of the current committed operation is the only target.
The confirmed path reuses unchanged private journal/commit primitives; existing
context-free Rollback and cleanup schemas/algorithms remain unchanged. Current
declared data is staged with retained immutable payload, not an older save snapshot.

A session-owned rollback flow/model/dialog captures the key before awaits,
performs hash/copy work off the dispatcher, defaults to Cancel and applies only
the displayed immutable digest. All identity/path/hash/limit text has inert,
bounded inspection targets using existing keyboard/controller modal routing.
Stale consent needs a new preview and fresh explicit action. Close/shutdown
revokes participation and drains registered work. Unlike redundant-copy cleanup,
rollback has no same-hash resume: interrupted transaction evidence refuses without
preview/reopen repair. The unchanged Commit primitive may already have attempted
transaction-internal recovery during a failed confirmed operation. Outcome
observation does not initiate recovery or retry. Completion is an observed
committed manifest; incomplete/unknown outcomes never claim cancellation left the
old generation unchanged or that retry is safe. This source
journey does not establish package/native/hardware accessibility acceptance.

## Staged broader architecture — not implemented

The long-term discovery/library model spans community-revived classics, retro games and emulation as well as native ports, recompilations and recreations. Future project types need explicit execution kind, emulator/core identity and license, compatible versions, user-supplied game/BIOS prerequisites, controller/keyboard setup and recoverable version/mod state. No emulator host/core manager or ROM/BIOS/key distribution is implemented or authorized by this architecture. Local/offline use remains account-optional. This design direction does not add or waive current native acceptance cells.

## Documentation boundary

[Documentation maintenance](DOCUMENTATION.md) maps source responsibilities to each active document. Generated facts do not infer semantic truth; architecture claims require review of the actual source diff and independent review before adoption.

## Acceptance

The [product requirements](PRODUCT_REQUIREMENTS.md) define feature, platform and improvement gates. Every native side effect requires unit/fault/security tests plus package and runtime evidence. A shared-code build alone cannot accept a platform.

## Marker preparation boundary

`AppFilesToAddService` treats marker metadata as untrusted filenames, not file
ownership or original-game prerequisites. It validates single platform-safe
names, rejects linked paths and reserves launcher metadata. Creation uses
`FileMode.CreateNew`, so an existing or concurrently created file is not opened
for a truncating write. Removed metadata never authorizes deletion. Retained
names are returned; strict preparation raises a preservation error before any
creation when removed entries still exist. No ownership ledger is inferred from
package contents or legacy lists. Automatic cleanup needs a separately reviewed
ownership/recovery design. Individual creates can succeed before a subsequent
failure; callers must not infer that an exception left the directory unchanged.

Manual entry edits pass their previous marker list through folder preparation, so
the same preserved-file warning applies after a saved manual or hosted edit.
Manual preparation checks ancestor links before creating directories, retains
existing committed-install validation ahead of marker writes, and creates its
instruction file with create-new semantics. A failed instruction write can leave
a partial newly created file; it is not silently overwritten on retry.

## Entry edits and portable authority

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

## Package-bound game-data setup

Compiled project definitions, rather than remote metadata instructions, bind the
initial local-data adapter to a release, package digest, executable digest and
input variant. The native library has an explicit file-selection, preview and
confirmation flow. Data lives outside immutable generations in an installation-
keyed user-data namespace; originals and committed copies are never overwritten.
A nonrecovering installation authority spans confirmation and copy. Retained
launch receipts and unresolved history refuse setup. The shared launch path
revalidates the exact local data receipt and fixed argument under launch authority.

A compiled package binding may provide a first-install executable default after
release and downloaded-asset identity match. The staged target must match the
compiled length and SHA256 before activation. The portable transaction uses that
default only without a prior manifest or explicit selection; updates retain the
existing choice. Unknown ambiguous packages still require explicit selection.

For unresolved multi-file packages, the download caller supplies an awaited
selection callback over an immutable snapshot of relative executable paths. The
transaction validates exact membership and rechecks the staged inventory after
selection. Writer authority remains held until the callback actually drains,
including cancellation; no abandoned UI task can activate a later choice.
The desktop modal supports keyboard/controller navigation. Automatic and headless
callers decline instead of prompting or trusting remote preferred-file metadata.
Cancellation preserves any prior generation and can retain private staging/history.
Staged payloads cannot supply the reserved transaction lock, including directories,
links or special files. Name checks precede initial inventory, follow the choice,
and run before the candidate lease is created. Locks created by the transaction
remain excluded from live and retained-generation inventories; this is not a
sandbox against hostile concurrent filesystem replacement.

The initial definition covers one Perfect Dark US 1.1 Linux x64 package only.
Synthetic fixtures exercise the source workflow; they do not establish gameplay,
rights or platform acceptance. Other project/build prerequisites remain unconfirmed,
and data migration/cleanup remain unimplemented. [Setup guide](GAME_SETUP.md).

Owned-data destination checks include the exact folder, not only its ancestors.
Preview inspects existing authority without creating history. Confirmed import
revalidates the input before creating writer history, then holds the destination
lease through both copy and receipt commit. Library evidence reads use a bounded
opened-file length and reject growth, rather than a path-size check followed by
an unbounded read. Library and detached rollback/settings reads also reuse the
regular-file and link-count guard before opening, preventing a pre-existing FIFO
from blocking cancellation. General settings/credential initialization is separate;
these checks do not certify hostile concurrent path replacement.

## Details-to-library navigation

Project details reuse the existing library navigation event for **Open in Library**.
The action is shown only for a matched local entry and re-resolves that entry at
activation; it does not treat a catalog listing as an installation. The handler
passes the resolved local instance through the same selection route used by catalog
cards. Adding an entry exposes this primary action, while removal remains separate.
A session-only target exception lets selection cross installed-only, tag and search
filters without changing their persisted values or clearing the query. Normal
matching entries stay visible. Deliberate filter/search changes and switching to
Browse clear the exception; background refiltering retains it only while that exact
local object remains in the library. Pending search input is applied without its
debounce before the target is revealed, so a delayed search cannot hide it after
navigation. A native notice explains how to restore the normal filtered view.
No new download, launch, remote request or prerequisite acceptance is introduced.


## Selected-asset digest policy

`PublishedAssetDigest` provides shared strict SHA-256 syntax normalization for the
installer and same-tag rebuild detection. `ReleaseCheck.MissingAssetDigest` records
selected-file integrity availability separately from the catalog's release state.
The installer resolves catalog then publisher digest before confirmation/download;
missing integrity reaches the existing interactive consent seam, while automatic
and headless implementations decline. Valid digests are compared before activation.
This does not establish publisher trust, software safety, rights or playable setup.


## Retained catalog identity

The library Add copy preserves `CatalogEntryId` before saving or background
requests. `CatalogReleases.Find` treats a retained entry ID as authoritative for
matching: if absent, repository/folder heuristics cannot substitute a sibling.
`CheckAsync` distinguishes that unknown identity from a repository never linked
to the catalog. The installer also retains the distinction if its check service is
unavailable or throws. Unknown evidence uses the existing explicit consent seam;
automatic/headless flows decline. Exact cached links and independent repositories
remain supported. A successful runtime link also retains its learned entry ID on
the current library object, so an empty refresh cannot erase that evidence or
substitute a sibling. The exception boundary checks identity learned during the
failed request as well as identity known before it. This does not rewrite legacy
library IDs, migrate them across restarts, or authenticate feeds.

## Optional engine data selection

The separate Freedoom action uses a compiled UZDoom package and Phase 2 data
binding. It reuses the local preview, confirmed copy and retained receipt
transaction, with an explicitly validated single-component `.wad` filename and
pinned SHA256 in addition to the variant MD5. The original Perfect Dark definition
keeps its existing filename and behavior. The optional action does not set the
engine's mandatory-data gate or alter ordinary Launch.

The dialog requires explicit Play after data setup. It checks the saved library,
settings and retained data before dispatch, and the launch service revalidates
the exact generation and data under the existing portable launch lifetime. The
dialog's linked cancellation token reaches readiness hashing and the final process
attempt boundary. Cancellation before that boundary disposes the unstarted launch
receipt; failures after an OS attempt still preserve uncertain lifetime evidence.
Closing a dialog drains pending work, and successful open-dialog completion
forwards the existing Close After Launch preference. A fixed `-iwad` argument carries the literal absolute file path; working directory
and engine configuration/save locations are unchanged. No game/reference binary
execution, universal WAD compatibility or native-platform acceptance follows from
synthetic tests or static package inspection.
