# Feature and platform coverage

This is an acceptance contract, not a native compatibility announcement. The
[product requirements](PRODUCT_REQUIREMENTS.md) remain normative, including all
subcriteria in a referenced section. P0/P1 sequence delivery; P1 baseline items
are not optional at final parity. `FEATURE_COVERAGE.json` expands the contract
into stable feature IDs and explicit target cells. It deliberately contains no
accepted native results. Passing the validator accepts the document's integrity,
not the launcher.

## Run the contract and fixture checks

Python's standard library is sufficient:

```sh
python tools/validate_feature_coverage.py
python -m unittest discover -s tests -p 'test_*coverage*.py' -v
python -m unittest discover -s tests -v
```

Generate samples in a new isolated test directory (the parent must exist):

```sh
python tools/coverage_fixtures.py --output ./synthetic-fixtures
```

Do not extract adversarial archives with a general-purpose tool or open samples
in a production library. The generator writes only fixed, top-level filenames;
it does not create host links, launch applications, change permissions, contact
services or execute faults. Existing output directories are rejected. Keep
generated outputs outside source control. `manifest.json` records deterministic
SHA-256 values, not package authenticity or native pass results.

## Version-1 data contract

The executable schema is `tools/validate_feature_coverage.py`. Unknown fields,
wrong types, unsupported schema versions and duplicate IDs are errors. A schema
change requires validator and mutation-test changes together.

- Root: `schema_version` (integer 1), `product`, `scope`, `categories`,
  `boundaries`, `platforms`, `evidence`, `features`.
- `boundaries`: nonempty `inspected_sources`, `search_scope`, `unknowns` lists.
  Source inspection is input to requirements, never implementation acceptance.
- Platform: unique `id`, `os`, `architecture`, launcher `package`, game
  `payload_kind`, `implementation`, and `checks` (`build`, `package`, `runtime`).
  Each check has `status` and evidence-ID list. Build is source-build evidence;
  package validation and actual native runtime are separate gates.
- Feature: unique `id`, `category`, `origin` (`baseline` or `additive`), `priority`
  (`P0`/`P1`), normative `requirement` document/section identifier, nonempty
  `acceptance` and `fixtures` lists, and `targets` keyed by every platform ID.
  A section identifier such as `docs/PRODUCT_REQUIREMENTS.md § R3` is a section
  reference, not a URL fragment. Broad rows require every listed subcriterion;
  checking a single happy path cannot accept the row.
- Target: `applicability`, nonempty `reason`, `implementation`, `validation`,
  evidence-ID list. OS-specific exclusions are explicit, not missing cells.
- Evidence: unique `id`, `feature`, `platform`, immutable lowercase 40-digit
  `commit`, scenario `fixture`, executed `command`/test ID, `result`, actual
  `output`, shareable relative `artifact` locator, `hardware`/environment, `kind`.
  Keep sensitive raw artifacts outside public source; retain a sanitized result
  or review-accessible artifact under a relative locator. Never publish private
  machine paths or substitute an invented pass record.

Status vocabularies:

| Field | Values and meaning |
| --- | --- |
| implementation | `planned`, `wip`, `supported`, `unsupported`, `not_applicable` |
| validation/check status | `not_run`, `blocked`, `inconclusive`, `failed`, `accepted`, `not_applicable` |
| applicability | `required`, `conditional`, `not_applicable` |
| evidence result | `pass`, `fail`, `blocked`, `inconclusive` |
| evidence kind | `source_inspection`, `source_build`, `fixture_definition`, `unit`, `browser`, `package`, `native_runtime` |

`planned` means required but not delivered; `wip` means implementation underway,
not supported. `unsupported` preserves an applicable capability's open gap.
`conditional` means a project/provider/package capability requires an explicit
adapter and test; it cannot silently waive the requirement. A genuine OS/package
exclusion requires all three not-applicable statuses, an explanation, and no
acceptance evidence. The validator pins minimum applicability for baseline and
additive mandatory IDs; changing that policy requires reviewed schema changes.

