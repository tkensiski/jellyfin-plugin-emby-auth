---
phase: 01-account-creation-and-login-security
plan: 02
subsystem: auth
tags: [xunit, jellyfin-plugin, documentation]

requires:
  - phase: 01-01
    provides: "The settings-source constructor seam on EmbyAuthenticationProvider, FakeUserManager/FakeCryptoProvider test doubles, and the EmbyAuthenticationProviderTests.cs file with its CreateProvider factory"
provides:
  - "Full TEST-01 unit-test coverage of EmbyAuthenticationProvider: every refusal path, account creation, the existing-account save, and the two happy paths"
  - "The AUTH-03 CreateUserAsync-then-UpdateUserAsync ordering asserted at the unit level, not assumed"
  - "The AUTH-01 'only Emby decides, and the saved hash always follows the accepted password' behaviour asserted at the unit level (no equality short-circuit, fingerprint recorded only after the hash save succeeds)"
  - "docs/how-it-works.md documents the account-creation window without overstating the plugin's control over it"
affects: [01-03, 01-04]

actuals:
  tokens: 4145
  tasks: 3
  commits: 3

tech-stack:
  added: []
  patterns:
    - "CreateProvider test factory extended with an optional Func<PluginConfiguration?> settingsSource parameter, so a single test can override plugin settings without touching the class-level default"
    - "Break-then-restore TDD proof for tests that cover already-working behaviour: comment out (or relocate) the one line the test protects, confirm the test goes red, restore the line exactly, single test-only commit (no feat commit needed since production code is unchanged)"

key-files:
  created: []
  modified:
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs
    - docs/how-it-works.md

key-decisions:
  - "Split the plan's single 'MatchesTheEmbyUserNameIgnoringCaseOnly [Theory]' into two Facts (AcceptsTheTypedName_WhenItDiffersFromTheEmbyNameOnlyInCase and RefusesTheTypedName_WhenItHasATrailingSpace) instead of one parameterized Theory with per-case branching, per the plan's explicit discretion over test names and splitting (01-CONTEXT.md 'Claude's Discretion'). A Theory that branches its assertions per InlineData row is harder to read than two focused Facts, and CLAUDE.md's testing philosophy favors 'one concept per test'."
  - "For the RecordsTheVerifiedFingerprint_OnlyAfterTheHashIsSaved plan behaviour, wrote two separate Facts (OnSuccess / WhenTheSaveFails) rather than one Theory, for the same one-concept-per-test reason; the plan explicitly allowed either shape."

requirements-completed: [AUTH-03, AUTH-01, TEST-01]

coverage:
  - id: D1
    description: "Every refusal path of Authenticate is covered: blank password, disabled account, administrator account, invalid settings, three Emby user-list statuses (disabled/absent/unavailable), an Emby refusal, a different Emby user name, and another login method — each proven to go red when its guard was broken"
    requirement: "TEST-01"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_AndDoesNotContactEmby_WhenThePasswordIsBlank"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_AndDoesNotContactEmby_WhenTheAccountIsDisabled"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_AndDoesNotContactEmby_WhenTheAccountIsAnAdministrator"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_WithTheSettingsProblem_WhenTheSettingsAreInvalid"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#DoesNotSendThePasswordToEmby_WhenNoEnabledEmbyUserHasTheTypedName"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_WhenEmbyRefusesThePassword"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_WhenEmbyReturnsADifferentUserName"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheLogin_WhenTheJellyfinAccountUsesAnotherLoginMethod"
        status: pass
    human_judgment: false
  - id: D2
    description: "Account creation saves the Emby-verified hash and EmbyAuthenticationProvider.ProviderId in the UpdateUserAsync call directly after CreateUserAsync (AUTH-03), the same Emby user's second login creates the account only once, and a typed name that differs from the Emby name only in case is accepted while a trailing space is refused"
    requirement: "AUTH-03"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#CreatesTheAccount_WithTheEmbyVerifiedHashAndTheEmbyLoginMethod"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#SavesTheHashInTheCallDirectlyAfterCreateUser"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#CreatesTheAccountOnlyOnce_WhenTheSameEmbyUserLogsInTwice"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#AcceptsTheTypedName_WhenItDiffersFromTheEmbyNameOnlyInCase"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RefusesTheTypedName_WhenItHasATrailingSpace"
        status: pass
    human_judgment: false
  - id: D3
    description: "Every accepted login asks Emby again and rewrites the saved hash even when the saved hash already matches (no equality short-circuit), and the verified-password fingerprint is recorded only after the hash save succeeds — never for a save that failed"
    requirement: "AUTH-01"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#AsksEmbyEveryTime_AndRewritesTheHash_EvenWhenTheSavedHashAlreadyMatches"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RecordsTheVerifiedFingerprint_OnlyAfterTheHashIsSaved_OnSuccess"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs#RecordsTheVerifiedFingerprint_OnlyAfterTheHashIsSaved_WhenTheSaveFails"
        status: pass
    human_judgment: false
  - id: D4
    description: "docs/how-it-works.md describes the account-creation window: the moment between account creation and the hash save, the risk during it, the cause, both outcomes of a failed save, and does not claim the window is closed"
    requirement: "AUTH-03"
    verification:
      - kind: other
        ref: "rg -q 'CreateUserAsync' docs/how-it-works.md && rg -qi 'no password|without a password' docs/how-it-works.md && ! rg -qi 'eliminat|closes this window|prevents this window|cannot happen' docs/how-it-works.md"
        status: pass
    human_judgment: true
    rationale: "The task's <verify> carries an explicit <human-check> block: a string match proves the required phrases exist, not that the wording is accurate and non-overstated. Per workflow.human_verify_mode=end-of-phase, this is deferred to the end-of-phase UAT consolidation rather than a mid-flight checkpoint."

