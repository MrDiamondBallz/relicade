# Documentation maintenance

Relicade maintains generated repository facts and source-sensitive narrative
separately through contribution-time checks and periodic maintainer reconciliation.
The existing maintainer workflow has been verified to run read-only audit/check
commands; that is not a guarantee of uninterrupted autonomous upkeep. Cloning the
repository or running these commands does not install a timer, activate a hosted
workflow or start a service. The tool does not approve prose: semantic changes
still need explicit source-diff review and independent review before adoption.

## Commands and exit status

From the repository root, Python 3.10+ and Git are sufficient (standard library
only, no package installation or network):

```sh
python tools/docs_sync.py --audit
python tools/docs_sync.py --check
python tools/docs_sync.py --write
```

All modes print deterministic JSON. Exit 0 means generated facts, inventory,
local links and explicit source-review acknowledgements match the current inputs.
Exit 1 means drift, unregistered documentation, a missing review or invalid input;
exit 2 is CLI misuse. A green check is not semantic approval, native acceptance,
a passing test run, package availability or hosted CI.

`--audit` and `--check` are read-only, including the metadata registry. Both
report errors, unregistered files, stale generated outputs, documents requiring
review, current source-group digests and the reviewed-status-input digest.
`--audit` is intended for inspection/periodic reconciliation; `--check` is the
same strict gate for closeout. Neither fixes anything silently.

`--write` updates only the text between `docs-sync:begin facts` and
`docs-sync:end facts` comment markers in README and [DOC_STATUS.md](DOC_STATUS.md).
It preserves every byte outside those markers. It may refresh facts while still
returning 1 for pending semantic review; it NEVER changes review digests or source
status. Invalid manifests/facts, broken local links, unknown docs, malformed
markers, symlinks and protected-content mismatches prevent ALL output writes.
A repeated write on unchanged inputs is byte-identical, without date stamps.
Link validation uses the prospective generated blocks plus unchanged manual and
protected text, including target anchors. Renaming/removing a registered guide
can therefore refresh obsolete generated inventory links without manual surgery;
any genuinely broken manual reference still prevents every output write.

`--root PATH` selects an exact existing Git worktree, primarily for isolated
regression fixtures; paths and ancestors cannot be symlinks. It is not a way to
write arbitrary files. Keep one documentation writer per isolated worktree.

## Inputs, generated facts and protection

[DOCUMENTATION.json](DOCUMENTATION.json) version 1 is the explicit responsibility
registry; [DOC_STATUS.md](DOC_STATUS.md) renders its document inventory/map.
Every current tracked or nonignored untracked Markdown, reStructuredText,
plain-text guide, LICENSE/COPYING/NOTICE file must be classified. The pinned
`requirements-dev.txt` is a dependency specification, not a guide, and is hashed
as tooling source. New unregistered documents fail before writes. Git ignores
untracked generated outputs using the repository's ignore rules, not guessed
directory basenames. Every tracked or nonignored guide is discovered, even under
`qa`, `bin`, `obj` or other output-like names; force-tracked ignored guides also
require classification. Do not put authored guides in ignored output locations.

Each document has exactly one policy:

- `review`: manual prose, explicit `sources` group list and `reviewed` digest map.
  README also has a bounded generated facts block; its rest remains manual.
- `generated`: only `docs/DOC_STATUS.md` is an allowed generated document, with a
  bounded block. Its title/manual wrapper is preserved.
- `protected`: legal/notices/trademark/license/font-license and historical
  changelog text, pinned by `sha256`. These are never outputs. Deliberate legal
  or historical updates need separate explicit review and a matching protection
  digest; the tool cannot bless them. Do not rewrite old entries to look current.

`protected_files` pins immutable evidence reports. Image/brand masters and the
single-orange-R identity and curated native product captures are never generator
write targets. Their actual bytes
are part of presentation/preview source digests, so legitimate changes require
review of corresponding prose and visual guards rather than generator edits.
No document policy, source root, status field or generated output is inferred
from untrusted instructions or accepted with unknown fields. Duplicate JSON keys
are rejected. Budgets are explicit: 10,000 repository files, 32 source groups,
128 roots per group, 256 documents/protected files, 128 MiB per source group,
16 MiB per source/fact file, 512 KiB registry/generated blocks, 64 KiB status
inputs and 2 MiB per authored output/document. Exceeding a budget is an error,
not truncated success. Paths use normalized repository-relative ASCII identifiers; no
absolute paths, traversal, arbitrary output selection or link aliases. Symlinks
(including ancestors) are rejected and hardlinked outputs refused.

The facts block derives actual list sizes/identities from:

