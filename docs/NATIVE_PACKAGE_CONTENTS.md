# Native package notices

Relicade preserves upstream rights separately from its product identity. The
Linux x64 runtime notice bundle is a version-bound inventory and copying check,
not a complete software bill of materials, legal clearance, security verdict or
release approval. Existing original-material terms and inherited MIT notices
remain unchanged. Publishing an owner's application is distinct from granting
new reuse rights over its separately owned source, branding or artwork.

## Current scope

`native/licenses/linux-x64/manifest.json` binds the inspected .NET 10 Linux x64
runtime to SDK 10.0.402 and the exact package versions, canonical content hashes,
asset paths and SHA-256 values recorded there. Its 25 package/runtimepack rows map
225 actual runtime files back to exact package members. The two SDK apphosts,
own assemblies, generated configuration and original icon are separate. The
`Relicade.Reference` metadata entry aliases `Relicade.dll`; it is not another
external binary or an extra independent dependency.

Full notice text is retained without rewriting copyright or license clauses.
Each file records its exact package member or pinned upstream repository/commit
and content hash. The bundle includes package-provided notices, applicable
upstream aggregate notices, Inter's SIL OFL text and SDL's notice. A package's
MIT expression does not replace the terms of its embedded components. Vendor
aggregate notices can mention components not shipped in this particular target;
the asset mapping, not the length of a notice, describes the inspected payload.

The inspected Inter Regular font bytes occur verbatim inside the package's
font assembly and identify version 3.019. SDL's source reference is the package
repository's pinned submodule, not an independently reproduced SDL build. The
inspected `libSDL3.so` references `GLIBC_2.34` symbols. That constrains this asset;
it does not prove every host with that glibc version is supported, inventory all
runtime-selected system libraries, or certify another RID.

Canonical NuGet content hashes are compared only with canonical dependency
metadata. Raw signed archive SHA-256 values are separate provenance fields.
The runtimepack's generated dependency hash is empty, so that comparison is
unavailable for its row. An absent hash is not a passing integrity check.

## Build and inspect

The additional bundle is copied to `licenses/runtime/linux-x64/` only for an
explicit `linux-x64` build/publish. The four existing inherited-source notice
destinations stay intact on all targets. Git attributes preserve the copied
notice bytes, including original line endings and inherited whitespace; these
files are not reformatted as authored prose. Other platform inventories remain
unaccepted; this bundle must not be relabeled as their coverage.

From the repository root, check the source bundle without a network request,
software execution or package installation:

```sh
python tools/check_runtime_notices.py
```

For a development directory build, use the documented SDK and restore the
project's official dependencies first. Both build-client opt-outs must be set
before restore/build/test/publish. This example omits symbols and maps source
paths; it does not promise that all private information is absent from all
possible future outputs. It also sacrifices matching line-level debug symbols.

```sh
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
dotnet restore native/Relicade.Desktop/Relicade.Desktop.csproj -r linux-x64
dotnet publish native/Relicade.Desktop/Relicade.Desktop.csproj \
  -c Release -r linux-x64 --self-contained true --no-restore \
  -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false \
  "-p:PathMap=$PWD=/_/relicade" -o native/artifacts/linux-x64
python tools/check_runtime_notices.py --publish native/artifacts/linux-x64
```

The checker reads an existing publish directory; it does not build, execute,
modify or upload it. It checks the runtime target, exact dependency identities
and comparable hashes, declared asset mappings, third-party runtime asset/apphost/notice bytes,
copied manifest and complete file set. Own application code still needs separate
source/build verification; its presence is not a code integrity verdict. Missing, changed or unmapped files fail.
It rejects linked/nonregular inputs and bounded-input violations. This is not
a sandbox against a hostile process replacing directories concurrently.

A successful result explicitly leaves distribution rights and platform support
unaccepted. A directory publish is not an AppImage, Flatpak or OS installer.
Clean installation, upgrade, removal, recovery, desktop/controller/accessibility
behavior, signing, system dependencies, complete component review and actual
release artifacts still need their own evidence. Remote game/artwork rights and
future emulator/core terms are outside this notice check.

## Updating dependencies

Do not silence a mismatch or reuse a prior notice inventory for changed bytes.
Reconcile every changed package/version, canonical hash, shipped member and
applicable complete notice with its authoritative source. Preserve earlier
provenance and protected legal files. Register any added notice as protected in
[the documentation contract](DOCUMENTATION.md), review the changed scope, and
run both source and actual publish checks before treating the new bundle as a
source checkpoint. Updating inventory does not itself approve a release.

## Preserved upstream citations

The Skia and HarfBuzz vendor aggregates contain three relative libjpeg-turbo
source citations each. Those original citations remain verbatim. Their explicit
protected-document classification records the package archive, member and hash;
the documentation audit reports the six upstream targets as not checked rather
than treating them as Relicade files or claiming they are valid local links.
No unrelated upstream implementation files are added to make those links appear
local. This does not resolve every transitive license obligation or promote the
package to distribution acceptance.
