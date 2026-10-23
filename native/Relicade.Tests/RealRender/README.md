# Native CPU rendering smoke tests

This is a separate executable test project. The parent `Relicade.Tests.csproj`
uses dummy drawing for layout tests and explicitly excludes this directory.
Avalonia renderer/font resources can outlive a headless application session;
initializing both backends in a single test process can produce blank frames.
Do not link these fixtures into the parent assembly or load both test assemblies
in one process. The library, catalog-details and setup fixtures in the `SharedCatalogRenderer`
collection share one application and dispatcher owner: application resources
include thread-affine Geometry objects. Do not dispose and recreate that owner
between collection members. Await a real composition commit before asserting a
newly rendered framebuffer; timer ticks alone do not guarantee completion.
These are lifecycle requirements, not a license to retry failed captures or
weaken assertions. `RealRenderSmokeTests` has separate session setup, and
`RendererProcessBoundaryTests` checks the assembly boundary. Neither belongs to
this shared collection; its passing selection does not establish acceptance of
the entire renderer project run. Run each project separately:

```sh
dotnet test native/Relicade.Tests/Relicade.Tests.csproj
dotnet test native/Relicade.Tests/RealRender/Relicade.RenderTests.csproj
```

`RendererProjectIsolationTests` in the parent and `RendererProcessBoundaryTests`
in this project assert the assembly boundary. Both projects disable parallel test
collections. The render fixture uses real CPU/Skia drawing, explicit headless
sessions and a bounded layout/compositor barrier, not sleeps or pixel retries.
Every captured frame must have expected dimensions, substantial opaque content
and more than 32 colors. The walkthrough also asserts visible native controls,
exercises library → injected offline catalog → retry → Appearance settings,
and drains close/shutdown while checking no SDL service/library was created.

Set `RELICADE_RENDER_OUTPUT` to an optional output directory for the three PNGs.
The data is synthetic: empty library/settings and an injected HTTP 503 handler.
No games, artwork payloads, provider credentials or real network are required.
Use an isolated empty HOME and deny device/display/network access when running
this controlled fixture. Production input defaults remain enabled; only the
existing injected `MainViewDependencies` test composition disables input/music
and automatic initialization. This exercises actual `MainView` plus
`DesktopHostController` in a headless `Window`, not the eager parameterless
`MainWindow` or production desktop entrypoint. It is local development rendering
evidence, not physical-platform, game/controller, accessibility, rights, package
or release acceptance.

The separate setup-dialog capture renders actual native controls before file
selection with a synthetic library identity and no installed game or user input.
It writes `04-owned-game-setup-before-selection.png` with create-new semantics
when an output directory is supplied. Use a new output directory for every
capture; do not overwrite historical evidence. It does not certify the platform
file picker, data import, native desktop host or gameplay.

The staged launch-file dialog capture writes `05-staged-launch-file-choice.png`.
It renders the actual native chooser with clearly labeled synthetic package paths
and a selected row before confirmation. No package is installed or executed by the
render test. This is a separate headless rendering check, not a desktop host,
physical controller or gameplay acceptance result.

A minimum-size long-path capture, `06-staged-long-path-choice.png`, exercises the
bounded selection summary with both confirmation controls still in the viewport.
Its paths are synthetic and no files are installed or launched.

The library-reveal fixture captures `07-library-filtered-target-reveal.png` after
opening actual project details and selecting its existing local entry. It shows a
synthetic uninstalled entry and the temporary-filter exception notice, with no game
artwork or network request. It is native CPU rendering, not desktop or gameplay
acceptance. Capture outputs use create-new semantics and unique directories.

The optional Freedoom setup renders before file selection at 760×660 and the
320×360 minimum size. The fixture uses a synthetic local entry, with no installed
game, download or launch. The explicit free-data action and official release-page
alternative are rendered with a throwing transport, never requested. It verifies footer bounds as well as framebuffer pixels;
these are native headless renders, not physical input or engine-runtime evidence.

`FreedoomLibraryRenderingTests` captures the optional library action in list, grid and compact layouts. The installed-state entry is synthetic and uses the built-in placeholder, not real game artwork or an installed engine. These captures verify native geometry and readable actions, not gameplay or desktop-host acceptance.

