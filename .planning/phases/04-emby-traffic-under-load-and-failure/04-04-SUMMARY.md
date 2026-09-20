---
phase: 04-emby-traffic-under-load-and-failure
plan: 04
subsystem: auth
tags: [jellyfin, emby, concurrency, logging, xunit]

requires:
  - phase: 04-emby-traffic-under-load-and-failure
    provides: "04-03's single-flight guard pattern and the SemaphoreSlim/CapturingLogger precedent it left in TestDoubles.cs (not consumed directly by this plan, but confirmed compatible)"
provides:
  - "A named unit test (RefusesTheLosingLogin_WhenTwoFirstLoginsForOneNameRace) that deterministically drives the race-loser path without Docker: FakeUserManager.CreateUserThrows the exact duplicate-name ArgumentException Jellyfin builds."
  - "LogCreateAccountFailed reworded to state both causes of a failed account creation (a benign concurrent race, or a name Jellyfin rejects) and the remedy for each."
  - "Two invariant tests (CreatesExactlyOneAccount_WhenOneFirstLoginArrivesAlone, TheWinningLogin_LeavesTheAccountOnTheEmbyLoginMethodWithTheVerifiedHash) that plan 04-06's end-to-end burst assertions can rely on without knowing which concurrent login won."
affects: [04-06]

actuals:
  tokens: 1576
  tasks: 2
  commits: 3

tech-stack:
  added: []
  patterns:
    - "RED commit ships a renamed+extended [Fact] under [Fact(Skip = \"...\")] when the change is a message reword the pre-commit test gate would otherwise block; GREEN commit rewords the message and drops Skip (Phase 01/03/04 precedent, STATE.md)."
    - "Coverage-of-already-working-behavior tests proven by break-then-restore: break one line, watch the new test fail, git checkout -- the single file to restore it byte-for-byte, confirmed via git diff --stat / git diff HEAD."

key-files:
  created: []
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs

key-decisions:
  - "LogCreateAccountFailed's new message: \"Jellyfin cannot create an account for Emby user {EmbyUserName}. Two logins for this user may have arrived at the same time. If so, one of them succeeded, and the user can log in again. If Jellyfin does not allow this user name, rename the user on Emby.\" This exact wording is the contract plan 04-06 asserts against a live server (see downstream_contract in the plan prompt) — match it rather than re-deriving it."
  - "No detection branch was added between the two causes: ArgumentException from CreateUserAsync is the same exception type for a duplicate name and an invalid name (Jellyfin source, UserManager.cs:619-623), so the message states both causes rather than picking one (D-04)."
  - "Task 2's two new tests duplicate assertions already covered by existing tests (SavesTheHashInTheCallDirectlyAfterCreateUser, CreatesTheAccount_WithTheEmbyVerifiedHashAndTheEmbyLoginMethod). This is intentional per the plan: the new names are the identifiers plan 04-06's end-to-end invariants reference, not new coverage."

patterns-established: []

requirements-completed: []  # TEST-05's unit half only. The requirement stays Pending in REQUIREMENTS.md: 04-06 (Wave 2) also declares TEST-05 for the end-to-end half, and the shared-ID gate (#2388) blocks Complete until every declaring plan has a SUMMARY.

coverage:
  - id: D1
    description: "A named unit test drives the concurrent first-login race loser deterministically: refused with AuthenticationException, no delete attempted, exactly one Error entry naming both causes and no secret."
    requirement: "TEST-05"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLosingLogin_WhenTwoFirstLoginsForOneNameRace"
        status: pass
    human_judgment: false
  - id: D2
    description: "LogCreateAccountFailed's message states both possible causes of a failed account creation (a benign race, or a rejected name) and the remedy for each, still at Error level with one catch and one log call site."
    requirement: "TEST-05"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLosingLogin_WhenTwoFirstLoginsForOneNameRace"
        status: pass
    human_judgment: false
  - id: D3
    description: "A lone first login (no race) still creates exactly one account: one CreateUserAsync, one UpdateUserAsync, no DeleteUserAsync."
    requirement: "TEST-05"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#CreatesExactlyOneAccount_WhenOneFirstLoginArrivesAlone"
        status: pass
    human_judgment: false
  - id: D4
    description: "Whichever concurrent login wins, the account it leaves behind is on the Emby login method's provider ID with the Emby-verified hash — an invariant plan 04-06's end-to-end burst can assert without knowing which caller created the account."
    requirement: "TEST-05"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#TheWinningLogin_LeavesTheAccountOnTheEmbyLoginMethodWithTheVerifiedHash"
        status: pass
    human_judgment: false

duration: ~7min
completed: 2026-09-20
status: complete
---

# Phase 4 Plan 4: TEST-05's Unit Half Summary

**Deterministic unit test for the concurrent first-login race loser, plus a reworded Error message that names both causes and the remedy for each.**

## Performance

