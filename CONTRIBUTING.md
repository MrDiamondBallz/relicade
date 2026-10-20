# Contributing to Relicade

Relicade has a brand/browser development preview and native development source with reviewed bounded safety milestones. Packaged distribution, complete parity and native platform acceptance remain unfinished. The vision spans community-revived classics, retro games and future emulation; source presence is not implemented emulation or a license to distribute game data. Before contributing material, confirm that you have the rights to submit it; source availability is not an open-source license grant.

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

## README freshness and documentation closeout

At every meaningful source milestone, and before main adoption:

1. Run `python tools/docs_sync.py --audit` and inspect the actual source diff against the last reviewed source, not merely the digest. The [maintenance map](docs/DOC_STATUS.md) identifies responsible documents.
2. Run `python tools/docs_sync.py --write` to regenerate bounded facts. This does not acknowledge semantic review; it still exits nonzero when source-sensitive prose is stale.
3. Review README vision/current progress, architecture, setup, security, roadmap and every affected registered guide. Correct claims and local links. Do not rewrite accurate prose solely to create activity. Never promote draft/unaccepted work to merged/released status.
4. Only after that reasoning pass, explicitly update the affected `reviewed` group/status digests in `docs/DOCUMENTATION.json` from audit output. Include the reviewed source diff and conclusions in the PR for independent review. The generator never does this step.
5. Run `python tools/docs_sync.py --write`, `python tools/docs_sync.py --check` and `python tools/docs_sync.py --audit`; run the applicable regression suites and inspect actual README pixels at mobile/desktop widths in light/dark themes. A second write must produce zero diff.
6. Complete the PR checklist and obtain independent review on the exact source before main. Generated facts, semantic acknowledgements and local test passes are not native acceptance or hosted CI.

Register newly authored Markdown, reStructuredText, plain-text guides and license/notice files with explicit policy. Legal notices, licenses, trademarks and historical changelog/evidence bytes are protected from regeneration. Follow [the complete contract](docs/DOCUMENTATION.md), including input/schema protections and pending periodic rollout.

No credentials, personal files, user game data, signing keys or sensitive diagnostics in source control. Use synthetic fixtures for destructive tests. Hosted CI status is documented in [docs/CI.md](docs/CI.md); a template or local pass is not a hosted run.
