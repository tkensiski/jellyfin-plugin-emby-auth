---
phase: 03-migration-status-and-target
plan: 01
subsystem: testing
tags: [sqlite, entity-framework-core, xunit, jsdom, node-test, jellyfin-plugin]

# Dependency graph
requires: []
provides:
  - "SqliteJellyfinDbContextFactory and FakeJellyfinDatabaseProvider — a real JellyfinDbContext on SQLite in-memory, reusable by every later TEST-02/TEST-03 plan in this phase"
  - "FakeTaskManager and FakeScheduledTaskWorker — hand-written ITaskManager/IScheduledTaskWorker doubles for the migration task API"
  - "FakeUserManager.AuthenticationProviders — settable GetAuthenticationProviders() for D-13's enabled-login-method filtering"
  - "EmbyVerifiedPasswords.RecordsAvailable() and MigrationStatus.RecordsUnavailable — the read-failure signal FPRT-03 needs, wired end to end to the settings page"
  - "TEST-02 baseline coverage of DefaultLoginMethod.MoveAsync and EmbyLoginMethodUsers.ListAsync, protecting plan 04's renames and target-parameter change"
affects: [03-02-safe-write-failure-wording-and-migr-02, 03-03-per-user-migration-state-and-task-info, 03-04-the-move-target-settings, 03-05-the-settings-page-layout, 03-06-renaming-the-move-classes-and-task, 03-07-the-jellyfinsecurity-e2e-test]

# Actuals (#2632)
actuals:
  tokens: 9731
  tasks: 3
  commits: 4

# Tech tracking
tech-stack:
  added: [Microsoft.EntityFrameworkCore.Sqlite 10.0.11 (test project only)]
  patterns:
    - "One held-open SqliteConnection(\"DataSource=:memory:\") backs IDbContextFactory<JellyfinDbContext>, so ExecuteUpdateAsync and other translations the EF Core InMemory provider cannot run become testable"
    - "Hand-written test doubles for every new Jellyfin interface seam (IJellyfinDatabaseProvider, ITaskManager, IScheduledTaskWorker), following the FakeUserManager convention — no mocking library"
    - "A load-bearing constructor-injected TDD gate: production RecordsUnavailable wiring shipped intentionally wrong (hardcoded false) in the test commit, confirmed red, then fixed in a separate feat commit"
    - "Skip = on a single theory row records an assumption-delta invariant test (DefaultLoginMethod.MoveAsync always writes the Default provider today) that a later plan unskips once the behavior generalizes"

key-files:
  created:
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoublesTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/DefaultLoginMethodTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs
  modified:
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs
    - src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs
    - src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs
    - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
    - tests/js/testHelpers.js
    - tests/js/configPage.test.js

key-decisions:
  - "RecordsAvailable() is a thin, correct wrapper over the already-tested Load() cache — written directly rather than through an artificial red phase, since the underlying failure detection already works"
  - "The RecordsUnavailable wiring was shipped intentionally wrong (hardcoded false) in the test commit, confirmed red, then corrected in the feat commit, because the field could not exist for the test to compile without some initial value"
  - "The second-target-provider theory row is skipped rather than given a real parameter, because DefaultLoginMethod.MoveAsync has no target parameter yet — plan 04 task 2 adds one and unskips this row"
  - "EmbyLoginMethodUsersTests and DefaultLoginMethodTests were proven by temporarily breaking the already-working production guard/filter/ordering logic and confirming the new tests caught the regression, then restoring the file to its exact committed state, rather than an artificial pre-implementation red"

patterns-established:
  - "SQLite in-memory JellyfinDbContext factory: construct-and-dispose per test class, never shared across classes, so parallel test classes cannot see each other's rows"

requirements-completed: [TEST-02, TEST-03, FPRT-03]

