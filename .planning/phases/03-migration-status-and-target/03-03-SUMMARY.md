---
phase: 03-migration-status-and-target
plan: 03
subsystem: api
tags: [aspnetcore, entity-framework-core, xunit, jsdom, node-test, bats, jellyfin-plugin]

# Dependency graph
requires:
  - phase: 03-01
    provides: "SqliteJellyfinDbContextFactory, FakeTaskManager, FakeScheduledTaskWorker, FakeUserManager.AuthenticationProviders, EmbyVerifiedPasswords.RecordsAvailable(), and MigrationStatus.RecordsUnavailable"
  - phase: 03-02
    provides: "TypeVisibilityTests' allowlist, pre-seeded with MigrationUserState, Api.MigrationTaskInfo, and EmbyMigrationTask"
provides:
  - "MigrationUserState (Ready/NeedsEmbyLogin/NoPassword/Unknown) replacing the ReadyToMove boolean everywhere it appeared"
  - "MigrationTaskInfo and MigrationStatus.Task, reporting the migration task's state through the worker's ScheduledTask type, never its Jellyfin-assigned Id"
  - "MigrationStatus.AvailableTargets, the server-filtered list of enabled login methods minus this plugin's own Emby method"
  - "A migration list on the settings page rendering four states, and an e2e suite reading .State instead of the removed boolean"
affects: [03-04-the-move-target-settings, 03-05-the-settings-page-layout, 03-06-renaming-the-move-classes-and-task, 03-07-the-jellyfinsecurity-e2e-test]

# Actuals (#2632)
actuals:
  tokens: 9036
  tasks: 3
  commits: 3

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "A sealed test double (EmbyVerifiedPasswords) cannot be subclassed for call counting; a CapturingLogger's side-effect count (one Error log per failed Load() retry) proves RecordsAvailable() ran once per ListAsync invocation, not once per user"
    - "FakeScheduledTaskWorker.LastExecutionResult made nullable (TaskResult?) to express Jellyfin's real, unannotated (nullable-oblivious) IScheduledTaskWorker.LastExecutionResult, confirmed by reflecting MediaBrowser.Model 12.1.0 with NullabilityInfoContext"
    - "Finding a scheduled task's worker by `worker.ScheduledTask is TTask`, never by IScheduledTaskWorker.Id — which is a Jellyfin-assigned REST-route identifier, not the plugin's own task Key"

key-files:
  created: []
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs
    - src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs
    - src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs
    - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs
    - tests/js/testHelpers.js
    - tests/js/configPage.test.js
    - e2e/30-migration-modes.bats

key-decisions:
  - "NoPassword takes precedence over Unknown, resolving Open Question 1: a missing saved password is a database fact the fingerprint file cannot make unknowable, and AUTH-06 requires every no-password account to be named even while the file cannot be read"
  - "The enabled-login-method list rides on GET /EmbyAuth/Migration rather than a second endpoint, resolving Open Question 2, per D-02's one-response principle"
  - "MigrationTaskInfo.Progress passes worker.CurrentProgress through with no additional State-based filtering, matching Jellyfin's own real behavior where CurrentProgress is populated only while the task actually runs"
  - "FakeScheduledTaskWorker.LastExecutionResult was widened to TaskResult? — the real interface property carries no nullable annotation in MediaBrowser.Model 12.1.0 (confirmed via reflection), so the double needed to accept null to express 'never run'"

patterns-established:
  - "Scheduled-task worker lookup by ScheduledTask type, never by IScheduledTaskWorker.Id"

requirements-completed: [AUTH-06, FPRT-03, UI-03, MIGR-01, TEST-03]

