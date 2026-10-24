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
UI is included in this source, not released or native-platform accepted. A session-owned flow
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
The generic Library maintenance menu no longer prefetches releases; explicit
Change version still fetches when needed. Cleanup's Core deletion, receipt and
lease algorithms are unchanged.

The separate **Inspect preservation…** journey reads a bounded raw committed
manifest/journal and compares only exact listed regular payload files. It does
not use the general inventory catch-all as ownership. Current declared data,
history and unknown/changed files remain preserved. Existing-file authority
spans asynchronous observation and guarded UI publication; bounded identity
rechecks detect changes without freezing external writers. Partial results and
uninspected external locations remain explicit. No deletion, recovery, download
or settings save is authorized by this informational result.

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

## Input-service ownership

Each input service owns one successfully initialized SDL gamepad subsystem
reference. Reinitialization releases and reacquires only that reference; disposal
closes the owner's controllers and releases its reference without a global SDL
shutdown that would invalidate a replacement service. Uninitialized and disposed
owners do not poll, reclaim controllers or restart their polling timer. Global
keyboard resolver callbacks retain their existing ownership checks. This lifetime
boundary does not change the sampled press edges, cross-source echo interval or
navigation policy, and does not certify hardware or native Windows behavior.

Shell menu owners require an attached top-level host before opening and release
their matching context-menu navigation registration on failed opening or
disposal. Injected test openers install the same close hook as the real shell.
A closed version-menu fixture previously retained global input ownership and
consumed a later independent pad press; the context-order regression preserves
the real pad/keyboard route and its original separation rather than changing
input timing to hide that state leak.

Settings stage files use platform-neutral stream options on Windows and explicitly
set owner-only Unix creation permissions on Unix. The Unix-only setter is never
invoked on Windows, including with a null value. Existing writer leases, strict
validation, durable flush, atomic replace/create publication, permission
intersection and temporary-file ownership rules still govern the save.

On Windows, generation-changing portable transactions hold the stable outside
writer and an exclusive outside hard-link name of the reserved empty mod lock
for each generation being renamed. Windows cannot rename a directory while a descendant
handle is open, even with delete sharing. The alias keeps the same file identity
and exclusive sharing as the legacy inside name while allowing directory renames.
Acquisition checks existing legacy ownership, verifies both path and held file
identities, and refuses uncertain or conflicting evidence. Coordination remains
on the same filesystem outside all moved generation and transaction trees;
permission denial never selects a weaker lock or another permission route.
Raw Unicode Win32 calls use normalized extended drive/UNC paths, matching .NET
filesystem support for installations longer than the legacy MAX_PATH limit.
Aliases remain as empty coordination evidence and are never overwritten or
automatically removed. Read-only rollback observations keep their existing
inside-lock behavior. Linux retains the original exclusive streams and evidence
rules. This protocol covers cooperative writer exclusion across activation and
recovery, not arbitrary external writers or a power-loss guarantee. Overlapping
unmanaged removal targets refuse before creating history when one target would
contain another target's stable writer.

## Artwork dispatcher ownership

Lazy library cards reserve a one-pixel height when no finite positive cached or
placeholder height exists. A zero-height child inside a clipped parent otherwise
receives a zero effective viewport and never materializes its controls. This cold
case does not depend on another card warming the height cache. Finite positive
estimates, viewport-triggered construction, offscreen dropping and focus/menu
retention keep their existing behavior.

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

Committed-result observation also captures executable and asset identity under
the existing rollback authority. Before publishing, a worker reacquires the
existing nonrecovering authority at the captured root and performs bounded
read-only history/manifest inspection. Exact committed operation identity and
metadata must still match; equal version strings alone are insufficient. That
lease stays held until the awaited dispatcher callback finishes, including
cancellation or close. The UI thread never acquires or waits synchronously on it.
The callback publishes only to the same unchanged live Library entry, without
status loading, recovery, selection saves or provider calls. Closed participation
and changed entry/root/version/selection/status context suppress publication
without relabeling the disk commit as a transaction failure. Owned-data readiness
is invalidated rather than inferred from restored engine metadata. In-memory
release preferences still determine update availability.

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
keyed user-data namespace; originals are never overwritten and existing copies
are not silently replaced.
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

The initial definition covers one Perfect Dark US 1.1 Linux x64 package.
The z2rs v0.6.0 Linux x64 definition adds a compiled NES body-validation rule:
only the known body length or that length plus a magic-identified 16-byte header
is accepted. Body CRC32/SHA-1 check compatibility; full-file SHA-256 and actual
length bind preview, copy, repair receipt and launch revalidation. No remote
metadata can supply these rules, and bytes are never normalized or stripped.
Synthetic fixtures exercise the source workflow; they do not establish gameplay,
rights or platform acceptance. Other project/build prerequisites remain unconfirmed,
and data migration/cleanup remain unimplemented. [Setup guide](GAME_SETUP.md).

