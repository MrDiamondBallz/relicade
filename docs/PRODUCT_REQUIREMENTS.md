# Relicade — product requirements and release gates

**Status: acceptance specification, not completed native readiness.** The current product includes a brand/browser preview and a buildable native development foundation. Native shared services and local/headless fixtures are not full functional, behavior-reconciliation or platform acceptance. Real installs, game launches, mod management, owned-data readiness and rollback remain requirements until their executable tests and per-platform evidence accept them. A design screenshot or cross-publish cannot accept a backend requirement. See [native build/status](../native/README.md).

**Position:** Old legends. New life. One coherent home for community-revived classics, retro games and emulation, including native ports, recompilations and recreations. Premium means players can understand what will happen, recover from mistakes and get back to a game—not inflated engagement metrics or visual effects. Follow [brand/BRAND.md](../brand/BRAND.md); retain community/project attribution and distinguish original games from the ports/projects that run them.

Release verification/checksums, durable library snapshots, version management, mod dependencies, controller input, kiosk mode and recoverable updates are baseline requirements—not novelty claims.

**Final-product contract:** complete applicable launcher functionality plus measurable improvements across Windows x64; Linux x64/ARM64 (AppImage and Flatpak packaging); macOS Intel/Apple Silicon; and Android APK. Every target needs independent package and runtime validation. No native platform release is delivered by the browser preview.

The phases sequence implementation, not permanent omissions. Baseline capabilities deferred to a later phase remain mandatory for the final product. Backend access, external licensing and unavailable hardware remain explicit unresolved dependencies rather than silently removed requirements.

## Staged broader vision — not a release-gate change

Discovery, setup and library journeys should eventually cover community-revived classics and retro games through native ports/recompilations/recreations and explicitly emulated project types. Future emulation requires compatible emulator/core identity, separate license review, transparent setup and lawfully user-supplied game data/BIOS. No ROM, BIOS or key distribution is implied. Account-optional offline use and controller/keyboard-complete journeys remain the direction.

Emulation is not implemented, accepted or added as an automatic condition of the existing native release gate. Define its own adapters and evidence before claiming support. The current feature matrix and all existing native acceptance requirements remain unchanged.

Merged development source includes bounded staged-installation/recovery, explicit vault-backed credential migration, offline instance shortcut/lifetime, managed-removal safeguards and explicit preview/hash-confirmed cleanup of older committed redundant immutable backup payload files. Cleanup preserves all previous generations, data snapshots and current-operation backups; it is not credential erasure, general retention or managed uninstall. Those milestones do not accept all R1–R10 journeys or any packaged/native platform release. [Documentation maintenance](DOCUMENTATION.md) keeps facts separate from semantic and release review.

The native library cleanup UI included in this source adds displayed-plan confirmation,
cancel and explicit same-plan resume over that bounded service; it is not
packaged or native-platform accepted. It accepts no additional R1–R10 or platform cells.

## 1. Scope and milestones

| Milestone | Deliverable | Claim permitted only after acceptance |
| --- | --- | --- |
| Foundation | Original identity, public-metadata discovery/detail browser prototype, locally persisted saved-library state, explicit unavailable native actions, source/binary/runtime evidence ledger | “Product prototype”; no “installed,” “playable,” fake downloads/playtime or launcher replacement claim |
| Native alpha — Windows first | Real local library + signed release ingestion + download/install/launch for an explicit portable-package compatibility set; readiness/import, recovery and controller/keyboard core journeys | “Windows alpha” with per-project support/limitations; no silent unsupported package fallback |
| Windows release gate | All P0 requirements, Windows parity items, fault/security/accessibility/hardware tests, migration/export/recovery, signed package/update feed | “Windows launcher”; “better” requires comparative task/performance evidence, not feature counting |
| Full-platform parity | Windows, Linux x64/ARM64, macOS x64/ARM64 and Android; all applicable baseline capabilities and package adapters; additive improvements separately measured | Final release requires tracked parity tests and build/runtime evidence across every required platform; blocked or unavailable hardware is not a pass |