- **Duration:** ~7 min (approximate — measured from the prior plan's STATE.md timestamp to this plan's final commit, not a precisely instrumented session start)
- **Started:** ~2026-09-20T20:08:54Z
- **Completed:** 2026-09-20T20:15:21Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- `RefusesTheLosingLogin_WhenTwoFirstLoginsForOneNameRace` drives the race-loser path deterministically (`FakeUserManager.CreateUserThrows` the exact duplicate-name `ArgumentException` Jellyfin's `UserManager.cs:619-623` builds), asserting the refusal, the absent delete, and the single reworded Error entry with no secret in any captured entry.
- `LogCreateAccountFailed` now states both causes of a failed account creation — a benign concurrent race the user can retry, or a name Jellyfin rejects that needs renaming on Emby — instead of only the rename remedy.
- `CreatesExactlyOneAccount_WhenOneFirstLoginArrivesAlone` and `TheWinningLogin_LeavesTheAccountOnTheEmbyLoginMethodWithTheVerifiedHash` pin the two edges plan 04-06's end-to-end burst needs: the degenerate one-caller case, and the account-shape invariant that holds regardless of which concurrent login wins.

## Task Commits

Each task was committed atomically:

1. **Task 1 (RED): assert the race-loser message names both causes** - `4109250` (test)
2. **Task 1 (GREEN): reword the failed-account-creation message for a concurrent race** - `9d20755` (feat)
3. **Task 2: pin the unraced first login and the winning account's shape** - `0af8ad9` (test)

**Plan metadata:** *(this commit)*

## Files Created/Modified
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` - `LogCreateAccountFailed`'s `[LoggerMessage]` text reworded; no other production line touched.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` - `RefusesTheLogin_WhenCreateUserFailsBecauseJellyfinRejectsTheName` renamed to `RefusesTheLosingLogin_WhenTwoFirstLoginsForOneNameRace` and extended with a `CapturingLogger` and message assertions; two new facts added, `CreatesExactlyOneAccount_WhenOneFirstLoginArrivesAlone` and `TheWinningLogin_LeavesTheAccountOnTheEmbyLoginMethodWithTheVerifiedHash`.

## Decisions Made
- **The reworded message's exact text** (verbatim contract for plan 04-06, per this plan's downstream_contract): "Jellyfin cannot create an account for Emby user {EmbyUserName}. Two logins for this user may have arrived at the same time. If so, one of them succeeded, and the user can log in again. If Jellyfin does not allow this user name, rename the user on Emby." The `{EmbyUserName}` placeholder is the Emby user name the caller typed (case-normalized), never a secret.
- **Task 1's RED phase used the repo's established Skip-then-unskip convention** (Phase 01/03/04 precedent), because `prek`'s pre-commit hook runs `mise run test` and blocks any commit that leaves a test failing. Confirmed red locally against the pre-existing message text before adding the Skip marker (see Issues Encountered).
- **Task 2 added no production code.** Both new tests describe behavior `CreateAccountAsync` already has; each was proven to catch a regression by breaking one line, watching the matching test fail, and restoring the file with `git checkout -- <file>` (confirmed byte-identical via `git diff HEAD -- src/` returning zero lines both times).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None. The RED-phase observation the `tdd_discipline` section asked to be recorded: the extended `RefusesTheLosingLogin_WhenTwoFirstLoginsForOneNameRace` test, run against the unmodified (pre-reword) `EmbyAuthenticationProvider.cs`, failed with `Assert.Contains() Failure: Sub-string not found` — `Not found: "at the same time"` — against the captured entry `"Error: Jellyfin cannot create an account for Emby ..."`. This confirmed the test was red for the right reason (missing wording) before the Skip marker and the RED commit were made.

For Task 2's two break-then-restore proofs:
- Breaking `CreatesExactlyOneAccount_WhenOneFirstLoginArrivesAlone`'s guarantee (adding a spurious `DeleteUserAsync` call after account creation) failed the test with `Assert.Equal() Failure: Collections differ` — actual `Calls` gained a third `"DeleteUserAsync"` entry.
- Breaking `TheWinningLogin_LeavesTheAccountOnTheEmbyLoginMethodWithTheVerifiedHash`'s guarantee (removing the `user.AuthenticationProviderId = ProviderId;` line) failed the test with `Assert.Equal() Failure: Strings differ` — actual `AuthenticationProviderId` was the DI-registered provider's own type name instead of `EmbyAuthenticationProvider.ProviderId`.

Both breaks were reverted with `git checkout -- src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` and confirmed restored via `git diff HEAD -- src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` returning zero lines.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- **TEST-05's unit half is complete.** `RefusesTheLosingLogin_WhenTwoFirstLoginsForOneNameRace` goes red without Docker if the catch block in `CreateAccountAsync` ever changes.
- **For plan 04-06 (Wave 2):** the exact Error message text to assert against a live server's Jellyfin log is recorded verbatim above under Decisions Made and in the reworded `[LoggerMessage]` attribute at `EmbyAuthenticationProvider.cs:259`. The losing login's outcome is: HTTP 401 (never 500), no account created for that call, and the one existing account belongs to whichever login won — asserted here as `TheWinningLogin_LeavesTheAccountOnTheEmbyLoginMethodWithTheVerifiedHash`.
- **TEST-05 stays Pending in REQUIREMENTS.md** until 04-06 lands its end-to-end half (shared-ID gate, #2388 convention) — this is expected, not a gap.
- Full suite: 210/210 unit tests passing, `mise run lint` clean.

## Known Stubs

None.

## Threat Flags

None.

---
*Phase: 04-emby-traffic-under-load-and-failure*
*Completed: 2026-09-20*
