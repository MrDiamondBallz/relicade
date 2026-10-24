# Windows Freedoom Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the existing explicit UZDoom/Freedoom setup and optional Play journey on Windows x64 with pinned packages and preserved data.

**Architecture:** Extend compiled adapters and the current first-install/owned-data authorities. Add a focused Windows play-profile service; preserve Linux adapters and ordinary engine Launch. The sole writer implements; a read-only reviewer checks immutable changes before publication.

**Tech Stack:** Existing pinned .NET10.0.402, C#, Avalonia, managed ZIP extraction, BCL cryptography/filesystem and xUnit; no new dependency or provider.

**Spec:** [Windows guided setup design](../specs/2026-10-23-windows-freedoom-design.md).

## Global Constraints

- Copy the exact package, executable and WAD pins from the spec; archive and executable digests differ.
- Cloud: source/synthetic only. Approved engine/data acquisition/execution: receiving Windows PC only.
- Existing Linux definitions default to Linux x64; Windows opt-in is only this compiled UZDoom adapter.
- Preserve all ownership, stale-context, preview/import, no-overwrite, recovery and data/history checks.
- Both commit identities: `MrDiamondBallz <264773240+MrDiamondBallz@users.noreply.github.com>`; normal reviewed pushes only.
- No paid services, hosted CI, provider/model changes, credentials, signing enrollment or premature release tag.
- Android SDK/license installation is deferred; Mac runtime access is unavailable.

## Review Focus

- Multiple ZIP executables: only pinned `uzdoom.exe` can become the guided target; helper/changed bytes refuse before activation.
- Case-equivalent/overlapping Windows paths: equivalent roots stay bound; another entry/root never gains authority.
- Interrupted transfer/import: no automatic transfer/recovery on reopen; prior generations, data and explicit retry remain visible.
- Existing profile/config alias or changed updater controls: preserve bytes and refuse; do not overwrite player preferences.
- Stock engine caches outside the redirected profile: record exact boundaries and reconcile PC scope before actual integrated launch.

---

### Task 1: Establish Windows adapter and staged-target contract

**Files:** `native/Services/OwnedGameSetupService.cs`, `GuidedEngineInstallService.cs`, `FirstInstallPackageConstraint.cs`, `GameDownloadInstallService.cs`; `native/Models/GameInfo.cs`; new `native/Relicade.Tests/WindowsFreedoomSetupTests.cs`.

**Interfaces:** Keep `FreedoomPhase2` as the Linux definition. Add `FreedoomPhase2Windows`, `SupportsFreedoomHere` and `CurrentFreedoomPhase2`. Add internal compiled `AllowZip` and `PinnedTarget` to `FirstInstallPackageConstraint`; consume that target before the existing ordinary-install target fallback. `InstallTarget.Validate` remains relative path/length/hash validation.

- [ ] Add Windows-native tests against existing `ProjectFor`/`PrepareFirstInstall` behavior: expected Windows ZIP, Windows confirmation, separate executable digest and exact size. Obtain the actual failing-before result on PC without acquiring a package.
- [ ] Implement platform selection and exact compiled pins; allow ZIP only through the compiled Windows adapter. Keep Linux AppImage/tar checks intact.
- [ ] Bind `uzdoom.exe` before selection; validate it after additional-file staging. Add synthetic ZIP with helper first, wrong/missing/changed executable, bad archive digest and changed context; chooser/activation must not run on refusal.
- [ ] Run synthetic Windows selection on PC, Linux existing handoff/progress regressions and source documentation checks. Review immutable diff, commit and verify remote ref.

### Task 2: Enable Windows owned-data authority without broadening other adapters

**Files:** `native/lib/Relicade.Core/Services/PortableInstallationTransaction.OwnedData.cs`, `native/Services/OwnedGameSetupService.cs`, `OwnedGameSetupFlow.cs`; Windows setup tests.

**Interfaces:** Add `OwnedDataPlatform` with Linux x64 default and Windows x64 opt-in, plus optional compiled executable-length metadata if required for the staged-target contract. Use `CurrentFreedoomPhase2` in optional Freedoom flows only.

- [ ] Add actual Windows synthetic red witnesses for valid import/receipt and unsupported Linux definition refusal before destination writes; use tiny synthetic WAD data and nonexecuted MZ payloads.
- [ ] Guard Unix directory/file mode calls by actual OS. Apply Windows-aware path equality/overlap to setup paths while preserving repository and exact library-byte checks.
- [ ] Exercise correct/wrong data, stale preview/context, cancellation, interrupted/reopened import, retained conflicting data, locked/link/alias cases where capabilities exist, and unchanged saves/receipt/library/backup bytes.
- [ ] Run affected Linux owned-data tests and native Windows tests; independently review, commit and verify publication.

### Task 3: Add bounded Windows optional-play profile

**Files:** new `native/Services/WindowsFreedoomProfile.cs`; `native/Services/GameLaunchService.cs`; Windows setup tests and argument tests.

**Interfaces:** `WindowsFreedoomProfile.Prepare(installRoot, dataRoot, requirement)` returns owned config/save/screenshot paths. `Configure(ProcessStartInfo, profile)` adds exact literal arguments. Existing `ConfigureOwnedDataArguments` keeps ordinary/Linux behavior.

- [ ] Write tests for first configuration creation, unchanged valid existing configuration, conflicting/duplicate updater controls, effective Vulkan preference1/replaygainfalse, nonregular/reparse/hardlinked configuration, unsafe overlap and long paths. Existing bytes must remain exact on refusal.
- [ ] Implement bounded regular single-link config inspection, create-new/durable publication, required disabled updater/node-write controls, and disjoint profile directories. Never replace an existing config or save.
- [ ] Configure literal arguments, owned-process execution and environment sanitation only for optional Windows Freedoom. Test spaces/Unicode/quotes as literal tokens, exact executable selection and unchanged ordinary/Linux arguments.
- [ ] Review documented upstream cache limits, Vulkan-to-GL/GLES automatic fallback and PC scope before any actual game start. A preference is not enforcement; keep that execution boundary unresolved until concretely reconciled. Run synthetic checks; review/commit/push.

### Task 4: Validate the native journey and reconcile docs

**Files:** `docs/GAME_SETUP.md`, `docs/FEATURE_COVERAGE.md`, `docs/ARCHITECTURE.md`, `native/README.md`, responsibility registry/generated status as required; native setup rendering tests.

- [ ] Render confirmation/progress/data/ready/error/cancel states with synthetic fixtures at ordinary/minimum sizes and scaling; ensure unsupported platform actions are not offered.
- [ ] Run the complete relevant Linux suite after production changes, and actual Windows setup/input/ownership/cancellation selections. Retain every failure/skip and exact source hash; do not repeat accepted probes without new context.
- [ ] Receiving PC: after cache-boundary reconciliation, verify exact approved package/notice hashes, real Relicade install/data/Play/save/load/normal exit/relaunch and launcher responsiveness; no other engine/game execution.
- [ ] Reconcile documentation against source and actual acceptance, cold-review immutable changes, publish and verify ref/identities. Integrate only accepted changes; official release remains separately gated.