- [FEATURE_COVERAGE.json](FEATURE_COVERAGE.json): feature and platform/package rows.
- [scenarios.json](../tests/fixtures/scenarios.json): synthetic fixture scenarios.
- [catalog.json](../prototype/data/catalog.json): bounded preview catalog entries.
- [SOURCE_STATUS.json](SOURCE_STATUS.json): explicit reviewed source-stage inputs,
  separate merged-source versus draft PR identities, unreleased/unaccepted
  boundaries and planned emulation. Its deliberately conservative version-1
  vocabulary cannot be promoted to new release/support claims without a reviewed
  schema/tool/docs change. The tool does not query a remote PR or infer status
  from a branch/file name. Reconcile these inputs against actual repository/PR
  state at source milestones; they are not a live GitHub status feed.

The counts do not imply passing tests, implemented features, platform acceptance
or license clearance. This tool never writes feature acceptance/evidence cells.

## Meaningful source drift, not recursive freshness

Each named source group lists exact existing file/directory roots. Digests hash
sorted repository-relative names and actual file contents, including additions
and deletions. Git's tracked plus nonignored-untracked inventory is used; root
selection is checked against the real Git worktree. An unmapped new source input
fails until assigned a responsibility. Relevant JSON/data, assets, native and
browser source, dependencies, test and tooling changes are real inputs.

Authored docs, the registry, source-status metadata (hashed separately), Git
metadata and Git-ignored untracked build/cache outputs are excluded. Tracked or
nonignored source files remain inputs regardless of directory names; generated
outputs should follow the repository's explicit ignore rules. Historical browser QA
reports are excluded source inputs and pinned protected evidence, not fresh pass
stamps. This avoids generated-doc/self-hash/Git/build recursion. A manual prose
clarification need not churn source hashes; a real source change cannot be made
current merely by regenerating facts. There are no timestamps in the registry.

## Source-review workflow for existing contributors/agents

1. Run audit and retain its result for the PR. Identify pending documents through
   the source responsibility map. Use Git history for the last reviewed registry
   revision (`git log -p -- docs/DOCUMENTATION.json`) and review the actual source
   diff since that baseline. A digest identifies bytes; it cannot explain them.
2. Regenerate facts with `--write`. Review actual implementation/status changes
   against README current progress, architecture, setup, security, roadmap and
   every affected guide. Fix semantic claims and links; unchanged accurate prose
   needs a recorded conclusion, not cosmetic churn. Add new docs to the registry.
3. After that reasoning pass, explicitly edit each affected document's `reviewed`
   map with the audit's current values for its declared source groups, plus
   `status` set to `status_digest`. There is deliberately no auto-ack command.
   Do not blindly copy every digest or claim the generator performed review.
   Preserve protected historical/legal text and exact accepted source scope.
4. Run `--write`, `--check`, `--audit`, applicable tests and actual README visual
   checks. A second write must produce zero diff. Include source-diff reasoning,
   acknowledgements, exact tested source and real results in the PR checklist.
5. An independent reviewer must cold-check the source and prose, exercise source
   drift/protection/idempotence and inspect README pixels before adoption. A
   forged/incorrect acknowledgement is still possible, just like a false code
   review; the tool detects stale bytes, not reviewer honesty or semantic truth.

No new polling workflow is needed. Existing feature/integration/operations work
uses the same commands at source closeout and during periodic maintainer
reconciliation. Keep periodic checks read-only on the canonical checkout;
regenerate factual drift on an isolated branch and route semantic changes through
the same review gates before publication. Scheduling and continued execution are
maintainer responsibilities, not side effects of a repository clone or script run.

## Safety and tests

Inputs are validated completely before ordinary writes; both outputs are
prestaged and individually atomically replaced. An ordinary I/O error rolls
back replaced outputs; a process/power failure could leave a subset refreshed.
The next check detects partial/stale output and no semantic acknowledgement is
ever modified. This is not a multi-file filesystem transaction, sandbox or a
promise against a concurrent hostile process changing the worktree mid-run.

```sh
python -m unittest discover -s tests -p 'test_docs_sync.py' -v
python -m unittest discover -s tests -p 'test_readme*.py' -v
python -m unittest discover -s tests -v
```

The updater tests use real temporary Git repositories and real source/fact edits:
stale check, fact refresh, still-pending semantic review, explicit acknowledgement
and a passing check. They cover no-write invalid inputs, protected/manual/history
preservation, tracked guide rename/removal, output-like directory discovery,
prospective local references/anchors and idempotence. Existing
preview/branding/safety suites remain required for their own surfaces, not native
runtime or full parity. See [Contributing](../CONTRIBUTING.md).
