---
phase: "04"
phase_name: "The Fingerprint Store, and Emby Traffic Under Failure"
project: "Jellyfin Emby Auth"
generated: "2026-09-20"
counts:
  decisions: 6
  lessons: 6
  patterns: 6
  surprises: 5
missing_artifacts:
  - "04-UAT.md"
---

# Phase 04 Learnings: The Fingerprint Store, and Emby Traffic Under Failure

`04-UAT.md` does not exist because verification passed with no `human_verification` items, so `/gsd-verify-work` was never required.

## Decisions

### The fingerprint records move to SQLite, one-way for data
The records move from `Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json` to a plugin-owned SQLite database in the plugin data folder. A server that has started the new version holds a `.db` file no earlier build can read.

**Rationale:** The JSON store rewrote every record on each write, holding a lock across disk I/O, and kept a failed record in memory until restart. SQLite commits each record durably as it is written, so no accepted login depends on a later write. The cost was accepted knowingly: records written after the upgrade are lost on a rollback, and affected users show "Needs an Emby login" until they log in through Emby once more.
**Source:** 04-CONTEXT.md D-14, 04-01-PLAN.md checkpoint task, answered `proceed` by the human

### `Microsoft.Data.Sqlite.Core`, not EF Core, pinned to the host's version
The plugin project takes `Microsoft.Data.Sqlite.Core` at exactly 10.0.11 with `<ExcludeAssets>runtime</ExcludeAssets>`, and does not take `Microsoft.EntityFrameworkCore.Sqlite` or `SQLitePCLRaw.bundle_e_sqlite3`.

**Rationale:** The store runs about three SQL statements, so an object-relational mapper would be machinery out of proportion to the job, and Phase 3's guard keeping EF Core out of the plugin project survives intact. Excluding runtime assets makes the plugin bind to the copy Jellyfin already loaded. 10.0.11 is the version inside the pinned `jellyfin/jellyfin:12.1.20260915-010956` image; a higher version makes .NET refuse the bind and the plugin fails to load with `FileLoadException`. The bundle package would ship a native binary beside the plugin DLL and break the one-DLL publish output.
**Source:** 04-CONTEXT.md D-15, 04-01-PLAN.md Task 1

### The Jellyfin version bump now moves four pins, not three
`CLAUDE.md`'s version-bump rule gained the SQLite pin alongside `Jellyfin.Controller`/`Jellyfin.Model`, the `jellyfin/jellyfin` image tag, and the target framework.

**Rationale:** The SQLite pin is only correct relative to the image it binds against, so it cannot move independently of the image tag.
**Source:** 04-01-PLAN.md Task 2, CLAUDE.md

### Shared cache is never enabled
The connection string leaves `Cache` at its default and never sets `SqliteCacheMode.Shared`.

**Rationale:** `sqlite3_enable_shared_cache` is process-global. Jellyfin's own `SqliteDatabaseProvider` warns that a plugin turning it on makes Jellyfin's connections share a cache and surface `SQLITE_LOCKED`, which a busy timeout does not cover.
**Source:** 04-CONTEXT.md D-16, 04-01-PLAN.md Task 1

### The legacy JSON file is read, never written, moved, or deleted
The one-time import reads the old file and leaves it exactly where it was.

**Rationale:** It is the whole of the rollback path. The human approved the one-way store change on the stated understanding that a rollback still has whatever that file held at upgrade time.
**Source:** 04-01-PLAN.md checkpoint context, 04-05-SUMMARY.md

### `docs/how-it-works.md` gets one owner in the last wave
Three plans changed behavior the document describes, but only 04-08 edits it.

**Rationale:** A single owner in the final wave keeps one file from being edited in three separate waves, which is where conflicting partial rewrites come from.
**Source:** 04-01-PLAN.md `planner_notes`

---

## Lessons

### WAL plus pooling means reading a `.db` file directly proves nothing
Under WAL mode with connection pooling — both enabled by `EmbyVerifiedPasswords` — an inserted row stays in the `-wal` sibling and is not checkpointed into the main `.db` file until the last real connection closes. Any assertion that reads the main file without first calling `SqliteConnection.ClearAllPools()` sees a file that does not contain the row.

