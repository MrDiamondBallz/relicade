# Contributing to Relicade

Relicade is currently a brand and browser development preview. Native applications and package distribution are still in development. Before contributing material, confirm that you have the rights to submit it; source availability is not an open-source license grant.

## Product and design

- Follow [the brand system](brand/BRAND.md) and [product requirements](docs/PRODUCT_REQUIREMENTS.md).
- Preserve complete applicable functionality and platform coverage. Minimal presentation is not permission to remove advanced capabilities.
- Distinguish working preview behaviour from future native functions. No successful-looking install, launch, verification or progress states without a real acknowledged operation.
- Use authentic public metadata or explicitly labeled synthetic fixtures. Do not invent releases, security verdicts, player counts or performance results.

## Run and test

The lightweight preview uses Python 3.10+:

```sh
python tools/preview.py
python -m unittest discover -s tests -v
```

Use the restricted preview server rather than exposing the repository root or a public tunnel.

Full developer QA uses Python 3.12+, the pinned development dependencies and Chromium:

```sh
python -m pip install -r requirements-dev.txt
python -m playwright install chromium
python prototype/tests/test_catalog_fetch.py
python prototype/tests/test_project_art.py
python prototype/tests/test_malformed.py
python prototype/tests/test_ui.py
python prototype/tests/test_safety.py
python prototype/tests/test_artwork.py
node --test prototype/tests/art_policy.test.cjs
node tools/check_artwork.cjs
python tools/qa_preview.py
```

Select an existing browser with `CHROME_EXECUTABLE` if needed. Node is required for artwork policy/manifest tests, not for opening the preview. Check JavaScript with `node --check prototype/app.js` and the other prototype scripts. Pillow supports original project-tile generation; it is not required just to open the bundled preview. Standard browser suites explicitly use offline artwork or deterministic synthetic image responses, preserving local-network assertions. Opt-in real-provider checks: `python tools/qa_artwork_live.py --network`; keep those ignored live screenshots private. Automated preview exports are separate from the curated README image.

## Change discipline

1. Define behaviour and failure states; add a reproducing test before functional fixes.
2. Keep presentation, metadata, native operations and platform adapters separate. Remote data must never become commands, HTML or arbitrary paths.
3. Run applicable tests and inspect desktop/narrow-screen rendering, keyboard focus, reduced motion, failed storage and offline states.
4. Obtain independent review before merging functional changes. Record the exact tested commit and actual results.
5. Preserve required third-party notices and accurate source links. Do not copy unlicensed game artwork into the repository; original project tiles are not gameplay.

No credentials, personal files, user game data, signing keys or sensitive diagnostics in source control. Use synthetic fixtures for destructive tests. Hosted CI status is documented in [docs/CI.md](docs/CI.md); a template or local pass is not a hosted run.