**P0:** required for the Windows milestone. **P1:** staged implementation after that milestone; baseline-parity items remain mandatory for the final product. A supported-project matrix must identify OS, architecture, format, release channel, mod adapter and game-data prerequisites. Temporarily unimplemented MSI/Flatpak/APK/DMG/runner cases get a factual explanation and project-site link, not an enabled install button. Full baseline-equivalent platform breadth is not the first Windows milestone, but is a final release gate. Android launches supported APK applications, not desktop binaries.

## 2. Player-facing information architecture

- **Discover:** original-game and project views, platform/type filters, concise project status and attribution; no storefront/account funnel. A project page states source URL, release/version, platform, package, verification evidence, game-data requirements and last metadata refresh.
- **Library:** continue/recent only from real launch records, search, sort, filters/collections, explicit readiness, selected version/profile and primary next action. Preserve focus/scroll during background updates. No full-screen metadata wait for local launch.
- **Downloads:** one durable queue for game/mod payloads with per-item stage and action; never conflate downloading, verifying, extracting and committing.
- **Game workspace:** Play / Finish setup / Repair reflect actual state. Versions, data, mods, files and recovery are contextual sections, not unrelated modal chains.
- **Settings:** input/accessibility, storage, network/privacy, update policy, export/recovery and diagnostics. Advanced controls are disclosed, not required to start. No default music or involuntary autoplay.

## 3. Minimum parity contract

| Area | Baseline capabilities to respect | Relicade release requirement |
| --- | --- | --- |
| Catalog | Paginated browse, detail/readme/reviews, platform filters, original-game grouping, custom lists | P0 discover/detail/search/filter and source attribution; custom list import with validation/provenance; readme/review parity required for final release, with lawful data sourcing/moderation access an explicit dependency |
| Library | Manual apps, existing folders, per-instance identity, search/sort/tags/custom presentation, offline startup | P0 add/link/manual entry, remove without deleting data, search/sort/filter/custom names/art, per-project stable IDs and duplicate/folder conflict handling |
| Storage/recovery | Fail-closed apps.json, deduplicated snapshots, legacy migration, export primitives | P0 equivalent durability plus visible restore/import preview and tests; never silently replace a corrupt library |
| Releases | Preferred/verified/latest releases, asset choice, skip and per-app auto-update, rolling-build detection | P0 explicit version/channel/pin/skip/update review; expose missing pin instead of silently changing intended version; preserve exact release/asset identity |
| Trust | Verified/unverified/blocked states, SHA-256 checks, warnings and reported scan verdicts | P0 separate provenance/digest/review/scan/unknown states; mismatch blocks; unavailable verification never becomes “verified” |
| Install/launch | Archive/single binary; OS-specific MSI/Flatpak and runners; executable selection | P0 supported Windows portable formats + real native launch/repair; MSI P1 until OS-ownership adapter tested; all other formats explicitly unsupported until accepted |
| Mods | Thunderstore/GameBanana, dependencies, update/remove, file/layout selection | P0 supported-provider browse/install/remove/dependency parity with failure preservation; P1 full multi-profile/conflict-resolution gate below |
| Input | Rebindable gamepad/keyboard, menus/modals, text entry/Steam OSK | P0 mouse/keyboard + controller-complete core journeys; Steam OSK/Deck P1 native Linux gate |
| Kiosk/desktop | Fullscreen/optional PIN, tray, close-after-launch, shortcuts | P0 intentional fullscreen/controller mode, application-level kiosk, safe exit, Windows desktop shortcuts; Steam/tray conveniences P1 unless needed by committed journeys |
| Updates/privacy | Launcher self-update with metadata backup; opt-in usage data | P0 signed isolated self-update, recovery, no accounts required, telemetry off by default and documented operational traffic |

A deliberate deferral must be listed in release notes. Quietly omitting an existing competitor capability does not satisfy parity.

## 4. Differentiators as measurable targets

### R1 — Truthful readiness and owned-data setup (P0)