coverage:
  - id: D1
    description: "A real JellyfinDbContext on SQLite in-memory, and hand-written ITaskManager/IScheduledTaskWorker doubles"
    requirement: "TEST-02"
    verification:
      - kind: unit
        ref: "TestDoublesTests.cs#SqliteJellyfinDbContextFactory_SharesRows_AcrossContexts"
        status: pass
      - kind: unit
        ref: "TestDoublesTests.cs#SqliteJellyfinDbContextFactory_ExecutesExecuteUpdateAsync"
        status: pass
      - kind: unit
        ref: "TestDoublesTests.cs#FakeTaskManager_RecordsQueuedTypeAndReturnsWorkers"
        status: pass
    human_judgment: false
  - id: D2
    description: "GET /EmbyAuth/Migration reports RecordsUnavailable, and the Migration section shows a message pointing at the Jellyfin log while it is true, with no restart needed once the file becomes readable"
    requirement: "FPRT-03"
    verification:
      - kind: unit
        ref: "EmbyAuthControllerTests.cs#GetMigrationStatus_ReportsRecordsUnavailable_WhenTheFingerprintFileCannotBeRead"
        status: pass
      - kind: unit
        ref: "EmbyAuthControllerTests.cs#GetMigrationStatus_ClearsRecordsUnavailable_OnceTheFileBecomesReadable"
        status: pass
      - kind: unit
        ref: "configPage.test.js#a records-unavailable migration status shows a warning naming the Jellyfin log"
        status: pass
      - kind: unit
        ref: "configPage.test.js#the records-unavailable message contains no file system path"
        status: pass
    human_judgment: false
  - id: D3
    description: "DefaultLoginMethod and EmbyLoginMethodUsers have unit tests against the SQLite seam, with exactly one deliberately skipped test that plan 04 unskips"
    requirement: "TEST-02"
    verification:
      - kind: unit
        ref: "DefaultLoginMethodTests.cs (5 test methods, 4 passing + 1 skipped theory row)"
        status: pass
      - kind: unit
        ref: "EmbyLoginMethodUsersTests.cs (6 test methods, all passing)"
        status: pass
      - kind: e2e
        ref: "mise run e2e (27/27 passing against the reshaped MigrationStatus response)"
        status: pass
    human_judgment: false

duration: 34min
completed: 2026-09-20
status: complete
---

# Phase 3 Plan 1: The Database Test Seam and Records-Unavailable Tracer Summary

**SQLite in-memory JellyfinDbContext factory, hand-written ITaskManager doubles, and an unreadable-fingerprint-file warning wired end to end from EmbyVerifiedPasswords through the API to the settings page**

## Performance

- **Duration:** 34 min
- **Started:** 2026-09-20T02:50:13Z
- **Completed:** 2026-09-20T03:24:25Z
- **Tasks:** 3
- **Files modified:** 11 (5 created, 6 modified)

## Accomplishments

- A real `JellyfinDbContext` runs against SQLite in-memory through `SqliteJellyfinDbContextFactory`, proven by writing a row through one context and reading it back through a second context from the same factory, and by observing `DefaultLoginMethod.MoveAsync`'s `ExecuteUpdateAsync` change a row — the write EF Core's InMemory provider cannot execute.
- `FakeTaskManager`, `FakeScheduledTaskWorker`, and `FakeJellyfinDatabaseProvider` extend `TestDoubles.cs` following the existing hand-written-double convention; `FakeUserManager.GetAuthenticationProviders()` now returns a settable value instead of throwing.
- `EmbyVerifiedPasswords.RecordsAvailable()` exposes the fingerprint file's already-working read-failure detection; `MigrationStatus.RecordsUnavailable` carries it through `GetMigrationStatus`; the Migration section of the settings page shows a warning naming the Jellyfin log while it is true, with no file system path in the message, and the condition clears on the next call with no restart.
- `DefaultLoginMethodTests.cs` and `EmbyLoginMethodUsersTests.cs` give TEST-02 baseline coverage to two previously untested classes, protecting plan 04's renames and target-parameter change behind a green suite.

## Task Commits

Each task was committed atomically:

1. **Task 1: The database and task-manager test seams** — `aef3f27` (feat)
2. **Task 2 (RED): failing test for the records-unavailable path** — `9aec67d` (test)
3. **Task 2 (GREEN): report records-unavailable status end to end** — `cba1fa0` (feat)
4. **Task 3: TEST-02 baseline coverage of the move-query classes** — `34c6fc9` (test)

_TDD tasks produced multiple commits (test → feat) per the RED-GREEN convention this repo established in phases 01 and 02._

## Files Created/Modified

- `tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj` — pins `Microsoft.EntityFrameworkCore.Sqlite` `10.0.11`, test project only
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — `SqliteJellyfinDbContextFactory`, `FakeJellyfinDatabaseProvider`, `FakeTaskManager`, `FakeScheduledTaskWorker`, `FakeUserManager.AuthenticationProviders`
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoublesTests.cs` — proves each new double works
- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` — `RecordsAvailable()`
- `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` — `MigrationStatus.RecordsUnavailable`, wired from `RecordsAvailable()`
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` — the records-unavailable warning in `loadEmbyAuthMigration`
- `tests/js/testHelpers.js` — `recordsUnavailable` stub option
- `tests/js/configPage.test.js` — the warning-present, warning-absent, and no-path jsdom tests
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs` — controller-level coverage of the records-unavailable path
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/DefaultLoginMethodTests.cs` — TEST-02 coverage of `MoveAsync`
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs` — TEST-02 coverage of `ListAsync`

