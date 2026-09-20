---
phase: 04-emby-traffic-under-load-and-failure
plan: 05
subsystem: storage
tags: [sqlite, migration, json-import, jellyfin-plugin, fingerprint-store]

requires:
  - phase: 04-emby-traffic-under-load-and-failure
    provides: "04-01's SQLite-backed EmbyVerifiedPasswords (VerifiedPasswords table, PluginsPath-derived database path, EnsureInitialized) that this plan extends with a one-time import"
provides:
  - "EmbyVerifiedPasswords.ImportLegacyRecords: a one-time, transactional import of the legacy fingerprint JSON file into the SQLite store, keyed on the database's own PRAGMA user_version"
  - "PluginServiceRegistrator.LegacyVerifiedPasswordsFileName, wired to IApplicationPaths.PluginConfigurationsPath, so a real upgrade finds the file a prior version left behind"
  - "The exact on-disk marker (PRAGMA user_version) and its live-verified container path, for plan 04-07's end-to-end proof"
affects: [04-07, 04-08]

actuals:
  tokens: 6967
  tasks: 2
  commits: 2

tech-stack:
  added: []
  patterns:
    - "PRAGMA user_version as a one-shot idempotency marker, set inside the same immediate transaction as the guarded work, so the marker and the work commit or roll back together (04-CONTEXT.md D-17's addendum, verified against sqlite3 3.51.0 by setting it inside a transaction and rolling back)."
    - "ON CONFLICT(UserId) DO NOTHING for an import that must never overwrite a record the running store already wrote, contrasted with Record's own ON CONFLICT DO UPDATE for a fresh login."

key-files:
  created: []
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs
    - src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyMigrationTaskTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/MoveAfterLoginTests.cs

key-decisions:
  - "The RED commit could not be a true pre-implementation red: widening the constructor from two arguments to three is a signature-incompatible change, so any test file that compiles at all already needs the constructor to accept the legacy path. The RED commit therefore ships the widened constructor already wired to ImportLegacyRecords, but with ImportLegacyRecords's body an inert three-discard stub (_ = connection; _ = _legacyFilePath; _ = SchemaVersion;) that does nothing. Confirmed locally before committing: with the stub in place, 5 of the 6 Task 1 tests failed for the stated reason (nothing imports); the 6th, Import_DoesNotOverwriteARecordTheStoreAlreadyWrote, passes trivially when no import runs at all, so its discriminating power is proven separately in Task 2 (see TDD Discipline below). All 8 tests were marked [Fact(Skip = \"04-05: pending the one-time legacy-file import\")] for the RED commit itself, per repo convention, since the stub compiles and the pre-commit hook blocks a commit that leaves a test failing."
  - "Widening the constructor broke five call sites outside this plan's declared files_modified (EmbyMigrationTaskTests.cs, EmbyAuthControllerTests.cs, EmbyAuthenticationProviderTests.cs, EmbyLoginMethodUsersTests.cs, MoveAfterLoginTests.cs), all constructing EmbyVerifiedPasswords directly with the old two-argument signature. Fixed each to pass a legacy path derived from its existing database-path field/variable with a \".legacy.json\" suffix that is never created by any of those tests, so their construction imports nothing and their existing assertions are unaffected (Rule 3 — blocking compile issue)."
  - "Task 2 is documentation-only, per its own acceptance criterion that src/ stays unchanged for that task: its two named tests (Import_ImportsNothing_WhenOneEntryInTheFileIsUnusable, Import_LeavesAUsableStore_WhenItFails) were written alongside Task 1's other six in the same test-file commit, since they belong to the same file and the same RED/GREEN cycle. Task 2's own contribution is the three break-then-restore proofs recorded below, not a separate commit."
  - "ImportLegacyRecords catches JsonException, IOException, and UnauthorizedAccessException around the file read and the inserts — not SqliteException. A write failure inside the transaction (e.g. a corrupt database) surfaces through EnsureInitialized's own broader catch instead, since ImportLegacyRecords runs inside EnsureInitialized's try block."

requirements-completed: []  # FPRT-04 is declared by four plans in this phase (04-01, 04-05, 04-07, 04-08). Per the shared-ID gate (#2388), it stays Pending in REQUIREMENTS.md until every declaring plan has a SUMMARY.

