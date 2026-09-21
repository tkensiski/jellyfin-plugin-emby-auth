---
phase: 05-public-repository
plan: 03
subsystem: infra
tags: [gitleaks, mise, pre-commit, secret-scanning, bats]

requires:
  - phase: 05-public-repository (plan 02)
    provides: "the pedantic zizmor persona as the standing [tasks.lint] gate, and named jobs/concurrency in both workflows"
provides:
  - ".gitleaks.toml, a root allowlist that extends the default rule set and clears only the five measured fixture findings, by value"
  - "gitleaks 8.30.1 pinned in [tools] and wired into [tasks.lint] as a full git-history scan (gitleaks git)"
  - "the pre-commit lint hook re-running when .gitleaks.toml changes"
  - "tests/scripts/gitleaks.bats, a standing test that the scan still detects a secret-shaped value"
affects: [05-05, phase-06-release-workflow]

actuals:
  tokens: 1515
  tasks: 3
  commits: 2

tech-stack:
  added: ["gitleaks 8.30.1 (mise aqua:gitleaks/gitleaks backend)"]
  patterns:
    - "Break-then-restore teeth proof (Task 2): remove the allowlist table, run mise run lint, confirm the exact failure and finding count, restore byte-for-byte, confirm clean diff and lint passing again — same shape 05-02 used for the zizmor gate"
    - "Split-fixture detection test (Task 3): a detectable secret-shaped value held as two halves in variables that avoid key/token/secret/api naming, joined only at scan time, so no assembled secret-shaped string ever enters a tracked file"

key-files:
  created:
    - .gitleaks.toml
    - tests/scripts/gitleaks.bats
  modified:
    - .mise.toml
    - .pre-commit-config.yaml
    - docs/development.md

key-decisions:
  - "`gitleaks git` invoked from the repository root with no explicit --source scans '.' and auto-discovers the root .gitleaks.toml under config-precedence rule 4 — confirmed live (not assumed) by running `gitleaks git` before and after creating .gitleaks.toml: 5 leaks found without it, 0 with it, matching RESEARCH.md's Assumption A2"
  - "A single global [[allowlists]] table with the default regexTarget (matches the extracted secret value, not the whole line or a path), holding the two D-07 fixture regexes and nothing else — no regexTarget override, no path list, no per-rule table"
  - "The Task 3 fixture is a never-issued AWS-access-key-ID-shaped value (AKIA + 16 chars), held as detect_head/detect_tail so neither variable name reads as a key assignment to gitleaks' own scan of the test file — measured live: AWS's own published EXAMPLE key ID produces zero findings under the pinned gitleaks 8.30.1 and would have made the test assert nothing while staying green"
  - "tests/scripts/gitleaks.bats stays in tests/scripts/ rather than a dedicated lint-config test root — the file's header comment records that a new root would need five coordinated wiring edits for one file, while tests/scripts/ is already the repository's one non-e2e bats root"

patterns-established: []

requirements-completed: [REL-03]

