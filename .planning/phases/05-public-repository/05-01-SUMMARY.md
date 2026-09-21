---
phase: 05-public-repository
plan: 01
subsystem: infra
tags: [bash, bats, github-actions, gh-cli, ci-cd]

requires: []
provides:
  - "scripts/release-gate.sh, a tracked script that refuses a release unless the tagged commit carries exactly one completed/success ci-success check run"
  - "tests/scripts/release-gate.bats, 16 bats tests covering every D-03 refusal state plus the request-shape guard"
  - "release.yml's gate step and checks: read permission grant"
  - "docs/development.md's Releases section describing the gate's refusal behavior"
affects: [05-02, 05-03, phase-06-release-workflow]

actuals:
  tokens: 2594
  tasks: 3
  commits: 6

tech-stack:
  added: []
  patterns:
    - "Fake gh-on-PATH bats testing: a fake gh executable is placed first on PATH and records its argv one-argument-per-line, so both response handling and request construction are exercised"
    - "One un-faked test calls the real gh CLI against a well-formed-but-unknown SHA to prove request-shape fidelity a fake cannot reproduce (HTTP method selection)"

key-files:
  created:
    - scripts/release-gate.sh
    - tests/scripts/release-gate.bats
  modified:
    - .github/workflows/release.yml
    - docs/development.md

key-decisions:
  - "The gh api call carries check_name=ci-success in the query string, never as a field flag — a field flag makes gh send POST instead of GET, which measured 404 on this repository and would make the gate refuse every release (D-03, RESEARCH.md Pattern 1)"
  - "Fixture data for the fake gh is plumbed through per-test files (FIXTURE_BODY_FILE/FIXTURE_STDERR_FILE/FIXTURE_EXIT_FILE) rather than exported env vars read inside @test bodies, avoiding shellcheck's SC2030/SC2031 bats-subshell false positive while keeping the same fake-on-PATH seam"
  - "Tests 4-15 (every already-implemented refusal state) were proven with break-then-restore against the working Task 1 script rather than an artificial pre-implementation red, per this repository's established TDD precedent for tests covering already-correct behavior"

patterns-established: []

requirements-completed: [REL-01]

coverage:
  - id: D1
    description: "scripts/release-gate.sh refuses a release unless the tagged commit carries exactly one completed/success ci-success check run"
    requirement: "REL-01"
    verification:
      - kind: unit
        ref: "tests/scripts/release-gate.bats (16 tests, all passing)"
        status: pass
    human_judgment: false
  - id: D2
    description: "The gh api request carries the check_name filter in the query string, never a field flag, so gh sends GET not POST"
    requirement: "REL-01"
    verification:
      - kind: unit
        ref: "tests/scripts/release-gate.bats#the request carries the filter in the query string and no field flag"
        status: pass
      - kind: unit
        ref: "tests/scripts/release-gate.bats#the real gh sends a GET for the query-string filter form, not a 404-triggering POST"
        status: pass
    human_judgment: false
  - id: D3
    description: "release.yml calls the gate between the tag check and mise run test, with checks: read granted and both permission grants commented"
    requirement: "REL-01"
    verification:
      - kind: other
        ref: "rg -q 'release-gate.sh check' .github/workflows/release.yml && rg -q '^\\s*checks:\\s*read' .github/workflows/release.yml"
        status: pass
    human_judgment: false
  - id: D4
    description: "docs/development.md's Releases section states the gate's refusal behavior, including the reachable cancelled state, and the fix (re-push the tag)"
    requirement: "REL-01"
    verification: []
    human_judgment: true
    rationale: "Prose adequacy for a human reader is a judgment call the plan's own <human-check> in Task 3's verify block calls for explicitly"

duration: ~20min
completed: 2026-09-21
status: complete
---

# Phase 5 Plan 1: Release Gate Summary

**A tracked `scripts/release-gate.sh` refuses a tagged release unless the commit carries exactly one `completed`/`success` `ci-success` check run, wired into `release.yml` between the tag check and the test build, with 16 bats tests proving every refusal state and the request's GET-not-POST shape.**

## Performance

- **Duration:** ~20 min
- **Started:** 2026-09-20T19:50Z (first commit)
- **Completed:** 2026-09-21T03:00Z
- **Tasks:** 3
- **Files modified:** 4 (2 created, 2 modified)

## Accomplishments