Represent at least: **Not installed → Queued → Downloading → Verifying → Installing → Needs game files / Needs runtime / Ready → Running**, plus **Update available, Repair needed, Failed, Cancelled** as appropriate. Readiness is derived from persisted installation manifest, available executable/runtime and project-specific prerequisite checks—not the presence of a folder or a mock progress state.

- Every blocked primary action names its missing requirement and next safe action. Unknown prerequisite metadata says “Requirements not confirmed,” not “Ready.” Project adapters can declare “No original game data required” only with upstream evidence.
- Local file picker/drop workflow states accepted variants/hash algorithms, exact destination, copy/link behavior and storage estimate before confirmation. A hash recognizes a data variant; **it does not verify ownership or legal entitlement**.
- Never obtain/distribute ROMs/BIOS/keys or send raw user game files/hashes to a remote service by default. Do not overwrite/delete the selected original; do not embed local paths or game data in source control, exports or analytics. Detect missing/removable/changed source files if link mode is offered; default copy into a protected user-data area with explicit consent.
- **Accept:** synthetic matching/wrong-region/invalid/truncated/locked/missing/renamed files and cancel-at-each-step fixtures; mismatches explain expected variant without deleting either file. Supported project with missing data cannot show Ready/Play. Offline accepted data can launch without contacting the catalog. New project adapters require a documented upstream prerequisite source and native launch test.

### R2 — Durable download operations, not progress theater (P0; conditional resume P1)

Persist jobs by project/release/asset identity with states queued, resolving, downloading, verifying, staging, committing, completed, failed, cancelled and interrupted. Show real bytes/total when known; unknown total is indeterminate. Queue positions/retry counts are not simulated.

- P0 global view, reorder pending items, cancel/retry, bounded configurable concurrency (default two payload downloads; one commit per game), actionable errors, available/required disk-space check and content-length-independent completion. Different jobs cannot write the same install concurrently. App/mod tasks share user-visible status, without pretending package-manager work is fully interruptible.
- P0 cancel reaches network streaming promptly: target **p95 ≤1 second** on the controlled throttled endpoint; it never publishes a partial install. At a noninterruptible commit/OS-installer phase, explicitly show “Finishing safely” and honor cancellation at the next safe boundary.
- P1 resume is **capability-dependent**: only verified Range support + stable ETag/Last-Modified/asset identity. A changed resource, ignored Range, mismatched size or missing validator restarts safely with an explanation. Saved progress is never trusted as integrity evidence; verify complete bytes before commit. Do not advertise resume before server/protocol tests pass.
- **Accept:** controlled HTTP fixtures for disconnect, 200/206/416, unknown length, redirect, 403/429/5xx, expired URL, ignored Range and changed ETag; restart launcher at 25/75% and after verify; deduplicate identical jobs; cancel one job without corrupting or cancelling others. Capture real stage/byte transitions and final disk hashes.

### R3 — Transactional portable upgrades and rollback (P0)

Download/verify/extract into isolated staging; build a manifest and validate launch target before activation. Preserve prior immutable payload and a durable operation journal. Commit uses atomic same-volume activation where supported, or explicitly journaled recoverable steps; no in-place merge masquerading as a transaction.

- Separate launcher-owned payload, project user data, original game files, saves and mod-profile state. Never infer all saves are under the install directory. Adapter must declare known data locations and uncertainty. Default uninstall keeps user data; removal and uninstall are distinct confirmations with path/size preview.
- P0 retain the previous successful payload until explicit cleanup or declared retention policy; one-step rollback selects that payload and compatible mod state without downloading. Warn before irreversible game save-format migrations; payload rollback does **not** promise save compatibility. Do not restore older saves without a separate informed confirmation.
- Refuse incompatible/running-game upgrades; pending update can wait for exit. A disk-full, access-denied, antivirus lock, crash or power loss must recover to the previous or new complete manifest—not a mixed manifest.
- **Accept:** inject failure/terminate at each journal boundary and during extraction/activation/metadata save; restart and verify manifest/files, selected version and user data. Rollback succeeds offline for retained portable payload. Before/after hashes of synthetic ROM/save/config fixtures must match unless an explicitly confirmed data migration is under test. MSI/OS package rollback is adapter-specific P1 and must not inherit portable guarantees.

