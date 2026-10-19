# Continuous integration

The repository includes a reviewed workflow template at [ci/prototype-quality.yml](ci/prototype-quality.yml). Hosted GitHub Actions is not active yet; no passing-build badge or native-platform CI result is claimed.

The inactive template runs server containment regressions, JavaScript syntax, offline metadata-fetch boundary tests, artwork URL/completeness checks, deterministic artwork loading/cache/failure/privacy/keyboard tests, malformed-snapshot tests, browser interactions, storage failures, brand rendering, DPI emulation and restricted-preview/CSP checks. Image response fixtures are explicitly synthetic; live provider QA is separate and opt-in, never fabricated by CI fixtures. Tool versions and dependencies are explicit. This change does not activate hosted Actions.

Before activation, verify the repository's workflow permissions and run the template on the exact proposed commit. Record the actual hosted run/result; a YAML file, local test pass, queued job or successful source push is not hosted-CI success. Browser checks do not certify native platform compatibility.
