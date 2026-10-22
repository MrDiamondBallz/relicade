# Local game-data setup

Some community projects provide an engine or recompilation, not the original game
data. Installing an executable does not establish that the game is ready to play.
Relicade's native development source includes a first package-bound local setup
flow. It is not a packaged release, gameplay certification or complete catalog
setup support.

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

1. Install the supported managed portable package. On its first installation,
   Relicade selects the pinned US executable and checks its size and SHA256 before
   activation. Updates retain an existing explicit launch selection; this default
   does not choose for unknown or ambiguous packages.
   Linked, shared, OS-package and unresolved recovery installations require
   separate support. An unsupported package says its requirements are unconfirmed.
2. Choose **Finish game setup** from the library card or its menu.
3. Select your lawful local `.z64` file. Relicade checks the installation, variant
   and available storage without modifying the selected file.
4. Review the displayed source, exact destination and additional bytes required.
   Confirm lawful use and the local copy explicitly. Selection alone does not copy.
5. After a verified result, close the dialog and select **Launch**. The shared
   launch path revalidates the selected package, executable, local receipt and
   data before passing the fixed `--rom-file` argument. Shortcuts do not bypass
   this data check. Native gameplay and hardware compatibility still need testing.

The original is never moved, linked or overwritten. Setup uses a separate
`owned-game-data` directory under Relicade's user-data location, keyed to the
installation. It is not injected into an immutable game generation. Existing
copies and conflicting receipts are preserved, not silently replaced.

## Cancellation, recovery and limits

Cancellation can leave an already committed copy in place. A fresh preview may
recognize identical retained bytes and explicitly adopt them for the current
generation without overwriting them. Partial uncommitted staging created by the
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
whether to install the exact UZDoom package above. Declining makes no download.
The selected release, asset name and official download address must still match;
the downloaded bytes must match the compiled adapter hash even if remote metadata
reports a different digest. Header and streamed bytes are bounded to the pinned
package size before writing beyond it. There is no fallback to another release or asset.
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

Cancellation prevents the handoff from opening data setup. An owned package transfer
is drained; an already committed engine can remain installed if cancellation occurs
at completion. Reopen the entry to inspect its actual state before retrying. This is
not a promise that every cancellation removes all work or that a first-install guard
protects against hostile concurrent filesystem replacement.

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

## Source checks and remaining acceptance

The [first-install package tests](../native/Relicade.Tests/FirstInstallPackageTests.cs)
cover retained data/history, ancestor authority, immutable bytes and size bounds.
The [guided handoff tests](../native/Relicade.Tests/FreedoomEngineHandoffTests.cs)
exercise actual installation with non-runnable synthetic payloads and injected
metadata/download responses, including consent, offline retry and cancellation.
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
