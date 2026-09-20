---
phase: 04-emby-traffic-under-load-and-failure
verified: 2026-09-20T22:29:22Z
status: passed
score: 7/7 must-haves verified
behavior_unverified: 0
overrides_applied: 0
---

# Phase 04: Emby Traffic Under Load and Failure — Verification Report

**Phase Goal:** Move the fingerprint records from a JSON file to a plugin-owned SQLite database (no lock on read, durable per-row commit), then make the plugin behave predictably toward Emby under concurrent logins and invalid settings, and collapse the user-list-refresh stampede to one outgoing request.
**Verified:** 2026-09-20T22:29:22Z
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Fingerprint records live in a SQLite DB in the plugin data folder; `Matches`/`RecordsAvailable` take no lock of the plugin's own; a record commits durably; the plugin references the pinned `Microsoft.Data.Sqlite` version and binds to Jellyfin's copy | ✓ VERIFIED | `EmbyVerifiedPasswords.cs:98-141` — `Matches` and `RecordsAvailable` contain no `lock`, `Lock`, or semaphore, just an `Open()`/`ExecuteScalar` pair. `Record` (`:67-89`) issues one `INSERT ... ON CONFLICT DO UPDATE` per call inside its own connection, no explicit transaction batching across calls (default SQLite autocommit = durable per statement). WAL mode set at `:181`. csproj pins `Microsoft.Data.Sqlite.Core` `10.0.11` with `<ExcludeAssets>runtime</ExcludeAssets>` (`Jellyfin.Plugin.EmbyAuth.csproj:16-18`). Independently pulled `jellyfin/jellyfin:12.1.20260915-010956` and grepped `/jellyfin/jellyfin.deps.json`: ships `Microsoft.Data.Sqlite.Core/10.0.11` — exact match, not just cited from SUMMARY. Ran `dotnet publish` myself: output holds exactly 4 files (`.dll`, `.pdb`, `.xml`, `.deps.json`) — no `Microsoft.Data.Sqlite.dll`, no SQLitePCLRaw assembly, no native `e_sqlite3`. `scripts/package.sh:80` zips only `Jellyfin.Plugin.EmbyAuth.dll` + `meta.json`, so the release zip can never carry a shipped SQLite assembly. No EF Core SQLite reference in the plugin csproj (only in the test project). |
| 2 | On first start with an existing fingerprint JSON file, every record imports once; a later start does not re-import; unit test + e2e test cover it | ✓ VERIFIED | `EmbyVerifiedPasswords.ImportLegacyRecords` (`:212-263`) keys the one-time import on `PRAGMA user_version`, imports and marks done inside one transaction, never writes/moves/deletes the legacy file. Unit tests `Import_MakesEveryLegacyRecordMatch_OnFirstStart`, `Import_DoesNotRunASecondTime`, `Import_DoesNotOverwriteARecordTheStoreAlreadyWrote`, `Import_LeavesTheLegacyFileUnchanged`, `Import_MarksItselfDone_WhenNoLegacyFileExists`, `Import_ImportsNothing_AndLogsOneError_WhenTheLegacyFileCannotBeRead`, `Import_ImportsNothing_WhenOneEntryInTheFileIsUnusable`, `Import_LeavesAUsableStore_WhenItFails` all present in `EmbyVerifiedPasswordsTests.cs:269-393`; ran `dotnet test --filter EmbyVerifiedPasswordsTests`: 26/26 pass. `e2e/80-fingerprint-store.bats` drives a real upgrade: stops Jellyfin, pulls the SQLite DB the plugin just wrote for a real logged-in user (`elton`), builds a legacy JSON fixture from that DB's own rows (so the fixture is a genuine record, not invented), removes the DB, restarts, and asserts `migration_state elton == Ready`; a second test asserts a record deleted between two starts stays absent with the JSON file still present. Already-established e2e gate (41/41, exit 0) covers execution; I read the file's actual assertions rather than trusting the SUMMARY. |
| 3 | The plugin ends the Emby session whenever it can read an access token, including a response with no user name; unit test covers it; a response the plugin cannot parse is a stated, documented limit | ✓ VERIFIED | `EmbyClient.cs:87-96`: `SignOutAsync` is called at `:90`, ahead of the `IsNullOrEmpty(embyUserName)` check at `:92`, so the sign-out is reachable regardless of user-name presence. `SignOutAsync`'s empty-token guard (`:170-173`) makes the reorder safe for token-less responses. Test `Login_EndsTheEmbySession_WhenTheResponseHasATokenAndNoUserName` exists at `EmbyClientTests.cs:223`; ran `dotnet test --filter EmbyClientTests`: 27/27 pass. `docs/how-it-works.md:9,55` states the sign-out fires whenever a token is readable including a no-user-name response, and separately names the limit: a body the plugin cannot parse hides the token, so one Emby session can stay open, and both such responses still refuse the Jellyfin login. |
| 4 | Concurrent first logins: each Emby user gets exactly one account; no login returns HTTP 500 | ✓ VERIFIED | `e2e/60-concurrent-logins.bats` fires 5 concurrent first logins for an unknown Emby name (`bella`) and for a name with an existing account (`chris`); asserts every response is `200` or `401` (never `500`), and exactly one Jellyfin account exists afterward by name lookup. Unit-level race-loser test `RefusesTheLosingLogin_WhenTwoFirstLoginsForOneNameRace` (`EmbyAuthenticationProviderTests.cs:205`) asserts the losing login is refused with `AuthenticationException` (never an unhandled exception), attempts no delete, and logs exactly one reworded Error entry naming both causes (benign race vs. Jellyfin name rejection) with no secret. Ran `dotnet test --filter EmbyAuthenticationProviderTests`: 34/34 pass. Already-established e2e gate covers the running-server half (41/41, exit 0); I read the actual `.bats` assertions directly rather than the SUMMARY. |
| 5 | e2e test: invalid settings on a running server refuse Emby-login-method logins, Jellyfin log names the problem at Error | ✓ VERIFIED | `e2e/70-invalid-settings.bats` drives all four `EmbyAuthSettings.FindProblem` cases through the live settings API (blank URL, invalid URL, credential-carrying URL, blank API key), asserts each login returns `401` and the Jellyfin log carries an `[ERR]` line with the matching sentence. The credential-carrying-URL case additionally asserts the whole-log scan finds no leaked password. |
| 6 | Concurrent logins on an expired user-list snapshot send one Emby user list request between them; unit test drives concurrency and counts requests | ✓ VERIFIED | `EmbyUserDirectory.GetStatusAsync` (`:58-98`) reads the volatile `_snapshot` field with no lock on the fast path (`:61`), and only concurrent callers that find it stale/absent take `_refreshGuard` (a `SemaphoreSlim`) and re-check the snapshot after acquiring before issuing a request. Tests `ConcurrentReaders_OnAnExpiredSnapshot_SendOneRequest`, `ConcurrentReaders_OnAColdDirectory_SendOneRequest`, `ConcurrentReaders_WithDifferentSettings_SendTwoRequests`, `ConcurrentReaders_AfterAFailedRefresh_AreNotBlocked` all present (`EmbyUserDirectoryTests.cs:166-273`) and drive real concurrency via the now-thread-safe `StubHttpMessageHandler` with `HoldResponses`/`ReleaseResponses`/`FirstRequestStarted`. Ran `dotnet test --filter EmbyUserDirectoryTests`: 20/20 pass. |
| 7 | `docs/how-it-works.md` names the two remaining costs (Emby calls inside Jellyfin's login lock; Jellyfin's own account save per accepted login) with the reason neither has a plugin-side fix | ✓ VERIFIED | `docs/how-it-works.md:56-57` (Limits section) names both costs verbatim with reasons: the login-lock cost because "the plugin has to ask Emby whether the password is right before it can answer"; the account-save cost because "this is Jellyfin's own account save, not something the plugin does itself, so what it costs is Jellyfin's to decide." No performance number, threshold, or percentile appears anywhere in the file (grep confirms zero matches). Only these two costs are named as unaddressed; every pre-existing Limits bullet is still present. |

