---
phase: 01-account-creation-and-login-security
verified: 2026-09-17T23:00:00Z
status: human_needed
score: 5/5 must-haves verified
behavior_unverified: 0
overrides_applied: 0
human_verification:
  - test: "Read the account-creation-window section of docs/how-it-works.md (the paragraphs starting \"Jellyfin has no login call that creates an account with a password already set...\" through \"...has the same window, for the same reason.\") and confirm it: (1) names the window between the account-creation call and the hash save, (2) says the account has no usable password during it and that the Default login method would accept a blank password then, (3) names the cause as Jellyfin having no create-with-password call, (4) describes the cleanup delete and what happens when that delete also fails, and (5) does not claim the window is eliminated."
    expected: "All five elements are present and the wording does not overstate the plugin's control over the window."
    why_human: "This is a prose-accuracy judgment call, not a fact a grep can settle. Plan 01-02 Task 3 carried this exact check as a deferred <human-check> block (workflow.human_verify_mode=end-of-phase), and 01-VALIDATION.md lists it under 'Manual-Only Verifications' with sign-off still pending. The verifier read the text and found all five elements present (see Observable Truth #2 below), but a human has not yet signed off on it, and the deferral was deliberate, not an oversight."
---

# Phase 1: Account Creation and Login Security Verification Report