**Context:** This bit twice in one phase. First in 04-01, where `EmbyAuthControllerTests` round-tripped the store file through `File.ReadAllText`/`WriteAllText` and corrupted it. Then silently, in `Database_DoesNotContainThePasswordHash`, which passed for a whole phase while asserting nothing — it would have passed had the plugin stored raw password hashes. The same rule applies to pulling the database out of a container: copy the whole plugin data directory, never the single file.
**Source:** 04-01-SUMMARY.md, 04-05-SUMMARY.md, 04-REVIEW.md WR-03, 04-VERIFICATION.md

### A storage-engine swap behind an unchanged API cannot go red the normal way
Every behavior in 04-01's test block already held under the old JSON store, because the public API did not change. Unskipping all 14 rewritten tests against the unmodified store passed 14/14.

**Context:** The red had to come from breaking the finished implementation, not from old code failing new tests. Recording the skip/unskip cycle as if it were a genuine red would have been false.
**Source:** 04-01-SUMMARY.md

### Widening a constructor breaks every call site at once
Adding the legacy-file path to `EmbyVerifiedPasswords`'s constructor broke five test files outside 04-05's declared `files_modified`.

**Context:** The blast radius of a constructor signature is every place the type is built, which a plan's `files_modified` list will not predict. Budget for it rather than treating it as a scope breach.
**Source:** 04-05-SUMMARY.md

### `docker compose exec` refuses a stopped container
The plan's own design — stop Jellyfin, then delete files inside it — could not use plain `exec`, which fails with `service "jellyfin" is not running`.

**Context:** Resolved with `docker run --rm --volumes-from <container_id>` against a throwaway container, which attaches to a stopped container's declared volumes (`/config` is a declared `VOLUME` in the Jellyfin image). Reused the `nginx:1.30.5-alpine` tag already pinned for the Emby proxy so no new dependency entered the repo.
**Source:** 04-07-SUMMARY.md

### A third-party plugin in the e2e stack shapes concurrency results
JellyfinSecurity's `TwoFactorAuthProvider`, mounted for Phase 3's verification, is tried ahead of Default and carries an IP-based app-password rate limiter. Five genuinely concurrent logins from one container IP trip it, and it refuses exactly one with a clean 401.

**Context:** The failure appeared only once a user had already moved to Default, so it surfaced in the second burst and not the first. The e2e stack is not a clean room; an unexplained single failure deserves a look at the live log before it is blamed on the code under test.
**Source:** 04-06-SUMMARY.md

### A new `SemaphoreSlim` field trips CA1001 under warnings-as-errors
Adding the single-flight guard made `EmbyUserDirectory` own a disposable, so the analyzer required `IDisposable`.

**Context:** The DI container already disposes it as a singleton, so implementing `Dispose` changed no wiring. Expect the analyzer to force the interface the moment a disposable field appears.
**Source:** 04-03-SUMMARY.md

---

## Patterns

### Break-then-restore as the red for already-correct behavior
Where an old-code-vs-new-test red is impossible, prove the test by breaking the finished implementation, watching the specific test fail, then restoring and confirming a zero diff against the last commit.

**When to use:** Refactors, storage-engine swaps, and any test written over behavior that already holds. Used across 04-01, 04-03, 04-04, 04-05 and again during verification to confirm WR-03. It is the only thing that distinguishes a passing test from a test that cannot fail.
**Source:** 04-01-SUMMARY.md, 04-03-SUMMARY.md, 04-04-SUMMARY.md, 04-05-SUMMARY.md, 04-VERIFICATION.md

### Gate concurrency tests on a signal the test controls
`StubHttpMessageHandler` gained `HoldResponses()`, `ReleaseResponses()` and `FirstRequestStarted` so a test forces genuine overlap instead of racing timing.

**When to use:** Any assertion about what happens when two operations overlap. No sleeps, no latency assertions, no "long enough to be safe" — a test that passes only sometimes is worse than no test.
**Source:** 04-03-SUMMARY.md, TestDoubles.cs

