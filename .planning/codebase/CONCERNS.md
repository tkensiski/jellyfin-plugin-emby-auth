---
last_mapped_commit: fb0fd999638d88fabb37bd9d449c23226a5b2f8b
---

# Codebase Concerns

**Analysis Date:** 2026-09-20

**Scope:** commit `fb0fd99` on branch `gsd/phase-04-emby-traffic-under-load-and-failure`.

Each item states how it was established: **confirmed** (read in the code or measured with a command), **documented** (the README, `docs/`, or a rules file already states it), or **unverified** (inferred; the note says what would verify it).

This file is a snapshot of what was established on the analysis date. An entry that a later phase proves wrong keeps its original text and gains a dated **Correction** note, so the record of what was believed at map time survives.

## Known Bugs

**A failed fingerprint write does not behave as the log and docs say:**
- Symptoms: `Record` adds the fingerprint to the in-memory dictionary (`src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:58`) before it writes the file (lines 61-63). If the write fails, the catch at lines 65-68 only logs. The in-memory record stays, so `Matches` returns `true` for that user until Jellyfin restarts.
- Mismatch: the original log message (from commit `ecee1ed`) said the plugin "does not move this user to the Default login method until the user logs in again through Emby". The XML doc said "the record is lost". `docs/how-it-works.md:40` says the plugin "moves no affected user to Default". The code behavior was: users do move in both `MoveAfterFirstLogin` and `JellyfinPasswordFirst` modes, until a restart.
- Current state: the log message at line 145 now says "the record on disk is behind until the next successful write, and is lost only if Jellyfin restarts before one happens", which is accurate. The XML doc at line 37 also states this correctly now.
- Impact: the hash is one that Emby verified, so no unverified password gains access. The defect was resolved by changing the log and doc messages to match the code behavior.
- Status: confirmed by reading. Unit tests cover write failures: `WriteFailure_KeepsTheRecordInMemory_AndLogsExactlyOneErrorSayingSo`, `WriteFailure_LeavesTheFilesPreviousContentsUnchanged`, `WriteFailure_DoesNotLogASecondTime_ForTheSameUserAndHash` (`tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs:181-220`).
- Fix approach: already resolved by docs and log updates in commit `fb0fd99`. Original concern was about documentation, not code — this has been fixed.

**The timing-case limit points to the wrong shutdown step:**
- `docs/how-it-works.md:41` says "Step 2 of Shut down Emby finds these users". In `docs/migration.md:39`, step 2 asks users who are not ready to log in. A user put back on the Emby login method still has a verified hash, so the list shows that user as ready (`src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs:51`). Step 1 (`docs/migration.md:38`) moves the user, and step 4 (`docs/migration.md:41`) checks that the list is empty.
- Status: confirmed by reading. The old README procedure had a "list the users" step 2; the procedure in `docs/migration.md` renumbered the steps.

## Fragile Areas

**Account creation leaves a Default account without a password for a moment:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:176-224`
- Why fragile: `CreateUserAsync` commits the account on the Default login method without a password (per `.claude/rules/plugin.md:34`), and the Default login method accepts a blank password for such an account (per `.claude/rules/plugin.md:33`). The provider saves the hash in a second call (`EmbyAuthenticationProvider.cs:197`). Between the two calls the account is open to a blank password.
- Failure paths (confirmed by reading):
  - If `UpdateUserAsync` fails, the `finally` block deletes the account (lines 209-217). If `DeleteUserAsync` itself throws, the account stays on Default without a password. That exception is not an `AuthenticationException`, so the login request returns HTTP 500 (per `.claude/rules/plugin.md:15`).
  - An exception from `UpdateUserAsync` other than the caught types (line 199) still triggers the delete, then escapes as HTTP 500.
  - An exception from `CreateUserAsync` other than the caught type (line 183) escapes as HTTP 500.
- Status: confirmed by tests. `EmbyAuthenticationProviderTests.cs` covers:
  - `RefusesTheLogin_WhenCreateUserFailsWithAnUnexpectedExceptionType` (line 210)
  - `RefusesTheLogin_WhenTheSaveAfterCreateUserFailsWithAnUnexpectedExceptionType` (line 223)
  - `RefusesTheLogin_WhenTheSaveAfterCreateUserFailsWithACaughtExceptionType` (line 235)
  - `RefusesTheLogin_WhenTheSaveAndTheCleanupDeleteBothFail` (line 247)
- Test coverage: comprehensive. No test forces an authentication-level failure (e.g., a concurrent first login for the same name), but this scenario requires deeper knowledge of Jellyfin's UserManager behavior.

**A fingerprint file read failure erases earlier records on the next login:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:116-140`
- Why fragile: `Load` sets `_fingerprints = []` before it reads (line 125). If the read fails (lines 129-137), it keeps that empty dictionary for the life of the process (returning `null` at line 136) and never reads the file again. The next `Record` writes a file that holds only the new record (lines 61-63), so all earlier records are gone for good.
- Status: documented. The log message says "the plugin records no verified password and moves no user to the Default login method while the read fails. It keeps the records that are in the file and reads the file again on the next login" (line 142). `UnreadableFile_MatchesNothing_AndLogsAnError` tests the read side (`tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs:107-120`).
- Impact (confirmed by reading):
  - The migration task skips every user whose record was erased (`src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs:51`), until that user logs in through Emby again.
  - The settings page lists each of those users as "needs one login while Emby runs" (`src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:103`). Nothing on the page says that the file could not be read; only the Jellyfin log does (`EmbyVerifiedPasswords.cs:142`).
  - A transient read error (for example, a file lock at startup) has the same effect as a corrupt file. This is unverified: it would need a test that makes the read fail once.