**Phase Goal:** No password that Emby did not verify opens an account. Every failure during account creation refuses the login and leaves no open account, and while a user is on the Emby login method, only Emby decides the login.
**Verified:** 2026-09-17
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Unit tests make the save after `CreateUserAsync` fail, make the cleanup delete fail as well, and throw an unexpected exception type; each is refused without HTTP 500, the plugin attempts the delete, and a double failure leaves the account on Default without a password, logged at Error naming the account. | ✓ VERIFIED | `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:150-199` — `CreateAccountAsync` has unconditional `catch (Exception ex)` around both `CreateUserAsync` (line 158) and `UpdateUserAsync` (line 174), a guarded, once-only `DeleteUserAsync` (line 186) inside its own try/catch, and always throws `AuthenticationException`. `SavePasswordAsync` (line 201-217) is likewise unconditional. Exercised by passing tests `RefusesTheLogin_WhenTheSaveAfterCreateUserFailsWithAnUnexpectedExceptionType`, `RefusesTheLogin_WhenTheSaveAfterCreateUserFailsWithACaughtExceptionType`, `RefusesTheLogin_WhenTheSaveAndTheCleanupDeleteBothFail` (asserts exactly one `Error:`-level log entry naming `alice`, and that neither the typed password nor the derived hash appears in any log entry), `RefusesTheLogin_WhenSavingThePasswordOfAnExistingAccountFailsWithAnUnexpectedExceptionType`, `RefusesTheLogin_WhenCreateUserFailsBecauseJellyfinRejectsTheName`, `RefusesTheLogin_WhenCreateUserFailsWithAnUnexpectedExceptionType` (`tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs:196-267`). All ran green in this session (`dotnet test` 115/115). |
| 2 | A new account gets the Emby-verified hash and the Emby login method in the `UpdateUserAsync` call directly after `CreateUserAsync`, and `docs/how-it-works.md` describes the brief moment before that save. | ✓ VERIFIED | `EmbyAuthenticationProvider.cs:151-166` — the only statements between `CreateUserAsync` (line 156) and `UpdateUserAsync` (line 172) are synchronous field assignments, no other `IUserManager` call. Test `SavesTheHashInTheCallDirectlyAfterCreateUser` (`EmbyAuthenticationProviderTests.cs:300-310`) asserts `userManager.Calls` equals exactly `["CreateUserAsync", "UpdateUserAsync"]`. `docs/how-it-works.md:18-22` names the window, its cause (`IUserManager.CreateUserAsync` takes only a name), the risk (a blank password would open the account), and both failure outcomes (delete succeeds / delete also fails, both logged at Error) — read directly, this session. The prose-accuracy half of this criterion is a deferred human-check; see Human Verification below. |
| 3 | An e2e test shows a user on the Emby login method cannot log in with the saved Jellyfin password when Emby refuses that password, and that after each Emby-accepted login the saved hash is the hash of that password. | ✓ VERIFIED | `e2e/30-migration-modes.bats:60-75` (`"Keep Emby in charge: an Emby password change is effective at once and the saved hash follows it"`) logs in as `oscar`, changes the Emby password, asserts the *old* password now returns 401 and the *new* one returns 200, then asserts `migration_ready_state oscar` is `true` (which `EmbyLoginMethodUsers.cs:18` defines as "the saved hash has an Emby-verified fingerprint" — read this session). `e2e/40-emby-outage.bats:42-47` (`"while Emby is unreachable, a verified saved hash does not open an account"`) asserts `uma`, who holds an Emby-verified saved hash, gets 401 while Emby is stopped. Verified by direct inspection of both files this session, per the task's own guidance to avoid a full `mise run e2e` re-run absent specific doubt; 01-03-SUMMARY.md and 01-SECURITY.md both record a 27/27 e2e pass at the commit these files are still at (confirmed no further `src/`, `e2e/` changes since via `git log`). |
| 4 | A search of `src/`, `tests/`, `e2e/`, `docs/`, and `README.md` finds no `JellyfinPasswordFirst` and no "Check the saved Jellyfin password first". | ✓ VERIFIED | `rg -n 'JellyfinPasswordFirst\|Check the saved Jellyfin password first' src tests e2e docs README.md` run this session: zero matches (exit 1). |
| 5 | `dotnet test` runs unit tests for `EmbyAuthenticationProvider` covering the account checks, the Emby login, account creation and update, and every failure path in criterion 1; each fix has a test that failed before the fix. | ✓ VERIFIED | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` run this session: 115/115 passed, 0 skipped. `EmbyAuthenticationProviderTests.cs` has 44 test methods (23 `[Fact]`/`[Theory]` groups) covering blank password, disabled/administrator account, invalid settings, three Emby user-list statuses, an Emby refusal, a name mismatch, another login method, account creation (both hash and login-method fields), the create-then-update ordering, repeat-login idempotency, the no-short-circuit AUTH-01 behavior, fingerprint-ordering, case-insensitive name matching, and all six `CreateUserAsync`/`UpdateUserAsync`/`DeleteUserAsync` failure combinations. `rg -n 'Skip = ' tests/` returns zero matches — no skipped test survives. Verified the RED-before-GREEN mechanism directly: `git show d7b1853:tests/.../EmbyAuthenticationProviderTests.cs` shows the first test committed as `[Fact(Skip = "RED — unskipped when the catch widens in the next commit (AUTH-04)")]`, unskipped in the paired `feat` commit `536fff6`. This satisfies "failed before the fix" through the documented Skip-then-unskip mechanism the repo's pre-commit hook (which blocks a commit that leaves a test failing) makes necessary — not a shortcut around it. |

**Score:** 5/5 truths verified (0 present-but-behavior-unverified)

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| AUTH-04 | 01-01 | No failure while creating or saving an account returns HTTP 500 or leaves an enabled account open. | ✓ SATISFIED | Truth #1 above. |
| TEST-01 | 01-01, 01-02 | Unit tests cover account checks, Emby login, account creation/update, and every AUTH-04 failure path. | ✓ SATISFIED | Truth #5 above. |
| AUTH-03 | 01-02 | Save-directly-after-create ordering, documented window. | ✓ SATISFIED | Truth #2 above (code half fully verified; doc-prose half deferred to human review). |
| AUTH-01 | 01-02, 01-03 | While on the Emby login method, only Emby decides; saved hash always follows the accepted password. | ✓ SATISFIED | Truth #3 above; also `AsksEmbyEveryTime_AndRewritesTheHash_EvenWhenTheSavedHashAlreadyMatches` and `RecordsTheVerifiedFingerprint_OnlyAfterTheHashIsSaved_*` unit tests (`EmbyAuthenticationProviderTests.cs:327-374`), both passing. |
| AUTH-02 | 01-03, 01-04 | `JellyfinPasswordFirst` removed from settings, page, docs, tests. | ✓ SATISFIED | Truth #4 above; `MigrationMode` enum has exactly two members (`PluginConfiguration.cs:9-20`); `configPage.html` has exactly two migration-behavior `<option>` elements; `docs/settings.md` migration-behavior table has exactly two rows, both stating Emby decides. |

No orphaned requirements: `.planning/REQUIREMENTS.md` maps exactly AUTH-01, AUTH-02, AUTH-03, AUTH-04, TEST-01 to Phase 1, matching the five IDs given for this verification.

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` | Settings-source seam, unconditional catches, guarded cleanup delete, `LogDeleteFailed`, no `MigrationMode` branch | ✓ VERIFIED | Read in full this session; matches every plan's `<action>` description. |
| `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` | Factory registration supplying the settings-source delegate | ✓ VERIFIED | Lines 35-42; matches D-10 exactly. |
| `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` | Two-member `MigrationMode` | ✓ VERIFIED | Lines 9-20; `MoveAfterFirstLogin`, `KeepEmbyInCharge`. |
| `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` | Two migration-behavior options | ✓ VERIFIED | Lines 25-26; five `option value=` total (2 + 3 account-access), matching the 01-03 acceptance criterion. |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` | `FakeUserManager`, `FakeCryptoProvider` | ✓ VERIFIED | Lines 94, 240; confirmed absent from `src/` (`rg -q 'FakeUserManager\|FakeCryptoProvider' src/` → no match). |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` | Full TEST-01/AUTH-04 coverage | ✓ VERIFIED | 44 test methods, all passing. |
| `e2e/30-migration-modes.bats` | `KeepEmbyInCharge` at-once-password-change test, `oscar` | ✓ VERIFIED | Lines 60-75. |
| `e2e/40-emby-outage.bats` | Verified-saved-hash-refused-during-outage test, `uma` | ✓ VERIFIED | Lines 42-47. |
| `docs/how-it-works.md` | Account-creation-window description, 6 numbered steps | ✓ VERIFIED | Steps numbered 1-6, no gap (`rg -c '^[0-9]\. \*\*'` → 6); window section at lines 18-22. |
| `docs/settings.md` | Two-row migration-behavior table | ✓ VERIFIED | Both rows state Emby decides the login. |
| `CHANGELOG.md` | D-07 settings-loss notice | ✓ VERIFIED | States loss of Emby server URL and API key, and re-entry requirement. |
| `docs/images/settings-page.png` | Screenshot matching shipped page (per 01-04 plan) | N/A — removed by recorded human direction | The maintainer directed removal of the screenshot entirely at the 01-04 Task 3 checkpoint instead of retaking it; `docs/images/` does not exist, and no reference to it remains in `README.md` or `CLAUDE.md`. This is a recorded deviation (01-04-SUMMARY.md "User-directed scope change"), not a gap. |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `PluginServiceRegistrator.cs` | `EmbyAuthenticationProvider.cs` | settings-source delegate in factory registration | ✓ WIRED | `() => EmbyAuthPlugin.Instance?.Configuration` passed at construction (line 41). |
| `EmbyAuthenticationProviderTests.cs` | `TestDoubles.cs` | real `ServiceCollection` registering `FakeUserManager` as `IUserManager` | ✓ WIRED | `CreateProvider` factory, lines 413-415. |
| `EmbyAuthenticationProvider.cs` | Jellyfin `UserManager.AuthenticateWithProvider` | only `AuthenticationException` escapes `Authenticate` | ✓ WIRED | Every throw site in `Authenticate`, `CreateAccountAsync`, `SavePasswordAsync`, and `GetSettings` throws `AuthenticationException`; confirmed no other exception type can escape via unconditional catches. |
| `docs/settings.md` | `PluginConfiguration.cs` | API-value column names only surviving enum members | ✓ WIRED | `MoveAfterFirstLogin`, `KeepEmbyInCharge` — no stale value. |
| `e2e/40-emby-outage.bats` | `e2e/setup_suite.bash` | reuses existing Emby users, adds none | ✓ WIRED | `sam`, `vic`, `tina`, `uma` are pre-existing suite users per `.claude/rules/e2e.md` convention; no new user created in the test file. |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Full unit suite passes | `mise run test` | 115/115 dotnet tests, 8/8 packaging script tests, 0 skipped | ✓ PASS |
| Lint is clean | `mise run lint` | `dotnet format`, `shellcheck`, `shfmt`, `actionlint`, `zizmor` all clean, 0 findings | ✓ PASS |
| No trace of removed behavior | `rg -n 'JellyfinPasswordFirst\|Check the saved Jellyfin password first' src tests e2e docs README.md` | zero matches | ✓ PASS |
| No skipped tests survive | `rg -n 'Skip = ' tests/` | zero matches | ✓ PASS |
| No debt markers in phase-touched files | `rg -n 'TBD\|FIXME\|XXX\|TODO\|HACK\|PLACEHOLDER'` over all files this phase modified | zero matches | ✓ PASS |
| e2e suite (real Emby/Jellyfin containers) | `mise run e2e` | **not re-run this session** — inspected `e2e/30-migration-modes.bats` and `e2e/40-emby-outage.bats` directly instead, per this task's own guidance ("run it only if you have specific reason to doubt criterion 3") | ? SKIP (inspected, not executed) |