A feature target becomes `supported`/`accepted` only with passing `unit`,
`package` and `native_runtime` records for its own feature/platform, scenario(s)
and the same build commit. A platform becomes supported only after separate
accepted source-build, package and native-runtime checks at the same commit.
Browser checks, source reading, cross-compiles and fixture construction cannot
replace native runtime. Unit tests include artificial in-memory evidence solely
to exercise schema branches; none is written to the product evidence ledger.
The validator checks attestations' structure and consistency, not their truth,
artifact existence, signatures, commit ancestry or completeness of assertions.
Independent review must inspect the exact commit, commands, outputs and artifacts
before recording acceptance. Do not use a successful schema check as sign-off.

## Platform boundaries

The eight independent targets are Windows x64; Linux x64/ARM64 AppImage; Linux
x64/ARM64 Flatpak; macOS x64/ARM64; Android APK. Launcher packaging is separate
from supported game payload formats. An AppImage build does not validate Flatpak
portals/host execution; a macOS source build does not establish signing,
notarization or Gatekeeper behavior. MSI, Flatpak, APK and DMG adapters must not
inherit portable rollback promises. Test data roots, permissions, credentials,
updates, recovery, input and accessibility on each target independently.

Android uses user-mediated package installation and observed package identity,
signing/version/readiness and launch activity. It does not launch desktop
executables. APK ABI/minimum SDK and macOS container compatibility remain explicit
implementation decisions, not assumed support. Original-data import and mods are
conditional on project/OS access. Desktop tray/CLI/shortcuts have no implied
Android equivalent; mobile lifecycle and package launch have their own gates.

## Fixtures and native adapter handoff

`tests/fixtures/scenarios.json` maps feature fixtures to input cases, expected
invariants, mode and generated artifact filenames. `generated` means small sample
bytes or JSON definitions exist, not that native behavior passed. `definition`
means the native test harness still needs to implement the scenario. Artifacts
use a synthetic test vocabulary; they do not prescribe the production library,
journal or mod-manifest schema. Adapters translate them explicitly.

- ZIP/path/link: valid two-file text payload, traversal/drive/UNC/device/ADS,
  backslash, reserved names, case/Unicode aliases, symlink target/ancestor,
  truncated ZIP, incorrect CRC and a 4 KiB payload exceeding a deliberately low
  1 KiB test budget. This is not a real archive bomb. Preexisting junctions,
  hardlinks, other archive formats, file-count and disk limits remain isolated
  adapter cases. No malicious entry name becomes a host output path here.
- Library: valid, corrupt, duplicate, legacy and future-schema documents;
  deterministic 1,000/10,000-entry inputs. Timing fields are never generated.
- Operations: a boundary/fault Cartesian-product definition plus unchanged-data
  sentinels. Implement native injection at download, verify, extract, journal,
  activate, metadata and cleanup; test disk-full, denied access, cancellation and
  termination. Restart and inspect actual old/new manifests and file hashes.
  Successful parsing of the schedule does not test transactions or recovery.
- Mods/readiness/privacy: chain/cycle/version/file-collision graphs, independent
  profiles/layouts, synthetic data variants and protected canaries. Native tests
  must prove failed replacements preserve old bytes, absent prerequisites block
  Ready, and exports/requests do not leak canaries. They contain no ROMs, saves,
  credentials or real executables.
- HTTP, queue, trust, input, platform, accessibility, shortcut and export cases
  also carry definition-only work: controlled endpoints, real devices, native
  process/network/filesystem observations and fault assertions remain required.

For each feature, connect its scenario to an adapter test first, reproduce the
failure, implement the behavior, then run its package/native journeys. Record the
actual tested build commit; a later evidence-document commit is not the build
identity. Split a broad row into additional stable sub-IDs if necessary without
removing the mandatory parent acceptance. Unsupported formats stay explicitly
unsupported, not optimistic fallbacks. No protected game data is needed.

## Scope and remaining unknowns

The matrix's `inspected_sources` pins selected actual upstream settings,
persistence, CLI, download/install, mods, shortcuts and Android adapter sources.
Those technical references preserve provenance without importing source code or
claiming a service/backend implementation. This is selected-path inspection,
not every branch/provider/dependency or source-to-binary equivalence. Main UI
binary member representation was unavailable at the analysis representation
budget; complete binary recovery is not claimed. Native runtime, backend
moderation/signing, provider terms/historical versions, project prerequisites and
all hardware/performance/accessibility acceptance remain open.

The existing browser preview regression suites remain useful for their own
surface only. Their results must not populate native acceptance cells. No feature
count, passing document validator or successful synthetic fixture generation
means full parity, universal compatibility or measured performance improvement.