Retained owned-data reuse is a separate typed consent action, not an original-file
import with a substituted source. Its preview binds the exact current operation,
compiled package/executable requirement, canonical destination and complete data
identity. An existing canonical data writer is mandatory. Pending staging and
conflicting evidence refuse without cleanup. Confirmation rederives the preview
under both writer authorities and creates a canonical current-operation receipt
with create-new staging and a non-overwriting atomic move; identical current
receipts are verified no-ops. Earlier receipts and game bytes are untouched.
The asynchronous worker retains both authorities through the awaited dispatcher
callback. Persisted context is checked on the worker; live entry, selection,
settings and dialog participation are checked before publishing readiness. Receipt
commit and ready publication are separate results, including cancellation or
failure after the receipt becomes visible. No status loader, automatic play,
original-file guard bypass or generic refresh is used by this path.

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
keeps its existing imported-data filename and validation. The optional action does not set the
engine's mandatory-data gate or alter ordinary Launch.

Uninstalled UZDoom/Freedoom, z2rs and Perfect Dark entries can explicitly confirm
their compiled adapter's first engine installation. `GuidedEngineInstallService` selects only
those compiled project definitions; remote metadata cannot add a project or
package type. The z2rs card's primary action enters this flow; dedicated Perfect
Dark entries join it on Linux x64, while other hosts keep their ordinary download
action. Their ordinary Download menu routes remain separate. The shared progress lifecycle receives the
project-specific confirmation and title. The first-install constraint defaults
to AppImage only; the compiled z2rs and Perfect Dark adapters explicitly enable
their exact tar.gz assets. The Windows x64 UZDoom adapter explicitly enables its
pinned ZIP and separately pinned `uzdoom.exe`; helpers cannot become the selected
setup target. A platform discriminator defaults existing owned-data definitions
to Linux x64 and opts in only this Windows definition. Unix permission setters
are never called on Windows; path identity/overlap uses Windows case comparison
while saved library bytes remain exact. All packages retain the same digest, byte limit and
transaction boundary. The former Perfect Dark repository identity resolves to
the canonical project's pinned package; a changed rolling tag cannot substitute
new bytes.
A constrained route reuses the ordinary download/install service, but requires the
exact release, asset and compiled SHA256 independently of mutable provider digests.
The transaction's opt-in first-install boundary inspects file kinds/presence before
opening metadata, coordinates ancestor and target writer authority, and refuses
existing data or prepared history before recovery. It permits only empty targets
and pre-journal interrupted staging. Context is revalidated after asynchronous
selection/download and at transaction boundaries. The ordinary update path is
unchanged; the constrained path never substitutes another release or invokes
recovery from its failure-finalization path. Success opens the existing data flow,
not a process. A setup-only confirmation/progress model publishes its work before
invoking callbacks, remains owned by the launcher session, and adds a linked
operation token without canceling sibling operations. The async-local scope restores
the outer token after completion or failure. Preflight and the original library
context remain bound across the explicit confirmation. Cancel/back/window-close
keeps the dialog pending while owned transfer/install work drains; a terminal
cancellation never opens the data dialog and can leave a committed engine.
A typed immutable guided-progress callback carries phases and measured transfer
bytes from the first-install constraint to this owning one-shot model. It does
not reinterpret the legacy phase-weighted library percentage. UI publication
rejects older phases, decreasing received counts and terminal/disposed updates;
a retry owns a different model. Unknown totals are indeterminate, and the actual
Applying/Committed boundaries remain distinct from receiving all payload bytes.
Shared metadata waiter cancellation retains its existing coordinator semantics;
the UI does not claim ownership of an independently shared metadata transport.

`GuidedInstallIntent` adds a separate versioned first-install intent store under
Relicade's user-data root. The per-entry persistent writer lease spans one explicit
attempt; a new attempt links to retained earlier history without rewriting it.
Records bind the compiled repository/release/asset/executable identity and a hashed
entry/root context. Bounded strict inspection creates no directory, acquires no
writer, and invokes no recovery or network. Unknown schema, partial writes,
conflicting chains and changed identity are refused with evidence preserved.

`LibrarySetupHistoryInspection` reuses the strict record parser with a captured
compiled identity, without calling first-install preparation or acquiring a writer.
The Library notice and paged review only inspect loaded entries and navigate back
to an existing current entry. Reads run off the UI thread, and publication checks
entry object, root, identity, status and lifetime again. Closing drains the actual
read before disposing its directory cursor; a later context cannot receive an old
result. This presentation is not retry or installation authority.

Resource limits are explicit continuation steps, not library-size support limits:
256 eligible entries and 1,024 directory names per step, at most 256 candidate
records per entry, 16 KiB per record and an 8 MiB maximum charged read budget.
Partial directory enumeration never validates an incomplete history chain. A
per-entry refusal preserves other inspectable entries; a directory-level failure
reports incomplete inspection. No background continuation, recursive crawl,
transfer, transaction recovery or record mutation is introduced.

