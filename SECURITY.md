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

## Native release requirements

Download, extraction, update, launch and mod operations require trust, signature/digest, path/link containment, transactional recovery, permissions and fault-injection tests. Executed games run with user authority unless a separately tested sandbox explicitly says otherwise.

Provider credentials belong in platform credential storage, never repository files or library exports. Importing another application's credentials requires informed user consent. Diagnostics must be reviewed and redacted before sharing.