### Anti-Patterns Found

None. Scanned every file this phase modified (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs`, `PluginServiceRegistrator.cs`, `Configuration/PluginConfiguration.cs`, `Configuration/configPage.html`, `tests/.../TestDoubles.cs`, `tests/.../EmbyAuthenticationProviderTests.cs`, `tests/.../EmbyAuthSettingsTests.cs`, `e2e/30-migration-modes.bats`, `e2e/40-emby-outage.bats`, `docs/how-it-works.md`, `docs/settings.md`, `CHANGELOG.md`, `README.md`) for debt markers, warning markers, "coming soon" language, and stub returns. Zero matches on all patterns.

### PLAN-Level Must-Haves Beyond the ROADMAP Criteria

The four plans' frontmatter add detail beyond the five ROADMAP criteria. All checked; none reduce or contradict the ROADMAP contract. Two carry `verification: backstop` (non-inferable claims the planner flagged rather than silently assuming):

- **01-01 backstop truth** ("No test in `EmbyAuthenticationProviderTests` mutates process-global state, so the suite is correct under xUnit's default parallel test-class execution.") — Code inspection this session confirms no test references `EmbyAuthPlugin.Instance` or any other static mutable field (`! rg -q 'EmbyAuthPlugin' tests/.../EmbyAuthenticationProviderTests.cs` — confirmed no match), and every test builds its own temp-file-backed `EmbyVerifiedPasswords`. Combined with an actual green 115/115 run under the project's default (parallel) xUnit execution this session, this is reasonably supported by direct evidence rather than presence alone, though it was not stress-tested by repeated runs. Not escalated to a blocking human item given the low stakes and the corroborating evidence.
- **01-02 backstop truth** (the account-creation-window race is documented, not closed) — This is exactly Truth #2 above; already verified as documented and not overstated.

No prohibition (`must_haves.prohibitions`) failed. All four are `status: resolved`; the two `verification: judgment` prohibitions (no half-made-account-without-log, no window-eliminated claim) were independently re-checked this session against the current code and docs, not taken on the plan's word.

## Human Verification Required

1 item, deferred from mid-phase per `workflow.human_verify_mode=end-of-phase` (harvested from `01-02-PLAN.md` Task 3's `<human-check>` block; also listed in `01-VALIDATION.md` under "Manual-Only Verifications" with sign-off still pending):

### 1. `docs/how-it-works.md` account-creation-window prose accuracy

**Test:** Read the account-creation-window paragraphs in `docs/how-it-works.md` (the text following step 4, before "## Password changes") and confirm it: (1) names the window between the account-creation call and the hash save, (2) says the account has no usable password during it and a blank password on Default would open it then, (3) names the cause as Jellyfin having no create-with-password call, (4) describes the cleanup delete and both outcomes when it also fails, and (5) does not claim the window is eliminated, closed, or prevented.
**Expected:** All five elements are present and accurately worded, with no overstatement of the plugin's control over the window.
**Why human:** A string-match gate (already run and passing: `rg -q 'CreateUserAsync'`, `rg -qi 'no password\|without a password'`, `! rg -qi 'eliminat\|closes this window\|prevents this window\|cannot happen'`) proves the required phrases exist; it does not prove the surrounding prose is accurate or non-misleading. The verifier read the section this session and found all five elements present and consistent with the code (`EmbyAuthenticationProvider.cs:150-199`), and the prior code review (`01-REVIEW.md` IN-02, fixed in `d929b0a`) already corrected one clarity nit. This is a documented, deliberate deferral, not a discovered gap — flagging it here is what closes the loop the plan left open, not new information.

## Gaps Summary

No gaps. All five ROADMAP success criteria, all five requirement IDs, every plan's artifacts and key links, and the prior code review's and security audit's findings are confirmed against the current codebase, not assumed from SUMMARY.md claims. `mise run test` and `mise run lint` were re-run in this session and are green; the removal search and the skip-marker search were re-run and are clean. The one open item is a documentation prose-accuracy check that the plan itself deliberately deferred to end-of-phase human review — it is not a discrepancy found during this verification, but it has not yet received that human sign-off, so `status: human_needed` rather than `passed`.

---

_Verified: 2026-09-17_
_Verifier: Claude (gsd-verifier)_