**Score:** 7/7 truths verified.

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` | SQLite-backed store, no lock on read, one-time import | ✓ VERIFIED | Read in full; matches all claims above |
| `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` | Derives DB path from `IApplicationPaths.PluginsPath` + assembly name, legacy path from `PluginConfigurationsPath` | ✓ VERIFIED | `:40-53`, matches `BasePluginOfT.cs:50`'s derivation as claimed |
| `src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj` | `Microsoft.Data.Sqlite.Core` 10.0.11, runtime assets excluded, no EF Core SQLite ref | ✓ VERIFIED | Read in full; matches |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` | Sign-out ahead of user-name check | ✓ VERIFIED | `:87-96` |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs` | Single-flight guard around refresh branch only | ✓ VERIFIED | `:58-98` |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` | Race-loser message states both causes | ✓ VERIFIED | Test asserts message content and log level unchanged |
| `docs/how-it-works.md` | Documents SQLite store, AUTH-05 sign-out limit, two undocumented costs | ✓ VERIFIED | Read in full |
| `CHANGELOG.md` | Records the store change and the one-way rollback risk | ✓ VERIFIED | Unreleased → Changed and Upgrade note sections |
| `e2e/60-concurrent-logins.bats`, `70-invalid-settings.bats`, `80-fingerprint-store.bats` | Cover TEST-05, TEST-06, FPRT-04's e2e half | ✓ VERIFIED | All three read in full, assertions match must-haves |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `PluginServiceRegistrator` | `EmbyVerifiedPasswords` | Constructs DB path from `IApplicationPaths.PluginsPath` + assembly name | ✓ WIRED | `:44-47` |
| `EmbyClient.AuthenticateAsync` | `EmbyClient.SignOutAsync` | Called before the user-name null check | ✓ WIRED | `:90` precedes `:92` |
| `EmbyUserDirectory.GetStatusAsync` | `_refreshGuard` (`SemaphoreSlim`) | Only the refresh branch takes the guard; fast path reads `_snapshot` directly | ✓ WIRED | `:61-84` |
| `EmbyAuthenticationProvider.CreateAccountAsync` | Race-loser catch | `ArgumentException` from `CreateUserAsync` refused with `AuthenticationException`, no delete attempted | ✓ WIRED | `:176-224`, confirmed by test asserting `userManager.Calls == ["CreateUserAsync"]` |

