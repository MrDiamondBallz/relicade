# Security

Relicade is a development preview, not a released game installer or an executable sandbox.

## Reporting

Do not include credentials, personal data, private screenshots or exploit payloads in a public issue. Use GitHub's private vulnerability reporting when available. If no private reporting path is available, open a minimal issue requesting a secure contact route without disclosing the vulnerability or sensitive details.

## Current boundary

- Browser preview: a local metadata snapshot and browser-local saved project IDs. No native install/launch privileges, account service or telemetry.
- Local preview server: loopback-only, restricted asset roots, no directory listings or mutation methods, Host validation, no-referrer policy and restrictive CSP.
- External project/source links are third-party destinations, not security endorsements.
- Normal bundled-preview use requires no remote runtime requests. Metadata refresh is an explicit developer operation with network-boundary tests.
- Project tiles are original presentation art. No ROMs, BIOS or commercial game files are supplied.

## Native release requirements

Download, extraction, update, launch and mod operations require trust, signature/digest, path/link containment, transactional recovery, permissions and fault-injection tests. Executed games run with user authority unless a separately tested sandbox explicitly says otherwise.

Provider credentials belong in platform credential storage, never repository files or library exports. Importing another application's credentials requires informed user consent. Diagnostics must be reviewed and redacted before sharing.
