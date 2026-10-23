# Local game-data setup

Some community projects provide an engine or recompilation, not the original game
data. Installing an executable does not establish that the game is ready to play.
Relicade's native development source includes package-bound local setup
flows. It is not a packaged release, gameplay certification or complete catalog
setup support.

## Before you play

Discover's project Overview explains known game-file requirements before a player
adds or downloads the project. The compiled guidance distinguishes z2rs and Perfect
Dark's player-supplied data, UZDoom's optional free Freedoom path, and a complete
Taisei package. Unreviewed projects explicitly say that their requirements have
not been reviewed and point to the upstream README. Advice is not a readiness
check, catalog approval, or a new download provider. Existing install, consent,
launch and local-data validation remain authoritative; manual setup is not blocked.

A **Setup guide** action appears only once a recognized project is resolved. It
opens this guide explicitly and participates in the existing keyboard/controller
navigation. It does not start an installation or fetch game files. The advice
lists supported package/platform limits rather than claiming that every version
of a project supports guided setup.

For [Taisei v1.4.6](https://github.com/taisei-project/taisei/releases/tag/v1.4.6),
the [pinned upstream README](https://github.com/taisei-project/taisei/blob/5bfc9b842fe6d9bb8d1610f76f442ba07c72c4cf/README.rst)
describes a complete game. Its Linux x64 tar.xz package includes its executable
and game data, so that package does not require a separate original-game ROM.
Static content inspection and local-transport installation do not establish
runtime compatibility or complete third-party notice/asset-rights coverage.
The [upstream notice](https://github.com/taisei-project/taisei/blob/5bfc9b842fe6d9bb8d1610f76f442ba07c72c4cf/COPYING.txt)
distinguishes software, music, portraits and historical background photographs;
its incomplete historical photo-source list and embedded dependency coverage
remain unresolved. No distribution clearance or new automatic provider is implied.
Relicade does not relocate the game's existing storage/configuration paths.
[Native interaction tests](../native/Relicade.Tests/ProjectSetupGuidanceTests.cs)
cover visible advice, source mismatch, page changes and explicit keyboard/controller
guide navigation. [Separate native rendering tests](../native/Relicade.Tests/RealRender/ProjectSetupGuidanceRenderingTests.cs)
check readable normal/minimum/scaled layouts with controlled metadata. These are
source checks, not new feature/platform acceptance records.

## Download integrity and catalog approval

A catalog's release approval is separate from verification of a selected download's
bytes. The native download flow accepts a SHA-256 only when it is exactly 64
hexadecimal characters. It prefers the selected file's catalog digest, with a
valid publisher-provided SHA-256 as a fallback when that digest is unavailable or
malformed. A valid nonrolling catalog digest cannot be overridden merely because
a publisher digest differs. Existing rolling-release revalidation remains separate.

If a catalog download has neither usable digest, interactive installation explains
that the bytes cannot be verified against a published digest and requires explicit
confirmation. Declining preserves the current installation without downloading the
package. Automatic/background and headless flows decline rather than assuming
consent. Existing blocked-release and flagged-file warnings still apply. A digest
mismatch stops activation and retains the prior installation. Malformed publisher
digests do not create a false same-tag update after a successful installation.

A matching checksum is not a guarantee of safe software, publisher authenticity,
rights clearance or gameplay. These checks have synthetic local-transport coverage,
not acceptance of every catalog asset or platform. Library entries added from the catalog retain their exact catalog ID before
background enrichment. If that ID cannot be matched to available status metadata,
automatic installs decline and interactive installs explain the unconfirmed identity
before asking. An absent explicit ID does not borrow another entry's approval just
because its repository or folder matches. Saved exact-ID status can still be used
offline with a valid selected digest. Independent repositories without catalog
identity retain their separate policy; complete catalog freshness, authentication
and explicit identity migration remain unaccepted. Links learned on the current
library object survive a later empty refresh; errors after linking do not permit
automatic installation. Legacy entries without saved IDs are not migrated across
restarts by this runtime safeguard.

Release-history continuations must complete before they can supply approval.
Failed or canceled partial reads retain known blocks separately from the last
complete in-memory history, without treating partial data as a successful refresh.
Exact source tags and build labels remain distinct; conflicting repeated records
retain their metadata but cannot lend arbitrary approval or checksums. See
[release-history completeness](../native/README.md#release-history-completeness)
for cursor, resource, offline-cache and session-lifetime limits.

## Interrupted guided engine setup

The explicit Linux x64 first-install adapters (Perfect Dark, z2rs and UZDoom for
Freedoom) save the reviewed repository, release, asset digest and destination-context
fingerprint before payload transfer. Each attempt has its own ID and durable last
stage. These IDs identify setup attempts, not portable transaction operations.
Credentials and absolute destination paths are not stored in these records.

Reopening the first-install confirmation reads saved history without starting a
request, repairing a transaction or committing an installation. **Retry engine
install** is a separate explicit choice: it creates a new attempt and rechecks the
current entry, target, exact release/address and compiled package digest. It starts
a fresh transfer, not a Range resume. Previous attempt records are retained; a
concurrent attempt cannot share the same entry's writer lease. Declining creates no
new attempt. Cancellation drains the active work and never starts game-data setup
or gameplay automatically.

Corrupt, unknown, conflicting or unfinished record writes are retained and refused,
not silently deleted. An attempt at the commit boundary requires inspection rather
than automatic retry. A failed status write before commit stops further work; if
the engine has already committed, that disk outcome remains committed even when
its saved status is uncertain. The library's actual installation and existing
recovery controls determine the next setup action, not the saved progress record.
Existing data-file consent and readiness checks are unchanged.

### Finding saved setup history

The Library can show a saved-setup notice for supported guided installs among its
currently loaded entries, even when the entry is hidden by the current filters.
**Review** opens a read-only list; **Show in Library** reveals the same current
entry without replacing your filters. Neither action retries, repairs, imports
files or launches a game. An old stage can describe interrupted or still-active
work; inspect the entry before choosing its ordinary setup or recovery controls.

Inspection is deliberately bounded. **Continue inspection** checks another batch;
an incomplete message is not a complete-library result. Oversized, unknown,
conflicting or changed records remain preserved and visibly require inspection.
Unloaded entries are not searched. **Refresh** starts a fresh read of the current
loaded entries; closing the review drains its pending read. These informational
records never grant installation status, owned-data readiness or launch authority.

This is a narrow first-install capability, not a global download queue, scheduler,
byte-range resume, general update/mod persistence or power-loss/platform acceptance.
The focused synthetic tests cover in-process reopening and real local transaction
callbacks. Two additional Linux checks terminate an owned test worker after the
Resolving or Downloading stage is persisted. A separate inspector opens the retry
confirmation without HTTP requests or application-data changes; a third process
explicitly retries pinned synthetic bytes, preserving the prior attempt and authored
save/original sentinels. These checks do not launch a game, cover every termination
boundary, or certify full desktop startup or physical power loss. Full R2 acceptance
remains open.

## Keeping a specific build

A version pin with a `+build` label stays attached to that exact source tag across
refresh, save/load and installation. If that build is unavailable, Relicade does
not substitute a different build or reuse a mismatched cached download. Use the
existing **Back to latest updates** or
**Back to verified updates** action to clear the pin. See
[explicit build-version pins](../native/README.md#explicit-build-version-pins)
for cache and legacy-alias boundaries.

**Reinstall** keeps the known installed target, and **Install Unverified** keeps
the requested target. If that release is missing or ambiguous, use **Change
Version** to choose another one; Relicade does not silently substitute a different
build. Verification and any required download confirmation still apply.

In **Change Version**, case-distinct release tags keep their own catalog check
labels. Latest, Installed and Preferred markers identify an exact target or a
unique legacy alias, not another build or an ambiguous match. These labels are
source metadata, not a promise that a game is ready to run.

Choosing a version changes the saved pin only after that chosen installation is
confirmed. Declining, cancelling or failing the operation keeps the existing pin
in memory and in the saved library, without undoing a separate preference edit.
If an error occurs after activation, the new installed files can still remain;
check the library and recovery state rather than assuming cancellation rolled
everything back.

**Update Now** and automatic updates clear an existing skipped-version preference
only after installation is confirmed. Refused, failed or cancelled attempts do
not count as completed updates, and a separate preference change made during the
transfer is kept. Already-current versions do not count as new installations.

For a rolling release, a valid changed package checksum can identify an update even
when the source tag stays the same. **Update Now** runs the normal download and
integrity checks for that rebuild instead of skipping it as already current.
Absent or malformed installed/published checksums do not invent a same-tag update;
unknown installed receipt bytes remain untouched. A package made current by another
completed installation is rechecked without counting a new update or clearing
version preferences; the cached receipt is refreshed from that check. Explicit
version selection can still confirm an already-current target. These checks do not certify
the publisher or game, and general version-equivalence behavior is unchanged.

## Choosing a launch file before installation

For a portable package with several unresolved launch files, the desktop download
flow now shows the staged relative paths before activating the installation.
Choose a file, then confirm **Install**. Selecting a row alone does not activate
or run anything. Cancel closes the choice and leaves the prior installation intact;
private staging and recovery history can remain. The transaction holds its writer
authority until the dialog finishes and rechecks staged bytes before activation.
On Linux, nonregular and multiply linked files are refused before inventory reads. Long
relative paths remain scrollable without pushing confirmation out of the window.

Existing valid launch selections, single-file packages and the exact compiled
package default below do not require this prompt. Remote preferred-file hints are
not trusted as selections. Automatic/background and headless flows decline an
unresolved choice without opening a dialog; they do not guess. This desktop
choice has synthetic transaction, controller-navigation and native-render
coverage, not physical-controller or mobile certification. Choosing an executable
does not establish that its required game data is present or that it can run.

## First supported setup definition

The initial definition recognizes **Perfect Dark US 1.1**, big-endian `.z64` data,
for one exact **Linux x64** portable package:

- Project: [Perfect Dark PC port](https://github.com/perfect-dark-pc-port/perfect_dark).
  The former `fgsfdsfgs/perfect_dark` repository identity is also recognized.
- Release tag: `ci-dev-build`; asset ID `512564020`;
  `pd-x86_64-linux.tar.gz`.
- Package SHA256:
  `0f28fd59b488e0a919aae4350083059faebd462fce52df7bfdc9a4aec26f92ae`.
- Selected US executable SHA256:
  `40e8e1317a712a0b29ebd715d25ab7ae366aa601444eb8dec45b97c308ddf9a5`.
- Required input: 33,554,432 bytes; upstream US 1.1 MD5:
  `e03b088b6ac9e0080440efed07c1e40f`.

The [pinned upstream instructions](https://github.com/perfect-dark-pc-port/perfect_dark/blob/32a1cb9f268dd3ac73016801025c6bbbfa20130f/README.md)
identify the variant. The [pinned ROM loader](https://github.com/perfect-dark-pc-port/perfect_dark/blob/32a1cb9f268dd3ac73016801025c6bbbfa20130f/port/src/romdata.c)
implements `--rom-file`; its [file-handling source](https://github.com/perfect-dark-pc-port/perfect_dark/blob/32a1cb9f268dd3ac73016801025c6bbbfa20130f/port/src/fs.c)
accepts the absolute path used by this adapter. The rolling
[release](https://github.com/perfect-dark-pc-port/perfect_dark/releases/tag/ci-dev-build)
can change: the tag or filename alone is not acceptance of a replacement asset.
Package-to-source reproducibility has not been established by these checks.

A variant hash is **not proof of ownership, legal entitlement or authenticity**.
Use only game data you may lawfully use. Relicade does not supply, download or
upload ROMs, BIOS, keys or the selected file. The local copy gets an additional
SHA256 integrity check. Other regions, byte-swapped files, archives, executables,
package revisions and operating systems are not silently accepted by this rule.

## Player flow

1. For a new dedicated Perfect Dark Library entry on Linux x64, choose **Set up
   game**. Confirm the reviewed `ci-dev-build` engine package explicitly. The
   former repository identity uses the same canonical package. The rolling tag
   cannot authorize changed bytes, a different asset or a replacement version;
   unavailable or mismatched packages are refused. Existing files, installed
   versions and pending recovery are not replaced by this first-install flow.
   Relicade selects the pinned US executable and checks its size and SHA256 before
   activation. Updates retain an existing explicit launch selection; this default
   does not choose for unknown or ambiguous packages. Other hosts retain their
   ordinary download action, not this Linux x64 setup path.
   Linked, shared, OS-package and unresolved recovery installations require
   separate support. An unsupported package says its requirements are unconfirmed.
2. After the confirmed engine installation, the separate game-data dialog opens.
   For an existing supported installation, choose **Finish game setup** from the
   library card or its menu. Installing the engine alone does not mark the game
   data ready, copy a file or start the game. Cancel waits for active setup work
   to drain; any already committed engine and recovery history are retained.
3. Select your lawful local `.z64` file. Relicade checks the installation, variant
   and available storage without modifying the selected file.
4. Review the displayed source, exact destination and additional bytes required.
   Confirm lawful use and the local copy explicitly. Selection alone does not copy.
5. After a verified result, close the dialog and select **Launch**. The shared
   launch path revalidates the selected package, executable, local receipt and
   data before passing the fixed `--rom-file` argument. Shortcuts do not bypass
   this data check. Native gameplay and hardware compatibility still need testing.

Long source paths, destinations, hashes and retained-backup details remain complete.
The preview divides them into measured, focusable sections that fit the available
window. Use Tab or controller Up/Down to inspect the ordered sections; the focus
ring does not rewrap their text. Inspection is not copy confirmation. The separate
confirmation and Cancel actions stay available at the supported dialog sizes.

The original is never moved, linked or overwritten. Setup uses a separate
`owned-game-data` directory under Relicade's user-data location, keyed to the
installation. It is not injected into an immutable game generation. Existing
copies and conflicting receipts are preserved, not silently replaced.

## Reuse retained game files after a compatible generation change

For the supported Perfect Dark and z2rs setup adapters, **Use retained game files…**
opens a separate, local-only preview. This is useful when a compatible engine
transaction or rollback has changed the installation generation but the verified
copy is still in Relicade's data store. The original-file chooser still requires
an original; selecting the retained destination there is not an adoption shortcut.

The reuse preview checks the exact current package, selected executable, compiled
variant rule and complete retained-file SHA-256 and length. It requires the
existing data-store writer identity from prior setup. Inspect the retained path,
generation and digest, then separately select **Confirm lawful use and reuse**.
A compatibility hash is not proof of ownership or permission. No original is
needed for this explicit reuse action, and no game file is copied, downloaded,
moved or launched. Confirmation creates only a small current-generation setup
receipt; an exactly matching receipt is reverified without rewriting it. Previous
receipts and data remain intact. This is not automatic adoption during rollback.

Pending staging, conflicting receipts, missing writer identity, linked files,
changed bytes or unsupported packages stop reuse and leave the original chooser
available. Cancellation before the receipt commit changes no setup receipt;
a later cancellation or context change may leave a receipt without publishing
readiness. Reopen setup to inspect that state. Both installation and data-store
writer authorities remain held until the guarded readiness callback has drained.
The normal launch-time package/data check is still required. These synthetic
source checks do not establish real-game or hardware acceptance.

## Cancellation, recovery and limits

Cancellation can leave an already committed copy in place. A fresh original-file
preview can recognize identical retained bytes, but still requires selecting the
verified original. The separate retained-file reuse action above does not require
that original and never overwrites the retained copy. Partial uncommitted staging created by the
current operation can be removed; originals, committed copies and earlier setup
receipts remain. Failure does not mean nothing changed. Resolve conflicting files
or recovery evidence before confirming a new preview.

A changed imported copy can be repaired only when its canonical local receipt
still matches this exact installation generation and the verified replacement.
The Freedoom setup dialog remains available with a diagnostic instead of treating
changed data as playable. Choose a verified original or use the explicit free-data
download, inspect the old-copy hash, retained-copy destination and required space,
then confirm replacement separately. Relicade first preserves the changed bytes
in a hash-named sibling backup and then atomically replaces the active copy. The
original selected file, existing receipt and retained backup are not overwritten.
An identical backup may be reused; a conflicting backup stops the operation.

Preservation inspection is bounded to twice the adapter's expected input size.
Larger files, linked or nonregular files, missing or conflicting receipts, and
unrecognized ownership require separate inspection and are left unchanged.
Cancellation or an error after preservation can leave a backup or a completed
replacement. Reopen setup for a fresh inspection before retrying; do not assume
failure means no change. Backups are not automatically deleted. These source
checks do not certify power-loss recovery or hostile concurrent filesystem changes.

Setup refuses active writers, retained launch receipts and incomplete or unknown
installation history rather than recovering it implicitly. The exact data-store
folder and its ancestors must also be free of conflicting installation authority;
a confirmed copy holds the destination writer lease through the receipt commit.
Preview does not create that history. Library identity and
writable settings are rechecked during the operation. Library and detached settings
evidence must be unlinked regular files before opening; a pipe is not a settings
document. These checks are not a sandbox against hostile concurrent path
replacement. Unknown requirements never
become confirmed merely because an executable exists.

This first adapter does not implement data-store migration, automatic owned-file
cleanup, original-data exports, all variants, all projects or emulation. Back up
originals and the local data store separately; do not assume metadata exports
include them. Full catalog and platform requirements remain in the
[coverage contract](FEATURE_COVERAGE.md). Synthetic copy, failure and UI tests are
not permission to execute external games or evidence of real gameplay acceptance.
The exact pinned archive has also been checked through the download/install caller
using a local test transport, without starting its executables. This establishes
bounded extraction/activation evidence, not a working game or a packaged launcher.

## Optional Freedoom Phase 2

The library offers **Set up Freedoom Phase 2…** for a new UZDoom entry and
**Play Freedoom Phase 2…** after engine installation. This is an optional,
explicit setup path, not a replacement for normal engine Launch or
existing WAD configuration. It does not change the working directory, move saves,
download data in the background or claim gameplay certification.

The compiled adapter accepts only this managed **Linux x64** package:

- [UZDoom 5.0.3](https://github.com/UZDoom/UZDoom/releases/tag/5.0.3), asset
  `584674268`, `Linux-UZDoom-Release-x86_64.AppImage`, 34,560,504 bytes.
- Package and selected executable SHA256:
  `9c5166febc09cbf80c24612a78829037c953f38c4d986e36f8475ddd507e0228`.
- [Freedoom v0.13.0](https://github.com/freedoom/freedoom/releases/tag/v0.13.0),
  `freedoom-0.13.0.zip`, asset `148625434`, 24,143,781 bytes.
- Official archive checksum and locally checked SHA256:
  `3f9b264f3e3ce503b4fb7f6bdcb1f419d93c7b546f4df3e874dd878db9688f59`.
- Extracted `freedoom-0.13.0/freedoom2.wad`, 28,787,748 bytes; SHA256
  `a8772e088847032510d97ba2312406a6998f21cbab44d4ff10696faa9c0ecd4b`;
  MD5 `cd666466759b5e5f63af93c5f0ffd0a1`.

For an uninstalled entry on Linux x64, **Set up Freedoom Phase 2…** first asks
whether to install the exact UZDoom package above. The setup window requires an
explicit **Install engine** action and shows progress. Declining makes no download.
The selected release, asset name and official download address must still match;
the downloaded bytes must match the compiled adapter hash even if remote metadata
reports a different digest. Header and streamed bytes are bounded to the pinned
package size before writing beyond it. There is no fallback to another release or asset.
Saved write permissions and the current library entry are revalidated after catalog
consent dialogs and immediately before requesting the engine package.
After installation, the existing data dialog opens for a separately confirmed
free-data download or local-file selection. Nothing is played automatically.

This first-time path does not replace an existing version, payload, personal data,
launch receipt or prepared recovery operation. It checks the target before the
prompt and again under installation authority before recovery or activation.
An enclosing managed installation or existing ancestor history is also refused
before child history is created. Only empty targets and incomplete pre-journal
candidate staging can proceed through the ordinary installer. Unknown or special
files are not opened to infer ownership. A conflict is retained and reported for
the ordinary recovery/version controls, rather than silently downgraded or retried.
Unsupported platforms and already remembered versions need ordinary installation
or version selection. Version preferences, normal updates and Launch are unchanged;
a later normal update can require a future adapter before this optional flow works.

The guided engine progress window distinguishes source checks, measured transfer,
verification, file preparation and safe activation. A known positive transfer total
shows received/total bytes and a bounded percentage; missing, zero or inconsistent
totals remain indeterminate. A fully received file still needs verification and
installation. During activation the window explains that cancellation cannot
interrupt the commit and does not promise to undo an installed engine. These are
observations for the current attempt, not readiness or recovery authority.

**Cancel setup**, Escape/controller Back, or closing the busy setup window cancels
this guided operation, not the launcher or unrelated downloads. The window stays
pending until its owned package transfer and installation work drain, and then
shows a result before Close. It cannot start a second attempt while work is pending.
Cancellation prevents the handoff from opening data setup; an already committed
engine and its history remain installed if cancellation arrives at completion.
Reopen the entry only after the terminal result to inspect its actual state before
retrying. Shared metadata requests retain their separate coordinator ownership.
This is not a promise that cancellation removes all work or that the first-install
guard protects against hostile concurrent filesystem replacement. Long or multiline diagnostic
text has keyboard/controller Previous/Next pages sized to the displayed text,
including short messages with many line breaks, and an explicit display limit;
the retained operation message is not truncated by that view.

Select **Get free game data** to download that exact archive explicitly, or use
the official release-page link and choose your existing `freedoom2.wad`. The
built-in option checks the ZIP size/hash, exact WAD member and original COPYING
and credits entries without extracting arbitrary paths or starting the engine.
It uses no account credentials or cookies, allows only official GitHub HTTPS
asset redirects and has bounded download size, redirects and a five-minute request
budget. Cancel closes and drains the request.

The verified ZIP and WAD stay under `free-game-downloads/freedoom-0.13.0` in
Relicade's user-data directory. The ZIP retains the original license and credits.
Together these use approximately 53 MB; the separately confirmed retained game
copy needs another 29 MB. A repeated request rechecks and reuses the cache without
a network transfer. Conflicting, linked, corrupt or unknown cache files are
preserved and reported, never silently overwritten or recovered. Only this
operation's incomplete staging is removed on ordinary failure; an interrupted
process can leave pending files requiring inspection. General cache cleanup and
crash recovery remain separate work. Cancel after a verified file was committed
can leave that download retained, but never confirms a game-data import or Play.

Review the local source, destination and storage estimate before confirming the
separate copy. The original download or selected file is retained. Select **Play
Freedoom Phase 2** to start explicitly; subsequent visits can use the verified
retained copy. Package, file and installation identity are checked again before
launch. Closing the dialog cancels pending validation and prevents a later process
attempt; work is drained before the dialog state is disposed. A process attempt
that has already begun still retains its normal tracking and recovery authority.
Successful optional play respects **Close After Launch**. Normal engine **Launch**
continues to use its existing behavior.

The pinned [argument definition](https://github.com/UZDoom/UZDoom/blob/7910df7102033fd7754a317f3254f24101135926/src/d_main.cpp)
and [IWAD loader](https://github.com/UZDoom/UZDoom/blob/7910df7102033fd7754a317f3254f24101135926/src/d_iwad.cpp)
accept a literal `-iwad` argument with an absolute data path. No shell command is
constructed from the selected path. The retained copy uses a `.wad` filename;
Perfect Dark's existing `.z64` location remains unchanged.

Freedoom is original free game data, not a commercial Doom data download. See its
[pinned license](https://github.com/freedoom/freedoom/blob/v0.13.0/COPYING.adoc)
and keep the archive's COPYING and credits when redistributing it. UZDoom has its
own [license and asset/branding exceptions](https://github.com/UZDoom/UZDoom/blob/7910df7102033fd7754a317f3254f24101135926/LICENSE).
Relicade does not bundle either package here. Comparing hashes is not signature
authentication, package-to-source reproducibility or a full distribution-rights
review. This adapter does not accept arbitrary WADs, Phase 1, other engine
versions, other platforms, or the full catalog's setup requirements.

Eligible UZDoom cards offer the separate setup/play action in list, grid and
compact layouts. Ordinary engine Download and Launch remain unchanged.
Keyboard focus can activate the separate action, and controller users can also
reach it through the existing App options menu. Kiosk/read-only settings and
recovery-blocked entries do not expose this shortcut. Opening the
flow still validates the exact supported package; the card itself is not a
compatibility guarantee. A focused secondary action stays bound to its own entry
when the visible collection changes; confirmation does not fall through to a
different game at the old selection index.

## z2rs Zelda II USA setup

For a new entry, the primary **Set up game** action offers the pinned engine
installation first. The confirmation identifies z2rs and the supported release,
then the shared setup window shows progress and its own Cancel action. Only this
explicit choice starts the package transfer. Existing versions, nonempty targets
and pending installation history are not replaced. Ordinary Download/version
controls remain available separately. Other platforms receive a specific
unsupported-setup explanation rather than an alternative package.

After confirmed installation, the separate owned-file dialog opens. Select your
own lawful ROM, inspect the source/destination and confirm the copy. No ROM is
fetched, and no game launches automatically. Cancellation waits for owned work;
if the engine committed before cancellation it remains installed, but no data
handoff or play begins. Inspect retained state before retrying.

Before activation, extraction removes its own empty wrapper directories and the
exact unchanged nested archive it consumed. This leaves committed history ready
for the separate data-import inspection. Unexpected files and failed extraction
evidence are retained and refused, not swept away. This does not automatically
repair older installations with unexplained staging in their history.

For a dedicated managed **z2rs v0.6.0 Linux x64** installation, **Finish game
setup** accepts your lawful Zelda II USA dump. Relicade supplies no ROM and does
not fetch, upload or transform it. Review the original and destination paths,
then explicitly confirm the local copy. The original stays untouched.

The definition is bound to the [official v0.6.0 release](https://github.com/troyedwardsjr/z2rs/releases/tag/v0.6.0):

- Archive `z2rs-0.6.0-x86_64-unknown-linux-gnu.tar.gz`, asset ID `664693912`,
  SHA-256 `356d9624eb1795179f2457e693901577b3343f931b162d24daca53dac9b99458`.
- Selected executable `z2rs`, 37,783,632 bytes,
  SHA-256 `a064a4587b23f0a4276ea81a2824a1ccf2e89be962454f54cb425b2b871081df`.
  The archive's separate launcher and signalling server are not substituted.
- ROM body: 262,144 bytes, CRC32 `BA322865`,
  SHA-1 `11333adb723a5975e0ecca3aee8f4747aa8d2d26`.
  A 262,160-byte file is also accepted only when its first four bytes are
  `NES` followed by `0x1a`; validation then checks the body after the complete
  16-byte iNES header. Other header fields follow the upstream compatibility
  rule, not a newly invented format certification.

These checks come from the exact release's
[ROM reader](https://github.com/troyedwardsjr/z2rs/blob/4c5e34418017cfda71ea0ca09c952ff93ba1591c/crates/z2-assets/src/rom.rs)
and [asset policy](https://github.com/troyedwardsjr/z2rs/blob/4c5e34418017cfda71ea0ca09c952ff93ba1591c/LEGAL.md).
CRC32 and SHA-1 identify the supported upstream variant; they are not modern
publisher authentication or proof of ownership. Relicade additionally records
the whole original file's SHA-256 and actual length, including any header, and
rechecks them when copying and launching. Existing valid but different copies
are preserved, not silently replaced. Repair still requires the originally
recorded complete bytes; switching header forms does not authorize replacement.

Launch passes the retained path as a literal `--rom` argument and adds
`--no-discord`, without online co-op flags. Relicade does not rewrite the engine's
config or save locations. The engine itself may remember the ROM path on a
successful launch and continues using its existing data-directory override, or
`$XDG_DATA_HOME/z2rs` / `~/.local/share/z2rs` by default. See the pinned
[desktop guide](https://github.com/troyedwardsjr/z2rs/blob/4c5e34418017cfda71ea0ca09c952ff93ba1591c/guide/desktop.md)
and [startup code](https://github.com/troyedwardsjr/z2rs/blob/4c5e34418017cfda71ea0ca09c952ff93ba1591c/crates/z2-native/src/app.rs).

Static inspection of that exact Linux executable records required `libudev.so.1`,
`libasound.so.2`, `libgcc_s.so.1`, `libm.so.6`, `libc.so.6` and the x86-64 loader,
with non-weak GLIBC symbols through 2.34. A weak GLIBC 2.39 reference is not a
hard 2.39 requirement. This is a dependency observation, not a tested distribution
or graphics/audio compatibility claim.

Unknown versions/platforms remain unconfirmed. Existing linked or manually
managed entries retain their ordinary launch flow. This definition does not add
engine auto-installation, general emulator setup or ROM downloads. Upstream's
[provenance statement](https://github.com/troyedwardsjr/z2rs/blob/4c5e34418017cfda71ea0ca09c952ff93ba1591c/PROVENANCE.md)
explains that z2rs remains a partial reconstruction with a ROM interpreter,
not a complete standalone reimplementation. Synthetic input, copy/repair and
native-dialog tests do not certify gameplay, controller hardware, platform
support or distribution rights.

## Source checks and remaining acceptance

The [catalog-to-library setup checks](../native/Relicade.Tests/CatalogOwnedSetupMappingTests.cs)
exercise controlled catalog identity through the real Add service and durable
library reload. They check both the returned live Library object and the saved
entry remain uninstalled and not game-data-ready. The supported z2rs and Perfect
Dark repository identities retain
their compiled package rules; another provider, a manual entry or an unrelated
repository does not acquire those rules. Library membership alone never means
that the engine is installed or the required game data is ready. These checks
use injected offline metadata and temporary synthetic state, not downloaded
engines, real user data or gameplay. They complement, rather than replace, the
separate installation and confirmed-copy tests below.

The [Perfect Dark first-install checks](../native/Relicade.Tests/PerfectDarkFirstInstallTests.cs)
exercise the actual engine-confirmation entry point, primary actions, explicit
engine consent and separate owned-file preview/copy. A small synthetic archive
and non-runnable payload use an injected transport and trusted test definition;
they do not execute the official package or contain game data. Rolling-package
changes, stale permissions, cancellation and existing user-file/history refusal
retain their separate assertions. The compiled exact-package executable selection
remains covered by the [installation-target checks](../native/Relicade.Tests/OwnedGameInstallTargetTests.cs),
not inferred from a synthetic single-executable archive.

The [z2rs setup tests](../native/Relicade.Tests/Z2rsSetupTests.cs) cover the compiled
package identity and synthetic header/body inputs, changed or special files,
copy cancellation, preserved originals, explicit repair and literal launch
arguments. [Native UI tests](../native/Relicade.Tests/Z2rsSetupUiTests.cs) exercise
explicit keyboard/controller confirmation without engine execution;
[rendering tests](../native/Relicade.Tests/RealRender/Z2rsSetupRenderingTests.cs)
check the actual requirements dialog at ordinary/minimum sizes and 200% scaling.
They use synthetic data, never a commercial ROM, and do not establish gameplay.
The [first-install journey tests](../native/Relicade.Tests/Z2rsFirstInstallTests.cs)
continue through the real picker, preview and separate confirmed copy after a
synthetic engine installation. [Extraction lifetime tests](../native/Relicade.Tests/PortableExtractionLifetimeTests.cs)
check ZIP, tar, single-file and nested wrappers, retained download bytes and
unrelated operation evidence.


The [first-install package tests](../native/Relicade.Tests/FirstInstallPackageTests.cs)
cover retained data/history, ancestor authority, immutable bytes and size bounds.
The [guided handoff tests](../native/Relicade.Tests/FreedoomEngineHandoffTests.cs)
exercise actual installation with non-runnable synthetic payloads and injected
metadata/download responses, including consent, offline retry and cancellation.
The [engine progress tests](../native/Relicade.Tests/FreedoomEngineProgressTests.cs)
exercise the actual confirmation composition, mouse/keyboard/controller routes,
original-context checks, setup-only cancellation, delayed owned-transfer drain,
late committed-engine retention and terminal retry. Synthetic operation renders
check the ordinary/minimum-size dialog separately from installer behavior.
The [library action tests](../native/Relicade.Tests/FreedoomLibraryActionTests.cs)
cover separate setup/play routing. These source checks do not accept real engine
execution, all catalog projects or target platforms.

The [pinned free-data source checkpoint](https://github.com/MrDiamondBallz/relicade/commit/d0f1d3cb9b5196cc4a555a924d4f54f93636e379)
records bounded Linux x64 source tests, not an AppImage runtime pass. Two narrowly
scoped unit records are linked from the coverage contract:

- `FreedoomDataDownloadTests.Verified_archive_data_and_original_notices_are_retained_and_reused_without_another_request`:
  passed once in the selected run. Synthetic archive/data/notice bytes matched,
  originals remained, and reuse made no additional request.
- `FreedoomDownloadLifetimeTests.Cancellation_mid_body_cleans_only_owned_partial_and_releases_cache_writer`:
  passed once in the same run. Controlled mid-transfer cancellation removed only
  the operation's incomplete file and released writer authority.

The exact test sources are [download tests](https://github.com/MrDiamondBallz/relicade/blob/d0f1d3cb9b5196cc4a555a924d4f54f93636e379/native/Relicade.Tests/FreedoomDataDownloadTests.cs)
and [lifetime tests](https://github.com/MrDiamondBallz/relicade/blob/d0f1d3cb9b5196cc4a555a924d4f54f93636e379/native/Relicade.Tests/FreedoomDownloadLifetimeTests.cs).
Run them from that source using `dotnet test native/Relicade.Tests/Relicade.Tests.csproj --filter FullyQualifiedName~FreedoomDataDownloadTests`
and the corresponding `FreedoomDownloadLifetimeTests` filter, with the documented
telemetry opt-outs. These are reproducible test definitions, not a claim that a
future rerun has already passed. The two coverage rows remain work in progress
and inconclusive as complete requirements. Live-network behavior, general queues,
crash recovery, native packaging, gameplay and other target platforms remain
unaccepted.