Bounded R3 source progress includes the native library's **Restore previous
portable version…** preview/hash-confirmation/cancel/result journey. It selects
only the immediate complete retained portable payload offline, preserves CURRENT
declared saves/data and never restores previous saves. Compatibility, external
data adapters, compatible mod activation and all native/package acceptance remain
open. Unknown/interrupted history requires separate inspection/recovery; preview
does not repair it and this journey does not implicitly resume transactions.

### R4 — Safer mods today, reproducible profiles next (P0/P1)

- P0 resolve dependencies before mutation; stage replacement before removing a working mod; preserve installed records on network/parse failure; store manifest changes atomically. Present package/file/provider/version and permissions/trust boundary. Never label mods “safe” merely because the game release is verified.
- P1 named independent profiles (e.g. Vanilla and Custom) contain exact package versions, selected files, order where meaningful and dependency constraints. Keep game payload immutable and activate a profile separately. Profile switching is reversible/offline for cached content; never silently affect another profile.
- P1 detect missing/incompatible dependency constraints, dependency cycles and overlapping destination paths before activation; show both owners and resolution choice. Do not claim to detect every semantic/gameplay conflict; distinguish **file collision**, **declared incompatibility** and **unknown compatibility**. Snapshot profile before changes; dependency removal shows impacted dependents.
- **Accept:** synthetic A→B→C dependency graph; conflicting B versions; A↔B cycle; two packages targeting one file; failed update retains previous bytes; switching/removing profiles preserves Vanilla/user data. Export/reimport exact manifests reproduces supported cached payload hashes. Provider APIs/terms/version availability and game-specific load order remain feasibility dependencies.

### R5 — Controller-first and keyboard-complete (P0)

Discover → details → add → library → supported install → data picker → launch, downloads/cancel/retry, settings, safe removal and kiosk unlock must be usable without pointer emulation. Controller connection changes must not steal typing focus; reconnect restores a meaningful focus target. Prompts reflect active input/binding, not hardcoded button glyphs.

- Visible focus everywhere, predictable Back/Escape, no focus traps or click-through behind modals; return focus/scroll to originating item. Rebind and reset keyboard/controller independently; text editing cannot accidentally trigger navigation/destructive actions.
- Controller/fullscreen mode uses readable large targets and removes nonessential chrome without hiding errors or exit. Optional kiosk PIN and unlock-rate limiting protect app configuration only; document **not OS lockdown**, no protection against an administrator/physical attacker.
- **Accept:** scripted keyboard paths and real Xbox/XInput + one SDL-recognized non-XInput controller; unplug/replug mid-modal, held-stick repeat, two controllers, mixed mouse/keyboard/controller, focus after game exit. Every core journey completes controller-only except native OS dialogs that require an explicit documented accessible fallback. Onscreen keyboard behavior must be observed, not inferred from URI dispatch.

### R6 — Fast, stable library (P0)

Reference acceptance device: documented Windows 11 x64, six physical CPU cores, 16 GiB RAM, SSD, fixed release build, power mode and display; publish actual hardware and fixture, not a simulated machine label. Benchmark **1,000 synthetic library entries** with cached artwork; zero real game files required. Thirty measured runs per scenario, cold/warm definition recorded; report p50/p95 and failures.

- Targets: cold local library actionable **p95 ≤2 seconds**; warm **≤1 second**; search/filter response **p95 ≤100 ms after debounce**; no blocking network call before local Play. Target render interaction frame time **p95 ≤16.7 ms at 60 Hz** during a fixed scroll; trace separately from load timing. Idle CPU target ≤1% of total machine CPU over 60 seconds after settling; idle working set target ≤300 MiB, cached-art fixture fixed.
- Retain item identity/focus/selection/scroll while metadata arrives. No unbounded image downloads/caches; lazy/virtualized artwork and deterministic sorting. Render 10,000-entry stress fixture without crash, even if performance targets initially cover 1,000.
- **Accept:** offline/DNS blackhole/slow API/429 cases; library remains navigable and native launch works. Record instrumented timings plus native process/render metrics. Compare the same journeys/fixtures against the reference launcher where legally and operationally feasible; do not publish “faster” from unmeasured impressions.