coverage:
  - id: D1
    description: "Every account on the Emby login method carries exactly one of four MigrationUserState values; a missing saved password always reports NoPassword even while the fingerprint file cannot be read"
    requirement: "AUTH-06"
    verification:
      - kind: unit
        ref: "EmbyLoginMethodUsersTests.cs#ListAsync_MarksUserNoPassword_EvenWhenTheFingerprintFileCannotBeRead"
        status: pass
      - kind: unit
        ref: "EmbyLoginMethodUsersTests.cs#ListAsync_MarksUserUnknown_WhenTheFingerprintFileCannotBeRead"
        status: pass
      - kind: unit
        ref: "EmbyLoginMethodUsersTests.cs#ListAsync_NeverMarksNeedsEmbyLogin_AndChecksAvailabilityOnlyOnce_WhenTheFingerprintFileCannotBeRead"
        status: pass
      - kind: unit
        ref: "EmbyLoginMethodUsersTests.cs (9 test methods total)"
        status: pass
    human_judgment: false
  - id: D2
    description: "GET /EmbyAuth/Migration reports the migration task's state/progress/last-result through MigrationTaskInfo (absent when no worker is registered) and the pickable login methods through AvailableTargets, with the Emby method filtered out server-side"
    requirement: "UI-03"
    verification:
      - kind: unit
        ref: "EmbyAuthControllerTests.cs#GetMigrationStatus_ReportsTaskStateAndProgress_WhenAWorkerIsRunning"
        status: pass
      - kind: unit
        ref: "EmbyAuthControllerTests.cs#GetMigrationStatus_ReportsTaskAsAbsent_WhenTheOnlyRegisteredWorkerWrapsADifferentTask"
        status: pass
      - kind: unit
        ref: "EmbyAuthControllerTests.cs#GetMigrationStatus_ReportsNoLastEndTimeOrResult_WhenTheWorkerHasNoLastExecutionResult"
        status: pass
      - kind: unit
        ref: "EmbyAuthControllerTests.cs#GetMigrationStatus_ReportsAvailableTargets_ExcludingTheEmbyMethod"
        status: pass
      - kind: unit
        ref: "EmbyAuthControllerTests.cs (9 test methods total)"
        status: pass
    human_judgment: false
  - id: D3
    description: "The migration list on the settings page renders one of four wordings per account instead of a boolean split, and the e2e suite reads .State from the reshaped response"
    requirement: "MIGR-01"
    verification:
      - kind: unit
        ref: "configPage.test.js#the migration list renders a distinct string for each of the four states"
        status: pass
      - kind: unit
        ref: "configPage.test.js#a user name containing markup characters renders as text"
        status: pass
      - kind: e2e
        ref: "e2e/30-migration-modes.bats (mise run e2e, 27/27 passing)"
        status: pass
    human_judgment: false

duration: 25min
completed: 2026-09-20
status: complete
---

# Phase 3 Plan 3: Per-User Migration State and Task Info Summary

**Reshaped `GET /EmbyAuth/Migration` into one response carrying a four-state `MigrationUserState` per account, the migration task's live state, and the server-filtered list of pickable login methods**

## Performance

- **Duration:** 25 min
- **Started:** 2026-09-20T03:44:00Z
- **Completed:** 2026-09-20T04:09:00Z
- **Tasks:** 3
- **Files modified:** 10

## Accomplishments

