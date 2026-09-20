---
phase: 04-emby-traffic-under-load-and-failure
plan: 08
subsystem: docs
tags: [documentation, sqlite, changelog, fingerprint-store, auth-05, perf-02]

# Dependency graph
requires:
  - phase: 04-emby-traffic-under-load-and-failure
    provides: "04-01's SQLite store, 04-02's widened Emby sign-out, 04-05's one-time legacy import, and 04-07's live-verified upgrade path — this plan documents what all four ship"
provides:
  - "docs/how-it-works.md describing the SQLite fingerprint store, the one-time legacy-file import and its rollback cost, the widened Emby sign-out, and the two remaining unfixed costs"
  - "CHANGELOG.md's Unreleased section recording the store move and the rollback warning"
affects: []

# Actuals (#2632)
actuals:
  tokens: 3100
  tasks: 2
  commits: 2

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified:
    - docs/how-it-works.md
    - CHANGELOG.md

key-decisions:
  - "Task 1 and Task 2's docs/how-it-works.md edits landed in one commit (3ddb2b5) rather than two, since both tasks touch the same file and the workspace CLAUDE.md asks for batched edits to a single file to minimize hook runs. CHANGELOG.md, the artifact unique to Task 2, still got its own commit (e042269)."
  - "The read-failure Limits entry says the migration list reports readiness unknown, correcting the plan's own must_haves wording (\"needing an Emby login\") against the shipped source: EmbyLoginMethodUsers.DetermineState maps a failed RecordsAvailable() read to MigrationUserState.Unknown, not NeedsEmbyLogin, and docs/migration.md already documents that state as \"readiness unknown\". The plan's phrase would have told an administrator a verified user needs to log in again when the truth is the plugin simply cannot say."

requirements-completed: [PERF-02, AUTH-05, FPRT-04]

coverage:
  - id: D1
    description: "docs/how-it-works.md names the SQLite database and its location in place of the JSON file, and states the read failure, write failure, and one-time import as they now behave, checked against EmbyVerifiedPasswords.cs and PluginServiceRegistrator.cs"
    requirement: "FPRT-04"
    verification:
      - kind: other
        ref: "command: rg -q 'VerifiedPasswords.json|in memory' docs/how-it-works.md; test $? -eq 1"
        status: pass
      - kind: other
        ref: "command: rg -q 'imports' docs/how-it-works.md"
        status: pass
    human_judgment: false
  - id: D2
    description: "docs/how-it-works.md states the plugin ends the Emby session whenever it can read an access token, including a login response with no user name, and names the one response it cannot end a session for, checked against EmbyClient.cs"
    requirement: "AUTH-05"
    verification:
      - kind: other
        ref: "command: rg -q 'whenever it can read an access token' docs/how-it-works.md"
        status: pass
      - kind: other
        ref: "command: rg -q 'cannot end' docs/how-it-works.md"
        status: pass
    human_judgment: false
  - id: D3
    description: "docs/how-it-works.md's Limits section names the two costs this version does not remove — Emby calls inside Jellyfin's login lock, and Jellyfin's own per-login account save — with the reason each stays, no number anywhere, and every pre-existing Limits entry still present (10 before, 14 after)"
    requirement: "PERF-02"
    verification:
      - kind: other
        ref: "command: rg -c 'inside a lock' docs/how-it-works.md && rg -q 'saves the account on every accepted login' docs/how-it-works.md && rg -n 'ms\\b|milliseconds|percentile|p95|p99|requests per second' docs/how-it-works.md; test $? -eq 1 && test ! -e docs/performance.md"
        status: pass
    human_judgment: false
  - id: D4
    description: "CHANGELOG.md's Unreleased section records the SQLite store move and states that a rollback after this version loses every record written since the upgrade"
    requirement: "FPRT-04"
    verification:
      - kind: other
        ref: "command: rg -q 'SQLite' CHANGELOG.md"
        status: pass
    human_judgment: false
  - id: D5
    description: "mise run test, mise run lint, and mise run e2e all pass unchanged after a documentation-only change"
    verification:
      - kind: unit
        ref: "mise run test (unit 218/218, script 8/8, settings-page JS 58/58)"
        status: pass
      - kind: other
        ref: "mise run lint (dotnet format, shellcheck, shfmt, actionlint, zizmor)"
        status: pass
      - kind: e2e
        ref: "mise run e2e (41/41)"
        status: pass
    human_judgment: false

# Metrics
duration: ~40min
completed: 2026-09-20
status: complete
---

# Phase 4 Plan 8: Documentation Matched to the SQLite Store, the Wider Sign-Out, and the Two Remaining Costs Summary

**Rewrote `docs/how-it-works.md`'s fingerprint-store and Emby-session prose against the shipped SQLite code, added a Limits entry for the one-time upgrade import and its rollback cost, named the two costs PERF-02 leaves in place, and recorded both in `CHANGELOG.md`.**

