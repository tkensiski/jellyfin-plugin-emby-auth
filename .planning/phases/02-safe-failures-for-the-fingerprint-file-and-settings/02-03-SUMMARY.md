---
phase: 02-safe-failures-for-the-fingerprint-file-and-settings
plan: 03
subsystem: auth
tags: [csharp, dotnet, xunit, jellyfin-plugin, fingerprint-file]

# Dependency graph
requires:
  - phase: 02-safe-failures-for-the-fingerprint-file-and-settings (02-01)
    provides: ordering only — this plan lands after 02-01 so every commit's pre-commit hook runs against a settled `mise run test` chain (the JS suite 02-01 added); no functional dependency, disjoint file set
provides:
  - "EmbyVerifiedPasswords.Load() caches only a successfully read (or missing) file; a failed read returns null and leaves the cache field untouched"
  - "Record() writes nothing and keeps nothing while the read fails; Matches() is false for every user"
  - "A failed read is retried on the very next Matches or Record call once the file becomes readable, with no Jellyfin restart"
  - "docs/how-it-works.md states the read-failure guarantees for an administrator"
affects: [phase-3-fprt-03, phase-3-ui-03]

# Actuals (#2632)
actuals:
  tokens: 2353
  tasks: 2
  commits: 3

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Load() signals a failed read by returning null and never writing the `_fingerprints` field, so a caller gets retry-on-next-call for free with no second flag"

key-files:
  created: []
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs
    - docs/how-it-works.md

key-decisions:
  - "Load() returns Dictionary<Guid, string>? — null only on a failed read — so Record and Matches can tell a failed read from a genuinely empty file without a second field or flag"
  - "No read-failure flag added to EmbyVerifiedPasswords in this phase, per the plan's explicit instruction — Phase 3 adds it alongside the single GET /EmbyAuth/Migration response change FPRT-03 and UI-03 share"
  - "Matches() kept as a single return expression, using `Load() is { } fingerprints && fingerprints.TryGetValue(...) && ...` pattern matching — avoids the `?.`-with-out-var construct entirely while satisfying the plan's 'keep the single-expression form' instruction"

patterns-established:
  - "Nullable-return cache loader: a private Load()-shaped method reports a failed read by returning null and skipping the field write, rather than a separate bool flag or exception"

requirements-completed: [FPRT-02]

coverage:
  - id: D1
    description: "A failed fingerprint-file read does not destroy the file's records when a later login is accepted"
    requirement: "FPRT-02"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#UnreadableFile_KeepsItsRecords_WhenALoginIsRecorded"
        status: pass
    human_judgment: false
  - id: D2
    description: "A failed read is retried, not cached forever — the same store instance answers correctly once the file becomes readable"
    requirement: "FPRT-02"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#UnreadableFile_IsReadAgain_WhenItBecomesReadable"
        status: pass
    human_judgment: false
  - id: D3
    description: "Fifty concurrent Record calls against an unreadable file leave it byte-identical, with no .tmp file left beside it"
    requirement: "FPRT-02"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#ConcurrentRecords_AreNotWritten_WhenTheFileIsUnreadable"
        status: pass
    human_judgment: false
  - id: D4
    description: "docs/how-it-works.md states the read-failure behaviour an administrator sees: no user moves, the records in the file are kept, and a temporary problem clears without a Jellyfin restart"
    requirement: "FPRT-02"
    verification:
      - kind: other
        ref: "rg checks in 02-03-PLAN.md Task 2 <verify>: 'cannot read the fingerprint file', 'keeps the records|records that are in the file', 'restart' all present; 'prevents data loss|eliminat|cannot lose|rules out' absent"
        status: pass
    human_judgment: true
    rationale: "Whether the wording reads as actionable to an administrator is a judgment call the plan's own verify block reserves for a human-check, not something a regex match can settle on its own"

# Metrics
duration: 28min
completed: 2026-09-19
status: complete
---

# Phase 02 Plan 03: Fingerprint-File Read Failsafety Summary

**`EmbyVerifiedPasswords.Load()` stops caching an empty dictionary before a read succeeds, so a failed read no longer lets the next accepted login silently erase every record in the fingerprint file.**

## Performance

