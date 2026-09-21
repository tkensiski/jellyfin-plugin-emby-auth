---
phase: 05-public-repository
plan: 02
subsystem: infra
tags: [github-actions, zizmor, mise, ci-cd]

requires:
  - phase: 05-public-repository (plan 01)
    provides: "scripts/release-gate.sh and the check-runs query it makes against ci-success"
provides:
  - "name: on all five jobs across ci.yml and release.yml, with ci-success byte-identical to its job id"
  - "release.yml's workflow-level concurrency group, cancel-in-progress: false"
  - "the pedantic zizmor persona as the standing [tasks.lint] gate"
affects: [05-03, phase-06-release-workflow]

actuals:
  tokens: 536
  tasks: 2
  commits: 2

tech-stack:
  added: []
  patterns:
    - "Break-then-restore teeth proof: remove a job's name, run mise run lint, confirm the exact failure, restore byte-for-byte, confirm git diff is empty and lint passes — proves a gate is live rather than decorative"

key-files:
  created: []
  modified:
    - .github/workflows/ci.yml
    - .github/workflows/release.yml
    - .mise.toml

key-decisions:
  - "The measured pedantic inventory in the plan (7 findings) is now 6 on this branch: plan 05-01's inline comments on release.yml's two permissions: grants already satisfy undocumented-permissions before this plan started. Confirmed by running the pinned zizmor 1.30.1 --persona=pedantic before any edit in this plan — 5 anonymous-definition + 1 concurrency-limits, zero undocumented-permissions. No fix was needed for that finding; it does not change this plan's task list, acceptance criteria, or must_haves, since all of them are stated as post-conditions (zero findings, specific name/concurrency fields) rather than a fixed count to close."
  - "ci-success keeps name: ci-success, byte-identical to its job id, with an inline comment explaining why, so scripts/release-gate.sh's check_name=ci-success query keeps matching the same check run (D-09)"
  - "release.yml's concurrency block uses cancel-in-progress: false, unlike ci.yml's true, with a comment stating why: a release can be interrupted after gh release create but before every asset uploads, so releases queue instead of being killed (D-10)"

patterns-established: []

requirements-completed: [REL-02]

coverage:
  - id: D1
    description: "All five jobs across ci.yml and release.yml carry a name:, with ci-success identical to its job id"
    requirement: "REL-02"
    verification:
      - kind: other
        ref: "rg -q '^\\s{4}name: ci-success$' .github/workflows/ci.yml && rg -q '^\\s{4}name: Lint$' .github/workflows/ci.yml && rg -q '^\\s{4}name: Build and test$' .github/workflows/ci.yml && rg -q '^\\s{4}name: End-to-end tests$' .github/workflows/ci.yml && rg -q '^\\s{4}name: ' .github/workflows/release.yml"
        status: pass
    human_judgment: false
  - id: D2
    description: "release.yml has a workflow-level concurrency group that never cancels an in-flight release"
    requirement: "REL-02"
    verification:
      - kind: other
        ref: "rg -q '^concurrency:' .github/workflows/release.yml && rg -q '^\\s+cancel-in-progress: false' .github/workflows/release.yml && rg -q '^\\s+cancel-in-progress: true' .github/workflows/ci.yml"
        status: pass
    human_judgment: false
  - id: D3
    description: "zizmor --offline --persona=pedantic .github/workflows reports zero findings"
    requirement: "REL-02"
    verification:
      - kind: other
        ref: "mise exec -- zizmor --offline --persona=pedantic .github/workflows (exit 0, 'No findings to report')"
        status: pass
      - kind: other
        ref: "mise exec -- actionlint (exit 0)"
        status: pass
    human_judgment: false
  - id: D4
    description: "The pedantic persona is the standing mise run lint gate, and it has teeth (a missing job name fails it)"
    requirement: "REL-02"
    verification:
      - kind: other
        ref: "mise run lint (exit 0); teeth proof: removed ci.yml's lint job name, mise run lint failed (exit 11, anonymous-definition at ci.yml:19), restored, git diff --exit-code .github/workflows (exit 0), mise run lint passed again"
        status: pass
    human_judgment: false

duration: ~10min
completed: 2026-09-21
status: complete
---

# Phase 5 Plan 2: Pedantic Zizmor Gate Summary

**All five CI/release jobs named (ci-success held byte-identical to its job id), release.yml gained a never-cancelling concurrency group, and `zizmor --persona=pedantic` became the standing `mise run lint` gate — proven live by a break-then-restore test.**

## Performance