## Performance

- **Duration:** ~40 min (approximate — no explicit session-start timestamp was captured before reading began)
- **Completed:** 2026-09-20T22:09:29Z
- **Tasks:** 2 of 2 completed
- **Files modified:** 2 (`docs/how-it-works.md`, `CHANGELOG.md`)

## Accomplishments

- `docs/how-it-works.md` § Login steps step 3 and a new Limits entry state that the plugin ends the Emby session whenever it can read an access token — including a no-user-name response Jellyfin still refuses — and that a response whose body the plugin cannot parse hides its token, so the plugin cannot end that one session (AUTH-05).
- § Security notes and two Limits entries now describe the SQLite store: the filename and its data-folder location replace the retired JSON file, a per-row commit replaces the whole-file rewrite, and the write-failure entry no longer claims an in-memory fallback that does not exist in the shipped code.
- A new Limits entry states the one-time legacy-file import and the rollback cost the human approved at 04-01's checkpoint: a server that rolls back after this version keeps whatever the JSON file held but loses records written since the upgrade, with no user locked out — each simply logs in through Emby once more.
- Two new Limits entries name the costs PERF-02 leaves unfixed — the Emby calls inside Jellyfin's shared login lock, and Jellyfin's own per-login account save — each with the reason no fix applies, no number, and no separate performance document.
- `CHANGELOG.md`'s Unreleased § Changed gained the store-move entry, and § Upgrade note gained the rollback paragraph, in the same plain, factual voice as the entries already there.
- Re-ran `mise run test`, `mise run lint`, and `mise run e2e` (41/41) as the plan's `<verification>` requires, confirming this phase's whole suite one final time.

## Task Commits

Each task was committed atomically, with one exception noted in Deviations below:

1. **Task 1: The store, the import, and the Emby session, as they now behave** — `3ddb2b5` (docs). This commit also carries Task 2's two docs/how-it-works.md Limits entries (login lock, account save) — see Deviations.
2. **Task 2: The two costs that stay, and the changelog entry** — `e042269` (docs, CHANGELOG.md only)

**Plan metadata:** *(this commit)*

## Files Created/Modified

- `docs/how-it-works.md` — step 3 of Login steps rewritten for the widened sign-out; the store sentence in Security notes rewritten for SQLite; the two fingerprint-file Limits entries replaced with the SQLite failure modes; four new Limits entries added (upgrade import and rollback, unreadable-response session limit, login-lock cost, account-save cost) — 10 pre-existing entries preserved, 14 total
- `CHANGELOG.md` — one new Unreleased § Changed entry for the store move; one new § Upgrade note paragraph for the rollback cost

## Decisions Made

See `key-decisions` in the frontmatter: the single-commit batching for Task 1/Task 2's shared file, and the "readiness unknown" correction to the plan's own must_haves wording, checked against `EmbyLoginMethodUsers.cs`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Process — same-file batching, not a Rule 1-4 category] Task 1 and Task 2's docs/how-it-works.md edits committed together**

- **Found during:** Task 2, before committing
- **Issue:** The plan structures Task 1 and Task 2 as separate commits, but both tasks add entries to the same file's same section (§ Limits). Task 1's edit already included all four new Limits entries in one Edit call, since writing them as a single ordered block was the only way to keep the section's entry order coherent and avoid a second pass re-reading and re-splicing the same list.
- **Fix:** None needed — the final content matches both tasks' specifications exactly, and every acceptance-criteria grep for both tasks passes against the committed file. Task 2's own unique artifact, `CHANGELOG.md`, still landed in its own commit.
- **Files modified:** `docs/how-it-works.md` (commit `3ddb2b5`)
- **Verification:** all Task 1 and Task 2 acceptance-criteria commands re-run and passing (see Self-Check)
- **Committed in:** `3ddb2b5`

