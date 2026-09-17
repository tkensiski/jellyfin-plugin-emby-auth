---
phase: 01-account-creation-and-login-security
plan: 01
subsystem: auth
tags: [xunit, dependency-injection, jellyfin-plugin]

requires: []
provides:
  - "A settings-source constructor seam on EmbyAuthenticationProvider (Func<PluginConfiguration?>), reusable by later phases needing to test classes that read the static plugin instance"
  - "FakeUserManager and FakeCryptoProvider test doubles in TestDoubles.cs, reusable by any future test of EmbyAuthenticationProvider"
  - "Every AUTH-04 failure path (create, save, cleanup delete, existing-account save) refused with AuthenticationException, never HTTP 500"
affects: [01-02, 01-03, 01-04]

actuals:
  tokens: 5209
  tasks: 2
  commits: 4

tech-stack:
  added: []
  patterns:
    - "Settings-source constructor injection: EmbyAuthenticationProvider takes Func<PluginConfiguration?> instead of reading EmbyAuthPlugin.Instance directly, so tests supply configuration with no static state"
    - "Hand-written throw-by-default IUserManager/ICryptoProvider fakes with per-method configurable exceptions, matching the existing StubHttpMessageHandler convention — no mocking library"

key-files:
  created:
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs
    - src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs

key-decisions:
  - "Restructured CreateAccountAsync's save/cleanup flow to log exactly one Error entry per failure (LogSaveFailed when the delete recovers, LogDeleteFailed when it does not), instead of the two-independent-catches shape RESEARCH.md sketched, to satisfy the plan's explicit 'exactly one Error entry' acceptance criterion without double-logging the same incident"
  - "Committed the RED phase of each TDD task with the failing test marked Skip, then unskipped it in the immediately following GREEN commit, because the repository's pre-commit hook runs the full test suite and blocks any commit that leaves a test failing — RED was still confirmed locally (exact failure reason logged) before each Skip was added"

requirements-completed: [AUTH-04, TEST-01]

coverage:
  - id: D1
    description: "A save failure of an unexpected exception type (outside the old DbUpdateException/ResourceNotFoundException filter) refuses the login with AuthenticationException, both for new-account creation and for an existing account's save"
    requirement: "AUTH-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_WhenTheSaveAfterCreateUserFailsWithAnUnexpectedExceptionType"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_WhenSavingThePasswordOfAnExistingAccountFailsWithAnUnexpectedExceptionType"
        status: pass
    human_judgment: false
  - id: D2
    description: "A save failure of an already-caught type (DbUpdateException) still refuses the login and attempts the cleanup delete — documents pre-existing coverage per ROADMAP criterion 1"
    requirement: "AUTH-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_WhenTheSaveAfterCreateUserFailsWithACaughtExceptionType"
        status: pass
    human_judgment: false
  - id: D3
    description: "When both the save and the cleanup delete fail, the login is still refused with AuthenticationException (not the delete's own exception), exactly one Error log entry names the account, and no log entry contains the password or the derived hash"
    requirement: "AUTH-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_WhenTheSaveAndTheCleanupDeleteBothFail"
        status: pass
    human_judgment: false
  - id: D4
    description: "EmbyAuthenticationProvider reads settings only through a constructor-injected Func<PluginConfiguration?>, with PluginServiceRegistrator supplying the production delegate; a correctly configured server still logs in successfully (proved by every e2e login test passing unmodified)"
    requirement: "TEST-01"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs (all cases construct the provider via the new seam)"
        status: pass
      - kind: e2e
        ref: "mise run e2e (27/27 passing, including every ordinary login path)"
        status: pass
    human_judgment: false

duration: 26min
completed: 2026-09-17
status: complete
---

# Phase 1 Plan 1: AUTH-04 Failure Paths and the EmbyAuthenticationProvider Test Seam Summary

