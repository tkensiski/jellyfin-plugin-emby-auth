# Codebase Concerns

**Analysis Date:** 2026-09-16

**Scope:** commit `ecee1ed` on `main`.

Each item states how it was established: **confirmed** (read in the code or measured with a command), **documented** (the README, `docs/`, or a rules file already states it), or **unverified** (inferred; the note says what would verify it).

This file is a snapshot of what was established on the analysis date. An entry that a later phase proves wrong keeps its original text and gains a dated **Correction** note, so the record of what was believed at map time survives.

## Known Bugs

**A failed fingerprint write does not behave as the log and docs say:**
- Symptoms: `Record` adds the fingerprint to the in-memory dictionary (`src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:53`) before it writes the file (lines 56-58). If the write fails, the catch at lines 60-63 only logs. The in-memory record stays, so `Matches` returns `true` for that user until Jellyfin restarts.
- Mismatch: the log message says the plugin "does not move this user to the Default login method until the user logs in again through Emby" (`EmbyVerifiedPasswords.cs:118`). The XML doc says "the record is lost" (line 37). `docs/how-it-works.md:40` says the plugin "moves no affected user to Default". In fact:
  - In `MoveAfterFirstLogin` mode the user moves right after the login (`MoveToDefaultLoginMethod.cs:48-53`).
  - In `JellyfinPasswordFirst` mode the saved password is accepted without Emby (`EmbyAuthenticationProvider.cs:80-86`).
  - The migration API reports the user as ready (`EmbyLoginMethodUsers.cs:51`, `Api/EmbyAuthController.cs:44-45`), and "Run migration now" moves the user (`MoveEmbyUsersToDefaultTask.cs:69-70`).
  - All of this holds until a restart.
- Impact: the hash is one that Emby verified, so no unverified password gains access. The defect is that the log message and docs describe behavior that the code does not have.
- Status: confirmed by reading. No unit test covers a write failure (`tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` has a read-failure test only, line 103).
- Fix approach: decide which behavior is correct, write the test for it first, then change either the code (add to the dictionary only after `File.Move` succeeds) or the log message, the XML doc, and `docs/how-it-works.md:40`.

**The timing-case limit points to the wrong shutdown step:**
- `docs/how-it-works.md:41` says "Step 2 of Shut down Emby finds these users". In `docs/migration.md:39`, step 2 asks users who are not ready to log in. A user put back on the Emby login method still has a verified hash, so the list shows that user as ready (`EmbyLoginMethodUsers.cs:51`). Step 1 (`docs/migration.md:38`) moves the user, and step 4 (`docs/migration.md:41`) checks that the list is empty.
- Status: confirmed by reading. The old README procedure had a "list the users" step 2; the procedure in `docs/migration.md` renumbered the steps.

## Fragile Areas

**Account creation leaves a Default account without a password for a moment:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:168-207`
- Why fragile: `CreateUserAsync` commits the account on the Default login method without a password (`.claude/rules/plugin.md:34`), and the Default login method accepts a blank password for such an account (`.claude/rules/plugin.md:33`). The provider saves the hash in a second call (`EmbyAuthenticationProvider.cs:189`). Between the two calls the account is open to a blank password.
- Failure paths (confirmed by reading):
  - If `UpdateUserAsync` fails, the `finally` block deletes the account (lines 197-202). If `DeleteUserAsync` itself throws, the account stays on Default without a password. That exception is not an `AuthenticationException`, so the login request returns HTTP 500 (`.claude/rules/plugin.md:15`).
  - An exception from `UpdateUserAsync` other than `DbUpdateException` or `ResourceNotFoundException` (line 192) still triggers the delete, then escapes as HTTP 500.
  - An exception from `CreateUserAsync` other than `ArgumentException` (line 175) escapes as HTTP 500.
- Unverified: whether two concurrent first logins for the same new name can both reach `CreateUserAsync`, and what Jellyfin throws for the second. This depends on whether Jellyfin resolves the user inside its login lock. Verify in Jellyfin 12.1 `UserManager.AuthenticateUser` and `CreateUserAsync`.
- Test coverage: none. There is no `EmbyAuthenticationProviderTests.cs`, and no e2e test forces a save or delete failure.

**A fingerprint file read failure erases earlier records on the next login:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:90-113`
- Why fragile: `Load` sets `_fingerprints = []` before it reads (line 97). If the read fails, it keeps that empty dictionary for the life of the process (lines 107-110) and never reads the file again. The next `Record` writes a file that holds only the new record (lines 56-58), so all earlier records are gone for good.
- Status: documented. The log message says "The next record replaces the file" (line 115), and `docs/how-it-works.md:40` says affected users do not move until they log in through Emby again. `UnreadableFile_MatchesNothing_AndLogsAnError` tests the read side.
- Impact (confirmed by reading):
  - The migration task skips every user whose record was erased (`EmbyLoginMethodUsers.cs:51`, `MoveEmbyUsersToDefaultTask.cs:69-70`), until that user logs in through Emby again.
  - The settings page lists each of those users as "needs one login while Emby runs" (`configPage.html:74`). Nothing on the page says that the file could not be read; only the Jellyfin log does (`EmbyVerifiedPasswords.cs:115`).
  - A transient read error (for example, a file lock at startup) has the same effect as a corrupt file. This is unverified: it would need a test that makes the read fail once.

