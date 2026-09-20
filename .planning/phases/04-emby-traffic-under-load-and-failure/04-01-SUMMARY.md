---
phase: 04-emby-traffic-under-load-and-failure
plan: 01
subsystem: storage
tags: [sqlite, microsoft.data.sqlite, jellyfin-plugin, fingerprint-store, wal]

# Dependency graph
requires:
  - phase: 03-migration-status-and-target
    provides: EmbyLoginMethodUsers and MoveAfterLogin call sites against EmbyVerifiedPasswords.Matches/RecordsAvailable, unchanged by this plan
provides:
  - A SQLite-backed EmbyVerifiedPasswords with unchanged public signatures (Record/Matches/RecordsAvailable)
  - A Microsoft.Data.Sqlite.Core 10.0.11 reference in the plugin project, runtime assets excluded
  - The plugin-data-folder path derivation PluginServiceRegistrator now uses for the database file
  - EmbyAuthPlugin.OnUninstalling clearing pooled SQLite connections
affects: [04-05 (JSON-to-SQLite import), 04-07 (e2e proof the database lives at the derived path), 04-08 (docs naming the store)]

# Actuals (#2632) — pairs with the plan's `estimate` to calibrate future estimates.
actuals:
  tokens: 9790
  tasks: 2
  commits: 3

# Tech tracking
tech-stack:
  added: [Microsoft.Data.Sqlite.Core 10.0.11]
  patterns:
    - "Lock-free reads over a durable per-row store: Matches/RecordsAvailable open their own connection and never take a lock of the plugin's own, mirroring EmbyUserDirectory's read-without-lock shape but backed by SQLite's own concurrency control instead of a volatile snapshot."
    - "Eager, non-negatively-cached initialization: the constructor calls EnsureInitialized() once and ignores the result; a later Record() retries initialization on its own, so a transient failure (disk full, permissions) self-heals without restarting the plugin."

key-files:
  created: []
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs
    - src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs
    - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs
    - src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs
    - CLAUDE.md

key-decisions:
  - "Checkpoint resolved `proceed` (human, before this continuation): the fingerprint store moves to SQLite, one-way for data written after the upgrade, and the plugin project gains a pinned Microsoft.Data.Sqlite.Core reference."
  - "The RED phase proved nothing by itself: unskipping all 14 rewritten tests against the pre-rewrite JSON store passed all 14, because this is a storage-engine swap behind an unchanged public API. Red was proven instead by break-then-restore against the finished SQLite implementation, for the durability, upsert-overwrite, and case-sensitivity guards, plus all four of Task 2's new failure-mode tests — see the TDD Discipline section below for the exact breaks and results."
  - "SchemaVersion (declared per Task 1's action text) is used to set PRAGMA user_version during EnsureInitialized, since an unused private const fails the build under CA1823 (AnalysisMode AllEnabledByDefault, warnings as errors). This is forward-compatible schema-version tracking; no code reads it back yet."
  - "PRAGMA user_version's value comes from a compile-time int constant, not external input; CA2100 was suppressed inline with a Justification comment rather than parameterized, since PRAGMA does not accept bound parameters."

requirements-completed: [FPRT-04]

coverage:
  - id: D1
    description: "SQLite-backed EmbyVerifiedPasswords: Record commits a per-row upsert, Matches/RecordsAvailable take no lock of the plugin's own, and a record written by one store instance is read back by a second instance over the same file"
    requirement: "FPRT-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#Matches_TheRecordedHash,Record_ReplacesTheEarlierHash,Records_SurviveARestart,ConcurrentRecords_AreAllKept,Fingerprint_IsCaseSensitive"
        status: pass
      - kind: e2e
        ref: "bats e2e/30-migration-modes.bats"
        status: pass
    human_judgment: false
  - id: D2
    description: "The plugin binds to Jellyfin's own SQLite copy at runtime and ships no SQLite assembly or native library of its own"
    requirement: "FPRT-04"
    verification:
      - kind: unit
        ref: "command: dotnet publish src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj -c Release -o artifacts/plugin && find artifacts/plugin -maxdepth 1 -name '*.dll' | wc -l"
        status: pass
      - kind: e2e
        ref: "command: docker compose -f e2e/compose.yaml exec -T jellyfin grep -c 'Microsoft.Data.Sqlite.Core/10.0.11' /jellyfin/jellyfin.deps.json"
        status: pass
    human_judgment: false
  - id: D3
    description: "The store's SQLite-specific failure modes (an uncreatable directory, a corrupt database file hit by RecordsAvailable and by Record) fail safely, log exactly the database path and the exception, and never leak a hash or fingerprint"
    requirement: "FPRT-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#StoreThatCannotBeOpened_MatchesNothing_AndLogsOneError,RecordsAvailable_IsFalse_WhenTheDatabaseFileIsNotADatabase,Record_LogsOneError_AndDoesNotThrow_WhenTheDatabaseFileIsNotADatabase,NoLogEntryNamesAHashOrAFingerprint"
        status: pass
    human_judgment: false
  - id: D4
    description: "CLAUDE.md's Jellyfin version bump rule names four pins instead of three, including Microsoft.Data.Sqlite.Core"
    requirement: "FPRT-04"
    verification:
      - kind: other
        ref: "command: rg -q 'four pins' CLAUDE.md && rg -q 'Microsoft.Data.Sqlite.Core' CLAUDE.md"
        status: pass
    human_judgment: false