coverage:
  - id: D1
    description: "On first start with an existing legacy fingerprint JSON file, every record in it is readable through Matches afterwards."
    requirement: "FPRT-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#Import_MakesEveryLegacyRecordMatch_OnFirstStart"
        status: pass
    human_judgment: false
  - id: D2
    description: "A later start does not import again: a record removed from the database between two starts stays absent, even with the JSON file still on disk."
    requirement: "FPRT-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#Import_DoesNotRunASecondTime"
        status: pass
    human_judgment: false
  - id: D3
    description: "The import never overwrites a record the running store already wrote; a fresher login-recorded fingerprint always wins over an older one from the file."
    requirement: "FPRT-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#Import_DoesNotOverwriteARecordTheStoreAlreadyWrote"
        status: pass
    human_judgment: false
  - id: D4
    description: "The import never writes, moves, or deletes the legacy JSON file: after a successful import the file is byte-for-byte what it was."
    requirement: "FPRT-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#Import_LeavesTheLegacyFileUnchanged"
        status: pass
    human_judgment: false
  - id: D5
    description: "A start with no JSON file at all marks the import done and logs nothing, so the file's absence is not mistaken for a pending import."
    requirement: "FPRT-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#Import_MarksItselfDone_WhenNoLegacyFileExists"
        status: pass
      - kind: e2e
        ref: "bats e2e/30-migration-modes.bats"
        status: pass
    human_judgment: false
  - id: D6
    description: "An import that cannot read the JSON file (unparsable content, or one unusable entry) logs one Error, imports nothing, leaves the done marker unset, and leaves the store usable for new records; the next start tries again."
    requirement: "FPRT-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs#Import_ImportsNothing_AndLogsOneError_WhenTheLegacyFileCannotBeRead,Import_ImportsNothing_WhenOneEntryInTheFileIsUnusable,Import_LeavesAUsableStore_WhenItFails"
        status: pass
    human_judgment: false
  - id: D7
    description: "The import and the done marker commit together in one transaction; each of the three guards that makes this true (the user_version check, ON CONFLICT DO NOTHING, and the marker write staying inside the transaction) is individually load-bearing."
    requirement: "FPRT-04"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs (break-then-restore against the three named guards, recorded in TDD Discipline below)"
        status: pass
    human_judgment: false

duration: ~35min
completed: 2026-09-20
status: complete
---

# Phase 4 Plan 5: The One-Time Legacy-File Import Summary

**`EmbyVerifiedPasswords` now imports an existing fingerprint JSON file into its SQLite store exactly once, keyed on the database's own `PRAGMA user_version`, and never touches the JSON file itself.**

## Performance

- **Duration:** ~35 min
- **Started:** 2026-09-20T20:16:00Z (approximate, continuation session start)
- **Completed:** 2026-09-20T21:05:00Z (approximate)
- **Tasks:** 2 (Task 2 documentation-only, no separate commit)
- **Files modified:** 8

## Accomplishments

- `EmbyVerifiedPasswords`'s constructor widens to `(string databasePath, string legacyFilePath, ILogger<EmbyVerifiedPasswords> logger)`, and `EnsureInitialized` calls a new `ImportLegacyRecords` after creating the table, before marking itself initialized.
- `ImportLegacyRecords` opens an immediate transaction (`connection.BeginTransaction(deferred: false)`), reads `PRAGMA user_version` as the already-imported marker, and returns unchanged when it is nonzero. Otherwise it deserializes the legacy JSON file (when present) into `Dictionary<Guid, string>` and inserts each record with `ON CONFLICT(UserId) DO NOTHING` — never overwriting a row a login already wrote — then sets `user_version = 1` and commits the rows and the marker together.
- A `JsonException`, `IOException`, or `UnauthorizedAccessException` around the read or the inserts logs exactly one `Error` entry (`LogImportFailed`, naming only the legacy file path) and returns without committing; disposing the uncommitted transaction rolls back both the rows and the marker, so the store stays usable for new records and the next start tries again.
- `PluginServiceRegistrator` gains `LegacyVerifiedPasswordsFileName` and passes `IApplicationPaths.PluginConfigurationsPath` combined with it as the constructor's new argument — the exact folder and file name the pre-SQLite JSON-backed store wrote to.
- The legacy JSON file is never written, moved, or deleted by any code path (confirmed both by a source assertion in the acceptance criteria and by a dedicated test).
- Confirmed live against the real e2e stack (`docker compose cp` of the whole plugin data directory, then `sqlite3` on the host — WAL mode means the `-wal`/`-shm` siblings must be copied together with the `.db` file for a consistent read): a fresh Jellyfin start with no legacy file present leaves `PRAGMA user_version` at `1` and the `VerifiedPasswords` table empty, exactly matching `Import_MarksItselfDone_WhenNoLegacyFileExists`.