**A concurrent session can put a user back on the Emby login method:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs:31-41`, `.claude/rules/plugin.md:21`
- Status: documented (`docs/how-it-works.md:41`). `MoveAsync` changes one column, so it does not overwrite other changes. But a later full `UpdateUserAsync` from another session of the same user can write the old login method back. Such a user appears in the Migration list, and the next migration run moves the user again (`EmbyLoginMethodUsers.cs:39-51`). See the known bug above for the wrong step reference.

**The Emby session can stay open after a login:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:81-94`, `EmbyClient.cs:165-186`
- Status: confirmed by reading.
  - If `Sessions/Logout` fails, the plugin logs a warning and keeps the login (lines 177-185, messages at lines 203 and 206). Tested by `Login_ReturnsLogin_WhenSignOutFails`.
  - If Emby accepts the login but the response cannot be read (lines 81-85) or has no user name (lines 88-92), the method returns before `SignOutAsync` (line 94). In the no-user-name case, a session token can be in the response and is not used to sign out. `Login_ReturnsNull_WhenEmbyResponseHasNoUserName` asserts this: for a response with a token and an empty name, only one request is sent (`tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs:181`, line 190).
- Impact: sessions build up on the Emby server. Emby is shut down at the end of a migration, so the effect is limited to the migration period.

**Settings page error handling:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html`
- Status: confirmed by reading.
  - The `pageshow` chain has `.finally` but no `.catch` (lines 85-93). If `getPluginConfiguration` fails, the fields stay empty, the Migration section does not load, and the page shows no message.
  - The submit handler has no `.catch` on `getPluginConfiguration` or `updatePluginConfiguration` (lines 110-118).
  - After "Run migration now", the page reloads the list once, after a fixed 3 seconds (lines 100-101). `POST /EmbyAuth/Migration/Run` returns 204 as soon as the task is queued, or if the task already runs (`Api/EmbyAuthController.cs:55-59`). If the task takes longer than 3 seconds, the list keeps showing moved users as ready until the page is opened again. Unverified: how long a run takes for a real user count.
- The migration list follows the `textContent` rule (`configPage.html:68-75`, `.claude/rules/plugin.md:50`).

## Performance Bottlenecks

All items here are unmeasured. No load test exists.

**Emby calls run inside Jellyfin's login lock:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:40`, `EmbyClient.cs:161`, `EmbyAuthenticationProvider.cs:88`, `EmbyAuthenticationProvider.cs:95`
- Cause: Jellyfin calls login methods inside a lock, and all logins for unknown names share one lock key (`.claude/rules/plugin.md:14`). A login that reaches Emby can send up to three requests, each on a client with a 5-second `Timeout`: the user list on a cache miss, `AuthenticateByName`, and `Sessions/Logout`. The provider passes `CancellationToken.None`, so a login cannot be cancelled early.
- Impact (unverified): when Emby is slow but still answers, first logins of new users wait for each other. After a failed user list read, the 30-second retry delay (`EmbyUserDirectory.cs:44`) makes later logins fail fast. A hang in `AuthenticateByName` alone has no such delay.
- The 5-second limit is fixed in code and documented (`docs/how-it-works.md:10`).
- **Correction (2026-09-20, Phase 4 discussion):** "all logins for unknown names share one lock key" is right, and its converse matters as much. Jellyfin locks on `user?.Id ?? Guid.Empty` (`UserManager.cs:573`, tag `v12.1`), so a **known** name takes its own user ID and repeat logins for different users do not wait for each other at all. This bottleneck is therefore a first-login condition only, which is the opposite of the condition under which the next entry can occur.

