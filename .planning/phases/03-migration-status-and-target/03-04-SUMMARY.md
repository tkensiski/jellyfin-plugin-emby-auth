---
phase: 03-migration-status-and-target
plan: 04
subsystem: auth
tags: [csharp, dotnet, xunit, jellyfin-plugin, migration]

# Dependency graph
requires:
  - phase: 03-01
    provides: "SqliteJellyfinDbContextFactory, and the deliberately skipped target-parameterized theory row in DefaultLoginMethodTests.cs this plan unskips"
  - phase: 03-02
    provides: "TypeVisibilityTests' allowlist, pre-seeded with EmbyMigrationTask"
  - phase: 03-03
    provides: "MigrationUserState, EmbyLoginMethodUsers.ListAsync, and the worker-lookup-by-ScheduledTask-type pattern EmbyMigrationTask's rename follows"
provides:
  - "PluginConfiguration.MigrationTarget and PasswordSetTarget settings, with shape validation and a RemainOnEmbyLoginMethod sentinel that means no path moves anyone"
  - "LoginMethodMove.MoveAsync taking the destination as a parameter, and ResolveMigrationTarget/ResolvePasswordSetTarget resolving Move/Remain/Invalid"
  - "MoveAfterLogin and EmbyMigrationTask (renamed from MoveToDefaultLoginMethod and MoveEmbyUsersToDefaultTask) reading the destination from settings instead of a hardcoded constant, with no static plugin instance read"
  - "The migration task's new key EmbyAuthMigration, consumed by e2e/helpers.bash's run_migration_task"
affects: [03-05, 03-06, 03-07]

# Actuals (#2632)
actuals:
  tokens: 15494
  tasks: 3
  commits: 4

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "MoveTargetKind (Move/Remain/Invalid) and the MoveTarget record struct: two silent-vs-logged failure modes need different treatment, so a nullable string could not carry the distinction on its own"
    - "A shared Func<PluginConfiguration?> singleton registered once in PluginServiceRegistrator, handed to every class that used to read the static EmbyAuthPlugin.Instance directly"
    - "RED proven via Skip-marked theory rows against unimplemented shape validation, rather than a genuine compile-blocking RED, so the pre-commit hook's build step still passes on the RED commit"

key-files:
  created:
    - src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs
    - src/Jellyfin.Plugin.EmbyAuth/MoveAfterLogin.cs
    - src/Jellyfin.Plugin.EmbyAuth/EmbyMigrationTask.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/LoginMethodMoveTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/MoveAfterLoginTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyMigrationTaskTests.cs
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs
    - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs
    - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs
    - src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs
    - src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoublesTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/TypeVisibilityTests.cs
    - e2e/helpers.bash
    - CLAUDE.md
    - .claude/rules/plugin.md
    - .claude/rules/e2e.md

key-decisions:
  - "PluginConfiguration.cs's MigrationTarget default and EmbyAuthSettingsTests.cs's helper default (both referencing the old DefaultLoginMethod/LoginMethodMove const across the Task 1/Task 2 boundary) were updated in Task 2's commit even though neither file is in Task 2's declared <files> list — the rename deletes the class those defaults referenced, so leaving them stale would not compile"
  - "MoveEmbyUsersToDefaultTask.cs's move call was given a minimal, temporary compile fix in Task 2's commit (writing LoginMethodMove.DefaultProviderId unconditionally, matching today's behavior) rather than left broken, since Task 2 changed MoveAsync's signature; Task 3 replaced that temporary line with the real target resolution"
  - "TypeVisibilityTests' allowlist entry for the old MoveEmbyUsersToDefaultTask name (pre-seeded by 03-02 as a forward-compat placeholder) was removed in Task 3's own commit once the rename it anticipated actually landed, rather than left as dead weight"
  - "CLAUDE.md and .claude/rules/plugin.md's remaining reference to MoveEmbyUsersToDefaultTask was corrected in Task 3's commit for doc accuracy, even though Task 3's <files> list and grep-based acceptance criteria did not name these two files — CLAUDE.md's own rule requires docs stay accurate when behavior changes"
  - "Task 1's RED commit could not literally ship a non-compiling state, because prek's pre-commit hook runs mise run test and would block it. Skip-marked the four new shape-validation test cases instead, confirmed the build still passes with them skipped, then unskipped them in the GREEN commit — a compiling variant of the RED/GREEN split, following the Phase 01 precedent recorded in STATE.md for exactly this hook constraint"

patterns-established:
  - "Two-column failure taxonomy for a resolved setting: Remain (deliberate, silent) vs. Invalid (misconfiguration, one Error log) — never collapsed into a single nullable outcome"

requirements-completed: [MIGR-01, TEST-02, AUTH-06]