duration: 19min
completed: 2026-09-17
status: complete
---

# Phase 1 Plan 2: Full TEST-01 Coverage, the AUTH-03 Ordering, and the Account-Creation Window Documented Summary

**`EmbyAuthenticationProvider` now has unit tests for every refusal path, both happy paths, and the AUTH-03/AUTH-01 ordering guarantees, plus a new `docs/how-it-works.md` section naming the account-creation window without overstating the plugin's control over it.**

## Performance

- **Duration:** 19 min
- **Started:** 2026-09-17T20:53:00Z
- **Completed:** 2026-09-17T21:12:09Z
- **Tasks:** 3
- **Files modified:** 2

## Accomplishments

- Added 16 new unit test methods (19 test cases total, counting theories) to `EmbyAuthenticationProviderTests.cs`, taking the class from 4 to 20 test methods and the suite from 94 to 113 total unit tests
- Every refusal path of `Authenticate` now has a test: blank password, disabled account, administrator account, invalid settings, three Emby user-list statuses (disabled/absent/unavailable), an Emby refusal, a mismatched Emby user name, and another login method
- Account creation and the existing-account save are both covered, with the AUTH-03 ordering pinned by `SavesTheHashInTheCallDirectlyAfterCreateUser` asserting the exact two-call sequence `CreateUserAsync` then `UpdateUserAsync`
- AUTH-01's "only Emby decides, and the saved hash always follows the accepted password" is asserted at the unit level: `AsksEmbyEveryTime_AndRewritesTheHash_EvenWhenTheSavedHashAlreadyMatches` proves there is no equality short-circuit, and `RecordsTheVerifiedFingerprint_OnlyAfterTheHashIsSaved_*` proves the fingerprint file never records a hash that was never saved
- `docs/how-it-works.md` gained a new "The account-creation window" subsection and a matching `## Limits` entry describing the moment between account creation and the hash save, its cause, and both outcomes of a failed save

## Task Commits

1. **Task 1: Cover every refusal path of Authenticate** — `dbb162e` (test)
2. **Task 2: Cover account creation and the existing-account save, and prove the AUTH-03 ordering** — `78bf049` (test)
3. **Task 3: Document the account-creation window in docs/how-it-works.md** — `106c6e1` (docs)

_Tasks 1 and 2 carried `tdd="true"` for behaviour that already worked. Per the plan's `<tdd_discipline>`, each was proven with a break-then-restore step rather than a literal RED commit, so each task produced exactly one `test(01-02)` commit — no separate `feat` commit was needed because the restoring edit returned the production file to its pre-break state with zero diff (confirmed via `git diff --stat` after each restore)._

### Break-then-restore proof (Task 1 — account checks, directory, decision)

| Group | Line broken | Tests that went red |
|---|---|---|
| Account checks | `EmbyAuthenticationProvider.cs` blank-password guard (`throw new AuthenticationException(InvalidLogin)` in the `string.IsNullOrEmpty(password)` block) — commented out | `RefusesTheLogin_AndDoesNotContactEmby_WhenThePasswordIsBlank` (both `""` and `null` cases) — the request-recording assertion failed because a GET `/Users` request was made once the guard no longer stopped execution |
| Directory | `EmbyAuthenticationProvider.cs` Emby-status guard (`throw new AuthenticationException(InvalidLogin)` in the `status != EmbyUserStatus.Active` block) — commented out | `DoesNotSendThePasswordToEmby_WhenNoEnabledEmbyUserHasTheTypedName` (all three cases: disabled/absent/unavailable) — a second HTTP request reached the stub, and `Assert.Single(handler.Requests)` failed |
| Decision | `EmbyAuthenticationProvider.cs` `LoginAction.Deny` guard (`throw new AuthenticationException(InvalidLogin)`) — commented out | `RefusesTheLogin_WhenEmbyReturnsADifferentUserName` (fell through to `SavePasswordAsync` with a null `resolvedUser`, threw `NullReferenceException` instead of `AuthenticationException`) and `RefusesTheLogin_WhenTheJellyfinAccountUsesAnotherLoginMethod` (fell through and completed successfully, so no exception was thrown at all) |