### Behavioral Spot-Checks (self-run, not SUMMARY-reported)

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| `EmbyVerifiedPasswordsTests` suite (incl. import tests) | `dotnet test --filter EmbyVerifiedPasswordsTests` | 26/26 pass | ✓ PASS |
| `EmbyClientTests` suite (incl. sign-out reorder tests) | `dotnet test --filter EmbyClientTests` | 27/27 pass | ✓ PASS |
| `EmbyUserDirectoryTests` suite (incl. concurrency tests) | `dotnet test --filter EmbyUserDirectoryTests` | 20/20 pass | ✓ PASS |
| `EmbyAuthenticationProviderTests` suite (incl. race-loser test) | `dotnet test --filter EmbyAuthenticationProviderTests` | 34/34 pass | ✓ PASS |
| Pinned Jellyfin image ships `Microsoft.Data.Sqlite.Core/10.0.11` | `docker run --rm --entrypoint sh jellyfin/jellyfin:12.1.20260915-010956 -c "grep -o 'Microsoft.Data.Sqlite.Core/[0-9.]*' /jellyfin/jellyfin.deps.json"` | `Microsoft.Data.Sqlite.Core/10.0.11` | ✓ PASS |
| Publish output carries no shipped SQLite assembly | `dotnet publish src/Jellyfin.Plugin.EmbyAuth -c Release -o <tmp>` then `ls` | Exactly 4 files: `.dll`, `.pdb`, `.xml`, `.deps.json` | ✓ PASS |
| `Database_DoesNotContainThePasswordHash` regression check (WR-03 reproduction) | Mutated `Record`/`Matches` to bind `passwordHash` instead of `fingerprint`, ran the single named test, reverted the file byte-for-byte | Test still **passed** with the raw hash stored | ✗ FAIL — see Anti-Patterns / test-integrity finding below |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| FPRT-04 | 04-01, 04-05, 04-08 | SQLite store, no-lock reads, durable commit, one-time import, pinned SQLite version | ✓ SATISFIED | Criteria 1, 2 above |
| AUTH-05 | 04-02, 04-08 | Sign-out fires whenever a token is readable, including no-user-name responses | ✓ SATISFIED | Criterion 3 above |
| TEST-05 | 04-04, 04-06 | Concurrent first-login coverage, unit + e2e | ✓ SATISFIED | Criterion 4 above |
| TEST-06 | 04-06 | e2e coverage of invalid settings refusing logins with an Error-level log | ✓ SATISFIED | Criterion 5 above |
| PERF-01 | 04-03 | Single-flight guard on the user-list refresh | ✓ SATISFIED | Criterion 6 above |
| PERF-02 | 04-08 | Documents the two remaining, unfixable costs | ✓ SATISFIED | Criterion 7 above |