**2. [Rule 1 - Bug in the plan's own prose, corrected against source] Read-failure Limits entry says "readiness unknown", not "needing an Emby login"**

- **Found during:** Task 1, while checking the rewritten read-failure sentence against `EmbyLoginMethodUsers.cs`
- **Issue:** The plan's own `must_haves` text describes the read-failure behavior as "the migration page reports every user as needing an Emby login until the read succeeds." `EmbyLoginMethodUsers.DetermineState` (`:91-104`) maps a failed `RecordsAvailable()` read to `MigrationUserState.Unknown`, a distinct state from `NeedsEmbyLogin` — and `docs/migration.md:25` already documents `Unknown` as "readiness unknown: ... its readiness cannot be reported until the read succeeds again." Writing "needing an Emby login" would have told an administrator that a user whose password Emby may already have verified must log in again, when the actual fact is narrower: the plugin cannot currently say either way.
- **Fix:** Wrote the entry against the code and the existing `docs/migration.md` terminology: "the migration list reports every user with a saved password as readiness unknown, rather than ready or needing an Emby login, until the read succeeds again."
- **Files modified:** `docs/how-it-works.md`
- **Verification:** cross-checked `EmbyLoginMethodUsers.cs:91-104` and `docs/migration.md:20-25` for the exact state names and their existing descriptions
- **Committed in:** `3ddb2b5`

---

**Total deviations:** 1 process note (same-file commit batching, no content impact) and 1 auto-fixed accuracy correction (Rule 1, applied per this plan's own `<accuracy_requirement>`: "where a summary [or plan] and the code disagree, the code wins").
**Impact on plan:** No scope creep. The commit-batching note has no effect on the shipped content; the wording correction makes the documentation more accurate than the plan's own draft text, which the plan's `<accuracy_requirement>` explicitly anticipates and authorizes.

## Issues Encountered

- Two acceptance-criteria greps needed a second pass after the first draft: the sentence "Whenever the plugin can read an access token..." (capitalized, sentence-initial) did not match the case-sensitive `rg` pattern `whenever it can read an access token`, and the phrase "the plugin performs itself" tripped the `ms\b` measurement-word grep because "performs" ends in "ms" before a word boundary. Both were reworded — the sentence restructured so "whenever" falls mid-sentence in lowercase, and "performs itself" replaced with "does itself" — and all `<acceptance_criteria>` and `<verify>` commands for both tasks were re-run and confirmed passing before committing.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- PERF-02, AUTH-05, and FPRT-04 are the last three requirements this phase declared. All three are now `Complete` in `REQUIREMENTS.md` — `requirements.ready-ids` confirmed `3/3 requirement(s) ready to mark complete` at the end of this plan, since 04-08 is the last plan declaring each of them.
- Phase 4 is complete: all 8 plans have summaries, `mise run test` (unit 218/218, script 8/8, JS 58/58), `mise run lint`, and `mise run e2e` (41/41) are all green.
- `docs/how-it-works.md` and `CHANGELOG.md` now match the shipped code with no stale references to the retired JSON-file store, and no unmeasured performance claim anywhere in the documentation.
- No blockers for Phase 5.

## Known Stubs

None.

## Threat Flags

None. The threat register's four entries (T-04-34 through T-04-37) are all `mitigate`/`accept` as designed: the overclaiming sentence about ending every Emby session is replaced and the unreadable-response limit is its own entry (T-04-34); both stale JSON-store sentences are replaced, checked against `EmbyVerifiedPasswords.cs` rather than against this plan (T-04-35); the database path entry keeps the existing fingerprint-not-hash statement, and the path itself was already discoverable to anyone with server filesystem access (T-04-36, accepted); the rollback cost is stated in both `CHANGELOG.md`'s Upgrade note and `docs/how-it-works.md`'s Limits section, the document an administrator reads first (T-04-37).

---
*Phase: 04-emby-traffic-under-load-and-failure*
*Completed: 2026-09-20*

## Self-Check: PASSED

- FOUND: docs/how-it-works.md (modified, on disk)
- FOUND: CHANGELOG.md (modified, on disk)
- FOUND: `3ddb2b5` in `git log --oneline --all`
- FOUND: `e042269` in `git log --oneline --all`
- Task 1 acceptance criteria: `rg -q 'VerifiedPasswords.json|in memory' docs/how-it-works.md; test $? -eq 1` — PASS. `rg -q 'whenever it can read an access token'` — PASS. `rg -q 'cannot end'` — PASS. `rg -q 'imports'` — PASS. `mise run lint` — clean.
- Task 2 acceptance criteria: `rg -c 'inside a lock'` — 1. `rg -q 'saves the account on every accepted login'` — PASS. `test ! -e docs/performance.md` — PASS. `rg -n 'ms\b|milliseconds|percentile|p95|p99|requests per second' docs/how-it-works.md` — no match (exit 1). Limits entry count — 14 (10 pre-existing + 4 added, both numbers confirmed by `awk`/`grep -c` against the committed file). `rg -q 'SQLite' CHANGELOG.md` — PASS, under Unreleased § Changed; Upgrade note gained one paragraph.
- Plan-level `<verification>`: `mise run test` — unit 218/218, script 8/8, JS 58/58, all green. `mise run lint` — clean (dotnet format, shellcheck, shfmt, actionlint, zizmor). `mise run e2e` — 41/41, run as the last plan in the phase per this plan's own instruction.
- `requirements.mark-complete PERF-02 AUTH-05 FPRT-04` — all three applied to both the checkbox and traceability-table surfaces in `REQUIREMENTS.md`, confirmed by re-reading the file.