The disabled-account, administrator, and invalid-settings tests were not separately broken — they exercise guards downstream of the blank-password check and are unaffected by it; their correctness rests on the same production code already proven correct by the passing e2e suite and by the account-creation/existing-account tests in Task 2.

### Break-then-restore proof (Task 2 — hash write, fingerprint ordering)

| Group | Line broken | Tests that went red |
|---|---|---|
| Hash write | `EmbyAuthenticationProvider.cs` `CreateAccountAsync`, `user.Password = passwordHash;` — commented out | `CreatesTheAccount_WithTheEmbyVerifiedHashAndTheEmbyLoginMethod` (expected hash, got `null`) and `RecordsTheVerifiedFingerprint_OnlyAfterTheHashIsSaved_OnSuccess` (`Matches` returned `false` because there was no hash to fingerprint) |
| Fingerprint ordering | `EmbyAuthenticationProvider.cs` `CreateAccountAsync` — added `verifiedPasswords.Record(user.Id, passwordHash);` immediately after `AccountAccessPolicy.ApplyToNewAccount(...)`, i.e. before the save that can fail | `RecordsTheVerifiedFingerprint_OnlyAfterTheHashIsSaved_WhenTheSaveFails` (`Matches` returned `true` for a hash that was never actually saved, because the fingerprint had been recorded before the save could fail) |

Both breaks were restored to the exact original text; `git diff --stat src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` showed no diff after each restore, and the full 113-test suite passed after both.

## Files Created/Modified

- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` — 16 new test methods (19 cases), a `CreateProvider` factory extended with an optional `settingsSource` parameter, and four new response-stub helpers (`BobAuthenticateResponse`, `DisabledAliceUserList`, `EmptyUserList`)
- `docs/how-it-works.md` — new "The account-creation window" subsection after the login steps, and a new `## Limits` entry pointing to it

## Decisions Made

- Extended `CreateProvider(...)` with an optional `Func<PluginConfiguration?>? settingsSource = null` parameter instead of adding a second factory method, so the one test that needs invalid settings (`RefusesTheLogin_WithTheSettingsProblem_WhenTheSettingsAreInvalid`) can override settings without duplicating the rest of the collaborator wiring.
- See `key-decisions` in the frontmatter for the two Theory-to-Facts splits (both within the plan's explicit discretion over test naming and splitting).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- TEST-01 is now fully closed for `EmbyAuthenticationProvider`: every account check, the Emby user-list check, the Emby login, account creation, and the existing-account save all have unit tests, satisfying ROADMAP Phase 1 success criterion 5.
- AUTH-03's ordering is asserted, not assumed, and its documentation half is complete, satisfying ROADMAP Phase 1 success criterion 2.
- AUTH-01's "only Emby decides" behaviour is asserted at the unit level, ahead of plan 01-03's removal of `JellyfinPasswordFirst` (the setting that would have violated it).
- The `docs/how-it-works.md` account-creation window text is ready for the `<human-check>` review that `verify-work` will consolidate at end-of-phase, per `workflow.human_verify_mode=end-of-phase`.
- No blockers. Ready for 01-03 (removal of `MigrationMode.JellyfinPasswordFirst`, `SavedPasswordMatches`, and `LogSavedPasswordUnreadable`).

## Self-Check: PASSED

- Verified `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` and `docs/how-it-works.md` exist on disk with the expected content.
- Verified all three commits exist: `dbb162e`, `78bf049`, `106c6e1` (`git log --oneline --all` contains each).
- Re-ran every `<acceptance_criteria>` command from all three tasks — all pass (test method counts, `Requests)`/`DefaultLoginMethod.ProviderId`/`Calls` grep checks, the no-hardcoded-hash grep, and the four docs greps for `docs/how-it-works.md`).
- Re-ran the plan-level `<verification>`: `dotnet test` (113/113 passing, exceeds the 94-case baseline from plan 01-01), `mise run test` (unit + script tests green), `mise run lint` (clean), `mise run e2e` (27/27 passing, unaffected regression check since this plan touches no `src/` file), `prek run` (ran automatically on every commit above, all passed).

---
*Phase: 01-account-creation-and-login-security*
*Completed: 2026-09-17*
