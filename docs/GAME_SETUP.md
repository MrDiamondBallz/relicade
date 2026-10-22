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
not acceptance of every catalog asset or platform. Noncatalog source policy and
catalog reachability are separate concerns; this change does not certify them.

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

The library offers **Play Freedoom Phase 2…** for the UZDoom project. This is an
optional, explicit data selection, not a replacement for normal engine Launch or
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

Use the dialog's official release link, download and extract the archive yourself,
then select `freedoom2.wad`. Review the local source, destination and storage
estimate before confirming the copy. The original is retained. Select **Play
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
