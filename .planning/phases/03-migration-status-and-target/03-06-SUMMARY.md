---
phase: 03-migration-status-and-target
plan: 06
subsystem: auth
tags: [migration-target, validation, jellyfin-plugin-api, password-change]

# Dependency graph
requires:
  - phase: 03-migration-status-and-target
    provides: LoginMethodMove.ResolveMigrationTarget/ResolvePasswordSetTarget, MoveTargetKind, the RemainOnEmbyLoginMethod sentinel, and the settings-page target dropdown (03-04, 03-05)
provides:
  - A server-side refusal of a migration target or password-set target Jellyfin does not report as an enabled login method, checked at the one point every settings save passes through
  - EmbyAuthenticationProvider.ChangePassword routed through the password-set target instead of hardcoding Jellyfin's Default login method
affects: [03-07, future phases touching EmbyAuthPlugin's DI surface or ChangePassword]

# Actuals (#2632)
actuals:
  tokens: 5856
  tasks: 2
  commits: 4

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "RED-phase Skip-marked tests for a genuinely new production class (MigrationTargetValidation), unskipped in the GREEN commit — same convention as 03-04's test(03-04) commit"
    - "RED-phase tests against pre-existing wrong behavior (ChangePassword's hardcoded Default) needed no stub; they failed for real against the unmodified code before being Skip-marked"

key-files:
  created:
    - src/Jellyfin.Plugin.EmbyAuth/MigrationTargetValidation.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/MigrationTargetValidationTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthPluginTests.cs
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs
    - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs

key-decisions:
  - "EmbyAuthPlugin keeps a traditional constructor body (not a C# primary constructor) so Instance = this; still runs at construction, even though the new IServiceProvider parameter is otherwise stored and used exactly like a primary-constructor capture"
  - "The resolve-time gap (a target valid at save time whose providing plugin is later uninstalled) is NOT closed by this plan — see 'Resolve-Time Gap' below, which the 03-04 executor flagged for this plan to weigh"

patterns-established:
  - "MigrationTargetValidation.FindProblem takes the already-resolved IReadOnlyList<NameIdPair> rather than IUserManager, staying a pure function callers can test without any Jellyfin dependency"

requirements-completed: [MIGR-01, AUTH-06]

coverage:
  - id: D1
    description: "A migration target or password-set target Jellyfin does not report as enabled is refused at UpdateConfiguration, with nothing written"
    requirement: "MIGR-01"
    verification:
      - kind: unit
        ref: "tests/MigrationTargetValidationTests.cs#ReturnsAProblem_ForAMigrationTarget_NotInTheEnabledList"
        status: pass
      - kind: unit
        ref: "tests/MigrationTargetValidationTests.cs#ReturnsAProblem_ForAMigrationTarget_EqualToThisPluginsOwnProviderId_EvenWhenItIsInTheEnabledList"
        status: pass
      - kind: unit
        ref: "tests/EmbyAuthPluginTests.cs#DeclaresItsOwnUpdateConfigurationOverride"
        status: pass
    human_judgment: true
    rationale: "The override's real refusal against a running Jellyfin (an actual save attempt returning an error to the settings page) is proven end to end in plan 07, per this plan's own <verification> block — not by a unit test standing up half of Jellyfin."
  - id: D2
    description: "The no-move sentinel and an empty password-set target are both accepted at save time"
    requirement: "MIGR-01"
    verification:
      - kind: unit
        ref: "tests/MigrationTargetValidationTests.cs#ReturnsNull_ForAMigrationTarget_EqualToTheNoMoveSentinel_EvenWhenTheEnabledListIsEmpty"
        status: pass
      - kind: unit
        ref: "tests/MigrationTargetValidationTests.cs#ReturnsNull_ForAnEmptyPasswordSetTarget"
        status: pass
    human_judgment: false
  - id: D3
    description: "A password set in Jellyfin sends the user to the password-set target, or to the migration target when that setting is empty, and never refuses the password change on an unusable target"
    requirement: "AUTH-06"
    verification:
      - kind: unit
        ref: "tests/EmbyAuthenticationProviderTests.cs#ChangePassword_SavesTheHash_AndMovesTheUserToThePasswordSetTarget"
        status: pass
      - kind: unit
        ref: "tests/EmbyAuthenticationProviderTests.cs#ChangePassword_WithAnEmptyPasswordSetTarget_MovesTheUserToTheMigrationTarget"
        status: pass
      - kind: unit
        ref: "tests/EmbyAuthenticationProviderTests.cs#ChangePassword_WithAPasswordSetTargetOfTheSentinel_SavesTheHash_AndLeavesTheLoginMethodUnchanged"
        status: pass
      - kind: unit
        ref: "tests/EmbyAuthenticationProviderTests.cs#ChangePassword_WithAnUnusableTarget_SavesTheHash_LeavesTheLoginMethodUnchanged_AndLogsOneError"
        status: pass
      - kind: unit
        ref: "tests/EmbyAuthenticationProviderTests.cs#ChangePassword_WithANullConfiguration_DoesTheSameAsAnUnusableTarget"
        status: pass
      - kind: unit
        ref: "tests/EmbyAuthenticationProviderTests.cs#ChangePassword_WithAnEmptyNewPassword_ClearsTheSavedPassword_AndLeavesTheLoginMethodUnchanged"
        status: pass
      - kind: e2e
        ref: "e2e (bats) — 27/27 passed, including 'a password that an administrator sets in Jellyfin moves the user to Default'"
        status: pass
    human_judgment: false

# Metrics
duration: 25min
completed: 2026-09-20
status: complete
---

# Phase 03 Plan 06: The Server-Side Migration Target Refusal Summary

**A `MigrationTargetValidation` validator that `EmbyAuthPlugin.UpdateConfiguration` runs against Jellyfin's live enabled-provider list before every settings save, plus `ChangePassword` routed through the resolved password-set target instead of a hardcoded Default assignment**

## Performance

- **Duration:** 25 min
- **Started:** 2026-09-19T23:00:00-07:00 (approx.)
- **Completed:** 2026-09-19T23:17:43-07:00
- **Tasks:** 2
- **Files modified:** 6 (3 created, 3 modified)

## Accomplishments

- `MigrationTargetValidation.FindProblem` checks a `MigrationTarget` and a `PasswordSetTarget` against a supplied enabled-login-method list: the no-move sentinel is accepted and short-circuits the rest of the check, this plugin's own Emby method is refused even when Jellyfin reports it enabled, and every other value must appear in the list. An empty `PasswordSetTarget` is accepted outright, because it defers to the migration target. No returned message contains the offending value or a provider id.
- `EmbyAuthPlugin` gained a third constructor parameter, `IServiceProvider`, and overrides `UpdateConfiguration`: it resolves `IUserManager` and a logger inside the method body (never in the constructor, to avoid the DI cycle `IUserManager` creates with every login method including this plugin's own), calls the validator with `GetAuthenticationProviders()`, and throws `ArgumentException` without calling the base method when a problem comes back — so a caller that posts straight to Jellyfin's plugin configuration API, never loading the settings page, is refused too.
- `EmbyAuthenticationProvider.ChangePassword` no longer hardcodes `LoginMethodMove.DefaultProviderId`. It resolves `LoginMethodMove.ResolvePasswordSetTarget(configurationSource())` and branches on the three `MoveTargetKind` outcomes: `Move` assigns the resolved provider id, `Remain` saves the hash and leaves the login method untouched, and `Invalid` (a blank, whitespace-only, or absent configuration) does the same while logging exactly one `Error` entry. None of the three throws — Jellyfin calls this method inside its own password-change flow, and a settings problem must not refuse an administrator's password change.

## Task Commits

Each task was committed with a RED/GREEN pair, following this repo's established Skip-then-unskip convention for a pre-commit hook that runs the full test suite on every commit:

1. **Task 1: the server-side refusal** — `566e716` (test, RED: `MigrationTargetValidation.FindProblem` stubbed to always accept; `EmbyAuthPlugin.UpdateConfiguration` wired for real; 8 of 12 new tests Skip-marked) → `5c417d8` (feat, GREEN: the four-rule validator implemented, all tests unskipped and passing)
2. **Task 2: the password-set target wiring** — `c364db2` (test, RED: 6 of 7 new `ChangePassword` tests failed for real against the unmodified hardcoded-Default code, then Skip-marked; the password-reset test needed no change) → `f6268e9` (feat, GREEN: `ChangePassword` routed through `ResolvePasswordSetTarget`, all tests unskipped and passing)

**Plan metadata:** this commit (docs)

## Files Created/Modified

- `src/Jellyfin.Plugin.EmbyAuth/MigrationTargetValidation.cs` — new pure validator, `FindProblem(PluginConfiguration?, IReadOnlyList<NameIdPair>)`
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs` — `IServiceProvider` constructor parameter; `UpdateConfiguration` override calling the validator
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` — `ChangePassword` routed through `LoginMethodMove.ResolvePasswordSetTarget`; two new log messages (`LogPasswordSetTargetRemainsOnEmby`, `LogPasswordSetTargetInvalid`); `LogPasswordSetInJellyfin` reworded to drop "Default"
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/MigrationTargetValidationTests.cs` — 12 tests covering the validator's nine documented rule behaviors plus a null-configuration case and the no-leaked-value assertion
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthPluginTests.cs` — one reflection test asserting the override exists, with no plugin construction
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` — 7 new `ChangePassword` tests (the plan's referenced pre-existing tests were not found in the current code — see "Note on the plan's `read_first` reference" below)

## Decisions Made

- `EmbyAuthPlugin`'s constructor stays a traditional constructor body rather than a C# primary constructor, so `Instance = this;` still runs at construction. A primary constructor was tried first and reverted, because a primary constructor has no natural place for that side-effecting assignment.
- The resolve-time gap is out of scope for this plan and documented below rather than silently closed or silently left unstated, per the explicit carry-over instruction from the 03-04 executor.

## Note on the plan's `read_first` reference

Task 2's `read_first` pointed at "the existing `ChangePassword` tests, including the password-reset case at line 187" in `EmbyAuthenticationProviderTests.cs`. No such tests exist in the code as 03-04 and 03-05 left it — `EmbyAuthenticationProviderTests.cs` had no `ChangePassword` tests at all before this plan; line 187 there is part of an unrelated `Authenticate` test. All 7 `ChangePassword` tests in this plan, including the password-reset case, are new. This did not change the plan's actual requirements — the password-reset behavior itself needed no code change, matching the plan's instruction to leave that branch untouched — only the RED/GREEN mechanics for that one test (no pre-existing test to "leave passing", so it was written fresh and passed immediately since it exercises unchanged code).

## Resolve-Time Gap

**This plan does not close the full resolve-time gap the 03-04 executor flagged.** Two distinct things now exist, and they cover different windows:

1. **Save-time refusal (Task 1, closed here):** `EmbyAuthPlugin.UpdateConfiguration` refuses to save a `MigrationTarget` or `PasswordSetTarget` that Jellyfin does not report as enabled *at the moment of the save*. After this plan, no administrator — through the settings page or a direct API call — can persist a value that fails this check.
2. **Resolve-time check (not implemented by this plan, in `ChangePassword`, `MoveAfterLogin`, or `EmbyMigrationTask`):** none of the three move paths consults `IUserManager.GetAuthenticationProviders()` when a password is set, a login happens, or the migration task runs. Each one only distinguishes `MoveTargetKind.Move` (a non-blank, non-sentinel string) from `MoveTargetKind.Invalid` (blank, whitespace-only, or absent configuration) — `LoginMethodMove.ResolveTarget` performs no live enabled-list lookup at all. If a target was valid when saved and its providing plugin is later uninstalled, all three move paths still resolve it to `Move` and write that now-stale provider id to the user's `AuthenticationProviderId` unconditionally.

This means the plan's own `<threat_model>` entry **T-03-04** overstates what `ChangePassword` now does: it claims "`ChangePassword` saves the password, leaves the login method alone, logs one Error, and does not throw" for "a target that was valid at save time and later disappears" — but as implemented (following Task 2's action text exactly, which explicitly instructs branching only on `MoveTargetKind` and explicitly says the method "takes no new dependency"), that description is accurate only for a target that is *blank or absent*, not for one that is *non-blank but no longer enabled*. For the latter, the login method is reassigned to the stale provider id, not "left alone."

**Decision: this residual gap is out of scope for 03-06.** Closing it would mean adding `IUserManager` (or an equivalent live check) to `ChangePassword`, `MoveAfterLogin`, and `EmbyMigrationTask` — a new dependency this plan's Task 2 explicitly declines to add, and a change to two files (`MoveAfterLogin.cs`, `EmbyMigrationTask.cs`) this plan's `files_modified` frontmatter does not list. Task 1's save-time refusal narrows the window considerably (a target must have been valid at least once, at save time) but does not close it retroactively, exactly as the 03-04 executor's carry-over note anticipated. A future plan that wants to close this fully should update T-03-04's mitigation text to match, and would need to touch `LoginMethodMove.ResolveMigrationTarget`/`ResolvePasswordSetTarget` (or their three callers) to accept the live enabled list.

## Deviations from Plan

None — plan executed exactly as written, including the RED/GREEN Skip-then-unskip mechanics both tasks required for the pre-commit hook. The one discrepancy found (the plan's threat-register claim about `ChangePassword`'s resolve-time behavior for a since-disappeared target) is documented above under "Resolve-Time Gap" rather than silently patched, per the explicit instruction not to expand scope beyond the plan.

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- `EmbyAuthPlugin.UpdateConfiguration`'s real refusal against a running Jellyfin server is proven end to end in plan 07, per this plan's own `<verification>` block.
- The resolve-time gap documented above remains open for a future plan to weigh, should it become a priority ahead of a general resolve-time enabled-list check across `ChangePassword`, `MoveAfterLogin`, and `EmbyMigrationTask`.

---
*Phase: 03-migration-status-and-target*
*Completed: 2026-09-20*

## Self-Check: PASSED

- All 6 key files found on disk (3 created, 3 modified).
- All 4 task commit hashes (`566e716`, `5c417d8`, `c364db2`, `f6268e9`) found in git log.
- `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` — 200/200 passed, 0 skipped.
- `mise run lint` and `mise run test` green on both GREEN commits.
- `mise run e2e` — 27/27 bats tests passed, no regression from this plan's changes.
- All plan-level `<verification>` commands re-run and green (see above).