coverage:
  - id: D1
    description: "mise run lint runs a full git-history secret scan with gitleaks, reached by the pre-commit hook and the CI lint job"
    requirement: "REL-03"
    verification:
      - kind: other
        ref: "mise run lint (exit 0, gitleaks stage: '206 commits scanned... no leaks found')"
        status: pass
      - kind: other
        ref: "mise tasks info lint --json | jq -r '.run | length' == 6, .run[-1] still names zizmor"
        status: pass
    human_judgment: false
  - id: D2
    description: ".gitleaks.toml extends the default rule set and suppresses only the two measured fixture-value regexes, exempting no path, directory, or rule id"
    requirement: "REL-03"
    verification:
      - kind: other
        ref: "rg -c '^\\s*[A-Za-z]+ *=' .gitleaks.toml == 4 (title, useDefault, description, regexes); no `paths =`; no `[[rules.allowlists]]`; no exit-code/error-swallowing form in .mise.toml"
        status: pass
    human_judgment: false
  - id: D3
    description: "The scan is live: removing the allowlist makes mise run lint fail on the repository's own fixture findings, and restoring it makes the task pass again"
    requirement: "REL-03"
    verification:
      - kind: other
        ref: "Task 2 break-then-restore proof: 5 findings with the allowlist removed, git diff --exit-code .gitleaks.toml clean after restore, mise run lint exit 0 again, git log -1 -- .gitleaks.toml still shows the Task 1 commit (348e793)"
        status: pass
    human_judgment: false
  - id: D4
    description: "A standing test proves the configuration detects a secret-shaped value and clears a clean one, and was witnessed going red when the fixture was swapped for a value the rule set ignores"
    requirement: "REL-03"
    verification:
      - kind: unit
        ref: "tests/scripts/gitleaks.bats (2 tests, both passing; also exercised via `mise run test`'s `bats tests/scripts` line, 26/26 passing)"
        status: pass
    human_judgment: false
  - id: D5
    description: "Editing .gitleaks.toml alone re-runs the pre-commit lint hook, and the three tool descriptions (.mise.toml, .pre-commit-config.yaml hook name, docs/development.md) all say the task scans for secrets"
    requirement: "REL-03"
    verification:
      - kind: other
        ref: "rg -q 'gitleaks\\.toml' .pre-commit-config.yaml (files pattern); rg -qi secret .mise.toml; rg -qi secret docs/development.md"
        status: pass
    human_judgment: false

duration: ~9min
completed: 2026-09-21
status: complete
---

# Phase 5 Plan 3: Gitleaks Secret Scanning Summary

**`mise run lint` now runs a full git-history `gitleaks git` scan gated by a four-key `.gitleaks.toml` allowlist that clears exactly the five known test-fixture findings by value, proven live with a break-then-restore teeth check and a standing bats test that still detects a fresh secret-shaped value.**

## Performance

- **Duration:** ~9 min
- **Started:** 2026-09-20T21:47Z (first read)
- **Completed:** 2026-09-20T21:55:41-07:00 (last commit)
- **Tasks:** 3
- **Files modified:** 5 (2 created, 3 modified)

## Accomplishments

- `gitleaks = "8.30.1"` pinned in `.mise.toml`'s `[tools]`; `gitleaks version` confirms `8.30.1`, and `gitleaks git --help` confirms the invocation form used: `gitleaks git` with no `--source` argument scans the current directory, and config precedence puts `--config`/`-c` first and `(target path)/.gitleaks.toml` fourth — verified live (not assumed) by running `gitleaks git` from the repository root before `.gitleaks.toml` existed (5 leaks) and again after creating it (0 leaks, "no leaks found")
- `.gitleaks.toml` created at the repository root: `title`, `[extend] useDefault = true`, and one global `[[allowlists]]` table with a `description` and exactly two `regexes` entries (`0123456789abcdef`, `sentinel-api-key-`) — no `regexTarget` override, no path or directory exemption, no per-rule table
- `[tasks.lint].run` gained a `gitleaks git` entry between `actionlint` and `zizmor`; `[tasks.lint].description` and `docs/development.md`'s task-table row now say the task also scans for secrets; the pre-commit `lint` hook's `files` pattern gained `^\.gitleaks\.toml$` and its `name` field now lists gitleaks
- **Task 2 teeth proof:** removing the `[[allowlists]]` table made `mise run lint` fail with `leaks found: 5` across the five known fixture files (`tests/js/configPage.test.js` x2, `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs`, `.planning/codebase/TESTING.md`); restoring the file byte-for-byte (`git checkout -- .gitleaks.toml`) made `git diff --exit-code .gitleaks.toml` and `mise run lint` both pass again, and `git log -1 -- .gitleaks.toml` still shows only the Task 1 commit — the proof committed nothing
- **Task 3:** `tests/scripts/gitleaks.bats` scans a temporary directory outside the repository with `gitleaks dir --no-banner --no-color --redact -c "$REPO_ROOT/.gitleaks.toml"`; one test asserts a finding for a detectable AWS-access-key-ID-shaped value (`AKIA` + 16 chars, assembled from `detect_head`/`detect_tail` at run time), the other asserts none for a clean file. AWS's own published example key ID (`AKIAIOSFODNN7EXAMPLE`) was measured to produce zero findings under the pinned gitleaks 8.30.1; the detection value used instead produced one finding, rule id `aws-access-token`. Swapping the fixture tail for the EXAMPLE value made the detection test go red (`not ok 1`); restoring it byte-for-byte made both tests pass again.