## Decisions Made

- `RecordsAvailable()` was written directly as a correct, minimal wrapper over `Load()` rather than through an artificial red phase — the failure-detection logic it exposes already works and is already tested by `EmbyVerifiedPasswordsTests`.
- The Task 2 RED commit shipped `MigrationStatus.RecordsUnavailable` wired to a hardcoded `false` (a real bug, not a stub that fails to compile), so `EmbyAuthControllerTests.cs` and `configPage.test.js` could compile and genuinely fail before the GREEN commit fixed the wiring. This follows the RED-then-Skip-then-unskip convention STATE.md records for phases 01 and 02, extended here to a case where the new tests exercise brand-new production surface rather than pre-existing behavior.
- Task 3's five behavior groups were proven by temporarily breaking `DefaultLoginMethod.MoveAsync`'s login-method/hash guard and `EmbyLoginMethodUsers.ListAsync`'s filter/order/readiness logic, confirming the new tests caught each regression, then restoring both files to their exact committed byte content (`git diff` empty) before committing the tests alone — these two classes already worked correctly in production (per `.planning/codebase/TESTING.md`'s "Types without unit tests" list) and needed coverage, not a fix.
- The second-target-provider theory row in `DefaultLoginMethodTests.cs` is the one deliberately skipped test the plan calls for: `DefaultLoginMethod.MoveAsync` has no target parameter today and always writes `DefaultLoginMethod.ProviderId`, so asserting a different target fails by design. Confirmed red when temporarily unskipped, then re-skipped with a message naming plan 04 task 2 as the unskip point.

## Deviations from Plan

None — plan executed exactly as written. The one execution note below is not a deviation from the plan's content, only from the expected single-agent, uninterrupted flow.

### Execution Note: mid-plan commit-signing checkpoint

Task 3's commit (`34c6fc9`) could not land on the first four attempts: `git commit` failed at the signing step (`error: 1Password: failed to fill whole buffer` / `error: 1Password: agent returned an error`, then `fatal: failed to write commit object`). This repo signs commits via SSH through the 1Password SSH agent (`commit.gpgsign=true`, `gpg.format=ssh`). Per the user's global CLAUDE.md 1Password rules, a stalled signing prompt is not evidence of being signed out, and bypassing signing (`--no-gpg-sign`) is prohibited without explicit request — so execution paused and returned a `checkpoint:human-action` to the orchestrator rather than retrying indefinitely or working around it. Task 3's test files were already fully implemented, verified (full suite green, lint clean, `mise run e2e` 27/27, and both new test files confirmed to catch a real regression via temporary break-and-restore), and staged before the pause — nothing about the code was uncertain, only the commit's signing step. Once the user was at the keyboard, the orchestrator retried the identical, already-prepared commit message and it succeeded with hooks running normally (no `--no-verify`, no `--no-gpg-sign`). No code changed as a result of the pause.

---

**Total deviations:** 0. **Impact:** none — the plan's content and every commit's actual content are exactly as designed; only the wall-clock timing of the final commit was affected by an infrastructure prompt outside the plan's scope.

## Issues Encountered

None beyond the signing checkpoint documented above.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- `SqliteJellyfinDbContextFactory`, `FakeTaskManager`, and `FakeScheduledTaskWorker` are ready for every remaining TEST-02/TEST-03 plan in this phase (03-03 through 03-07).
- `FakeUserManager.AuthenticationProviders` is ready for 03-04's D-13 enabled-login-method filtering.
- `MigrationStatus.RecordsUnavailable` is live; plan 03-03 completes the response reshape (`MigrationTaskInfo`, the per-user `MigrationUserState` enum) that D-02 calls for.
- The skipped theory row in `DefaultLoginMethodTests.cs` is plan 04 task 2's explicit signal to add `MoveAsync`'s target parameter and unskip it — no other action needed until then.
- No blockers for 03-02 through 03-07.

---
*Phase: 03-migration-status-and-target*
*Completed: 2026-09-20*