coverage:
  - id: D1
    description: "MigrationTarget and PasswordSetTarget settings exist with defaults that preserve today's behaviour on an upgrading install; a blank migration target and a whitespace-only password-set target are refused without echoing the value"
    requirement: "MIGR-01"
    verification:
      - kind: unit
        ref: "EmbyAuthSettingsTests.cs#DefaultsMigrationTargetToJellyfinsDefaultProviderId_AndPasswordSetTargetToEmpty"
        status: pass
      - kind: unit
        ref: "EmbyAuthSettingsTests.cs#Rejects_BlankMigrationTarget"
        status: pass
      - kind: unit
        ref: "EmbyAuthSettingsTests.cs#Rejects_WhitespaceOnlyPasswordSetTarget"
        status: pass
      - kind: unit
        ref: "EmbyAuthSettingsTests.cs#Accepts_EmptyPasswordSetTarget"
        status: pass
    human_judgment: false
  - id: D2
    description: "LoginMethodMove.MoveAsync writes whichever login method it is given, proven against two distinct provider ids; MoveAfterLogin resolves its destination from an injected settings source with three distinct outcomes and no static plugin instance"
    requirement: "MIGR-01, TEST-02"
    verification:
      - kind: unit
        ref: "LoginMethodMoveTests.cs#MoveAsync_WritesTheGivenTarget (2 InlineData rows)"
        status: pass
      - kind: unit
        ref: "LoginMethodMoveTests.cs (ResolveMigrationTarget/ResolvePasswordSetTarget, 6 test methods)"
        status: pass
      - kind: unit
        ref: "MoveAfterLoginTests.cs (6 test methods: move/Remain/Invalid/unverified-hash/wrong-mode)"
        status: pass
      - kind: e2e
        ref: "mise run e2e (27/27 passing)"
        status: pass
    human_judgment: false
  - id: D3
    description: "The migration task is renamed to EmbyMigrationTask and rekeyed to EmbyAuthMigration, moves every ready account to the configured target, moves nobody and says why when the target is Remain or Invalid, and the end-to-end suite finds it by its new key"
    requirement: "MIGR-01, AUTH-06"
    verification:
      - kind: unit
        ref: "EmbyMigrationTaskTests.cs (6 test methods)"
        status: pass
      - kind: e2e
        ref: "mise run e2e (27/27 passing, including the migration task and Run migration tests that depend on run_migration_task finding the task by its new key)"
        status: pass
    human_judgment: false

duration: 36min
completed: 2026-09-20
status: complete
---

# Phase 3 Plan 4: The Move Target Settings Summary

**Turned the after-login move, the migration task, and the password-set path's destination from a hardcoded Default constant into two configurable settings, and renamed the three classes and the task that were named after that constant**

## Performance

- **Duration:** 36 min
- **Started:** 2026-09-20T04:12:00Z
- **Completed:** 2026-09-20T04:47:42Z
- **Tasks:** 3
- **Files modified:** 22 (6 created, 16 modified — including 3 renames-with-content-changes counted as modified)

## Accomplishments

- `PluginConfiguration.MigrationTarget` and `PasswordSetTarget` exist as string settings, with a `RemainOnEmbyLoginMethod` sentinel const that means "move nobody, no fallback to Default." Shape validation in `EmbyAuthSettings.FindProblem` refuses a blank migration target and a whitespace-only password-set target, naming the setting without ever repeating the configured value.
- `DefaultLoginMethod` and `MoveToDefaultLoginMethod` are renamed to `LoginMethodMove` and `MoveAfterLogin` (git history follows). `LoginMethodMove.MoveAsync` takes the destination as a `targetProviderId` parameter instead of writing a hardcoded constant, proven against two distinct provider ids. `LoginMethodMove.ResolveMigrationTarget`/`ResolvePasswordSetTarget` return one of three outcomes — `Move`, `Remain`, or `Invalid` — so a deliberate no-move and a misconfiguration get different treatment (silent vs. one Error log).
- `MoveAfterLogin` reads its settings through an injected `Func<PluginConfiguration?>` instead of the static `EmbyAuthPlugin.Instance`, registered once as a shared singleton in `PluginServiceRegistrator` and handed to both `EmbyAuthenticationProvider` and `MoveAfterLogin`.
- `MoveEmbyUsersToDefaultTask` is renamed to `EmbyMigrationTask`, rekeyed from `EmbyAuthMoveUsersToDefault` to `EmbyAuthMigration`, and its `ExecuteAsync` resolves the migration target once before the loop — moving every ready account to that target, or moving nobody (with the appropriate silent/logged treatment) when the target is `Remain` or `Invalid`, always reporting progress to completion.
- The 03-01 invariant test proving `MoveAsync` writes whichever target it is given (previously `Skip`-marked, since the move had no target parameter yet) is unskipped and green.

## Task Commits

Each task was committed atomically:

1. **Task 1 (RED): failing tests for the two migration target settings** — `9cd3876` (test)
2. **Task 1 (GREEN): shape validation for the migration target settings** — `bffd70f` (feat)
3. **Task 2: the move takes its destination as a parameter** — `30091cb` (feat)
4. **Task 3: rename and rekey the migration task, target the configured login method** — `604d859` (feat)

_Task 1 (`tdd="true"`) produced a RED-then-GREEN pair, using the Phase 01 Skip-attribute convention (STATE.md) rather than a genuinely non-compiling commit, because the pre-commit hook runs `mise run test` and would block a build failure. Tasks 2 and 3 (`tdd="true"`) each produced one commit: their new behavior was proven correct before committing (build, full test suite, and — for Task 2 — `mise run e2e`, and for Task 3 additionally `scripts/dev-env.sh up`/`down` and `mise run e2e` again) rather than through a separate compiling RED state, following the 03-02/03-03 precedent for changes too structural to split into a meaningful intermediate red without contortion."_

## Files Created/Modified

- `src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs` — renamed from `DefaultLoginMethod.cs`; `MoveAsync`'s target parameter; `MoveTargetKind`/`MoveTarget`/`Resolve*Target`
- `src/Jellyfin.Plugin.EmbyAuth/MoveAfterLogin.cs` — renamed from `MoveToDefaultLoginMethod.cs`; injected settings source; Remain/Invalid handling
- `src/Jellyfin.Plugin.EmbyAuth/EmbyMigrationTask.cs` — renamed from `MoveEmbyUsersToDefaultTask.cs`; new `Key`/`Name`; resolves the target once before the loop
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` — `MigrationTarget`, `PasswordSetTarget`, `RemainOnEmbyLoginMethod`
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs` — carries both settings; two new shape checks
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` — one line updated to the renamed const (password-set path unchanged in behavior; plan 06 rewires it)
- `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` — the shared `Func<PluginConfiguration?>` singleton; renamed event-consumer registration
- `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` — references the renamed task type
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/LoginMethodMoveTests.cs` — renamed from `DefaultLoginMethodTests.cs`; unskipped invariant test; new resolver tests
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/MoveAfterLoginTests.cs` — new; six behaviors over the SQLite seam
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyMigrationTaskTests.cs` — new; six behaviors over the SQLite seam
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs`, `EmbyAuthenticationProviderTests.cs`, `EmbyUserDirectoryTests.cs`, `EmbyAuthControllerTests.cs`, `TestDoublesTests.cs`, `TypeVisibilityTests.cs` — renamed-type references and new coverage
- `e2e/helpers.bash`, `.claude/rules/e2e.md` — the task key lookup
- `CLAUDE.md`, `.claude/rules/plugin.md` — the renamed types and files

## Decisions Made

- Task 1's RED could not literally fail to compile without violating the pre-commit hook, so the four new shape-validation tests were `Skip`-marked in the RED commit (confirmed genuinely red by removing the Skip locally and observing three assertion failures plus one `NullReferenceException`, none of them a compile error) and unskipped in the GREEN commit — the same convention Phase 01 established for exactly this constraint.
- Two out-of-declared-scope compile fixes were folded into Task 2's commit rather than left broken: `PluginConfiguration.cs`'s default-value reference to the renamed const, and `MoveEmbyUsersToDefaultTask.cs`'s temporary (soon-superseded) call-site fix. Both are unavoidable, mechanical consequences of Task 2's own rename and signature change, not new scope.
- `TypeVisibilityTests`' pre-seeded allowlist entry for `MoveEmbyUsersToDefaultTask` (added in 03-02 as a forward-compat placeholder) was deleted in Task 3's commit once the rename it anticipated actually landed — a stale entry naming a deleted type is dead weight, and Task 3's own acceptance criteria greps `src/` and `tests/` for the old name.
- `CLAUDE.md` and `.claude/rules/plugin.md`'s one remaining reference to `MoveEmbyUsersToDefaultTask` (outside Task 3's declared `<files>` and grep gates) was corrected in Task 3's commit for the same reason `CLAUDE.md` itself states: keep docs accurate when behavior changes.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated PluginConfiguration.cs's default-value reference and three other test files after the LoginMethodMove rename**
- **Found during:** Task 2 (renaming `DefaultLoginMethod` to `LoginMethodMove`)
- **Issue:** `PluginConfiguration.MigrationTarget`'s default value, `EmbyAuthSettingsTests.cs`'s `Config()` helper default, `EmbyUserDirectoryTests.cs`'s static `Settings` field, and `TestDoublesTests.cs`'s two `DefaultLoginMethod` references all named the class the rename deletes. None of these four files is in Task 2's declared `<files>` list.
- **Fix:** Updated each reference to `LoginMethodMove.DefaultProviderId` / `LoginMethodMove.MoveAsync` with its new parameter.
- **Files modified:** `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoublesTests.cs`
- **Verification:** Full-solution `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` green (173/173) after Task 2's commit.
- **Committed in:** `30091cb` (Task 2)

**2. [Rule 3 - Blocking] Gave MoveEmbyUsersToDefaultTask.cs a temporary compile fix for the new MoveAsync signature**
- **Found during:** Task 2 (adding `targetProviderId` to `LoginMethodMove.MoveAsync`)
- **Issue:** `MoveEmbyUsersToDefaultTask.cs:70` called the old three-argument `MoveAsync`, which no longer exists. This file is Task 3's own file, not Task 2's.
- **Fix:** Passed `LoginMethodMove.DefaultProviderId` explicitly, preserving today's exact behavior (always moves to Default) until Task 3 replaced the line with real target resolution.
- **Files modified:** `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs`
- **Verification:** `dotnet test --solution` green after Task 2; superseded by Task 3's real implementation in the same file.
- **Committed in:** `30091cb` (Task 2), superseded by `604d859` (Task 3)

**3. [Rule 2 - Doc accuracy] Removed a stale allowlist entry and corrected two repository notes after the EmbyMigrationTask rename**
- **Found during:** Task 3 (renaming `MoveEmbyUsersToDefaultTask` to `EmbyMigrationTask`)
- **Issue:** `TypeVisibilityTests.cs`'s allowlist still listed the now-deleted `MoveEmbyUsersToDefaultTask` as a forward-compat placeholder from 03-02; `CLAUDE.md`'s Layout section and one `.claude/rules/plugin.md` bullet still named the old task type. None of these three files is in Task 3's declared `<files>` list, and neither doc file is checked by Task 3's grep-based acceptance criteria.
- **Fix:** Deleted the stale allowlist line; updated `CLAUDE.md` and `.claude/rules/plugin.md` to name `EmbyMigrationTask`.
- **Files modified:** `tests/Jellyfin.Plugin.EmbyAuth.Tests/TypeVisibilityTests.cs`, `CLAUDE.md`, `.claude/rules/plugin.md`
- **Verification:** `dotnet test --solution` green (179/179); `mise run lint` clean.
- **Committed in:** `604d859` (Task 3)

---

**Total deviations:** 3 auto-fixed (2 blocking, 1 doc accuracy). **Impact:** All three were unavoidable, mechanical consequences of the plan's own renames and signature changes reaching files just outside a task's declared scope. No behavior or design beyond what the plan specified was added.

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- `LoginMethodMove.ResolveMigrationTarget`/`ResolvePasswordSetTarget` and the `MoveTargetKind`/`MoveTarget` shape are ready for plan 05 (the settings page's target dropdown) and plan 06 (the server-side save-time refusal).
- **Note for plan 06:** `ResolveMigrationTarget`/`ResolvePasswordSetTarget` distinguish only blank/null (`Invalid`) from the sentinel (`Remain`) from an ordinary string (`Move`) — they do not check whether Jellyfin currently reports the configured target as enabled. A target that was valid when saved and later disappears (its plugin uninstalled) resolves to `Move` here, and `LoginMethodMove.MoveAsync` would write that stale provider id unconditionally. This is deliberate for this plan (Task 1's action text: "plan 06 answers it at the one place a save passes through"), and D-14/T-03-04's "skip and log" runtime behavior for a disappeared target is not yet implemented anywhere — plan 06's save-time refusal narrows the window but does not close it retroactively for an already-saved value. Confirmed by re-reading plan 06's own objective ("This plan puts the refusal where every save has to pass"), which is save-time only.
- `EmbyAuthenticationProvider.ChangePassword` still writes `LoginMethodMove.DefaultProviderId` unconditionally (unchanged in this plan by design) — plan 06 rewires it to `ResolvePasswordSetTarget`.
- The migration task's new key `EmbyAuthMigration` is live in `e2e/helpers.bash`'s `run_migration_task`; plan 07's JellyfinSecurity e2e test can rely on it.
- No blockers for 03-05 through 03-07.

---
*Phase: 03-migration-status-and-target*
*Completed: 2026-09-20*

## Self-Check: PASSED

All 6 created files exist on disk (`LoginMethodMove.cs`, `MoveAfterLogin.cs`, `EmbyMigrationTask.cs`, `LoginMethodMoveTests.cs`, `MoveAfterLoginTests.cs`, `EmbyMigrationTaskTests.cs`), all 4 commits (`9cd3876`, `bffd70f`, `30091cb`, `604d859`) are present in `git log`, and every plan-level verification (`mise run test`, `mise run lint`, `mise run e2e` 27/27, `scripts/dev-env.sh up`/`down`) ran green after Task 3. Every acceptance-criteria grep gate for all three tasks was re-run and passed.
