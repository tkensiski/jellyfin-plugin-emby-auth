---
phase: 03-migration-status-and-target
verified: 2026-09-20T07:10:04Z
status: human_needed
score: 10/10 must-haves verified
behavior_unverified: 0
overrides_applied: 0
human_verification:
  - test: "Review whether the T-03-04 threat-register mitigation text should be corrected, and whether the resolve-time gap in LoginMethodMove.ResolveTarget should be closed before AUTH-05/Phase 4, given it also affects ChangePassword's password-set path."
    expected: "A maintainer decision: accept the documented residual gap as-is, or schedule a follow-up plan that adds a live enabled-provider check to ChangePassword, MoveAfterLogin, and EmbyMigrationTask, and corrects the T-03-04 mitigation text in 03-06-PLAN.md to match actual behavior."
    why_human: "This is a risk-acceptance and prioritization call, not something a grep or a test can resolve. The gap is real, code-confirmed, and already documented candidly in 03-06-SUMMARY.md — it needs a maintainer decision, not more code from this phase."
---

# Phase 3: Migration Status and Target Verification Report

**Phase Goal:** The Migration section of the settings page shows what the migration task and the fingerprint file actually do, the login method that users move to becomes a setting instead of a fixed value, and the migration code has unit tests against an SQLite in-memory `JellyfinDbContext`. The database test seam and the `ITaskManager` fake come before the fixes.
**Verified:** 2026-09-20T07:10:04Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (ROADMAP.md Success Criteria, verbatim, lines 92-101)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Unreadable fingerprint file: Migration section tells the administrator and points to the Jellyfin log | ✓ VERIFIED | `EmbyVerifiedPasswords.RecordsAvailable()` (`src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:101-107`) → `MigrationStatus.RecordsUnavailable` (`Api/EmbyAuthController.cs:60,97-101`) → settings page renders the message (`configPage.html:297-341`, tested in `tests/js/configPage.test.js`) |
| 2 | After **Run migration now**, the list shows the migration runs and updates when the task finishes, also for a run over 3s; one `GET /EmbyAuth/Migration` response carries both the read failure and the task state | ✓ VERIFIED | `MigrationStatus(RecordsUnavailable, Task, Users, AvailableTargets)` is one record (`Api/EmbyAuthController.cs:59-63,105`); polling loop with 2s interval, `Running`-follows-indefinitely, ~20s start cap (`configPage.html:234-296`, no `setTimeout(loadEmbyAuthMigration, 3000)` anywhere in the file — confirmed by grep) |
| 3 | Unwritable fingerprint file: log message, XML doc, and `docs/how-it-works.md` say the record stays in memory until restart; a unit test covers the write failure | ✓ VERIFIED | Log message and XML doc both say "the record on disk is behind... lost only if Jellyfin restarts before one happens" (`EmbyVerifiedPasswords.cs:37,145`); `docs/how-it-works.md:53` states the same; `EmbyVerifiedPasswordsTests.cs` (17 `[Fact]`/`[Theory]`) covers write failure |
| 4 | `docs/how-it-works.md` points to the shutdown step in `docs/migration.md` that finds users who went back to Emby | ✓ VERIFIED | `docs/how-it-works.md:54` → "Step 2 of [Shut down Emby](migration.md#shut-down-emby) finds these users." |
| 5 | `dotnet test` runs unit tests for the move classes, `EmbyLoginMethodUsers`, and the migration task against SQLite in-memory `JellyfinDbContext`, and for `EmbyAuthController`: status, run request, task state | ✓ VERIFIED (see note) | `SqliteJellyfinDbContextFactory` (`TestDoubles.cs:329-343`) used by `EmbyMigrationTaskTests.cs`, `EmbyAuthControllerTests.cs`, `LoginMethodMoveTests.cs` (11 cases), `EmbyLoginMethodUsersTests.cs` (9 cases); `EmbyAuthControllerTests.cs` (9 cases) covers `GetMigrationStatus` and task state. Ran `TypeVisibilityTests` directly: 2/2 passed. **Note:** the criterion's literal text still names `MoveEmbyUsersToDefaultTask`, a class this same phase deleted and renamed to `EmbyMigrationTask` (D-19/D-20) — a stale label in ROADMAP.md, not a functional gap; `EmbyMigrationTaskTests.cs` exists and uses the SQLite seam. |
| 6 | Two settings (**Migration target**, **Password-set target**) with "Remain on Emby Login" and "Same as the migration target"; dropdown offers only Jellyfin-enabled methods; an out-of-list value is refused with a message; every move uses the setting that governs it; no class/task name/key says "Default" | ✓ VERIFIED, with one flagged residual gap | Save-time refusal: `EmbyAuthPlugin.UpdateConfiguration` → `MigrationTargetValidation.FindProblem` (`EmbyAuthPlugin.cs:76-88`, `MigrationTargetValidation.cs`), proven by 11 unit-test cases and e2e `50-migration-target.bats` "the server refuses a migration target..." / "...this plugin's own Emby login method...". Dropdown built only from `AvailableTargets` (`configPage.html:128-148`), Run-migration button refuses without a known pick (`configPage.html:387-403`) and saves before queuing (`:405-411`, matches D-17). Rename complete: `LoginMethodMove`, `MoveAfterLogin`, `EmbyMigrationTask` (`Key = "EmbyAuthMigration"`, `Name = "Finish the Emby migration"`); grep for `MoveEmbyUsersToDefaultTask\|DefaultLoginMethod\|MoveToDefaultLoginMethod\|EmbyAuthMoveUsersToDefault` outside `.planning/` finds only the CHANGELOG's historical note. **Flagged:** `LoginMethodMove.ResolveTarget` (`LoginMethodMove.cs:69-80`) never re-checks Jellyfin's live enabled-provider list — it distinguishes only blank/sentinel/non-blank. A target valid at save time whose providing plugin is later uninstalled still resolves to `Move` in `ChangePassword`, `MoveAfterLogin`, and `EmbyMigrationTask`, and `LoginMethodMove.MoveAsync` writes that stale id to `AuthenticationProviderId` unconditionally. This is a documented, deliberate scope decision (03-06-SUMMARY.md "Resolve-Time Gap"), not an oversight — but I independently confirmed by code trace that `EmbyAuthenticationProvider.ChangePassword`'s `default:` branch (`EmbyAuthenticationProvider.cs:153-155`, logs `LogPasswordSetTargetInvalid`) is reached only when `ResolveTarget` returns `Invalid` (blank/whitespace config), never for a non-blank-but-since-disabled target — so **`03-06-PLAN.md:248`'s T-03-04 mitigation text ("ChangePassword saves the password, leaves the login method alone, logs one Error, and does not throw") is inaccurate for the exact scenario it claims to cover.** No test in `EmbyAuthenticationProviderTests.cs`, `LoginMethodMoveTests.cs`, or `MoveAfterLoginTests.cs` exercises the "target valid at save, later disappears" path (confirmed: no match for stale/disappear/uninstall in those files). The criterion's *literal* text ("a value that is not one of them is refused with a message") is about the settings-page save path, which is fully verified. The runtime resolve-time behavior it does not mention is where the gap lives — see Human Verification. |
| 7 | An e2e test installs JellyfinSecurity v2.6.1 (`-jf12`, pinned, checksum-verified) beside this plugin, moves a user to it, and that user logs in with the Emby-verified password; test stops at the hand-over | ✓ VERIFIED | `scripts/fetch-jellyfinsecurity.sh` pins tag `v2.6.1`, asset `Jellyfin.Plugin.TwoFactorAuthv2.6.1.0-jf12.zip`, checksum `feef7f8f...`, fails closed on mismatch (`:73-77`); `e2e/50-migration-target.bats` "a user migrates to the second login method and then logs in without reaching Emby" reads the target id from the plugin's own API (`jellyfinsecurity_provider_id()`), runs the migration task, and asserts `emby_login_requests` count is unchanged after the post-migration login |
| 8 | A unit test fails if `EmbyAuthenticationProvider` becomes public; a comment says why it stays internal | ✓ VERIFIED | `TypeVisibilityTests.EmbyAuthenticationProvider_StaysInternal` — ran directly, passed; class-level `<remarks>` comment at `EmbyAuthenticationProvider.cs:22-26` states the reason |
| 9 | Migration list names every no-password account; warns Default opens such an account with a blank password and recommends setting one first; plugin neither refuses the move nor writes a password; e2e test shows a no-password user still logs in through Emby and is named in the list | ✓ VERIFIED | `MigrationUserState.NoPassword` computed in DB (`EmbyLoginMethodUsers.cs:91-96`, takes precedence over `Unknown`); warning text in `configPage.html:206-224` makes the specific claim only for Default, "cannot tell" for other targets, nothing for Remain; `e2e/50-migration-target.bats` "an account with no saved password is named, not moved, and still logs in through Emby" |
| 10 | `docs/how-it-works.md` states Default accepts a blank password for a no-password account, and connects it to the deleted-account case, the settings-page warning, and the plugin's interim-tool role | ✓ VERIFIED | `docs/how-it-works.md:41` — single paragraph naming all three connections verbatim |

