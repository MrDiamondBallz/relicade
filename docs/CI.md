# Continuous integration

The repository includes a reviewed workflow template at [ci/prototype-quality.yml](ci/prototype-quality.yml). Hosted GitHub Actions is not active yet; no passing-build badge or native-platform CI result is claimed.

The inactive template runs server containment regressions, JavaScript syntax, offline metadata-fetch boundary tests, artwork URL/completeness checks, deterministic artwork loading/cache/failure/privacy/keyboard tests, malformed-snapshot tests, browser interactions, storage failures, brand rendering, DPI emulation and restricted-preview/CSP checks. Image response fixtures are explicitly synthetic; live provider QA is separate and opt-in, never fabricated by CI fixtures. Tool versions and dependencies are explicit. This change does not activate hosted Actions.

## Documentation checks — local and inactive-hosted boundary

Run locally at source closeout:

```sh
python tools/docs_sync.py --audit
python tools/docs_sync.py --write
python tools/docs_sync.py --check
```

The writer only refreshes bounded facts; source-sensitive prose remains blocked
until explicit review acknowledgements and independent review. See
[documentation maintenance](DOCUMENTATION.md) and the PR checklist. Neither these
commands nor this guide activates Actions or a new scheduled service. Periodic
reconciliation is pending independently reviewed publication and operator rollout.

Before activation, verify the repository's workflow permissions and run the template on the exact proposed commit. Record the actual hosted run/result; a YAML file, local test pass, queued job or successful source push is not hosted-CI success. Browser checks do not certify native platform compatibility.
