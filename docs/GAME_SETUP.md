# Local game-data setup

Some community projects provide an engine or recompilation, not the original game
data. Installing an executable does not establish that the game is ready to play.
Relicade's native development source includes a first package-bound local setup
flow. It is not a packaged release, gameplay certification or complete catalog
setup support.

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

1. Install the supported managed portable package and select its US executable.
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