### Seed the negative case so "it did not happen again" is observable
`80-fingerprint-store.bats` proves the no-second-import guarantee by removing a row from the pulled database, restarting, and requiring the user to stay `NeedsEmbyLogin`. A second import would restore the row and turn the state `Ready`, failing the test.

**When to use:** Any "runs once and never again" or "does not re-run" assertion. Without a seeded observable difference, the test cannot distinguish "did not run again" from "ran again and had nothing to do".
**Source:** 04-07-SUMMARY.md, e2e/80-fingerprint-store.bats

### A precondition guard so a test file cannot pass vacuously
`80-fingerprint-store.bats` asserts the user is `Ready` before the upgrade and states that otherwise "the rest of this file would prove nothing".

**When to use:** Any test whose meaning depends on the starting state being what you think it is.
**Source:** e2e/80-fingerprint-store.bats

### A compile-time red is a legitimate red where a runtime test cannot reach
Removing `SignOutAsync`'s fallback produced `CS8604` rather than a test failure, because nullable reference types catch the regression before a test can run.

**When to use:** When a guard is enforced by the type system rather than at runtime. Record which kind of red was obtained rather than implying a runtime failure. Phase 3's MIGR-02 set the precedent.
**Source:** 04-02-SUMMARY.md

### Write the marker inside the transaction it describes
The one-time import writes `PRAGMA user_version = 1` inside the same `BEGIN IMMEDIATE` transaction as the imported rows.

**When to use:** Any "done" marker for a multi-row operation. A crash partway through then leaves neither half-imported rows nor a marker claiming an import that did not finish.
**Source:** 04-05-SUMMARY.md

---

## Surprises

### The test guarding the phase's most security-relevant property could not fail
`Database_DoesNotContainThePasswordHash` passed throughout the phase while asserting nothing. The code review found it; the verifier confirmed it independently by changing `Record` to store the raw hash and watching the test still pass.

**Impact:** The property it guards does hold — `Record` computes and stores only the fingerprint — so nothing shipped wrong. But a whole phase of TDD discipline, in which every other test was proven by break-then-restore, produced one test that was never mutated because it was written during a RED commit and taken on trust afterwards. Fixed in `e0efc77`.
**Source:** 04-REVIEW.md WR-03, 04-VERIFICATION.md

### A test failed only in the full suite, never standalone
The second bella burst failed deterministically across three full-suite runs and passed 5/5 standalone.

**Impact:** The difference was ordering — the user had already moved to Default by the second burst, routing the login through a provider the standalone run never reached. A test that passes in isolation is not evidence it passes in the suite, and vice versa.
**Source:** 04-06-SUMMARY.md

### Commit signing stopped the phase twice, mid-plan
The 1Password SSH agent failed to sign on two separate occasions, with different errors each time (`failed to fill whole buffer`, then `agent returned an error`), because the biometric prompt expired while the human was away.

**Impact:** Both times the executor had finished and verified its work and could only leave it staged. One of those times the working implementation existed solely in the finished agent's context, not on disk, so recovery meant resuming that agent rather than starting a fresh one. Exercising the credential at the start of a run, rather than at the first commit, would have failed the same way but much earlier.
**Source:** phase 04 execution session, 04-05 dispatch and recovery

### A prior phase's completed requirement became false
`FPRT-01` was marked complete in Phase 3 and read "the record stays in memory until Jellyfin restarts" — behavior this phase deliberately deleted, along with its test and its log wording.

**Impact:** A checked box asserted a guarantee the code no longer made. Replacing a store invalidates the requirements written against the old one, and the traceability ledger does not notice on its own. Restated in `eeb016d`.
**Source:** .planning/REQUIREMENTS.md, 04-01-SUMMARY.md

### The plan's own text was wrong and the docs were right
04-08's `must_haves` said a read failure leaves a user "needing an Emby login". `EmbyLoginMethodUsers.DetermineState` maps a failed read to `MigrationUserState.Unknown`, and `docs/migration.md` already called it "readiness unknown".

**Impact:** The executor wrote the accurate state rather than the plan's paraphrase. A plan is a source of intent, not a source of truth about behavior that already exists.
**Source:** 04-08-SUMMARY.md
