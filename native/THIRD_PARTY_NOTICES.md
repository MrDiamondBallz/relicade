# Native source and asset notices

The native application is adapted from Quiver Launcher 3.5.1, source commit
`66c45cbe2779818791948c7bd769d388e3a47b44`:
https://github.com/tgeorgiadis/quiver-launcher/tree/66c45cbe2779818791948c7bd769d388e3a47b44
That project derives from GithubLauncher: https://github.com/SirDiabo/GithubLauncher.
This is technical provenance, not an affiliation, endorsement or support claim.

The complete, unmodified MIT copyright/permission notices are retained in
`LICENSE` (2026 SirDiabo and Thomas Georgiadis) and
`lib/Relicade.Core/LICENSE` (2025 SirDiabo). `TRADEMARKS.md` is retained verbatim
as the source project's trademark notice, not as Relicade's own policy.
Renamed namespaces and assemblies do not change the origin of inherited code.

## Import inventory

- Shared C#/AXAML application, domain/core, desktop and Android host source and
  applicable unit/headless tests are adapted under MIT. Installation, launch,
  release-provider, mods, recovery, controller and kiosk services remain present.
- No source-product logo, icon, screenshot, game artwork, operator guidance,
  source workflow or source repository history is imported.
- All bundled native PNG/ICO artwork is newly exported from Relicade's original
  `brand/` masters or newly drawn geometric UI glyphs by `tools/export_assets.py`.
  Generic device glyphs are not platform-vendor logos. `Assets/Github.png` is a
  generic source-code glyph, not a copied GitHub logo.
- Android launcher icons likewise derive from the Relicade app-icon master.
- Archive test fixtures inherited with the code contain only tiny synthetic text
  payloads (including text named `game.exe`); they are never executed as games.
  Fixture metadata/URLs are test inputs, not bundled game payloads or art.
- The third-party catalog and provider endpoints remain accurately identified.
  Their data, images, reviews, packages and trademarks have separate rights.
  A public URL is not a redistribution license. Remote metadata/art is not an
  independently operated Relicade backend or an independent safety verdict.

## Dependencies

NuGet dependencies retain their own licenses; source MIT notices do not replace
them. Avalonia/desktop/headless/Fluent themes and AsyncImageLoader use MIT;
Inter is distributed by its package with its font license; SDL3-CS and native
SDL, SharpCompress and codec dependencies, Velopack, System.* packages, and
Windows NAudio retain their package license/copyright notices. Consult the
restored packages' `.nuspec`, license and third-party-notice files for the exact
versions and transitive components. Test-only dependencies include xUnit,
Microsoft.NET.Test.Sdk, coverlet and FluentAssertions 8 (its own license, not MIT).
This foundation is not a redistribution-license or production supply-chain audit.
A complete package SBOM, dependency-license review and platform signing checks
remain distribution gates. Required source notices are copied to desktop build
and publish output under `licenses/`.