Atomic, flushed stage records precede payload request, verification, staging and
the existing transaction's BeforeApplying boundary. Only its actual Committed
callback marks engine completion. A postcommit status-write failure cannot reverse
that disk outcome or authorize a retry. The saved attempt ID is not represented as
the portable manifest's operation ID. Explicit retry repeats current context,
source and first-install authority checks; no old consent, payload offset or
progress value is installation authority. This is not a generic job scheduler,
transaction-recovery replacement or hostile-writer/power-loss guarantee.

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


The optional free-data request is a compiled archive adapter, not catalog-provided
execution or path instructions. A dedicated no-auth/no-cookie transport handles
at most three official HTTPS redirects, enforces the pinned compressed-byte limit
and SHA256, then checks exact WAD/notice entries. Extraction writes only a generated
staging name and the fixed retained WAD name. The archive retains upstream notices.
The cache uses conservative root/ancestor writer authority and create-new writes;
existing files are rehashed or refused, never overwritten. Only owned incomplete
staging is disposable. The actual dialog rechecks local library/settings authority
before requesting, tracks cancellation/drain, and passes the retained source into
the existing explicit preview/import protocol. Neither download completion nor a
cache hit authorizes the separate game-data import or starting a process.

## Confirmed game-data repair

A bounded repair preview requires the canonical receipt for the exact current
generation, a verified replacement matching that receipt, and a regular unlinked
changed copy within twice the adapter input size. It binds the changed bytes and
retained destination to the confirmation. The existing installation and data
writer authority spans staging, preservation, revalidation and commit. A
create-new sibling backup is flushed before atomic replacement of the active
copy; existing matching backups are reused, never overwritten. Receipts remain
unchanged. Errors and cancellation retain committed replacements and backups,
requiring a fresh inspection rather than automatic retry or rollback. Unknown
ownership, conflicting evidence and oversized inputs refuse without replacement.
This is not general data migration, backup cleanup, hostile-writer isolation or
platform power-loss certification.

## Release-note loading

Release-note loading in project details has explicit loading and retry state,
separate from display text. One request belongs to the current page generation;
closing or switching pages invalidates its result. Transient failures expose a
user-initiated retry, while successful loaded/empty histories remain cached for
that page and HTTP missing/removed sources do not create a retry loop. Catalog
and repository reads preserve their existing provenance and do not install files.

## Library cover identity

`CatalogCover` keeps catalog entry, source URL, variant and baseline icon separate
from the project's icon. Mapping, strict local-library mutation parsing and
catalog synchronization retain that record. Unknown stored shapes refuse a
mutation rather than being silently rewritten. `GameInfo.CardImageUrl` resolves
only a matching record and yields to custom local art or an edited icon.
Cache-only Add checks precede session-owned enrichment; cancelled or changed
entries cannot publish late shared-loader results. The three library templates
bind the effective image and its attribution without changing primary actions.
This reuses the existing image transport, not a new security-certified downloader;
see [native artwork scope](ARTWORK.md#native-library-cover-references).

One compiled, hash/dimension-bound Taisei portrait has a separate reviewed local
resource path and exact catalog/project identity. It uses the same cover record,
custom-image priority and shared loader but needs no image-provider request.
The bundled notice records artist, source, license and deterministic crop/resize;
see [reviewed portrait scope](ARTWORK.md#reviewed-taisei-portrait).


## Development Linux container assembly

The Linux packaging templates and Python builders assemble a checked directory
publish into an AppDir and then an unsigned development AppImage. They preserve
payload bytes and existing notices, use explicit local pinned tool/runtime assets,
and do not register desktop settings, fetch dependencies or configure updates.
The embedded AppImage runtime introduces separate component-rights obligations;
the directory-publish inventory does not cover them automatically. See the
[development recipe](LINUX_APPIMAGE_DEVELOPMENT.md). This does not accept a
platform, installer migration, full desktop behavior or public release.

## Original-game metadata index

`GameDiscoveryIndex` loads a pinned embedded Wikidata JSONL snapshot with a 2 MiB
byte bound, exact SHA256, strict source assertion/ID checks and exactly 10,000
records. It pre-normalizes titles and keeps distinct source IDs through local
search and bounded 50-record result pages. It does not flatten game/edition IDs
into project identity or execute metadata as commands. `GameDiscoveryDialog`
uses native list navigation, explicit source opening and an explicit title-search
bridge to the existing source catalog. The toolbar action participates in both
Browse controller layouts. Closing or session shutdown drains the modal and any
already requested browser handoff; reopening never starts a download. Browser
failures are bound to the requested source ID, so an earlier request cannot append
its error to another selected record.

This first broad discovery slice has unknown play readiness, not 10,000 reviewed
play paths. Live source-project metadata and its existing cache remain separate
from the immutable bundled game snapshot; no Quiver bulk mirror or artwork
redistribution is introduced. Future source enrichment/mappings need stable
provenance and explicit compatibility evidence rather than title matching.
