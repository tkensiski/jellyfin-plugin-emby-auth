---
phase: 03-migration-status-and-target
plan: 02
subsystem: testing
tags: [xunit, logging-message, type-visibility, documentation, jellyfin-plugin]

# Dependency graph
requires:
  - phase: 03-01
    provides: "CapturingLogger<T>, the EmbyVerifiedPasswordsTests file conventions, and EmbyVerifiedPasswords.RecordsAvailable()"
provides:
  - "A corrected write-failure log message and XML summary on EmbyVerifiedPasswords.Record, agreeing with the code's actual cache-then-write order"
  - "TypeVisibilityTests — an assembly-wide exported-surface guard, seeded with the type names plans 03 and 04 add later"
  - "docs/how-it-works.md's blank-password passage (DOCS-05) and a verified, unique DOCS-01 pointer"
affects: [03-03-per-user-migration-state-and-task-info, 03-04-the-move-target-settings, 03-05-the-settings-page-layout, 03-06-renaming-the-move-classes-and-task, 03-07-the-jellyfinsecurity-e2e-test]

# Actuals (#2632)
actuals:
  tokens: 3359
  tasks: 3
  commits: 4

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "RED confirmed via a Skip-marked test whose log-content assertion fails against the currently shipped text, then unskipped in the GREEN commit that corrects the text — used when a TDD task's behavior already works and only the stated text is wrong"
    - "Break-and-restore extended to a cascading compile-error case: making EmbyAuthenticationProvider public alone fails the build (CS0051) rather than compiling with a failing test; confirmed the internal boundary runs deep by also flipping its two internal dependencies public and observing further build failures, then reverting all three files to byte-identical content"

key-files:
  created:
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/TypeVisibilityTests.cs
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs
    - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs
    - docs/how-it-works.md

key-decisions:
  - "MIGR-02's guard covers every exported type in the plugin assembly, not only EmbyAuthenticationProvider, per the plan's resolved discretion question — a guard on one class catches one mistake, and the rule in .claude/rules/plugin.md is already assembly-wide"
  - "The allowlist in TypeVisibilityTests is seeded with type names plans 03 and 04 add later (MigrationUserState, MigrationTaskInfo, EmbyMigrationTask) so those plans land with no edit to this file — the assertion is that every exported type is listed, never that every listed name is exported yet"
  - "Confirming the MIGR-02 red run stopped at a compile-error demonstration rather than chasing a passing-build failing-test result: flipping EmbyAuthenticationProvider public alone already fails to compile (CS0051), and flipping its two internal dependencies public as well cascades into further compile errors (EmbyLogin, EmbyUser, EmbyUserStatus, EmbyAuthSettings would all need to follow) rather than converging on a green build — recorded as the red evidence and not pursued further, since a compile failure is at least as strong a signal as a runtime test failure"

requirements-completed: [FPRT-01, MIGR-02, DOCS-01, DOCS-05]

coverage:
  - id: D1
    description: "EmbyVerifiedPasswords.Record's write-failure path: the in-memory record survives, exactly one Error entry is logged, the log message and XML summary both say the record stays in memory until a restart, Record's statement order is unchanged"
    requirement: "FPRT-01"
    verification:
      - kind: unit
        ref: "EmbyVerifiedPasswordsTests.cs#WriteFailure_KeepsTheRecordInMemory_AndLogsExactlyOneErrorSayingSo"
        status: pass
      - kind: unit
        ref: "EmbyVerifiedPasswordsTests.cs#WriteFailure_LeavesTheFilesPreviousContentsUnchanged"
        status: pass
      - kind: unit
        ref: "EmbyVerifiedPasswordsTests.cs#WriteFailure_DoesNotLogASecondTime_ForTheSameUserAndHash"
        status: pass
      - kind: unit
        ref: "EmbyVerifiedPasswordsTests.cs#Record_Throws_ForAnEmptyOrNullHash"
        status: pass
      - kind: unit
        ref: "EmbyVerifiedPasswordsTests.cs#Fingerprint_IsCaseSensitive"
        status: pass
      - kind: other
        ref: "rg -q -i 'in memory' src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs"
        status: pass
    human_judgment: false
  - id: D2
    description: "A unit test fails if EmbyAuthenticationProvider becomes public, and a second fails if any other type joins the plugin assembly's exported surface without a recorded reason; the class documents why it stays internal"
    requirement: "MIGR-02"
    verification:
      - kind: unit
        ref: "TypeVisibilityTests.cs#EmbyAuthenticationProvider_StaysInternal"
        status: pass
      - kind: unit
        ref: "TypeVisibilityTests.cs#NoTypeJoinsTheExportedSurface_WithoutARecordedReason"
        status: pass
    human_judgment: false
  - id: D3
    description: "docs/how-it-works.md states Jellyfin's blank-password behaviour and its three connections, its write-failure entry agrees with the corrected code, and DOCS-01's shutdown-step pointer is present exactly once with a resolving anchor"
    requirement: "DOCS-01, DOCS-05"
    verification:
      - kind: other
        ref: "rg -q -i 'blank password' docs/how-it-works.md && rg -q 'DefaultAuthenticationProvider' docs/how-it-works.md && rg -q -i 'interim' docs/how-it-works.md && rg -q -i 'cannot tell|does not know' docs/how-it-works.md"
        status: pass
      - kind: other
        ref: "test \"$(rg -c 'migration\\.md#shut-down-emby' docs/how-it-works.md)\" = \"1\""
        status: pass
      - kind: e2e
        ref: "mise run e2e (27/27 passing)"
        status: pass
    human_judgment: false

