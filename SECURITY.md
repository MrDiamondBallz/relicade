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

The native library cleanup dialog included in this source (not released or native-platform accepted)
confirms only the displayed plan hash, defaults to Cancel and does not
implicitly confirm a changed plan or automatically resume. The shared typed
service checks persisted library/settings context and the session manager's own
injected recovery state again under Core authority and on explicit resume.
Detached persisted reads preserve the live settings object and effective values,
refuse malformed/missing-after-seen/legacy input and never initialize credentials.
Unsupported or uninitialized evidence providers fail closed without a Load fallback.
Inspection-row navigation and confirmation cannot authorize deletion.
That read-only recovery query does not prove arbitrary injected-store persistence
or cross-store atomicity. Closing a session cancels UI participation and drains
work; it cannot undo already deleted redundant copies or promise immediate
hashing cancellation. Cleanup's own operations add no network/save/general
initialization calls. Existing library-menu release prefetch is separate; this
does not certify product-wide offline privacy or native/hardware acceptance.

Future emulation requires separate emulator/core licensing and user-supplied data/BIOS handling; no unlicensed ROMs, BIOS or keys will be distributed. An emulator/core is an additional execution trust boundary, not a sandbox claim or an implemented capability.

Explicit portable-version rollback binds the immediate validated retained
payload, current declared-data inventory and live/detached persisted application
context to a displayed SHA-256. Apply independently revalidates under its own
composed writer/ancestor/mod authority, including before durable activation.
Preview never repairs history. Unknown, pending, linked, shared, package-owned
or uncertain launch/mod evidence refuses. Current declared data is copied, never
previous saves; external/undeclared data and save compatibility are not covered.
After durable Applying, activation finishes safely rather than promising an
immediate stop. Only an observed complete committed manifest is success; other
outcomes require inspection. The existing commit primitive can attempt internal
recovery during a failed confirmed operation, including restoring the old live
generation; an error does not certify unchanged state or safe retry. Preview,
outcome observation and dialog reopen initiate no new recovery, repair, resume or
retry. These bounded
local-source checks are not native-filesystem, hostile-writer or power-loss certification.

Source changes affecting security must trigger the [documentation review gate](docs/DOCUMENTATION.md), not automatic semantic approval.

## Native release requirements

Download, extraction, update, launch and mod operations require trust, signature/digest, path/link containment, transactional recovery, permissions and fault-injection tests. Executed games run with user authority unless a separately tested sandbox explicitly says otherwise.

Provider credentials belong in platform credential storage, never repository files or library exports. Importing another application's credentials requires informed user consent. Diagnostics must be reviewed and redacted before sharing.

## Empty marker files

Catalog marker preparation does not trust metadata as deletion authority. It
retains removed entries, refuses linked paths and creates missing empty files
without overwriting existing paths. These checks do not certify hostile
concurrent filesystem-writer resistance, owned-marker cleanup or game-data
readiness. See [the native marker boundary](native/README.md#catalog-marker-files).

## Local game-data setup

Compiled adapters pin a package and selected executable; remote catalog
metadata cannot override their file rules or destinations. The z2rs body CRC32/SHA-1
rule is a compatibility check, not modern authentication; a full-file SHA-256 and
actual length bind the confirmed local copy and subsequent revalidation. Local selection is
followed by a preview and explicit copy confirmation. Originals, committed copies
and prior receipts are retained. Input variant hashes do not establish ownership
or publisher authenticity. That local-import path creates no remote data transfer; product-wide
network behavior remains separate. Copies and receipts use owner-only Linux file
permissions, not encryption. Hostile concurrent filesystem writers, full power-loss
recovery, migration and real-game compatibility remain unaccepted. See
[the setup guide](docs/GAME_SETUP.md).

## Catalog download integrity

A release review flag does not verify a selected package's bytes. Native catalog
installation requires a usable published SHA-256 or explicit interactive consent
to proceed without it; automatic/headless flows decline the missing-digest case.
Valid nonrolling catalog digests retain precedence over publisher fallback digests,
and mismatches block activation. Syntax validation and a matching digest are not
publisher authentication, malware clearance or a sandbox. Known catalog identity is retained when adding a library entry. Missing or
malformed status evidence, unavailable check services and a missing explicit ID
produce an unconfirmed diagnostic instead of implicit noncatalog permission.
Exact cached identity remains usable; unknown independent repositories retain
separate policy. This is not complete metadata schema, freshness, authentication
or catalog identity-migration acceptance. Identity learned during a failed check
or retained on the current library object still requires confirmation; a later
empty refresh does not erase it. Legacy saved IDs are not silently rewritten.

The optional Freedoom setup pins a separate engine package and data SHA256,
requires local copy confirmation and an explicit Play action, and passes the
validated path as a literal `-iwad` argument. Ordinary engine Launch, working
directory and save/configuration locations are unchanged. An explicit free-data
button uses a separate no-authentication/no-cookie client, restricted official
HTTPS redirects, fixed byte budgets and pinned archive/member SHA256 checks.
The original ZIP (with notices) and verified WAD are retained, not overwritten;
only the operation's incomplete staging is disposable. Cached files must be
unlinked regular files. This is not an unattended install, signature verification
or a sandbox against hostile concurrent filesystem replacement.

Retained owned-file reuse is a distinct local consent action for supported compiled
adapters. It requires exact current package/executable/data verification and an
existing data-store writer identity, refuses pending staging or conflicting
receipts, and never overwrites earlier evidence. Both installation and data-store
writer authorities span the awaited guarded readiness callback. Cancellation or
changed UI context after receipt creation can retain that receipt without Ready.
This does not establish legal entitlement, hostile filesystem safety or power-loss
acceptance; the ordinary original-file and launch-time checks remain required.