- `MigrationUserState` (`Ready`, `NeedsEmbyLogin`, `NoPassword`, `Unknown`) replaces the `ReadyToMove` boolean on `EmbyLoginMethodUser` and `MigrationUser`. A missing saved password always reports `NoPassword`, even while the fingerprint file cannot be read — the database fact wins over the file fact, resolving RESEARCH.md Open Question 1.
- `EmbyAuthController` now takes `IUserManager` and returns `MigrationTaskInfo` (the migration task's `State`, `Progress`, `LastEndTimeUtc`, `LastResult`, absent when no worker is registered) and `AvailableTargets` (Jellyfin's enabled login methods minus this plugin's own Emby method) on the same `GET /EmbyAuth/Migration` response, resolving Open Question 2 in favor of one endpoint over two.
- The task's worker is found by `worker.ScheduledTask is MoveEmbyUsersToDefaultTask` — a type check — never by `IScheduledTaskWorker.Id`, which is a Jellyfin-assigned REST-route identifier that would silently match nothing.
- The migration list on `configPage.html` renders one of four wordings per account and a per-state count in the summary; `tests/js/testHelpers.js`'s stub carries the full response shape; `e2e/30-migration-modes.bats` reads `.State` from the reshaped response and passes against a real Jellyfin server.

## Task Commits

Each task was committed atomically:

1. **Task 1: One state per account, with the database fact winning over the file fact** — `497cca0` (feat)
2. **Task 2: The response carries the task state and the login methods an administrator may pick** — `66f9fb4` (feat)
3. **Task 3: The migration list renders four states, and the end-to-end suite reads them** — `9614dd9` (feat)

_All three tasks carried `tdd="true"`. Each new production behavior was proven by a break-then-restore cycle against the already-written, already-correct implementation and tests — the production code and tests were written together, then a targeted regression was introduced, confirmed red against the exact tests meant to catch it, and reverted to the byte-identical committed content — following the established phase 01/02/03-01/03-02 precedent for TDD tasks where the behavior is genuinely new. No task produced a separate `test(...)` commit, because in every case the red run was confirmed through this break-and-restore cycle rather than a compile-blocked or pre-implementation red state — the same convention 03-02's Task 2 used._

## Files Created/Modified

- `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs` — `MigrationUserState` enum; `EmbyLoginMethodUser.State`; `ListAsync` computes state with `NoPassword` checked before `RecordsAvailable()`, called exactly once per invocation
- `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs` — `candidate.ReadyToMove` became `candidate.State == MigrationUserState.Ready` (compile-blocking consequence of the field removal, fixed within Task 1 — see Deviations)
- `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` — `IUserManager` constructor parameter; `MigrationTaskInfo` record; `MigrationStatus.Task` and `.AvailableTargets`; `MigrationUser.State`; worker lookup by `ScheduledTask` type
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` — `migrationStateText` switch over the four state names; per-state summary counts
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs` — the eight Task 1 behaviors, including the `CapturingLogger`-based once-per-call proof
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs` — task-state, absent-task, no-last-result, and available-targets coverage
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — `FakeScheduledTaskWorker.LastExecutionResult` widened to `TaskResult?`
- `tests/js/testHelpers.js` — `stubApiClient` resolves `Task` and `AvailableTargets` alongside `Users` and `RecordsUnavailable`
- `tests/js/configPage.test.js` — the four-state rendering test (same name across all four users, so only the state-derived text can make the four strings distinct) and updated fixtures
- `e2e/30-migration-modes.bats` — `migration_user_state` reads `.State`; three call sites updated to the new state strings

## Decisions Made

- **NoPassword precedes Unknown** (Open Question 1): documented on the enum's XML `<remarks>`, so the next reader does not rediscover the reasoning.
- **One response, not two** (Open Question 2): `AvailableTargets` rides on the existing `GET /EmbyAuth/Migration` route rather than a new endpoint, per D-02.
- **`Progress` passes through with no `State`-based filtering**: Jellyfin's own `CurrentProgress` is already non-null only while a task runs; adding a plugin-side filter would duplicate that guarantee for no benefit.
- **`FakeScheduledTaskWorker.LastExecutionResult` widened to `TaskResult?`**: confirmed via `NullabilityInfoContext` reflection against `MediaBrowser.Model` 12.1.0 that the real interface property carries no nullable annotation (compiled without a nullable context), so assigning `null` to express "never run" is legitimate and matches the plan's own `worker.LastExecutionResult?.` guidance.
- **Strengthened the "four distinct rendered strings" test** beyond the acceptance criterion's literal wording: seeding four *different* names (as a first draft did) would make the strings distinct by name alone regardless of whether `migrationStateText` worked at all. Seeding the *same* name across all four states makes the test genuinely exercise the state-to-text mapping — confirmed by breaking `migrationStateText` and watching this test fail for the right reason.
- **Call-count proof via log side effects, not a counting subclass**: `EmbyVerifiedPasswords` is `sealed`, so a counting subclass (the plan's first suggested option) is unavailable. `Load()` logs an `Error` on every failed retry and never caches a failure, so the number of log entries after seeding several users with an unreadable file is exactly the number of times `RecordsAvailable()`/`Matches()` touched the file — an equally rigorous, available alternative.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed the two consumers left compiling against the removed `ReadyToMove` field**
- **Found during:** Task 1 (immediately after removing `EmbyLoginMethodUser.ReadyToMove`)
- **Issue:** `MoveEmbyUsersToDefaultTask.cs:69` and `EmbyAuthController.cs:45` both read `candidate.ReadyToMove` / `user.ReadyToMove`. Neither file is in Task 1's `<files>` list, but removing the field broke the full-solution build (`dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx`), which is Task 1's own final verify gate.
- **Fix:** `MoveEmbyUsersToDefaultTask.cs` (not touched again this plan) got its permanent fix: `candidate.State == MigrationUserState.Ready`. `EmbyAuthController.cs` got a minimal, temporary compile fix (`user.State == MigrationUserState.Ready`, keeping the old boolean `MigrationUser.ReadyToMove` shape for one commit) so Task 1 closed on a green build; Task 2's own commit then replaced that temporary line with the full reshape described above.
- **Files modified:** `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs`, `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs`
- **Verification:** Full-solution `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` green after Task 1's commit and after Task 2's commit.
- **Committed in:** `497cca0` (Task 1), `66f9fb4` (Task 2, permanent reshape)

---

**Total deviations:** 1 auto-fixed (1 blocking). **Impact:** No scope creep — both fixes were the direct, unavoidable consequence of Task 1's own field removal, and Task 2 replaced the temporary fix with the plan's intended shape on schedule.

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- `MigrationUserState`, `MigrationTaskInfo`, and `AvailableTargets` are in place for plan 04 (the move-target settings) and plan 05 (the settings page's target dropdown and polling loop), both of which read directly off this response.
- `TypeVisibilityTests`' allowlist already carried `MigrationUserState` and `Api.MigrationTaskInfo`, so this plan needed no edit to that file.
- The worker-lookup-by-type pattern (`worker.ScheduledTask is MoveEmbyUsersToDefaultTask`) will need its type argument updated when plan 06 renames the class to `EmbyMigrationTask`.
- No blockers for 03-04 through 03-07.

---
*Phase: 03-migration-status-and-target*
*Completed: 2026-09-20*

## Self-Check: PASSED

All ten files listed under Files Created/Modified exist on disk with the described content, and all three commits (`497cca0`, `66f9fb4`, `9614dd9`) are present in `git log`. `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` (154 total, 153 passed, 1 pre-existing skip), `mise run lint`, `mise run test`, and `mise run e2e` (27/27) all ran green after Task 3. Every acceptance-criteria grep gate for all three tasks was re-run and passed.