- Clarification (confirmed by reading): The comment at line 137 says `Load` returns `null` on read failure and does not touch the cache. The caller `Record` then returns early at line 50, never writing the file. So the one-login-retry behavior stated in the log message is accurate.

**A concurrent session can put a user back on the Emby login method:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs:33-44`, per `.claude/rules/plugin.md:21`
- Status: documented (`docs/how-it-works.md:41`). `MoveAsync` changes one column, so it does not overwrite other changes. But a later full `UpdateUserAsync` from another session of the same user can write the old login method back. Such a user appears in the Migration list, and the next migration run moves the user again (`src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs:39-51`).

**The Emby session can stay open after a login:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:81-94`, `EmbyClient.cs:165-186`
- Status: confirmed by reading.
  - If `Sessions/Logout` fails, the plugin logs a warning and keeps the login (lines 177-185, messages at lines 203 and 206). Tested by `Login_ReturnsLogin_WhenSignOutFails` (`EmbyClientTests.cs:177`).
  - If Emby accepts the login but the response cannot be read (lines 81-85) or has no user name (lines 88-92), the method returns before `SignOutAsync` (line 94). In the no-user-name case, a session token can be in the response and is not used to sign out. `Login_ReturnsNull_WhenEmbyResponseHasNoUserName` asserts this: for a response with a token and an empty name, only one request is sent (`tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs:181`, line 190).
- Impact: sessions build up on the Emby server. Emby is shut down at the end of a migration, so the effect is limited to the migration period.

**Settings page error handling has been significantly improved:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html`
- Prior state (commit `ecee1ed`): The `pageshow` chain had no `.catch`, so if `getPluginConfiguration` failed, the fields stayed empty and no message appeared. The submit handler had no `.catch` on `getPluginConfiguration` or `updatePluginConfiguration`. After "Run migration now", the page reloaded the list once after a fixed 3 seconds, but no feedback appeared if the task took longer.
- Current state (commit `fb0fd99`): 
  - The `pageshow` chain now has proper `.catch` and `.finally` handlers (lines 370-377). If `getPluginConfiguration` fails, the page shows a message, disables save, and clears the migration section.
  - The submit handler has `.catch` and `.finally` handlers (lines 439-442). If either API call fails, the page shows a message and restores the save button.
  - After "Run migration now", the page polls the status every 2 seconds (line 256) and stops when the task finishes or after 10 failed polls (line 292). The migration result message is updated dynamically (line 294).
- Status: confirmed by reading. Comprehensive tests added: 58 test cases in `tests/js/configPage.test.js` cover error handling, load failures, save failures, migration failures, polling behavior, and more. The page now properly reports errors and provides user feedback.

## Performance Bottlenecks

All items here are unmeasured. No load test exists.

**Emby calls run inside Jellyfin's login lock:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:40`, `EmbyClient.cs:161`, `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:88`, `EmbyAuthenticationProvider.cs:96`
- Cause: Jellyfin calls login methods inside a lock, and all logins for unknown names share one lock key (per `.claude/rules/plugin.md:14`). A login that reaches Emby can send up to three requests, each on a client with a 5-second `Timeout`: the user list on a cache miss, `AuthenticateByName`, and `Sessions/Logout`. The provider passes `CancellationToken.None`, so a login cannot be cancelled early.
- Impact (unverified): when Emby is slow but still answers, first logins of new users wait for each other. After a failed user list read, the 30-second retry delay (`src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs:44`) makes later logins fail fast. A hang in `AuthenticateByName` alone has no such delay.
- The 5-second limit is fixed in code and documented (`docs/how-it-works.md:10`).
- **Correction (2026-09-20, Phase 4 discussion):** "all logins for unknown names share one lock key" is right, and its converse matters as much. Jellyfin locks on `user?.Id ?? Guid.Empty` (`UserManager.cs:573`, tag `v12.1`), so a **known** name takes its own user ID and repeat logins for different users do not wait for each other at all. This bottleneck is therefore a first-login condition only, which is the opposite of the condition under which the next entry can occur.