- **Duration:** ~10 min
- **Started:** 2026-09-20T20:07:05-07:00 (first commit)
- **Completed:** 2026-09-20T20:08:35-07:00 (last commit)
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- `ci.yml`'s `lint`, `test`, `e2e` jobs got descriptive names (`Lint`, `Build and test`, `End-to-end tests`); `ci-success` got `name: ci-success`, identical to its job id, with a comment explaining why — `scripts/release-gate.sh` queries the check run by that exact name
- `release.yml`'s `release` job got `name: Build and publish the release`, and the file gained a workflow-level `concurrency:` block (`cancel-in-progress: false`) with a comment stating why it differs from `ci.yml`'s `true`
- `.mise.toml`'s `[tasks.lint]` zizmor entry now runs `--offline --persona=pedantic`, kept as the last entry in the array, position and quoting style unchanged
- `zizmor --offline --persona=pedantic .github/workflows` and `mise run lint` both exit 0 with no findings
- The gate's teeth were proven, not assumed: removing `ci.yml`'s `lint` job's `name:` field made `mise run lint` fail with an `anonymous-definition` finding at `ci.yml:19` (exit 11); restoring the field byte-for-byte made `git diff --exit-code .github/workflows` pass and `mise run lint` pass again

## Task Commits

Each task was committed atomically:

1. **Task 1: Name every job, and serialize releases instead of cancelling them** - `ce6a7dc` (feat)
2. **Task 2: The pedantic persona becomes the standing lint gate** - `4c05629` (feat)

**Plan metadata:** (this commit)

## Files Created/Modified

- `.github/workflows/ci.yml` - `name:` added to `lint`, `test`, `e2e`, and `ci-success` (the last identical to its job id, with an explanatory comment)
- `.github/workflows/release.yml` - `name:` added to the `release` job; a new workflow-level `concurrency:` block with `cancel-in-progress: false` and an explanatory comment
- `.mise.toml` - `[tasks.lint]`'s zizmor entry gained `--persona=pedantic`; no entry added, removed, or reordered

## Decisions Made

- The plan's measured inventory (7 pedantic findings) had already shrunk to 6 before this plan's first edit: plan 05-01 added inline comments to both of `release.yml`'s `permissions:` grants, which independently satisfied the `undocumented-permissions` audit. Verified by running the pinned `zizmor` 1.30.1 with `--persona=pedantic` before touching either workflow file — 5 `anonymous-definition` + 1 `concurrency-limits`, zero `undocumented-permissions`. This changed nothing about the task list or acceptance criteria, which are all stated as post-conditions (a specific `name:`/`concurrency:` shape, zero findings) rather than "close exactly 7 findings."
- `ci-success`'s name stays identical to its job id per D-09, with a comment recorded directly above it so a later contributor does not "improve" it into something descriptive and break `scripts/release-gate.sh`'s `check_name=ci-success` query.
- `release.yml`'s concurrency block uses `cancel-in-progress: false` per D-10, deliberately diverging from `ci.yml`'s `true` and from zizmor's generic remediation text, which only checks that a `concurrency:` block exists.

## Deviations from Plan

None - plan executed exactly as written. The pre-existing pedantic-finding-count discrepancy (documented above under Decisions Made) required no code change; it is a measurement artifact of running the check before vs. after plan 05-01 landed, not a defect this plan needed to fix.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

The pedantic zizmor gate is live in `mise run lint`, reached by both the `lint` CI job and the pre-commit hook, with no suppression added anywhere. `ci-success`'s check-run name is unchanged, so plan 05-01's release gate keeps working. Ready for 05-03 (gitleaks), which touches `.mise.toml` and a new `.gitleaks.toml` but not the files this plan modified.

---
*Phase: 05-public-repository*
*Completed: 2026-09-21*

## Self-Check: PASSED

- All key files found on disk: `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `.mise.toml`.
- All task commits found in git log: `ce6a7dc`, `4c05629`.
- Every task's `<acceptance_criteria>` re-run and passing: `rg` name/concurrency checks, `actionlint` (exit 0), `zizmor --offline --persona=pedantic` (exit 0, "No findings to report"), no `zizmor:ignore` present, `.mise.toml` zizmor-count and array-position checks, `mise run lint` (exit 0).
- Plan-level `<verification>` re-run: `zizmor --offline --persona=pedantic .github/workflows` (exit 0), `mise run lint` (exit 0), `actionlint` (exit 0), `rg -n 'name: ci-success' .github/workflows/ci.yml` (matches), teeth proof recorded above with a clean `git diff` after restore.