## Task Commits

Each task was committed atomically (Task 2 produced no commit — it is a proof against Task 1's already-committed file):

1. **Task 1: The allowlist and the history scan land together** - `348e793` (feat)
2. **Task 2: Prove the scan has teeth and the allowlist is narrow** - no commit (break-then-restore proof against `348e793`; `git diff --exit-code .gitleaks.toml` confirms nothing changed)
3. **Task 3: A standing test that the configuration detects, not only that it passes** - `590f6d3` (test)

**Plan metadata:** (this commit)

## Files Created/Modified

- `.gitleaks.toml` - New. Root allowlist: `title`, `[extend] useDefault = true`, one global `[[allowlists]]` table with two fixture-value regexes
- `.mise.toml` - `gitleaks = "8.30.1"` added to `[tools]`; `[tasks.lint].run` gained a `gitleaks git` entry; `[tasks.lint].description` mentions secrets
- `.pre-commit-config.yaml` - The `lint` hook's `files` pattern gained `^\.gitleaks\.toml$`; the hook's `name` lists gitleaks
- `docs/development.md` - The `mise run lint` task-table row now says the task scans for secrets
- `tests/scripts/gitleaks.bats` - New. 2 tests proving the scan detects a fresh secret-shaped value and clears a clean one, using `gitleaks dir` against a temporary directory outside the repository

## Decisions Made

- Confirmed RESEARCH.md's Assumption A2 live rather than carrying it forward unchecked: `gitleaks git` with no source argument, run from the repository root, scans `.` and auto-discovers the root `.gitleaks.toml` — measured with the file absent (5 leaks) and present (0 leaks)
- Used a single global `[[allowlists]]` table with the default `regexTarget`, per D-07/RESEARCH.md Pattern 2 — no `regexTarget` override was needed since the default already targets the extracted secret value, which is what the two regexes were measured against
- The Task 3 fixture avoids "key"/"token"/"secret"/"api" in its variable names (`detect_head`/`detect_tail`) and never assembles the full value in the tracked file, so no new allowlist entry was needed and `mise run lint` reports nothing for the test file itself

## Deviations from Plan

None - plan executed exactly as written. Task 2 produced no commit by design (its own acceptance criteria require `git log -1 -- .gitleaks.toml` to still show the Task 1 commit).

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

`mise run lint` now performs the full-history secret scan REL-03 requires, reached identically by the pre-commit hook and the CI `lint` job, with a standing test proving detection rather than only a one-time pass. This is the same scan PUB-01's pre-publication audit will reuse (D-14) rather than reimplementing — plan 05-05 can invoke `mise run lint` or `gitleaks git` directly with no risk of a second, disagreeing scanner. No files this plan touched are shared with 05-04 or 05-05's expected scope.

---
*Phase: 05-public-repository*
*Completed: 2026-09-21*

## Self-Check: PASSED

- All key files found on disk: `.gitleaks.toml`, `.mise.toml`, `.pre-commit-config.yaml`, `docs/development.md`, `tests/scripts/gitleaks.bats`.
- Both task commits found in git log: `348e793`, `590f6d3` (Task 2 intentionally produced no commit).
- Every task's `<acceptance_criteria>` re-run and passing: `mise run lint` (exit 0), `.mise.toml`/`.gitleaks.toml` `rg` assertions, `mise tasks info lint --json` (length 6, zizmor last), `.pre-commit-config.yaml` gitleaks/config-path checks, `docs/development.md`/`.mise.toml` secret-mention checks, the Task 2 break-then-restore proof (5 findings removed, clean diff restored, no new commit), Task 3's `bats`/`shellcheck`/`shfmt` checks and the swap-to-EXAMPLE red/restore proof.
- Plan-level `<verification>` re-run: `mise run lint` (exit 0), `mise run test` (exit 0, 26/26 bats + 58/58 node tests), `prek run` (clean tree, hooks skipped with nothing to check), `mise tasks info lint --json` length 6.
