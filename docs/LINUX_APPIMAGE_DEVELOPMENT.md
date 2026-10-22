# Linux AppDir and AppImage development builds

This recipe makes a private, unsigned Linux x64 development container from a
self-contained directory publish. It is not a supported release, distribution
approval, Flatpak recipe or another platform's acceptance. The actual desktop
session, FUSE-mounted launch, clean installation/upgrade, accessibility,
controllers, game execution and recovery still require their own evidence.

## Inputs and host

Use a Linux x64 build host, Python 3.10+, `/bin/sh`, `readlink`, the pinned .NET SDK
and the dependencies described in [the native guide](../native/README.md).
The assembly step requires the host's `renameat2` no-replace operation; it does
not fall back to replacing an existing directory. The current runtime inventory
uses SDK 10.0.402. Its inspected SDL asset references `GLIBC_2.34`; that is an
observed constraint, not a complete or sufficient Linux compatibility promise.

First make a new directory publish. Keep earlier build evidence separately;
these commands do not clean an existing compiler output directory for you.
Run from the repository root:

```sh
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
mkdir -p native/artifacts/linux-x64/tools
dotnet publish native/Relicade.Desktop/Relicade.Desktop.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false \
  "-p:PathMap=$PWD=/_/relicade" \
  -o native/artifacts/linux-x64/publish
python3 tools/check_runtime_notices.py \
  --publish native/artifacts/linux-x64/publish
```

The notice check binds this payload to the existing package inventory. It does
not prove that arbitrary own-source assemblies came from a particular commit.
Keep the compiler inputs, source revision and resulting file hashes together.

## Assemble an AppDir

```sh
python3 tools/build_linux_appdir.py \
  --publish native/artifacts/linux-x64/publish \
  --output native/artifacts/linux-x64/RELICADE.AppDir --epoch 0
```

The output must not exist. Every input file is checked and copied; no input is
executed or modified. The launcher keeps the caller's working directory and
literal arguments, including paths containing spaces. Executable files become
mode 0755 and other files 0644, rather than carrying owner-only execution bits
into a root-owned filesystem image. The original icon, a desktop entry and a
file/hash/mode manifest accompany the payload. No file associations or desktop
settings are registered. Unsupported control characters in build paths are
refused rather than producing a broken launcher.

The current build budget is 512 MiB of input files and 256 MiB per file. Linked,
nonregular, unexpectedly writable and changed inputs are refused. Existing
output files/directories remain untouched, including a destination that appears
at final publication. These checks are not a hostile concurrent filesystem
sandbox or a guarantee against power loss.

## Make the AppImage with explicit tools

Download these official assets into the local tools folder:

- [appimagetool 1.9.1 x86_64](https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage)
- [type-2 runtime 20251108 x86_64](https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64)

[The toolchain record](../native/packaging/linux/appimage-toolchain.json) retains
the release/asset identifiers, source commits, exact sizes and publisher SHA-256
values. The builder verifies and privately copies both assets before execution.
It never downloads a missing tool or substitutes the mutable continuous runtime.
A matching published hash is not an independent signature or source-build proof.

```sh
python3 tools/build_linux_appimage.py \
  --publish native/artifacts/linux-x64/publish \
  --appimagetool native/artifacts/linux-x64/tools/appimagetool-x86_64.AppImage \
  --runtime native/artifacts/linux-x64/tools/runtime-x86_64 \
  --output native/artifacts/linux-x64/RELICADE-development-x86_64.AppImage \
  --epoch 0
```

This creates its own temporary AppDir. It supplies the local runtime explicitly,
uses one compressor worker, fixed timestamps and a restricted build environment,
and does not inherit account tokens, signing settings or an unrelated VERSION.
No signing, update feed, AppImageHub submission, automatic installation or network
fetch is requested. The tool uses extraction instead of mounting itself through
FUSE. The default work deadline is 120 seconds, with bounded termination/drain.
A successful leader exit is not enough: the entire process group must disappear.
Shutdown allows up to five seconds after TERM and another five after KILL.
An unconfirmed group, including an adopted but unreaped descendant, leaves
staging in place. A pending-process marker also protects it if a later diagnostic
or interruption masks the lifetime error. Do not retry or remove retained
staging blindly; reconcile the original process ownership first.
Ordinary failures emit the build diagnostic and do not publish a partial image.

Use new output names for repeatability comparisons. Matching SHA-256 values
across equivalent builds are useful evidence for those exact inputs, not a
promise that different SDKs, packages, runtimes or hosts reproduce them.
Running the resulting image with `APPIMAGE_EXTRACT_AND_RUN=1` and `--help` in a
disposable HOME/XDG profile checks only extraction and own-program loading. It
does not exercise the full desktop application or certify normal FUSE mounting.

## Remaining distribution obligations

The [directory-publish notice inventory](NATIVE_PACKAGE_CONTENTS.md) covers the
application payload, not the newly embedded AppImage runtime. That runtime's
[pinned license](https://github.com/AppImage/type2-runtime/blob/dd6cebedcbddde9c82f89b011e8e1d40b6e43868/LICENSE)
also identifies statically linked components, including libfuse under LGPLv2.
Their complete applicable notices, source/relink obligations and exact build
provenance still need reconciliation before public distribution. Do not label
the whole container MIT or change Relicade's original-material terms.

AppStream metadata is intentionally absent from this preliminary recipe, and its
validation is explicitly disabled only for this private-development scope.
That is an unresolved distribution-quality gate, not a passed metadata test or a
new license grant over original product material. Signing, reproducible release
packages, platform acceptance and the full release contract remain unaccepted.

## Offline regression checks

```sh
PYTHONPATH=tests python3 -m unittest test_linux_appdir test_linux_appimage -v
```

These use synthetic payloads and own-source process fixtures. They cover byte
preservation, deterministic layout, executable permissions, argument/cwd
forwarding, changed or linked inputs, exclusive output publication, explicit
local-tool selection and cancellation/drain reporting. They do not run an
acquired game engine, mount FUSE or establish gameplay support.
