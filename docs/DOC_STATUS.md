# Documentation inventory and repository facts

Generated facts describe repository inputs, not native acceptance or a release.
See [the maintenance contract](DOCUMENTATION.md) for review and protection rules.

<!-- docs-sync:begin facts -->
This inventory is generated from the explicit maintenance registry. Semantic review is a separate gate, never inferred from these counts.

| Document | Policy | Source responsibilities |
| --- | --- | --- |
| [.github/pull_request_template.md](../.github/pull_request_template.md) | review | tooling, contracts |
| [CHANGELOG.md](../CHANGELOG.md) | protected |  |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | review | native, preview, presentation, tooling, contracts |
| [NOTICE.md](../NOTICE.md) | protected |  |
| [README.md](../README.md) | review | native, preview, presentation, tooling, contracts |
| [SECURITY.md](../SECURITY.md) | review | native, preview, presentation, tooling, contracts |
| [brand/BRAND.md](../brand/BRAND.md) | review | presentation, preview |
| [brand/assets/fonts/Barlow-OFL.txt](../brand/assets/fonts/Barlow-OFL.txt) | protected |  |
| [brand/assets/fonts/Manrope-OFL.txt](../brand/assets/fonts/Manrope-OFL.txt) | protected |  |
| [docs/ARCHITECTURE.md](ARCHITECTURE.md) | review | native, preview, presentation, tooling, contracts |
| [docs/ARTWORK.md](ARTWORK.md) | review | preview, presentation, native |
| [docs/CI.md](CI.md) | review | tooling, preview, contracts |
| [docs/DOCUMENTATION.md](DOCUMENTATION.md) | review | tooling, contracts |
| [docs/DOC_STATUS.md](DOC_STATUS.md) | generated |  |
| [docs/FEATURE_COVERAGE.md](FEATURE_COVERAGE.md) | review | contracts, tooling, native |
| [docs/PRODUCT_REQUIREMENTS.md](PRODUCT_REQUIREMENTS.md) | review | native, preview, presentation, tooling, contracts |
| [docs/ROADMAP.md](ROADMAP.md) | review | native, preview, presentation, tooling, contracts |
| [native/LICENSE](../native/LICENSE) | protected |  |
| [native/README.md](../native/README.md) | review | native, contracts, tooling |
| [native/Relicade.Tests/RealRender/README.md](../native/Relicade.Tests/RealRender/README.md) | review | native |
| [native/THIRD_PARTY_NOTICES.md](../native/THIRD_PARTY_NOTICES.md) | protected |  |
| [native/TRADEMARKS.md](../native/TRADEMARKS.md) | protected |  |
| [native/lib/Relicade.Core/LICENSE](../native/lib/Relicade.Core/LICENSE) | protected |  |
| [prototype/README.md](../prototype/README.md) | review | preview, presentation, tooling |
| [prototype/assets/art/README.md](../prototype/assets/art/README.md) | review | preview, presentation |

Repository facts (not acceptance results): **12 catalog projects**, **71 required feature rows**, **8 platform/package targets** and **23 synthetic fixture scenarios**. Counts describe repository inputs, not working games or passing tests.

Source status: merged development source includes [reviewed safety work](https://github.com/MrDiamondBallz/relicade/pull/3); [backup cleanup](https://github.com/MrDiamondBallz/relicade/pull/3) is recorded as merged development source, not release acceptance. No packaged release, full-parity acceptance or native-platform acceptance is recorded here. Emulation is planned, not implemented.

Source responsibilities:

- `contracts`: `docs/FEATURE_COVERAGE.json`, `docs/ci`
- `native`: `native`
- `presentation`: `brand`, `docs/assets`
- `preview`: `prototype`
- `tooling`: `tools`, `tests`, `.github`, `requirements-dev.txt`, `.gitignore`, `.gitattributes`
<!-- docs-sync:end facts -->