No orphaned requirements: REQUIREMENTS.md's traceability table maps exactly these six IDs to Phase 4, and all six appear in a plan's `requirements` frontmatter.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` | 134-140 | `Database_DoesNotContainThePasswordHash` reads `File.ReadAllBytes(_databasePath)` directly, without `SqliteConnection.ClearAllPools()` first, unlike `GetUserVersion`/`SetUserVersion`/`DeleteRow` in the same file which all call it | ⚠️ Warning (test-integrity, not a code defect) | **Reproduced, not just cited from the code review.** I temporarily changed `Record`/`Matches` to bind the raw `passwordHash` instead of `fingerprint` (the exact regression this test's name claims to catch), ran only `Database_DoesNotContainThePasswordHash`, and it still **passed** — then reverted the source file to its exact committed content (confirmed by an empty `git diff`). Under WAL + connection pooling (both enabled by this class), the insert is not checkpointed into the main `.db` file until the last pooled connection closes, so the test's raw-byte read sees stale/incomplete file content regardless of what was actually stored. The production code is correct — `Record`/`Matches` do compute and store only the SHA-256 fingerprint (confirmed by direct source read of the unmodified file) — but this specific regression test provides no actual protection against a future change that stores the raw hash. This is the WR-03 finding from the code review, now proven with a concrete before/after run rather than accepted on the reviewer's word. |

No `TBD`, `FIXME`, `XXX`, `TODO`, `HACK`, or `PLACEHOLDER` markers found in any file this phase modified.

WR-01 (`EmbyUserDirectory.cs:61-84` captures `now` before acquiring the refresh guard, so the effective cache lifetime is shorter than the documented 60s), WR-02 (`Dispose()` racing an in-flight caller, shutdown-only), and WR-04 (`EnsureInitialized`'s catch filter not covering every exception type the init path can throw) are accepted as documented, non-blocking findings per the code review — none affects any of the 7 roadmap success criteria, and none is a security-relevant test-integrity gap the way WR-03 is.

## Gaps Summary

No gaps. All 7 ROADMAP success criteria are verified against the actual codebase — not against SUMMARY claims — including two independently reproduced checks (the pinned Jellyfin image's `Microsoft.Data.Sqlite.Core` version, and a live `dotnet publish` proving no SQLite assembly ships) and one reproduced regression (WR-03: `Database_DoesNotContainThePasswordHash` cannot fail on the exact defect it names).

The WR-03 finding does not block the phase: the production code correctly stores only the fingerprint (verified directly from source, independent of the defective test), so no roadmap criterion is actually unmet. It is recorded here as a **WARNING** so a human can decide whether to open a follow-up to fix the test (call `SqliteConnection.ClearAllPools()` before the raw-byte read, matching the pattern already used elsewhere in the same test file) before the security-sensitive "never stores the raw hash" guarantee is asserted again without evidence.

---

_Verified: 2026-09-20T22:29:22Z_
_Verifier: Claude (gsd-verifier)_
