# Native CPU rendering smoke tests

This is a separate executable test project. The parent `Relicade.Tests.csproj`
uses dummy drawing for layout tests and explicitly excludes this directory.
Avalonia renderer/font resources can outlive a headless application session;
initializing both backends in a single test process can produce blank frames.
Do not link these fixtures into the parent assembly or load both test assemblies
in one process. Run each project separately:

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