# Metrics
duration: 18min (this continuation session; RED phase was a prior session, committed 2026-09-20T12:19-07:00)
completed: 2026-09-20
status: complete
---

# Phase 04 Plan 01: The SQLite Fingerprint Store Summary

**Replaced the JSON-backed, lock-around-I/O fingerprint store with a plugin-owned SQLite database — durable per-row commits, lock-free reads, and a pinned `Microsoft.Data.Sqlite.Core` 10.0.11 reference that binds to Jellyfin's own copy.**

## Performance

- **Duration:** 18 min (this continuation session, Task 1 GREEN through Task 2; the RED phase and the checkpoint were a prior session)
- **Started:** 2026-09-20T19:21:00Z (approx., this session)
- **Completed:** 2026-09-20T19:39:36Z
- **Tasks:** 2 of 2 (plus the resolved checkpoint) completed this session; RED committed previously
- **Files modified:** 8 (across this session's two commits)

## Accomplishments

- `EmbyVerifiedPasswords` rewritten over `Microsoft.Data.Sqlite`: `Record` upserts one row per commit, `Matches`/`RecordsAvailable` take no lock of the plugin's own and never call `EnsureInitialized`, and initialization is eager but not negatively cached, so a later `Record` retries after a transient failure.
- `Jellyfin.Plugin.EmbyAuth.csproj` references `Microsoft.Data.Sqlite.Core` 10.0.11 with `<ExcludeAssets>runtime</ExcludeAssets>`; the Phase 3 guard keeping EF Core SQLite out of the plugin project stays intact.
- `PluginServiceRegistrator` derives the database path from `IApplicationPaths.PluginsPath` and the plugin's own assembly file name — the same derivation Jellyfin uses for `BasePlugin.DataFolderPath`.
- `EmbyAuthPlugin.OnUninstalling` clears pooled SQLite connections before unload, matching Jellyfin's own provider.
- Four new unit tests cover the SQLite-specific failure modes that replace the retired JSON-specific ones, and `CLAUDE.md`'s version-bump rule now names four pins.
- Proved end-to-end inside the running Jellyfin container (`bats e2e/30-migration-modes.bats`, 7/7), and confirmed the publish output holds exactly one DLL with no bundled SQLite assembly.

## Task Commits

1. **Checkpoint: Decision — the fingerprint store moves to SQLite** — resolved `proceed` (prior session, not a commit)
2. **Task 1 RED: rewrite the fingerprint store tests for SQLite** - `bc29bd6` (test)
3. **Task 1 GREEN: rewrite the fingerprint store over SQLite** - `aaeb34e` (feat)
4. **Task 2: cover the SQLite store's failure modes, add the fourth pin** - `d795794` (test)

**Plan metadata:** pending (this commit)

## Files Created/Modified

- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` - SQLite-backed store: `Record`, `Matches`, `RecordsAvailable`, `EnsureInitialized`, `Open`, three reworded/new `[LoggerMessage]` partials
- `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` - `VerifiedPasswordsDatabaseFileName` and the `PluginsPath`-derived database path
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs` - `OnUninstalling` override clearing pooled SQLite connections
- `src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj` - `Microsoft.Data.Sqlite.Core` 10.0.11 reference (committed in the RED phase)
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` - rewritten suite (unskipped) plus four new SQLite failure-mode tests
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs` - deviation: byte-safe fingerprint-database round-trip
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs` - deviation: construct-then-corrupt reordering so the single-log-entry assertion still proves what it always proved
- `CLAUDE.md` - version-bump rule now names four pins, including `Microsoft.Data.Sqlite.Core`

## Decisions Made

- **Checkpoint resolved `proceed`** (prior session): the fingerprint store moves to SQLite, one-way for data written after the upgrade; the plugin project gains a pinned `Microsoft.Data.Sqlite.Core` reference. This is recorded here per the continuation instructions; it was not re-raised.
- **`SchemaVersion` needed a real use to compile.** Task 1's action text declares `private const int SchemaVersion = 1;` but never states where it is read. An unused private const fails the build under `CA1823` (`AnalysisMode` `AllEnabledByDefault`, warnings as errors). Used it to set `PRAGMA user_version = 1;` during `EnsureInitialized` — a standard, forward-compatible SQLite idiom for schema versioning that nothing currently reads back. `CA2100` (SQL built from a string) was then suppressed inline with a `Justification` comment, since `SchemaVersion` is a compile-time constant and `PRAGMA` does not accept a bound parameter.
- **Break-then-restore, not RED-then-GREEN, proves this task's tests** (see TDD Discipline below). The human explicitly directed this before this continuation began, and it is recorded honestly rather than described as a genuine red.

## TDD Discipline

**Task 1 RED did not fail against the pre-rewrite code — this is expected, not a gap.** A prior session unskipped all 14 rewritten tests and ran them against the unmodified JSON-backed store: all 14 passed, because this is a storage-engine swap behind an unchanged public API. Nothing in the `<behavior>` block is false before the rewrite and true after it. Per the human's direction, red was proven instead by breaking a guard in the *finished* SQLite implementation, confirming the specific test goes red, then restoring the file exactly. Verified with `git diff --stat` against the last commit (empty) after every restore, and a full green run (`dotnet test`, 198/198) at the end of each break-restore cycle group.

| Test | Guard broken | Break | Result | Restored |
|---|---|---|---|---|
| `Record_ReplacesTheEarlierHash` | upsert overwrite | Dropped `ON CONFLICT(UserId) DO UPDATE ...` from the `INSERT` in `Record` | `Assert.False(store.Matches(userId, HashB))` failed: `Expected: False, Actual: True` — the second write silently no-op'd against the unique-key conflict instead of overwriting | Yes, `git diff` empty |
| `Fingerprint_IsCaseSensitive` | case-sensitive hashing | First tried `COLLATE NOCASE` on the `Matches` SQL — **no effect**, because the test compares two SHA-256 digests of differently-cased *input*, not two case-variant digests of the same value, so SQL collation was the wrong guard; reverted that no-op, then broke `Fingerprint()` by calling `passwordHash.ToUpperInvariant()` before hashing | `Assert.False(store.Matches(userId, differentCaseHash))` failed: `Expected: False, Actual: True` | Yes, `git diff` empty |
| `Records_SurviveARestart` | cross-instance durability (why the test seam is a temp file, not `:memory:`) | Changed the connection string's `DataSource` to `:memory:` | Failed as expected, along with 5 other tests that also depend on a real file (each `Open()` call gets an isolated blank in-memory database) — `Records_SurviveARestart`: `Expected: True, Actual: False` | Yes, `git diff` empty |

**Task 2's four new tests, each proven the same way** (per Task 2's own acceptance criterion, recorded here for each):

| Test | Guard broken | Line/change | Went red | Restored |
|---|---|---|---|---|
| `StoreThatCannotBeOpened_MatchesNothing_AndLogsOneError` | `EnsureInitialized`'s catch | Dropped `IOException` from `catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)` | Yes — the constructor threw `System.IO.IOException` uncaught instead of returning gracefully | Yes, `git diff` empty |
| `RecordsAvailable_IsFalse_WhenTheDatabaseFileIsNotADatabase` | `RecordsAvailable`'s catch | Removed the `try/catch` around `RecordsAvailable`'s body | Yes — `Microsoft.Data.Sqlite.SqliteException: SQLite Error 26: 'file is not a database'` escaped uncaught | Yes, `git diff` empty |
| `Record_LogsOneError_AndDoesNotThrow_WhenTheDatabaseFileIsNotADatabase` | `Record`'s catch | Removed the `try/catch` around `Record`'s body | Yes — the same `SqliteException` escaped uncaught instead of being logged and swallowed | Yes, `git diff` empty |
| `NoLogEntryNamesAHashOrAFingerprint` | never log a secret | Changed `LogWriteFailed(_logger, ex, _databasePath)` to `LogWriteFailed(_logger, ex, fingerprint)` in `Record`'s catch | Yes — `Assert.DoesNotContain` failed: `Filter matched in collection`, the fingerprint appeared in the captured log entry | Yes, `git diff` empty |

Every restore was confirmed with `git diff --stat` against the last commit showing no output, and the full 198-test suite was green immediately before each task's commit.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug, outside `files_modified`, human-approved before this continuation] `EmbyAuthControllerTests.cs` corrupted the SQLite file via a text round-trip**
- **Found during:** Task 1 GREEN
- **Issue:** `ValidFingerprintFileContents()` read the seeded fingerprint file with `File.ReadAllText` and the calling test wrote it back with `File.WriteAllText`. That is lossless for JSON but corrupting for a SQLite binary file, since non-UTF-8 byte sequences get re-encoded through the text round-trip.
- **Fix:** Switched `ValidFingerprintFileContents()` to `File.ReadAllBytes`/return `byte[]`, the caller to `File.WriteAllBytes`, and added `SqliteConnection.ClearAllPools()` before the overwrite so a pooled connection does not retain stale schema or page-cache state. Also switched `Dispose()` to delete the `-wal`/`-shm` siblings instead of the JSON store's `.tmp` file, and reworded `UnreadableContents` and its doc comment for the SQLite failure shape.
- **Files modified:** `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs`
- **Verification:** `GetMigrationStatus_ClearsRecordsUnavailable_OnceTheFileBecomesReadable` passes; full suite green
- **Committed in:** `aaeb34e` (Task 1 GREEN commit)

**2. [Rule 1 - Bug, outside `files_modified`, discovered this session] `EmbyLoginMethodUsersTests.cs`'s single-log-entry regression test broke under eager construction-time initialization**
- **Found during:** Task 1 GREEN, full-suite run after the SQLite rewrite (1 failure out of 194)
- **Issue:** `ListAsync_NeverMarksNeedsEmbyLogin_AndChecksAvailabilityOnlyOnce_WhenTheFingerprintFileCannotBeRead` wrote corrupt bytes to the file *before* constructing the store, then asserted exactly one log entry to prove `RecordsAvailable()` was called once (not once per user). The new store's constructor also calls `EnsureInitialized()` eagerly, so construction against an already-corrupt file logs once by itself, and the later `RecordsAvailable()` call logs a second time — 2 entries, not 1.
- **Fix:** Reordered the test to construct on a healthy (not-yet-existing) database path first — construction then logs nothing — then corrupt the file (with `SqliteConnection.ClearAllPools()` first) before calling `ListAsync`. The single log entry the test asserts on can now only come from `RecordsAvailable()` itself, restoring the test's original discriminating power (one call vs. three). Also reworded `UnreadableContents` and its doc comment (the old comment named `JsonException`, no longer accurate) and updated `Dispose()` to clear pools and delete `-wal`/`-shm` siblings.
- **Files modified:** `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs`
- **Verification:** `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` — 194/194, then 198/198 after Task 2's additions
- **Committed in:** `aaeb34e` (Task 1 GREEN commit)

---

**Total deviations:** 2 auto-fixed (both Rule 1, both outside this task's declared `files_modified`, both directly caused by the storage-engine swap)
**Impact on plan:** Both fixes were necessary for correctness of pre-existing regression tests; neither changes production behavior. No scope creep beyond what the swap required.

## Issues Encountered

None beyond the deviations above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- The store half of FPRT-04 is complete: records live in a plugin-owned SQLite database, reads take no lock of the plugin's own, and every row commits as it is written. The import half (migrating an existing JSON file's records into the new database) is deliberately not in this plan — plan 04-05 owns it, per this plan's own `<success_criteria>`.
- `FPRT-04` is declared by four plans in this phase (04-01, 04-05, 04-07, 04-08). Per the shared-ID gate, it is not marked `Complete` in `REQUIREMENTS.md` yet — `requirements.ready-ids` confirmed `0/1 requirement(s) ready` at the end of this plan, since the sibling plans have not finished.
- Plan 04-05 (JSON-to-SQLite import) can now build directly on `EmbyVerifiedPasswords`'s new constructor signature, the `VerifiedPasswordsDatabaseFileName` constant, and the `UserId TEXT PRIMARY KEY` / `Fingerprint TEXT` schema this plan created.

---
*Phase: 04-emby-traffic-under-load-and-failure*
*Completed: 2026-09-20*

## Self-Check: PASSED

- All 3 task commits (`bc29bd6`, `aaeb34e`, `d795794`) found in `git log --oneline --all`.
- All 8 key files found on disk.
- `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` — 18/18 passed.
- `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` — 198/198 passed.
- `mise run lint` — clean (dotnet format, shellcheck, shfmt, actionlint, zizmor).
- `bats e2e/30-migration-modes.bats` — 7/7 passed.
- `bats tests/scripts/package.bats` — 8/8 passed.
- `dotnet publish -c Release` — exactly one DLL, no `Microsoft.Data.Sqlite.dll`.
- `docker compose exec jellyfin grep -c 'Microsoft.Data.Sqlite.Core/10.0.11' /jellyfin/jellyfin.deps.json` — non-zero.
- All Task 1 and Task 2 acceptance-criteria greps re-run and passing.
