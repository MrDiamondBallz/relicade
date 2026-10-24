# Windows UZDoom / Freedoom guided setup

## Intent and authorization

Extend the existing explicit engine-install, separate free-data import and Play
journey to Windows x64. The user delegated routine design, source changes,
independent review and verified GitHub publication to the sole cloud writer.
Actual acquisition/execution remains limited to the exact approved packages on
the receiving Windows PC; cloud work uses source and synthetic payloads only.

This design is one release milestone. Broad native discovery with at least
10,000 real named game records, all declared platform quality, readable reference
Markdown, a real branded Windows install wizard and authenticated automatic
launcher updates remain separate work. No catalog count implies playability.
Android SDK/license installation is explicitly deferred; no Mac is available.
No paid services, hosted CI, provider/model changes, credential creation/copy,
signing enrollment, force pushes or premature release tags are authorized.

## Approach

Use a compiled Windows adapter in the existing guided transaction, ownership,
receipt, progress/cancellation and library-binding architecture. This is the
writer's implementation choice within the delegated scope. A parallel installer
or a generic removal of Linux guards would duplicate authority or incorrectly
broaden Perfect Dark/z2rs; ordinary UZDoom Launch stays unchanged.

The UI offers guided Freedoom only on a supported Linux/Windows x64 host before
presenting an action. Unsupported host builds retain discovery and ordinary
supported engine actions. Existing Linux adapter IDs and behavior remain intact.

## Exact compiled package and data contract

| Item | Pin |
| --- | --- |
| Repository / release | `UZDoom/UZDoom`, `5.0.3` |
| Windows asset | `Windows-UZDoom-Release-x86_64.zip`, asset `584674267` |
| Archive bytes / SHA256 | `29337585`, `3a41131845415bc4bb5474f8de301a77736bd858e2ab489926325a2675e8263f` |
| Selected executable | `uzdoom.exe`, `17872896` bytes |
| Executable SHA256 | `e6df57d1027cee089d33388ab743817a236148992ac3881d9141ea2b547677d9` |
| Data | existing Freedoom `0.13.0` archive/notices pins, `freedoom2.wad` |
| WAD bytes / MD5 | `28787748`, `cd666466759b5e5f63af93c5f0ffd0a1` |
| WAD SHA256 | `a8772e088847032510d97ba2312406a6998f21cbab44d4ff10696faa9c0ecd4b` |

ZIP and executable hashes are separate. Only a compiled adapter can allow ZIP
first-install or bind a staged executable. Remote catalog metadata cannot choose
these rules. The selected executable is verified after additional-file staging
and before activation; an executable chooser must not substitute `updater.exe`.
The whole approved archive, including bundled notices, remains intact.

## Platform and data authority

Add an internal platform discriminator to `OwnedDataRequirement`, defaulting to
Linux x64 for all existing definitions. Only the new Windows definition opts in
to Windows x64. Keep package, operation, selected-file hash, receipt, unchanged
settings/library, nonrecovering preview, stale-consent and no-overwrite checks.
Use Windows path comparison for path identity/overlap, not for repository IDs or
saved library bytes. Guard Unix directory modes and UnixCreateMode assignments;
Windows never invokes those unsupported setters.

A compiled first-install target carries relative path, exact length and hash.
Its test definition may use clearly synthetic values through existing internal
fixture injection; production values above cannot come from network metadata.

## Windows optional-play profile

Keep configuration, single-player saves and ordinary screenshots under
`<user-data>/engine-profiles/<install-key>/<adapter-id>/`, disjoint from the engine
and transaction trees. Create configuration once without replacing existing
bytes. Reuse only a bounded regular, unlinked configuration whose updater controls
remain disabled, Vulkan preference is `1` and replaygain is disabled;
conflicting/duplicate controls or uncertain paths refuse without rewriting.
Never overwrite saves or silently reset player preferences.

Seed these settings before first launch:

```ini
[GlobalSettings]
updater_check_updates_initialized=true
updater_check_updates=false
updater_auto_updates=false
gl_cachenodes=false
vid_preferbackend=1
mus_usereplaygain=false
```

Use literal `ArgumentList` tokens for `-iwad`, `-config`, `-savedir`, `-shotdir`,
`-noautoload` and `-noautoexec`; start only the validated `uzdoom.exe`. The Windows
guided path uses an owned process and sanitized inherited environment. Ordinary
engine Launch keeps its existing policy and Linux Freedoom behavior is preserved.

Pinned upstream source is commit `7910df7102033fd7754a317f3254f24101135926`:
[updater](https://github.com/UZDoom/UZDoom/blob/7910df7102033fd7754a317f3254f24101135926/src/widgets/updatebuttonbar.cpp),
[configuration](https://github.com/UZDoom/UZDoom/blob/7910df7102033fd7754a317f3254f24101135926/src/gameconfigfile.cpp),
[startup](https://github.com/UZDoom/UZDoom/blob/7910df7102033fd7754a317f3254f24101135926/src/d_main.cpp),
[Windows paths](https://github.com/UZDoom/UZDoom/blob/7910df7102033fd7754a317f3254f24101135926/src/common/platform/win32/i_specialpaths.cpp).

This redirects the named profile files, not every filesystem access. Node-cache
writes can be disabled; existing node-cache reads remain possible. Vulkan and
Intel OpenGL may write `%LocalAppData%/doom/uzdoom/vulkanpipelinecache` and
`glshadercache`; portable mode and environment variables do not redirect those
known-folder paths. Multiplayer ignores `-savedir`. Do not claim full filesystem
isolation or multiplayer/save compatibility. The user additionally approved the
same PC test reading/updating the Vulkan cache and per-map node-cache paths,
including existing cache bytes. No GL/GLES, replaygain, other backend/device or
external-file deletion scope was added. Keep Vulkan preference and replaygain
disabled in the acceptance profile; do not re-ask the same cache permission.
`vid_preferbackend=1` is not Vulkan-only enforcement: upstream catches a Vulkan
initialization error and falls back to GL/GLES ([fallback](https://github.com/UZDoom/UZDoom/blob/7910df7102033fd7754a317f3254f24101135926/src/common/platform/win32/hardware.cpp#L84-L95)).
The user explicitly chose to keep gameplay pending if fallback cannot be excluded.
Actual engine/game execution is therefore on hold; do not re-ask or treat this
preference as enforcement. Source/synthetic checks and separately confirmed
install/data/Close checks may proceed without selecting Play.

## Acceptance

Synthetic tests must cover package/executable mismatch, no helper substitution,
changed context, cancellation and reopen without auto-download, data hash/import,
receipt and current-save preservation, profile no-overwrite/refusal, and literal
launch arguments. Keep real Linux regressions and explicit capability limits.
Windows-only skips on Linux are not Windows passes.

The receiving PC first verifies confirmation, engine install, separate data
confirmation/import and Close with no launch. After the gameplay hold is resolved,
the remaining acceptance includes Play, normal game exit,
responsive launcher, save/load and relaunch/restart. Direct engine smoke is not
Relicade integration. Optional manual rapid/held-A testing was waived; do not
request it again or call it complete. Do not promote release/support claims from
builds, counts or virtual input tests. Review actual package notices separately.