### R7 — Accessibility and DPI are release criteria (P0)

Meet applicable WCAG 2.2 AA web criteria for prototype and equivalent native interaction requirements; this is a target, not a certification. Normal text contrast ≥4.5:1; large text and required control/focus indicators ≥3:1 against adjacent colors. Status is never color-only. Shared tokens, real project art and restrained motion implement the premium direction.

- Complete keyboard path, semantic names/roles/states, visible labels and actionable error announcements. Native Narrator reads library items, readiness, progress and dialogs; label a button by action, not icon filename.
- Respect OS reduced motion and high contrast; no mandatory marquee/parallax/animated backgrounds. User can enlarge text without losing actions; no hiding essential content on hover. Minimum routine interactive targets 44 logical px; controller primary actions 48 logical px.
- **Accept:** actual Windows 100/125/150/200% DPI, 1280×720 logical minimum, 1920×1080 and 4K, mixed-DPI monitor move, 200% text scaling/browser zoom as applicable; no clipped dialogs/focus, unreachable actions or horizontal overflow in core journeys. Automated checks plus manual keyboard/Narrator/high-contrast/reduced-motion audits; browser emulation alone cannot accept native DPI or accessibility.

### R8 — Offline-first privacy and no coercion (P0)

No required account, subscription, launcher-provider API token or analytics consent for local library/launch/import/export. Provider rate limits remain possible without tokens and must produce a recoverable explanation, not an account wall. Tokens are optional and least-privileged; native secrets go in the OS credential vault, not serializable library/settings/export files.

- Telemetry off by default, affirmative opt-in only, same prominence for decline, no repeated nagging after decline. Describe each operational request separately from analytics: catalog refresh, asset download, update checks and any broken-download/security report. Reports outside analytics require explicit policy/consent; an Offline/network-disabled mode emits **no application requests** and pauses checks/queues. Do not equate analytics-off with no networking.
- Show metadata freshness and cached/unknown verification state offline. No upload of ROMs/keys/save data/local paths/search text, no undisclosed installation fingerprint. Remote images may expose an IP to third parties; disclose/cache them and offer network-disabled behavior.
- No fake urgency/player counts, forced marketing, disguised external links, prechecked destructive options or installed-state theatrics. User-approved actions only; no silent migration of another launcher's credentials/settings.
- **Accept:** network capture for first run, decline, opt-in, revoke and Offline mode; revoke stops pending analytics and rotates/removes optional ID per policy. Scan exports/logs/repository using synthetic secret/path canaries; no token or protected fixture leaves. Inspect credential-vault entries and deletion without printing credentials.

### R9 — Export, restore and actionable failures (P0)

Versioned human-readable metadata export includes stable IDs, local library presentation, selected releases/profiles and source provenance. Exclude secrets and user game files by default; show any local-path disclosure before export. Actual save/ROM backup is a separate optional flow with per-project support, not implied by “library backup.”

- Import preview shows adds/duplicates/changes/path conflicts; no live mutation until confirmation. Snapshot before apply; validate schema/version, URL/path policy and complete document before commit. A corrupt local store opens recoverable read-only/error mode, never an empty-success replacement.
- Recovery center lists snapshots with time/content identity and scope, previews restore and supports offline recovery. Settings/mod-manifest corruption is not silently discarded. Errors say which operation failed, whether old data is intact, and offer retry/repair/open location as appropriate.
- Diagnostics stay local until explicit review/export; default redact user paths, credentials and protected content; raw reports must be opt-in and visibly sensitive. Avoid claiming perfect automated redaction.
- **Accept:** round-trip fixture, duplicate/legacy/future-schema/partial/corrupt JSON, simultaneous writes, disk-full/permissions, corrupted backup, bad profile sidecar; originals preserved until valid commit. One guided restore returns a consistent library without backend/network access.

### R10 — Supply chain, filesystem and update safety (P0)