**User list refresh:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs:55-83`
- Cause: each refresh downloads the full Emby user list (line 62), and each login searches it linearly (line 76). Concurrent logins that find an expired snapshot each send their own request (lines 58-64); there is no single-flight guard. The snapshot is an immutable record swapped through a `volatile` field (line 46), so readers always see a consistent list.
- Impact (unverified): duplicate list requests when the cache expires. The effect grows with the number of Emby users and concurrent logins.
- **Correction (2026-09-20, Phase 4 discussion):** concurrent **first** logins cannot produce this stampede, because they serialize on the shared `Guid.Empty` lock (see the correction above). It needs concurrent logins for users who already have accounts, which lock on their own IDs and so run in parallel. A load-test scenario that drives first logins measures the entry above, not this one.

**Fingerprint file writes hold the lock:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:45-69`, `EmbyVerifiedPasswords.cs:81-84`
- Cause: `Record` rewrites the whole JSON file inside the same lock that `Matches` uses. Every successful Emby login calls `Record`, unless the fingerprint is unchanged (lines 53-56). The migration status call and the migration task call `Matches` once per user on the Emby login method (`src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs:46-51`).
- Impact (unverified): `Matches` calls wait for disk writes. `ConcurrentRecords_AreAllKept` tests correctness under concurrency, not latency (`tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs:169-175`).
- **Correction (2026-09-20, Phase 4 discussion):** "unless the fingerprint is unchanged" describes an exception that almost never applies. The fingerprint is a SHA-256 of the password **hash**, and `CreatePasswordHash` calls `GenerateSalt()` on every call (`Emby.Server.Implementations/Cryptography/CryptographyProvider.cs:18-30`, `:89-95`, tag `v12.1`), so the hash string differs on every login even for the same password. The skip therefore does not fire for a repeat login, and the file is fully read, modified, serialized, written, and moved under the lock on **every** accepted Emby login. Treat the write path as the normal path, not the exception.

## Security Considerations

These are documented design choices. They are listed so that a change does not weaken them by accident.