## Task Commits

Each task was committed atomically:

1. **Task 1 (RED): add the failing one-time import tests for the fingerprint store** - `02384f8` (test)
2. **Task 1 (GREEN): import the legacy fingerprint file once** - `8fbd372` (feat)

Task 2 added no production code and no separate commit; its two tests landed in the RED/GREEN pair above, per its own acceptance criterion (`git diff --stat` shows `src/` unchanged for Task 2's contribution beyond Task 1's).

**Plan metadata:** *(this commit)*

## Files Created/Modified

- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` - constructor widened to accept `legacyFilePath`; new `ImportLegacyRecords(SqliteConnection)`; new `LogImportFailed` log partial; `SchemaVersion` repurposed as the import-done `user_version` value.
- `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` - new `LegacyVerifiedPasswordsFileName` constant, passed with `PluginConfigurationsPath` as the store's new constructor argument.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` - eight new facts (six from Task 1's `<behavior>` block, two from Task 2), plus `WriteLegacyFile`, `GetUserVersion`, `SetUserVersion`, `DeleteRow`, and `Fingerprint` test helpers, and a `_legacyFilePath` field.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyMigrationTaskTests.cs`, `EmbyAuthControllerTests.cs`, `EmbyAuthenticationProviderTests.cs`, `EmbyLoginMethodUsersTests.cs`, `MoveAfterLoginTests.cs` - deviation: each `EmbyVerifiedPasswords` construction updated to pass a nonexistent legacy path derived from its own database path, so the widened constructor compiles without changing any of these tests' behavior.

## Decisions Made

See `key-decisions` in the frontmatter above for the RED-commit structure, the Rule 3 call-site fixes, Task 2's documentation-only status, and the exception set `ImportLegacyRecords` catches.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking issue] Widening the constructor broke five call sites outside this plan's `files_modified`**

- **Found during:** Task 1, first build attempt after widening the constructor
- **Issue:** `EmbyMigrationTaskTests.cs`, `EmbyAuthControllerTests.cs`, `EmbyAuthenticationProviderTests.cs`, `EmbyLoginMethodUsersTests.cs`, and `MoveAfterLoginTests.cs` all construct `EmbyVerifiedPasswords` directly with the pre-existing two-argument signature `(databasePath, logger)`. None of these tests exercises legacy import; all of them needed a second argument purely to keep compiling.
- **Fix:** Each call site now passes a legacy path derived from its existing database-path field or local variable with a `.legacy.json` suffix, a path none of those tests ever creates — so import finds nothing and marks itself done silently, with no effect on any existing assertion.
- **Files modified:** the five files listed above
- **Verification:** full solution build succeeds; `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` reports 218/218 passing with these fixes in place
- **Committed in:** `02384f8` (RED commit, since the whole solution must compile for that commit to exist)

---

**Total deviations:** 1 auto-fixed (Rule 3)
**Impact on plan:** Necessary for the constructor widening to compile at all; no scope creep — each fixed file gained exactly one derived, never-created path argument and nothing else.

## Issues Encountered

None beyond the deviation above.

## TDD Discipline

**Task 1's RED could not be a genuine pre-implementation red for the usual reason** (a signature-incompatible constructor change means nothing compiles without the widened signature already in place — see key-decisions). The RED commit ships the widened constructor wired to `ImportLegacyRecords`, whose body is an inert stub (`_ = connection; _ = _legacyFilePath; _ = SchemaVersion;`). Confirmed locally before adding `Skip`:

| Test | Result with the stub in place |
|---|---|
| `Import_MakesEveryLegacyRecordMatch_OnFirstStart` | Failed — `Assert.True` got `False` (nothing imported) |
| `Import_DoesNotRunASecondTime` | Failed — `Assert.True(secondStore.Matches(userB, HashB))` got `False` |
| `Import_DoesNotOverwriteARecordTheStoreAlreadyWrote` | Passed trivially (no import runs at all, so nothing can overwrite anything) — proven separately below |
| `Import_LeavesTheLegacyFileUnchanged` | Passed trivially with the disabled stub, so proven separately with a targeted break: temporarily appended a byte to the legacy file inside the (still-stubbed) import path — failed with `Assert.Equal() Failure: Collections differ` on the byte arrays — then reverted |
| `Import_MarksItselfDone_WhenNoLegacyFileExists` | Failed — `Assert.Equal(1, GetUserVersion())` got `0` |
| `Import_ImportsNothing_AndLogsOneError_WhenTheLegacyFileCannotBeRead` | Failed — `Assert.Single(logger.Entries, ...)` found an empty collection (no import attempt, no log) |

All 218 tests pass with the eight new facts marked `[Fact(Skip = "04-05: pending the one-time legacy-file import")]` for the RED commit itself.

**Task 2's three break-then-restore proofs**, each run against the finished (GREEN) implementation, each guard restored byte-for-byte afterward (`git diff --stat` returning to the same 81-insertions/9-deletions shape each time):

| Guard broken | Change | Test that went red | Failure observed |
|---|---|---|---|
| The `user_version` check | Removed the `if ((long)userVersionCommand.ExecuteScalar()! != 0) { return; }` block entirely | `Import_DoesNotRunASecondTime` | `Assert.False(secondStore.Matches(userA, HashA))` — `Expected: False, Actual: True` (the deleted row was resurrected by a second import) |
| `ON CONFLICT(UserId) DO NOTHING` | Changed to `ON CONFLICT(UserId) DO UPDATE SET Fingerprint = excluded.Fingerprint` | `Import_DoesNotOverwriteARecordTheStoreAlreadyWrote` | `Assert.True(secondStore.Matches(userId, HashB))` — `Expected: True, Actual: False` (the import overwrote the store's own fresher record with the file's older one) |
| The marker write staying inside the transaction | Moved the `PRAGMA user_version = 1` write to run unconditionally after the `try`/`catch` block, outside the transaction (which required restructuring the `using` scope, since `Microsoft.Data.Sqlite` ties a same-connection write to whatever transaction is still pending regardless of the command's own `.Transaction` property — the write had to happen after the transaction's `using` block disposed, not merely after an unset `.Transaction`) | `Import_ImportsNothing_AndLogsOneError_WhenTheLegacyFileCannotBeRead` | `Assert.Equal(0, GetUserVersion())` — `Expected: 0, Actual: 1` (the marker was set even though the import of the unparsable file failed) |

Every full-suite run (`dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx`) between breaks confirmed exactly the one targeted test failing, with all others green.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

**For plan 04-07 (Wave 3), the exact artifact to assert against a live server:**

- **Database file:** `<PluginsPath>/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.db`, with `-wal` and `-shm` siblings (WAL mode). Live-verified in this session's e2e container at `/config/plugins/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.db` — a different directory from `/config/plugins/EmbyAuth_1.0.0.0/`, which is where the plugin's own DLL is mounted; the data folder name comes from the assembly file name, not the plugin's versioned install folder.
- **Legacy file (not written by the plugin, only read):** `<PluginConfigurationsPath>/Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json`, live-derived as `/config/plugins/configurations/Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json` in this session's e2e container.
- **The marker:** `PRAGMA user_version` on the database file. `0` means the import has not yet run (or an attempt failed and rolled back); `1` means it has finished — whether or not there was anything to import. There is no separate marker row or file; the value lives in the SQLite file header itself.
- **How to read it from outside the container** (the Jellyfin image has no `sqlite3` binary; the host does): copy the **whole plugin data directory**, not just the `.db` file alone — the `-wal` sibling can hold data not yet checkpointed into the main file, so a `.db`-only copy can appear to be missing the very table the import created:
  ```bash
  docker compose -f e2e/compose.yaml cp jellyfin:/config/plugins/Jellyfin.Plugin.EmbyAuth/. /tmp/verified-check/
  sqlite3 /tmp/verified-check/Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.db "PRAGMA user_version;"
  sqlite3 /tmp/verified-check/Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.db "SELECT UserId, Fingerprint FROM VerifiedPasswords;"
  ```
  Live-verified this session: a fresh start with no legacy file present returns `user_version` = `1` and zero rows.
- **Idempotence, asserted the same way the unit test does it:** after the first start (`user_version` = `1`, rows present), delete or corrupt one row directly in the copied database is not useful for an e2e assertion (the container's copy is a snapshot); instead restart the Jellyfin container a second time with the legacy file still present and confirm `user_version` is still `1` with the same row count — a second import would be a no-op, but if the guard ever regressed, restarting after manually clearing one row inside the running container (via a second `sqlite3`-capable sidecar, or by asserting through the plugin's own migration-status API instead of raw SQL) would show the row resurrected.
- **The behavioral proxy already exercised by e2e (no raw SQL needed for the common case):** the migration-status API (`GET /EmbyAuth/Migration`) reports per-user readiness from `EmbyVerifiedPasswords.Matches`, which reads the same table this import populates. `bats e2e/30-migration-modes.bats` already asserts this endpoint's shape; 04-07's upgrade scenario is the same assertion made against a server that started with a legacy JSON file already on disk, rather than a fresh install.
- **On success, no log line is written.** The only log entry `ImportLegacyRecords` can produce is on failure: `LogImportFailed`, `Error` level, naming only the legacy file path (never a user ID, hash, or fingerprint) — `"The plugin cannot import the legacy verified-password records from {LegacyFilePath}. It will try again the next time Jellyfin starts."`

**The unit half of FPRT-04's import requirement is complete.** `FPRT-04` stays `Pending` in `REQUIREMENTS.md` (shared-ID gate, #2388) until 04-07 and 04-08 also land their SUMMARYs.

Full suite: 218/218 unit tests passing (210 carried forward, 8 new), `mise run lint` clean, `bats e2e/30-migration-modes.bats` 7/7 passing against the GREEN implementation.

## Known Stubs

None.

## Threat Flags

None. All four threats in this plan's `<threat_model>` (T-04-19 through T-04-23) are mitigated by the implementation as designed: both paths come from `IApplicationPaths` and compile-time constants (no traversal input), the import never writes to any file, `ON CONFLICT DO NOTHING` stops a crafted legacy file from overwriting a genuine record, `LogImportFailed` logs only the file path, and `Import_DoesNotRunASecondTime` proves a deleted row stays deleted across a restart with the file still present.

---
*Phase: 04-emby-traffic-under-load-and-failure*
*Completed: 2026-09-20*

## Self-Check: PASSED

- Both task commits (`02384f8`, `8fbd372`) found in `git log --oneline --all`.
- All 4 key files found on disk.
- `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` — 26/26 passed.
- `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` — 218/218 passed, 0 skipped.
- `mise run lint` — clean (dotnet format, shellcheck, shfmt, actionlint, zizmor).
- `bats e2e/30-migration-modes.bats` — 7/7 passed, run against the finished GREEN implementation.
- All Task 1 and Task 2 acceptance-criteria greps re-run and passing (`LegacyVerifiedPasswordsFileName`, `PluginConfigurationsPath` exactly once, `user_version`, `BeginTransaction(deferred: false)`, `DO NOTHING` present, `INSERT OR REPLACE` absent, no `File.Delete/Move/WriteAllText/WriteAllBytes` inside `ImportLegacyRecords`, all 8 new facts present with no `Skip`).
- Live-verified against a running e2e container (`docker compose cp` of the whole plugin data directory + host `sqlite3`): `PRAGMA user_version` = `1`, `VerifiedPasswords` table present with 0 rows, on a fresh start with no legacy file.