- Explicitly distinguish publisher signature, trusted manifest signature, expected digest match, catalog human review and scan result. SHA-256 from the same untrusted source is not independent publisher authentication. No “verified” without recorded evidence/version/time. Unknown verification requires clear user decision; digest mismatch never silently overrides or installs. Blocked releases are not eligible for auto-update.
- Validate archive paths, drive/UNC/device/ADS forms, normalization/case behavior, traversal, links and destination ownership **before writes**; enforce expansion/file-count/size budgets and free-space policy. Isolate extraction; no network catalog/import entry may choose arbitrary destructive destinations. User-linked existing folders remain explicitly scoped and never receive blind whole-folder deletion.
- Allow only supported URL schemes; constrain credential forwarding across redirects; separate host metadata policy from asset CDN policy. No arbitrary command construction from remote metadata. Portable launch is not sandboxed; tell the user executables run with their account authority.
- Own application/package/data/update identities. Signed native installer and authenticated update feed with documented trust root/key rotation, checksum verification, prerelease policy, consent/defer and safe failure/restart recovery. Do not consume the reference launcher's updater or telemetry channels as Relicade. Self-update must not run while commits are active or include user-game-data migration silently.
- **Accept:** adversarial synthetic ZIP/7z/RAR/tar fixtures for supported formats, links/preexisting junctions, traversal, zip bombs/size limits, locked destinations and unexpected executables; no writes outside granted roots. Test altered installer/feed/package, wrong product ID, obsolete/replayed versions, interrupted download/restart, antivirus/UAC and data-root migration on a clean Windows user. Produce SBOM/licenses, signed-package verification and dependency review. No unsupported archive format gets an optimistic fallback.

## 5. Implementation dependencies — not a frontend promise

| Capability | New work / boundary | Required proof |
| --- | --- | --- |
| Browser prototype | Metadata snapshot, local saved IDs, UI state, original brand; no arbitrary native filesystem/process privilege | Browser interaction/storage-failure/a11y/render tests; visibly unavailable installs/launch |
| Queue/install/rollback/readiness | Native privileged host/service APIs, operation journal, durable manifests, extraction policy, project adapters; browser UI alone cannot provide these | Unit/fault/security fixtures + packaged Windows integration + actual launch |
| Catalog/trust | Licensed/reusable catalog provenance; optional independently operated API or signed static feed/cache. Reference backend source/operations are separate, not present in this audit | Schema contract, moderation/verification/key policy, stale/offline tests, rights review; do not claim own backend is live |
| Mod profiles | Provider integration/terms, available historical artifacts, constraint/file-ownership resolver and profile activation | Dependency/collision/provider fixtures; supported-game adapter + Windows runtime tests |
| Owned-data setup | Per-project requirements/recognized variants, local hashing/import permission and protected data directory | Synthetic tests and consented native tests; no ROM service required or permitted by this specification |
| Controller/a11y/performance | Native focus/input/OSK/automation integration, virtualization/instrumentation and physical devices | Hardware runs, Narrator/DPI evidence and recorded benchmarks |
| Distribution/self-update | Distinct identifiers, signing credentials outside Git, independent authenticated feed/package storage, migration/recovery | Clean install/update/uninstall/rollback matrix and signed artifact verification |

A custom backend is not required for local/offline library use. Every service outage must degrade to cached metadata/local launch, except an operation genuinely requiring a new remote payload. No protected user files are needed to build/test the launcher itself.

## 6. Evidence and sign-off protocol

Each accepted requirement records **build commit, platform/hardware, fixture, command/test ID, actual output, artifact/log path and result**. Keep screenshots subordinate to filesystem/process/network evidence for side effects. Mark blocked/not-run/inconclusive distinctly. Redact user identities/secrets; retain only redistributable test data.

Release cannot pass with fake successful downloads/launches, a screenshot-only native workflow, unmeasured “faster,” a backend stub returning success, or a broad “fully reverse engineered” claim. Source inspection informs design; it does not substitute for binary tracing, native runtime tests or demonstrated Relicade behavior.