**User list refresh:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs:55-83`
- Cause: each refresh downloads the full Emby user list (line 62), and each login searches it linearly (line 76). Concurrent logins that find an expired snapshot each send their own request (lines 58-64); there is no single-flight guard. The snapshot is an immutable record swapped through a `volatile` field (line 46), so readers always see a consistent list.
- Impact (unverified): duplicate list requests when the cache expires. The effect grows with the number of Emby users and concurrent logins.
- **Correction (2026-09-20, Phase 4 discussion):** concurrent **first** logins cannot produce this stampede, because they serialize on the shared `Guid.Empty` lock (see the correction above). It needs concurrent logins for users who already have accounts, which lock on their own IDs and so run in parallel. A load-test scenario that drives first logins measures the entry above, not this one.

**Fingerprint file writes hold the lock:**
- Files: `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:45-64`, `EmbyVerifiedPasswords.cs:81-84`
- Cause: `Record` rewrites the whole JSON file inside the same lock that `Matches` uses. Every successful Emby login calls `Record`, unless the fingerprint is unchanged (lines 48-51). The migration status call and the migration task call `Matches` once per user on the Emby login method (`EmbyLoginMethodUsers.cs:46-51`).
- Impact (unverified): `Matches` calls wait for disk writes. `ConcurrentRecords_AreAllKept` tests correctness under concurrency, not latency.
- **Correction (2026-09-20, Phase 4 discussion):** "unless the fingerprint is unchanged" describes an exception that almost never applies. The fingerprint is a SHA-256 of the password **hash**, and `CreatePasswordHash` calls `GenerateSalt()` on every call (`Emby.Server.Implementations/Cryptography/CryptographyProvider.cs:18-30`, `:89-95`, tag `v12.1`), so the hash string differs on every login even for the same password. The skip therefore does not fire for a repeat login, and the file is fully read, modified, serialized, written, and moved under the lock on **every** accepted Emby login. Treat the write path as the normal path, not the exception.

## Security Considerations

These are documented design choices. They are listed so that a change does not weaken them by accident.

- **Plain HTTP to Emby is allowed:** `EmbyAuthSettings.cs:49-53` accepts `http` and `https`. Passwords go to Emby in the request body (`EmbyClient.cs:63-66`). `docs/settings.md:7` and `configPage.html:15` tell the admin to use `https` or a private network address. The code does not enforce this.
- **Disabling or deleting a user on Emby does not always revoke Jellyfin access:** this is true after the user moves to Default (`docs/how-it-works.md:34`). It is also true in `JellyfinPasswordFirst` mode when the saved password matches (`docs/how-it-works.md:8`, `EmbyAuthenticationProvider.cs:80-86`).
- **The API key is readable by Jellyfin administrators** through the plugin settings API (`docs/how-it-works.md:29`). The settings page loads the key into a `type="password"` input (`configPage.html:19`, `configPage.html:87`).
- **Invalid settings show up only at login:** `GetSettings` validates on each login and throws `AuthenticationException` with an Error log (`EmbyAuthenticationProvider.cs:144-153`, `docs/settings.md:12`). The settings page has no validation other than `type="url"` on the URL input (`configPage.html:14`). Unit tests cover `EmbyAuthSettings.TryCreate`; no e2e test covers invalid settings on a running server.
- **The migration API is admin-only:** `[Authorize(Policy = Policies.RequiresElevation)]` (`Api/EmbyAuthController.cs:23`). The e2e test checks 403 for a regular user on both endpoints (`e2e/30-migration-modes.bats:95`). This is not a concern; it is the guard that `.claude/rules/plugin.md:48` requires.

## Test Coverage Gaps

Confirmed from the file list in `tests/`, the `@test` names in `e2e/`, and `git diff --stat 0dc87e4 ecee1ed` (no change under `tests/Jellyfin.Plugin.EmbyAuth.Tests/`).

- **No unit tests** for `EmbyAuthenticationProvider`, `MoveToDefaultLoginMethod`, `MoveEmbyUsersToDefaultTask`, `DefaultLoginMethod`, `EmbyLoginMethodUsers`, `Api/EmbyAuthController`, or `PluginServiceRegistrator`. These depend on `IUserManager`, `JellyfinDbContext`, or `ITaskManager`. The e2e files cover their main paths: `e2e/10-login-checks.bats`, `e2e/20-accounts.bats`, `e2e/30-migration-modes.bats` (the migration API at lines 95 and 112), and `e2e/40-emby-outage.bats`.
- **No test** for any of these:
  - a failed account save or delete (`EmbyAuthenticationProvider.cs:186-203`)
  - a failed fingerprint write (`EmbyVerifiedPasswords.cs:60-63`)
  - concurrent logins through Jellyfin
  - invalid settings on a running server
- **Settings page JavaScript:** no test runs it. The only e2e check is that Jellyfin serves the page (`e2e/10-login-checks.bats:15`). The migration section, the "Run migration now" button, and the error messages (`configPage.html:63-105`) are untested.
- **Emby Connect email logins** have no dedicated test. The refusal (`docs/how-it-works.md:36`) follows from the user name check that `e2e/10-login-checks.bats:56` tests.

## Tooling Gaps

**Measured on `ecee1ed`:**
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

**Releases skip lint and e2e:**
- `release.yml` runs `scripts/package.sh check-tag`, `mise run test`, and `mise run package` (lines 32-41). It does not run `mise run lint` or `mise run e2e`. The trigger is any pushed `v*` tag (lines 6-9), with no check that the tagged commit passed CI.
- CI runs lint, test, and e2e on pull requests and on pushes to `main` (`ci.yml:5-9`, lines 19-62).
- Status: confirmed by reading. Unverified: whether a GitHub ruleset or tag protection limits who can push a `v*` tag, or on which commit. That setting is not in the repository.

## Deployment Status

- Tested only in local containers with Jellyfin 12.1.0 and Emby 4.10.0.40. Not tested on a production server. Not published to a plugin repository (`README.md:12`).
- Built against Jellyfin 12.1.0 packages only (`src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj:10-15`, `README.md:16`). No other Jellyfin or Emby version is tested.
- The repository is private (`README.md:23`). Each release has a `manifest.json`, but Jellyfin downloads the manifest and the zip without GitHub credentials. So the manifest works as a plugin repository URL only when the release files are public (`README.md:35`). Documented.

---

*Concerns audit: 2026-09-16*