duration: 16min
completed: 2026-09-20
status: complete
---

# Phase 3 Plan 2: Safe Write-Failure Wording and MIGR-02 Summary

**Corrected the fingerprint write-failure log message and XML summary to say what the code does, added an assembly-wide exported-type guard, and stated Jellyfin's blank-password behaviour in the docs**

## Performance

- **Duration:** 16 min
- **Started:** 2026-09-20T03:28:00Z
- **Completed:** 2026-09-20T03:43:41Z
- **Tasks:** 3
- **Files modified:** 5 (1 created, 4 modified)

## Accomplishments

- `EmbyVerifiedPasswords.Record`'s write-failure log message and XML summary now say what lines 58-68 actually do: the in-memory record survives a failed write and the user still moves, and only the on-disk copy falls behind until the next successful write or a Jellyfin restart. `Record()`'s cache-then-write order (D-09) is unchanged.
- `TypeVisibilityTests.cs` fails if `EmbyAuthenticationProvider` becomes public, and fails if any other type in the plugin assembly joins the exported surface without a recorded reason on its allowlist — which is already seeded with the names plans 03 and 04 add later.
- `docs/how-it-works.md` states that Jellyfin's Default login method opens a no-password account with a blank password (verified at `DefaultAuthenticationProvider.cs:61-68`) and connects it to the deleted account after a failed save, the settings-page warning, and the plugin's interim role. Its write-failure entry in Limits now agrees with the corrected code, and DOCS-01's shutdown-step pointer was verified present exactly once with a resolving anchor.

## Task Commits

Each task was committed atomically:

1. **Task 1 (RED): failing test for the write-failure log text** — `2d0b53d` (test)
2. **Task 1 (GREEN): correct the write-failure log text and XML summary** — `d8c0a34` (feat)
3. **Task 2: guard EmbyAuthenticationProvider and the exported surface** — `cfc9e74` (test)
4. **Task 3: state the blank-password behaviour and fix the write-failure entry** — `8e761e0` (docs)

_Task 1 (`tdd="true"`) produced a RED-then-GREEN pair per the phase's TDD convention. Task 2 (`tdd="true"`) produced one commit because its guard passed against already-working behavior with no separate RED phase — its red run was confirmed by breaking the production code instead, per below._

## Files Created/Modified

- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` — five new test methods covering the write-failure path, the argument guard, and fingerprint case sensitivity
- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` — corrected `[LoggerMessage]` text and XML summary on `Record`
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TypeVisibilityTests.cs` — the MIGR-02 guard: one test on `EmbyAuthenticationProvider`, one on the assembly's exported surface
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` — added a `<remarks>` block naming `TypeVisibilityTests` and stating why the class stays internal
- `docs/how-it-works.md` — corrected the write-failure entry in Limits, added the blank-password passage under Security notes, verified the DOCS-01 pointer

## Decisions Made

- MIGR-02's guard covers the whole exported surface of the assembly, not only `EmbyAuthenticationProvider`, resolving the plan's discretion question in favor of the broader guard.
- The allowlist names types not yet added (`MigrationUserState`, `MigrationTaskInfo`, `EmbyMigrationTask`) in advance, so plans 03 and 04 need no edit to this file when those types land.
- Confirming MIGR-02's red run stopped at a compile-error demonstration: making `EmbyAuthenticationProvider` public alone fails to compile (CS0051, inconsistent accessibility on its internal constructor parameters). Making its two internal dependencies (`EmbyClient`, `EmbyUserDirectory`) public as well cascades into further compile errors rather than converging on a passing build with a failing test — the internal boundary runs several types deep. All three files were reverted to byte-identical content before the real commit.

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- The corrected write-failure text and the `TypeVisibilityTests` allowlist are in place for the remaining plans in this phase; no rework needed.
- `TypeVisibilityTests`'s allowlist already anticipates `MigrationUserState`, `Api.MigrationTaskInfo`, and `EmbyMigrationTask` — plans 03 and 04 add these types with no edit to that test file.
- No blockers for 03-03 through 03-07.

---
*Phase: 03-migration-status-and-target*
*Completed: 2026-09-20*

## Self-Check: PASSED

All five files listed under Files Created/Modified exist on disk with the described content, and all four commits (`2d0b53d`, `d8c0a34`, `cfc9e74`, `8e761e0`) are present in `git log`. The plan-level `dotnet test --solution` (145 total, 144 passed, 1 pre-existing skip), `mise run lint`, `mise run test`, and `mise run e2e` (27/27) all ran green after Task 3.