- `scripts/release-gate.sh check SHA` queries `GET repos/$GH_REPO/commits/$SHA/check-runs?check_name=ci-success` and exits 0 only for exactly one `completed`/`success` run; every other state (missing, `in_progress`, `cancelled`, `failure`, `skipped`, `timed_out`, an unenumerated conclusion, more than one match, or a failed API call) exits 1 with a distinct stderr message
- `tests/scripts/release-gate.bats` — 16 tests: the pass/refuse/request-shape core (Task 1), every D-03 refusal state plus the usage triad (Task 2), and one test that calls the real `gh` CLI to prove the query-string filter form is not the 404-triggering POST that a field flag would produce
- `.github/workflows/release.yml` calls the gate right after `Check that the tag matches the plugin version` and before `Build and run unit tests`; the `release` job's `permissions:` block now grants `checks: read` alongside `contents: write`, each with an inline comment naming its consuming step
- `docs/development.md`'s Releases section restates the workflow's real step order and names every refusal state including `cancelled` (reachable via `ci.yml`'s `cancel-in-progress: true`), with the fix: wait for CI to go green, then re-push the tag

## Task Commits

Each task was committed atomically (TDD RED/GREEN split for Task 1):

1. **Task 1 RED: fake-gh scaffolding + 4 tests** - `04d8b7b` (test) — script absent, confirmed red
2. **Task 1 GREEN: the gate script** - `38f47ad` (feat)
3. **Task 1 WIRE: release.yml step + checks: read** - `e5c062b` (feat)
4. **Task 2: every D-03 state + usage triad** - `1a11757` (test) — proven via break-then-restore against Task 1's implementation
5. **Task 3: Releases section update** - `eff9019` (docs)

**Plan metadata:** (this commit)

## Files Created/Modified

- `scripts/release-gate.sh` - New. `check SHA` action; reads `GH_REPO`/`GH_TOKEN` (consumed by `gh` itself); one `gh api` call on one physical line with the filter in the query string and no field flag
- `tests/scripts/release-gate.bats` - New. 16 tests; fake `gh` on `PATH` records argv one-argument-per-line and serves fixture bodies via per-test files
- `.github/workflows/release.yml` - Added the gate step and the `checks: read` permission grant, both commented
- `docs/development.md` - Restated the Releases section's step 3 and added the refusal-behavior sentence

## Decisions Made

- The `gh api` request's `check_name` filter stays in the query string, never a field flag — measured against the live repository, a field flag makes `gh` send POST and returns 404, which would make the gate refuse every release
- Fixture plumbing for the fake `gh` uses per-test files rather than env vars mutated inside `@test` bodies, clearing a shellcheck SC2030/SC2031 false positive specific to bats' subshell model while keeping D-04's "no test-only env var in the script" constraint intact
- Tests 4 through 15 in Task 2 cover states the Task 1 script already handled correctly; each was proven with break-then-restore rather than an artificial red, following this repository's established TDD precedent (STATE.md, Phase 01-04) for tests over already-working behavior

## Deviations from Plan

None - plan executed exactly as written. The `run !` bats assertion needed one iteration (the initial `[ "$status" -eq 0 ]` check after `run !` was backwards — `run !` already asserts non-zero and leaves `$status` set to the command's real exit code) — a self-caught test-authoring bug fixed before any commit, not a deviation from the plan.

## Issues Encountered

None. One local debugging surprise: the real-`gh` bats test's `gh auth status` check must run after `unset GH_TOKEN`, because `setup()`'s sentinel `GH_TOKEN=sentinel-api-key-gh` fixture value overrides the real keyring credential for that call — resolved by reordering the unset before the auth check.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

The release gate is proven end-to-end at the script and workflow-wiring level; its live pass path (a real tagged release against a genuinely green `ci-success`) is proven later by Phase 6's rehearsal release, per this plan's own `<verification>` note. Ready for 05-02 (zizmor pedantic persona) and 05-03 (gitleaks), which touch `.mise.toml` and `.gitleaks.toml` but not this plan's files.

---
*Phase: 05-public-repository*
*Completed: 2026-09-21*

## Self-Check: PASSED

- All key files found on disk: `scripts/release-gate.sh`, `tests/scripts/release-gate.bats`, `.github/workflows/release.yml`, `docs/development.md`.
- All task commits found in git log: `04d8b7b`, `38f47ad`, `e5c062b`, `1a11757`, `eff9019`.
- Every task's `<acceptance_criteria>` re-run and passing (bats 16/16, source-assertion `rg` checks, `shellcheck`/`shfmt`/`actionlint` clean, workflow-order checks, docs checks).
- Plan-level `<verification>` re-run: `bats tests/scripts/release-gate.bats` (16 tests), `mise run test`, `mise run lint`, all exit 0.