- **Plain HTTP to Emby is allowed:** `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs:49-53` accepts `http` and `https`. Passwords go to Emby in the request body (`src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:63-66`). `docs/settings.md:7` and `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:27` tell the admin to use `https` or a private network address. The code does not enforce this.
- **Disabling or deleting a user on Emby does not always revoke Jellyfin access:** this is true after the user moves to Default (`docs/how-it-works.md:34`). It is also true in `JellyfinPasswordFirst` mode when the saved password matches (`docs/how-it-works.md:8`, `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:80-86`).
- **The API key is readable by Jellyfin administrators** through the plugin settings API (`docs/how-it-works.md:29`). The settings page loads the key into a `type="password"` input (`src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:31`, `:358`).
- **Invalid settings show up only at login:** `GetSettings` validates on each login and throws `AuthenticationException` with an Error log (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:164-173`, `docs/settings.md:12`). The settings page has no validation other than `type="url"` on the URL input (`src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:26`). Unit tests cover `EmbyAuthSettings.TryCreate`; no e2e test covers invalid settings on a running server.
- **The migration API is admin-only:** `[Authorize(Policy = Policies.RequiresElevation)]` (`src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs:23`). The e2e test checks 403 for a regular user on both endpoints (`e2e/30-migration-modes.bats:95`). This is not a concern; it is the guard that `.claude/rules/plugin.md:48` requires.

## Test Coverage Gaps

Confirmed from the file list in `tests/`, the `@test` names in `e2e/`, and the code review of recent changes.

- **No e2e test** covers invalid settings on a running server. Unit tests cover `EmbyAuthSettings.TryCreate`. A running server that receives invalid settings will refuse all Emby-method logins; the error is logged but not visible to the admin until they look at the Jellyfin log.

- **No concurrent-session test** for the same Jellyfin user under high concurrency. The fragile area "A concurrent session can put a user back on the Emby login method" is documented but not measured. A test would need to drive concurrent logins for the same user (different passwords) while the migration runs.

- **No test** for Emby user list read failure followed by successful read. The fragile area "A fingerprint file read failure erases earlier records" is documented and the happy path is tested. A scenario that reads the file, fails on a transient error, then succeeds on retry would verify the retry behavior.

- **No test** for load under concurrent first-login pressure. The performance bottleneck "Emby calls run inside Jellyfin's login lock" is documented with no load test. A scenario with many concurrent first logins would measure whether the single-flight lock becomes a bottleneck.

- **Settings page JavaScript:** comprehensive tests added. `tests/js/configPage.test.js` has 58 test cases covering:
  - Failed settings load and recovery (e.g., `a failed settings load shows a message on the page`)
  - Failed settings save (e.g., `Save fails and the page reports the failure`)
  - Failed migration status reads (e.g., `Jellyfin cannot read the migration status`)
  - Migration run failures and polling behavior (e.g., `The migration runs. This list updates until it finishes`)
  - Target selection and no-password warnings (e.g., `Users with no saved password get a warning`)
  - The page is no longer untested.

- **Emby Connect email logins** have no dedicated test. The refusal (`docs/how-it-works.md:36`) follows from the user name check that `e2e/10-login-checks.bats:56` tests.

## Tooling Gaps

**Measured on `fb0fd99`:**
- `shellcheck -x e2e/*.bash e2e/*.bats scripts/*.sh tests/scripts/*.bats`: no output, exit 0.
- `shfmt -d e2e scripts tests/scripts`: no output, exit 0.
- `actionlint`: no output, exit 0.
- `zizmor --offline .github/workflows`: "No findings to report" (7 suppressed), exit 0.
- `zizmor --offline --persona=pedantic .github/workflows`: 7 findings (5 informational, 2 low, 0 medium, 0 high). Examples: `release.yml` has no `concurrency` setting (line 6), no comment on `contents: write` (line 19), and no job `name` (line 15).

**Version bump rule misses pins:**
- `CLAUDE.md:49` says a Jellyfin version bump changes three pins together: `Jellyfin.Controller` and `Jellyfin.Model`, the Jellyfin image tag, and the target framework.
- The test project also pins `Jellyfin.Controller` 12.1.0 (`tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj:9`).
- `scripts/package.sh:34-38` derives the manifest `targetAbi` from the plugin project. But `tests/scripts/package.bats:47` and `:61` expect the fixed value `12.1.0.0`, so a bump without a change to those lines fails `mise run test`.
- Status: confirmed by reading.

**Releases now run unit tests but still skip full lint and e2e:**
- `release.yml` runs `scripts/package.sh check-tag`, `mise run test`, and `mise run package` (lines 32-41). It runs unit tests, script tests, and settings-page tests. However, it does not run `mise run lint` (which runs shellcheck, shfmt, actionlint, and zizmor) or `mise run e2e`.
- CI runs lint, test, and e2e on pull requests and on pushes to `main` (`ci.yml:5-9`, lines 19-62).
- Status: confirmed by reading. The release workflow has improved (now runs test), but skipping lint and e2e means a `v*` tag can be pushed from a commit that passed CI if it was force-pushed after CI, or if CI was disabled. Unverified: whether a GitHub ruleset or tag protection limits who can push a `v*` tag, or on which commit. That setting is not in the repository.

## Deployment Status

- Tested only in local containers with Jellyfin 12.1.0 and Emby 4.10.0.40. Not tested on a production server. Not published to a plugin repository (`README.md:12`).
- Built against Jellyfin 12.1.0 packages only (`src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj:10-15`, `README.md:16`). No other Jellyfin or Emby version is tested.
- The repository is private (`README.md:23`). Each release has a `manifest.json`, but Jellyfin downloads the manifest and the zip without GitHub credentials. So the manifest works as a plugin repository URL only when the release files are public (`README.md:35`). Documented.

---

*Concerns audit: 2026-09-20*