**Every exception `IUserManager` can throw during account creation or password save now becomes `AuthenticationException`, closing four HTTP-500 gaps behind a new settings-source constructor seam and the first unit tests for `EmbyAuthenticationProvider`.**

## Performance

- **Duration:** 26 min
- **Started:** 2026-09-17T20:26:59Z
- **Completed:** 2026-09-17T20:52:28Z
- **Tasks:** 2
- **Files modified:** 4 (1 created, 3 modified)

## Accomplishments

- Added a `Func<PluginConfiguration?> configurationSource` constructor parameter to `EmbyAuthenticationProvider`, removing its last read of the static `EmbyAuthPlugin.Instance`, with `PluginServiceRegistrator` supplying the production delegate via a factory registration
- Added `FakeUserManager` and `FakeCryptoProvider` test doubles to `TestDoubles.cs`, following the repo's existing throw-by-default convention — no mocking library
- Added `EmbyAuthenticationProviderTests.cs`, the first unit tests for `EmbyAuthenticationProvider`, covering all four AUTH-04 failure paths: an unexpected-type save failure on account creation, an already-caught-type save failure, a save-and-cleanup-delete double failure, and an unexpected-type save failure on an existing account
- Widened both narrow `catch (Exception ex) when (ex is DbUpdateException or ResourceNotFoundException)` clauses to unconditional `catch (Exception ex)`, so no exception type from `UpdateUserAsync` can escape as HTTP 500
- Guarded the previously unguarded cleanup `DeleteUserAsync` call with its own try/catch and a new `LogDeleteFailed` log method, so a delete failure is logged and swallowed, never escaping or replacing the original login refusal

## Task Commits

Each task followed the RED-GREEN TDD cycle:

1. **Task 1: unexpected-type save failure, end-to-end** — `d7b1853` (test, RED, committed Skip) → `536fff6` (feat, GREEN, unskips and widens `CreateAccountAsync`'s catch)
2. **Task 2: the remaining AUTH-04 paths** — `4203aaa` (test, RED, committed Skip) → `cd95bcd` (feat, GREEN, unskips, guards the cleanup delete, adds `LogDeleteFailed`, widens `SavePasswordAsync`'s catch)

## Files Created/Modified

- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` — new file: 4 test cases covering every AUTH-04 failure path, a `CreateProvider` factory building real `EmbyClient`/`EmbyUserDirectory`/`EmbyVerifiedPasswords` collaborators over a real `ServiceCollection`
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — added `FakeUserManager` (22-member `IUserManager` fake, 3 configurable) and `FakeCryptoProvider` (5-member `ICryptoProvider` fake, 1 real)
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` — settings-source constructor seam, both save catches widened, cleanup delete guarded and restructured to log exactly one Error entry per failure
- `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` — factory registration supplying the settings-source delegate

## Decisions Made

- **Restructured the save/cleanup flow to avoid double-logging.** The plan's illustrative shape (two independent catches, one around the save and one around the cleanup delete) would log both `LogSaveFailed` and `LogDeleteFailed` when both fail — but the plan's own acceptance criterion requires exactly one Error entry for that case. Reworked `CreateAccountAsync` to capture the save exception, attempt the delete once, and log only the more actionable of the two outcomes (the save failure if the delete recovers, the delete failure if it does not), then always throw `AuthenticationException` wrapping the original save exception. Delete is still attempted exactly once and never retried (D-03).
- **RED-phase commits used `[Fact(Skip = ...)]` instead of a literally failing test.** The repository's pre-commit hook runs the full test suite (`mise run test`) and blocks any commit where a test fails, which is incompatible with a literal RED-state commit. For each of the two RED phases, the failure was confirmed locally first (exact reason logged in this session, matching RESEARCH.md's predicted failure for each case), then committed as `Skip` with a comment explaining why, then unskipped in the immediately following GREEN commit alongside the fix. This follows CLAUDE.md's "never bypass hooks" rule while preserving the required `test(01-01)` → `feat(01-01)` commit ordering and the actual RED verification the TDD discipline requires.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] CA1031 analyzer error on the guarded cleanup-delete catch**
- **Found during:** Task 2 (guarding the cleanup delete)
- **Issue:** The plugin project's `AnalysisMode: AllEnabledByDefault` flags `catch (Exception ex)` that does not rethrow as CA1031, and warnings are errors — the new guarded delete catch (which must swallow per D-03) failed the build.
- **Fix:** Added `[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "...")]` on `CreateAccountAsync`, per the repository's existing suppression convention in `PluginConfiguration.cs`.
- **Files modified:** src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs
- **Verification:** `mise run lint` and `dotnet test` both pass.
- **Committed in:** cd95bcd

**2. [Rule 4-adjacent - Judgment] Restructured CreateAccountAsync to log exactly one Error entry per failure**
- **Found during:** Task 2, writing `RefusesTheLogin_WhenTheSaveAndTheCleanupDeleteBothFail`
- **Issue:** The plan's `<action>` describes two independent catches (save catch calls `LogSaveFailed` and throws; a separate `finally`-nested catch around the delete calls `LogDeleteFailed`), which produces two Error log entries when both fail. The plan's own `<behavior>` and `<acceptance_criteria>` for the same test both explicitly require "exactly one" Error entry.
- **Fix:** Captured the save exception instead of logging immediately; attempted the delete once; logged `LogSaveFailed` only if the delete recovered, `LogDeleteFailed` only if it did not; always threw `AuthenticationException` wrapping the original save exception afterward. See Key Decisions above.
- **Files modified:** src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs
- **Verification:** `RefusesTheLogin_WhenTheSaveAndTheCleanupDeleteBothFail` asserts `Assert.Single` on Error-level log entries; passes.
- **Committed in:** cd95bcd

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 judgment call resolving an internal plan inconsistency)
**Impact on plan:** Both changes were necessary to satisfy the plan's own explicit requirements (build correctness and the stated acceptance criterion). No scope creep — no new behavior beyond what AUTH-04 and D-02/D-03/D-04 require.