**Score:** 10/10 truths verified (0 present-but-behavior-unverified)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` | reshaped `MigrationStatus` response | ✓ VERIFIED | Single record carries `RecordsUnavailable`, `Task`, `Users`, `AvailableTargets` |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` | corrected write-failure semantics/docs | ✓ VERIFIED | Log message and XML doc match D-08 |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs` | four-state enum | ✓ VERIFIED | `MigrationUserState`: Ready/NeedsEmbyLogin/NoPassword/Unknown |
| `src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs` (renamed from `DefaultLoginMethod.cs`) | target-agnostic move | ✓ VERIFIED | `MoveAsync` parameterized on `targetProviderId`, single-column `ExecuteUpdateAsync` |
| `src/Jellyfin.Plugin.EmbyAuth/MoveAfterLogin.cs` (renamed from `MoveToDefaultLoginMethod.cs`) | settings-sourced, no static read | ✓ VERIFIED | Takes `Func<PluginConfiguration?> configurationSource`, no `EmbyAuthPlugin.Instance` reference |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyMigrationTask.cs` (renamed from `MoveEmbyUsersToDefaultTask.cs`) | new name/key | ✓ VERIFIED | `Key = "EmbyAuthMigration"`, `Name = "Finish the Emby migration"` |
| `src/Jellyfin.Plugin.EmbyAuth/MigrationTargetValidation.cs` | save-time refusal | ✓ VERIFIED | Pure function, wired into `EmbyAuthPlugin.UpdateConfiguration` |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` | SQLite seam + `ITaskManager` fake | ✓ VERIFIED | `SqliteJellyfinDbContextFactory` (`:329-343`), `FakeTaskManager` used across controller/task tests |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/TypeVisibilityTests.cs` | MIGR-02 guard | ✓ VERIFIED, behaviorally run | 2/2 passed on direct `dotnet test` run |
| `e2e/50-migration-target.bats` | criterion 7/9 e2e coverage | ✓ VERIFIED | 6 `@test` blocks covering hand-over, no-password account, server refusals, 403 |
| `docs/how-it-works.md`, `docs/migration.md`, `docs/settings.md`, `README.md`, `CHANGELOG.md` | DOCS-01/05, MIGR-01 documentation | ✓ VERIFIED | All describe the two settings, the rename, the reshaped response, and the blank-password behavior |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `EmbyVerifiedPasswords.RecordsAvailable` | `EmbyAuthController.GetMigrationStatus` | `MigrationStatus.RecordsUnavailable` | ✓ WIRED | `Api/EmbyAuthController.cs:60` |
| `MigrationStatus.RecordsUnavailable` | Migration summary element | `loadEmbyAuthMigration` | ✓ WIRED | `configPage.html:297-341` |
| `IUserManager.GetAuthenticationProviders` | dropdown options | `AvailableTargets` filter dropping this plugin's own Emby method | ✓ WIRED | `Api/EmbyAuthController.cs:55-57` filters `provider.Id != EmbyAuthenticationProvider.ProviderId`; `configPage.html:128-148` renders from `AvailableTargets` only |
| `PluginConfiguration.MigrationTarget` | `LoginMethodMove.ResolveMigrationTarget` | `MoveAfterLogin` / `EmbyMigrationTask` | ✓ WIRED | Both callers confirmed at `MoveAfterLogin.cs:42`, `EmbyMigrationTask.cs:65` |
| `PluginConfiguration.PasswordSetTarget` | `LoginMethodMove.ResolvePasswordSetTarget` | `EmbyAuthenticationProvider.ChangePassword` | ⚠️ WIRED, with resolve-time gap | Wired and functional for the blank/valid cases; does not re-validate a stale non-blank target — see Truth 6 above |
| `IUserManager.GetAuthenticationProviders` (save time) | `EmbyAuthPlugin.UpdateConfiguration` | `MigrationTargetValidation.FindProblem` | ✓ WIRED | `EmbyAuthPlugin.cs:76-88`; only override, no bypass path (grep confirms) |
| `POST /EmbyAuth/Migration/Run` | poll stop condition | last-end-time baseline captured before the request | ✓ WIRED | `configPage.html:405,414` baseline captured before save/queue |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| `TypeVisibilityTests` (MIGR-02 guard) actually passes | `dotnet test --filter "FullyQualifiedName~TypeVisibilityTests"` | 2/2 passed | ✓ PASS |
| `mise run test` (full suite, run once by orchestrator before this verification) | — | exit 0, per verified_context | ✓ PASS (relied on orchestrator's run, not re-run here to avoid a second full-suite pass) |
| `mise run e2e` (32/32 bats, run once by orchestrator) | — | exit 0, per verified_context | ✓ PASS (same — not re-run) |
| No stale `MoveEmbyUsersToDefaultTask`/`DefaultLoginMethod`/`MoveToDefaultLoginMethod`/`EmbyAuthMoveUsersToDefault` references outside `.planning/` | `rg` across repo | only `CHANGELOG.md`'s historical "changed from X to Y" note | ✓ PASS |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|--------------|--------|----------|
| FPRT-01 | 03-02 | write-failure stays in-memory, docs corrected, unit test | ✓ SATISFIED | `EmbyVerifiedPasswords.cs:37,59-68,145`; `EmbyVerifiedPasswordsTests.cs` |
| FPRT-03 | 03-01, 03-03 | read failure surfaced on settings page | ✓ SATISFIED | `MigrationStatus.RecordsUnavailable`, `configPage.html` message |
| UI-03 | 03-01, 03-03, 03-05 | polling instead of fixed 3s reload | ✓ SATISFIED | `configPage.html:234-296`; no `setTimeout(...,3000)` |
| MIGR-01 | 03-03, 03-04, 03-05, 03-06, 03-07 | two-setting configurable target, refused when invalid | ✓ SATISFIED, gap flagged | See Truth 6 |
| MIGR-02 | 03-02 | `EmbyAuthenticationProvider` stays internal, guarded by test | ✓ SATISFIED | `TypeVisibilityTests`, ran and passed |
| AUTH-06 | 03-03, 03-04, 03-05, 03-06, 03-07 | no-password account named, warned, never refused/invented | ✓ SATISFIED | `MigrationUserState.NoPassword`, warning wording, e2e test |
| TEST-02 | 03-01, 03-04 | move classes + `EmbyLoginMethodUsers` + migration task tested against SQLite in-memory | ✓ SATISFIED | `SqliteJellyfinDbContextFactory`; see Truth 5 note on stale roadmap class name |
| TEST-03 | 03-01, 03-03 | `EmbyAuthController` tested: status, run, task state | ✓ SATISFIED | `EmbyAuthControllerTests.cs` (9 cases) |
| DOCS-01 | 03-02 | pointer from how-it-works to migration.md shutdown step | ✓ SATISFIED | `docs/how-it-works.md:54` |
| DOCS-05 | 03-02 | blank-password statement connecting three places | ✓ SATISFIED | `docs/how-it-works.md:41` |

No orphaned requirements found — `.planning/REQUIREMENTS.md`'s traceability table lists exactly these ten for Phase 3, and all ten are claimed by at least one of the seven plans' `requirements` frontmatter.

### Anti-Patterns Found

None. Scanned all 29 files this phase's git history touched under `src/`, `docs/`, `e2e/`, `scripts/`, `CHANGELOG.md`, `README.md` for `TBD|FIXME|XXX|TODO|HACK|PLACEHOLDER` and `placeholder|coming soon|not yet implemented|not available` (case-insensitive). Every match is a legitimate use: an HTML `placeholder` attribute on the Emby-URL input, and the DOM "not one of the login methods Jellyfin reports as enabled" placeholder `<option>` the code review already examined (WR-01, informational, not blocking).

### Human Verification Required

### 1. Resolve-time target re-validation and the T-03-04 mitigation-text mismatch

**Test:** Decide whether the documented residual gap in `LoginMethodMove.ResolveTarget` (no live re-check of Jellyfin's enabled-provider list at move time — only at save time) is acceptable to carry forward, or whether it should be closed now.
**Expected:** Either an explicit acceptance (and a correction to `03-06-PLAN.md:248`'s T-03-04 mitigation text, which currently claims `ChangePassword` "leaves the login method alone" for a since-disappeared target — a claim that is only true for a blank/absent target, not a stale non-blank one, as I confirmed by tracing `ChangePassword`'s `switch` against `ResolveTarget`), or a follow-up plan that adds a live enabled-list check to `ChangePassword`, `MoveAfterLogin`, and `EmbyMigrationTask`.
**Why human:** This is a risk-acceptance and documentation-accuracy call about a security-relevant threat-model entry, not something a grep or unit test can adjudicate. The gap is real (code-traced, not inferred from the SUMMARY), already candidly documented by the executor in `03-06-SUMMARY.md`'s "Resolve-Time Gap" section, and does not block ROADMAP criterion 6 as literally worded (which only covers the settings-page save path) — but it is the kind of open item the maintainer's "scope is binary" rule says should be resolved, not left implicit.

### Gaps Summary

No blocking gaps. All ten ROADMAP.md success criteria are observably true in the codebase, verified by direct code reading and one live test run (`TypeVisibilityTests`, 2/2 passed), not by trusting SUMMARY.md claims. `mise run test` (dotnet + script + JS suites) and `mise run e2e` (32/32 bats) were each run once by the orchestrator before this verification and both exited 0; I did not re-run either full suite, per the constraint against re-running a full suite for spot-checking.

One item is not a gap against this phase's literal success criteria but is worth a maintainer decision before it is forgotten: the resolve-time gap in `LoginMethodMove.ResolveTarget`, and the inaccurate T-03-04 threat-register text that describes a mitigation the code does not actually provide for the "target later disappears" scenario. This was flagged to me as a known, pre-recorded gap (03-06-SUMMARY.md), and I independently confirmed both the code behavior and the threat-register mismatch by tracing `EmbyAuthenticationProvider.ChangePassword`, `LoginMethodMove.ResolveTarget`, and the absence of a corresponding test. Routed to human_needed rather than gaps_found because it does not contradict any roadmap success criterion as written — it is an honestly-disclosed scope boundary that needs a decision, not a defect that needs a fix to close this phase.

---

_Verified: 2026-09-20T07:10:04Z_
_Verifier: Claude (gsd-verifier)_