- **Duration:** 28 min
- **Started:** 2026-09-19T00:20:00-07:00 (approx.)
- **Completed:** 2026-09-19T00:46:11-07:00
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- `Load()` (`EmbyVerifiedPasswords.cs`) no longer assigns `_fingerprints = []` before the read. A failed read now returns `null` and never touches the field, so the next `Matches` or `Record` call re-enters `Load()` and reads the file again instead of reusing a cached empty map for the life of the process.
- `Record()` returns early — writing nothing, keeping nothing — when `Load()` reports a failed read; `Matches()` is `false` for every user under the same condition. `Record` and `Matches` still hold the one `_lock`; `Load()` gained no lock of its own.
- Three new unit tests, confirmed genuinely RED against the unfixed code before the fix landed (D-05's byte-identical-file assertion, the same-instance retry assertion, and the fifty-way concurrent-write assertion), then GREEN after the fix. All 12 `[Fact]`/`[Theory]` cases in the file pass, plus the 9 pre-existing ones.
- `docs/how-it-works.md` splits the combined read/write-failure bullet into two. The read half now states the three guarantees an administrator can act on: no user moves while the read fails, the records in the file survive, and a temporary problem (such as a file locked while Jellyfin starts) clears on the next login without a Jellyfin restart. The write half's wording is unchanged, because FPRT-01 (Phase 3) owns it.
- `mise run e2e` (27/27) and `mise run test` (118 xUnit cases + 18 `node --test` cases) both pass, run twice across the two task commits.

## Task Commits

Each task was committed atomically, following the plan's RED/GREEN TDD discipline for Task 1:

1. **Task 1 (RED):** `1e72346` — `test(02-03): add failing tests for a fingerprint-file read that keeps failing` (test, `[Fact(Skip = ...)]`)
2. **Task 1 (GREEN):** `8de683a` — `feat(02-03): keep the fingerprint file's records through a failed read` (feat, unskips the three tests)
3. **Task 2:** `20ba502` — `docs(02-03): state what a fingerprint-file read failure does` (docs)

_All three tests in the RED commit were run unskipped first and observed failing exactly as the plan predicted — `UnreadableFile_KeepsItsRecords_WhenALoginIsRecorded` on the post-`Record` byte-equality assertion, `UnreadableFile_IsReadAgain_WhenItBecomesReadable` on the same-instance retry assertion, `ConcurrentRecords_AreNotWritten_WhenTheFileIsUnreadable` on the post-write byte-equality assertion — before the `Skip` attribute was added._

## TDD Gate Compliance

RED gate (`1e72346`, `test(...)`) precedes GREEN gate (`8de683a`, `feat(...)`) in git history, as `tdd="true"` on Task 1 requires. No REFACTOR commit was needed — the GREEN commit's diff was already the minimal shape.

## Files Created/Modified

- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` — `Load()` returns `Dictionary<Guid, string>?`; caches only a successful read or a missing file; a failed read returns `null` without touching the field. `Record` returns early on a `null` `Load()` result. `Matches` folds the null case into its existing single-expression return via `Load() is { } fingerprints && ...`. `LogReadFailed`'s message text no longer says "the next record replaces the file" — it now says the plugin records nothing and moves no user while the read fails, keeps the records that are in the file, and reads the file again on the next login. XML docs on `Record` and `Matches` updated to match.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` — adds `UnreadableFile_KeepsItsRecords_WhenALoginIsRecorded`, `UnreadableFile_IsReadAgain_WhenItBecomesReadable`, `ConcurrentRecords_AreNotWritten_WhenTheFileIsUnreadable`, a shared `UnreadableContents` constant (a JSON record cut short mid-value, not a placeholder string), and a second temporary-path field (`_retryFilePath`) with matching `Dispose` cleanup.
- `docs/how-it-works.md` — the fingerprint-file-failure bullet under `## Limits` is now two bullets: a rewritten read-failure bullet and an unchanged write-failure bullet.

## Decisions Made

- **`Load()`'s nullable-return contract.** Returning `Dictionary<Guid, string>?` and skipping the field write on failure was the exact one-line-of-intent fix `02-RESEARCH.md` identified: it satisfies D-02 (retry on next call), D-03 (both callers already treat a failed read as "nothing recorded, nothing matched"), and D-04 (no change to the combined catch filter) in one change, with no second field or flag needed.
- **The read-failure flag stays out of scope**, per the plan's explicit instruction (`02-CONTEXT.md`'s "Claude's Discretion" item, resolved by the plan itself, not left open here). Phase 3 adds it next to the single `GET /EmbyAuth/Migration` response change FPRT-03 and UI-03 share, so its shape is chosen alongside the response that exposes it.
- **`Matches()`'s null-check idiom.** `Load() is { } fingerprints && fingerprints.TryGetValue(userId, out var recorded) && recorded == fingerprint` keeps the method a single return statement (matching the plan's "keep the single-expression form" instruction) while sidestepping the `Load()?.TryGetValue(userId, out var recorded) == true` idiom's definite-assignment edge case entirely.

## Deviations from Plan

None - plan executed exactly as written. All three of the plan's flagged assumptions held:

- **Assumption #1** (the retry test's second-store dependency): recording through a separate `EmbyVerifiedPasswords` instance over `_retryFilePath` produced text the primary store read back correctly — no fallback to the delete-and-recheck alternative was needed.
- **Assumption #2** (analyzer behavior on the nullable return): `mise run lint`'s `dotnet format --verify-no-changes` and the analyzer pass (`AllEnabledByDefault`, warnings as errors) both passed clean on the first attempt with the `is { } fingerprints &&` pattern — no suppression was needed.
- **Assumption #3** (the numbered-step count in `docs/how-it-works.md`): read and recorded as 6 before editing (`rg -c '^[0-9]\. \*\*' docs/how-it-works.md`), and confirmed unchanged at 6 after Task 2 — the edited bullet lives under `## Limits`, an unordered list, not among the numbered login steps.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

FPRT-02 is closed. `EmbyVerifiedPasswords` now fails closed and heals on its own from a transient read failure, with every existing caller (`EmbyLoginMethodUsers`, `DefaultLoginMethod`) unaffected because `Matches`'s public contract (`bool`, `false` on any failure) did not change.

Phase 3 has two things waiting on this plan's `Load()` change: FPRT-03 (show a read failure on the settings page) and UI-03 (the migration list follows real task state) both read the single planned `GET /EmbyAuth/Migration` response change, which is where the read-failure flag this plan deliberately did not add will live. No blockers.

---
*Phase: 02-safe-failures-for-the-fingerprint-file-and-settings*
*Completed: 2026-09-19*

## Self-Check: PASSED

All created/modified files and all three task commit hashes (`1e72346`, `8de683a`, `20ba502`) verified present on disk and in git history.