`OwnedDataRecoveryRenderingTests` captures the actual replacement-preview dialog
at normal and minimum logical sizes, including 200% interface scaling. Its
receipt, paths and four-byte data are explicitly synthetic; it performs no copy,
download or game execution. Footer geometry and controller focus are checked
separately from the real-data-operation synthetic fixtures.

`OwnedPreviewNavigationRenderingTests` uses the shared renderer owner and actual
modal windows at ordinary and minimum sizes, including 200% interface scaling.
Natural controller navigation must expose every complete synthetic source,
destination, hash and retained-backup fragment before confirmation. It checks
44-logical-pixel inspection targets, viewport bounds and stable focus-ring layout with
real fonts. Separate native fixtures exercise natural keyboard/controller copy
and repair confirmation with disposable four-byte data. The render fixtures never
copy, download or execute a game; these checks are not physical-controller,
platform file-picker, native accessibility or gameplay acceptance.

`BrowseReleaseRetryRenderingTests` renders the actual release-notes retry action
at normal and minimum desktop layout sizes after an injected offline failure.
Its project and failure are synthetic, with a throwing transport and no network,
download or game execution. It checks button bounds and real framebuffer pixels.

The release-notes retry and catalog-cover fixtures share one collection-owned
renderer dispatcher and application lifetime. Recreating a per-assembly session
while the prior application remains alive loses its synchronization context;
shared platform-icon geometries also belong to their original UI thread. Each case still owns and closes its own window. It awaits an actual
composition batch before capturing once; no pixel retry or sleep is used.

`CatalogCoverRenderingTests` uses the shared dispatcher across all three
native library layouts. Procedural synthetic pixels are saved to a disposable
local cache and read through the actual image pipeline with an injected rejecting
HTTP transport. Captures are explicitly synthetic, not real game artwork or an
installed game. Local cache validation, attribution and visible primary actions
are asserted before capture; no remote images or acquired executables are used.

The seven cover/retry cases are exercised together with unchanged individual
window cleanup. This scoped check does not establish combined execution of every
other renderer fixture or physical desktop behavior.

`FreedoomInstallRenderingTests` renders the new uninstalled setup action using the
shared renderer owner, in all three layouts at ordinary and minimum card sizes.
Entries and placeholder art are synthetic; no engine, data or network request is
used. It checks button labels, framebuffer content and geometry separately from
the injected actual download/install handoff tests.

`FreedoomEngineProgressRenderingTests` uses the same renderer owner for the actual
setup dialog at ordinary and minimum sizes, including pending cancellation,
retained-engine, long-error and short/multiline error states. Its controlled operation is synthetic and
performs no download or execution. Footer geometry and rendered pixels are
separate from the actual injected-installer, keyboard/controller and lifetime tests.

`NativeTaglineRenderingTests` captures the empty native Library at normal and
minimum window sizes, with a light-theme control. The established tagline sits
beneath the original wordmark, inside the sidebar bounds. The isolated profile
has no games or external requests; these frames verify native product copy and
layout, not desktop-host, accessibility-device or gameplay acceptance.

`Z2rsSetupRenderingTests` uses that same shared renderer owner for the actual
setup requirements dialog with compiled package/input descriptions, but no
installed game or ROM. Natural controller navigation exposes the complete
compatibility and engine-config/privacy disclosure at 760×660, 320×360 and
200% interface scaling. Separate native UI tests exercise the confirmed local
copy with synthetic bytes. No acquired engine is executed by these captures.

`DiscoverVisualRenderingTests` uses the shared renderer owner for the actual
native catalog surface with six clearly labeled synthetic entries and an injected
image loader. Normal/minimum dark and light windows and 200% interface scaling
retain the filters and existing image binding. At enlarged scale, natural
controller selection scrolls the first card into view. No provider request or
game artwork is used; these are presentation checks, not catalog rights or
physical controller/platform acceptance.
`Z2rsFirstInstallRenderingTests` uses the same shared owner for the native
project-specific engine confirmation at normal/minimum size and 200% scaling.
Its synthetic entry starts no download or game. Footer controller focus, the
separate ROM-consent disclosure and visible geometry are checked independently
of the injected installer and cancellation tests.
