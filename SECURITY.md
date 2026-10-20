# Security

Relicade is a development preview, not a released game installer or an executable sandbox.

## Reporting

Do not include credentials, personal data, private screenshots or exploit payloads in a public issue. Use GitHub's private vulnerability reporting when available. If no private reporting path is available, open a minimal issue requesting a secure contact route without disclosing the vulnerability or sensitive details.

## Current boundary

- Browser preview: a local metadata snapshot and browser-local saved project IDs. No native install/launch privileges, account service or telemetry.
- Local preview server: loopback-only, restricted asset roots, no directory listings or mutation methods, Host validation, no-referrer policy and restrictive CSP.
- External project/source links are third-party destinations, not security endorsements.
- Online artwork contacts the credited SteamGridDB provider at runtime, exposing IP/user agent but forwarding no credentials or referrer values. Explicit offline artwork mode (`?art=offline` on first visit) uses bounded decoded cache/original fallback without provider requests; local static shell/snapshot requests remain. Worker CSP and exact-host/path validation restrict transport; unsupported servers/browsers fail closed. Browser opaque responses cannot provide native byte/DNS/content-integrity guarantees. See [runtime artwork](docs/ARTWORK.md).
- Metadata refresh remains an explicit metadata-only developer operation with network-boundary tests; it does not download game art.
- Project tiles are original presentation art. No ROMs, BIOS or commercial game files are supplied.

## Merged native source boundary

The native development source now includes staged portable installation and journaled generation recovery, OS-vault credential sessions with explicit legacy migration, offline instance shortcuts/lifetime handling and managed-removal loss-prevention guards. These are bounded source changes, not full runtime/security acceptance. Removal refuses managed/recovery trees rather than implementing managed uninstall; managed game/mod activation is not universally supported. No packaged launcher release or complete platform acceptance is claimed. [Native limitations](native/README.md) remain important.

Vault failures are explicit and have no general-settings plaintext fallback. Legacy plaintext credentials require an informed preview/confirmation flow; the existence of migration code does not mean every previous copy has been scrubbed. Sensitive old originals/backups/rescue files and filesystem/OS remnants remain a separate privacy/retention problem. The redundant portable backup cleanup source only removes explicitly previewed/hash-confirmed duplicate immutable payload files from older committed operations, retaining all previous generations, data snapshots, journals and current-operation backups. It does not erase credentials, securely erase files, implement general history pruning or provide managed uninstall. Its durable local approval is not publisher authentication; hostile OS writers and native/power-loss guarantees remain unaccepted. Never put credentials or migration diagnostics into public docs/status inputs.

Future emulation requires separate emulator/core licensing and user-supplied data/BIOS handling; no unlicensed ROMs, BIOS or keys will be distributed. An emulator/core is an additional execution trust boundary, not a sandbox claim or an implemented capability.

Source changes affecting security must trigger the [documentation review gate](docs/DOCUMENTATION.md), not automatic semantic approval.

## Native release requirements

Download, extraction, update, launch and mod operations require trust, signature/digest, path/link containment, transactional recovery, permissions and fault-injection tests. Executed games run with user authority unless a separately tested sandbox explicitly says otherwise.

Provider credentials belong in platform credential storage, never repository files or library exports. Importing another application's credentials requires informed user consent. Diagnostics must be reviewed and redacted before sharing.