## Issues Encountered

- The repository's pre-commit hook (`mise run test`) blocks any commit that leaves a test failing, which is structurally incompatible with a literal RED-state git commit. Resolved via the Skip-then-unskip pattern described in Key Decisions — RED was still independently confirmed for each case before committing.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- AUTH-04 and TEST-01 are fully closed: every `IUserManager` exception from `EmbyAuthenticationProvider` becomes `AuthenticationException`, and the class has its first unit test coverage.
- The settings-source seam (D-10) is available for plan 01-03, which needs to test `MoveToDefaultLoginMethod` (also reads the static `EmbyAuthPlugin.Instance` today).
- `FakeUserManager` and `FakeCryptoProvider` are available in `TestDoubles.cs` for any later plan's `EmbyAuthenticationProvider` tests (e.g., plan 01-02's AUTH-01/AUTH-03 coverage, plan 01-03's `JellyfinPasswordFirst` removal).
- No blockers. Ready for 01-02.

## Self-Check: PASSED

- Verified `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs`, `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` all exist on disk with the expected content.
- Verified all four commits exist: `d7b1853`, `536fff6`, `4203aaa`, `cd95bcd` (`git log --oneline --all` contains each).
- Re-ran every `<acceptance_criteria>` command from both tasks — all pass (see Task Commits and Files sections above for the specific checks).
- Re-ran the plan-level `<verification>`: `dotnet test` (94/94 passing, exceeds the 90-case baseline), `mise run test` (unit + script tests green), `mise run lint` (clean), `mise run e2e` (27/27 passing), `prek run` (ran automatically on every commit above, all passed), `git status --porcelain tests/Jellyfin.Plugin.EmbyAuth.Tests` (no stray files).

---
*Phase: 01-account-creation-and-login-security*
*Completed: 2026-09-17*
