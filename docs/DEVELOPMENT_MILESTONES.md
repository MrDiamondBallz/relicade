# Development milestones and limits

This is engineering history for unreleased development source. See the
[project overview](../README.md) for the native application and the
[release gates](FEATURE_COVERAGE.md#first-public-release-gates) for remaining
acceptance. Earlier pull requests and evidence retain their original scope;
they do not certify later source or every platform.


**No packaged launcher release.** This is development source and a browser design preview, not complete functionality or accepted native platform support.

Current source also includes [guided engine-setup history](GAME_SETUP.md#interrupted-guided-engine-setup): saved reviewed intent and last stage, read-only reopening, and an explicit fresh retry for supported Linux x64 first-install adapters. A Library notice and bounded review can reveal a saved setup’s current entry even when filters hide it; reviewing history never starts setup. This is not a general download queue or automatic resume; game-file consent remains separate.

The Library also offers an [export-only metadata preview](../native/README.md#loaded-library-metadata-export) for its successfully loaded saved entries, including filtered entries. It excludes local installation paths, settings and game data, but included names and repository/release labels can still be personal. Review the exact fields before choosing a new file. This is not backup, import or restore.

Recent merged work includes [rollback-state consistency and explicit retained-file reuse](https://github.com/MrDiamondBallz/relicade/pull/28). Earlier work adds [separate CPU-rendered native UI tests](https://github.com/MrDiamondBallz/relicade/pull/11) for the library, offline catalog and settings views. The [earlier maintenance update](https://github.com/MrDiamondBallz/relicade/pull/12) aligns the Avalonia dependencies on the official 12.1.4 patch and adds input-session cleanup and application-lifetime regressions. These changes have independent local review and test coverage; they are development milestones, not a packaged release or a claim that every target platform has been tested.

- **Merged development source:** reviewed staged installation and recovery, explicit vault-backed credential migration, offline instance shortcuts and process lifetime handling, managed-removal safeguards, and [previewed, hash-confirmed redundant-backup payload cleanup](https://github.com/MrDiamondBallz/relicade/pull/3). Cleanup retains all previous generations, data snapshots and the current operation's entire backup; it is not credential erasure, general history pruning or managed uninstall. These are bounded source milestones, not a production-safe installer or a promise of compatibility with every game. See [the earlier source milestone](https://github.com/MrDiamondBallz/relicade/pull/1) and the [native guide](../native/README.md).
- **Included in this source:** the native library context menu exposes a redundant-backup preview, explicit displayed-plan confirmation, cancellation, truthful results and explicit same-plan resume. This UI is not released or native-platform accepted. **In progress:** full behavior reconciliation, packaged installation, security/rights checks and real platform/controller/accessibility acceptance remain open.
- **Longer term:** the broader retro-game and emulation experience is not yet implemented. Versions, mods, owned-data setup and recovery need complete tested player journeys, not just service code.

The library also includes **Restore previous portable version…**: an offline,
explicitly hash-confirmed preview of the immediate retained payload. Current
declared saves/data stay current; older saves are not restored and save-format
compatibility is not guaranteed. Interrupted or uncertain history requires
inspection, not preview/reopen repair or resume. A confirmed operation can attempt
transaction-internal recovery on failure; an error is not an unchanged-state or
safe-retry assurance. This is source-stage functionality,
not a packaged or native-platform acceptance claim; see the [native guide](../native/README.md).

The native source includes **Finish game setup** for specific Perfect Dark and
z2rs Linux x64 packages. New dedicated Perfect Dark and z2rs entries on Linux x64
offer **Set up game** to confirm their pinned engine download before selecting a
lawful local file. Review the copy,
confirm explicitly and revalidate before launch. After a compatible generation
change, **Use retained game files…** offers separate confirmation to reuse a
verified existing copy without its original or another game-data copy. This is bounded development
source, not gameplay certification or complete project support. See
[local game-data setup](GAME_SETUP.md) for the exact package and limits. The separate
**Set up / Play Freedoom Phase 2…** action can explicitly install one pinned
UZDoom package for the matching Windows/Linux x64 host in a new entry, then offer a separately confirmed local
data copy or free-data download. Windows guided Play remains unavailable in this
development candidate while its profile contract is implemented; install/data and
Close are separate from execution. Existing engines and normal Launch are not
replaced. The first engine setup has explicit confirmation, progress and its own
Cancel action; cancellation waits for owned work and preserves a committed engine.
Neither flow is complete catalog coverage or gameplay certification.

The browser preview supports a bounded selection of real public project metadata, search, filters, project details, keyboard navigation and browser-local saved-library bookmarks. Saved projects are not installed games. It does not install, update, verify or launch executables.

Native **All games** discovery now includes a pinned offline snapshot of 10,000 named Wikidata game records, with local search and bounded pages. Distinct source IDs keep editions separate; metadata-only records do not imply a playable download. An explicit title search can look for community projects in the existing source catalog. The bundled [metadata attribution and scope](../native/CatalogData/ATTRIBUTION.txt) covers CC0 structured data, without artwork or game payloads.

The [native development source](../native/README.md) uses .NET 10/Avalonia. Isolated Linux desktop and Windows native checks have exercised catalog search/details, metadata-only Add/Open in Library, settings persistence, export, close/restart and corrupt-library refusal. Reviewed Windows settings, controller lifetime, menu routing and generation-lease repairs are integrated through `627f6ea`; synthetic tests and bounded native journeys do not establish full platform or release acceptance. Integrated Windows gameplay acceptance remains pending. See the [evidence limits and ordered release gates](FEATURE_COVERAGE.md#first-public-release-gates).

**Windows x64 is the first public-release target.** Linux remains a private development proof path. Windows cross-publish and the [private Linux x64 AppDir/AppImage recipe](LINUX_APPIMAGE_DEVELOPMENT.md) are development checks, not supported downloads or platform acceptance.
