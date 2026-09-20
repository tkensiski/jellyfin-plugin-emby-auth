---
phase: "04"
slug: "emby-traffic-under-load-and-failure"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: validated
nyquist_compliant: false
wave_0_complete: true
created: "2026-09-20"
validated: "2026-09-20"
---

# Phase 04 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

Seeded from `04-RESEARCH.md` § Validation Architecture, then rewritten on 2026-09-20 when the load test was cut and the SQLite store swap became the lead work. No k6, no toxiproxy, no load-test task. The per-task map is filled once plans exist; every other section is decided.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 (`xunit.v3` 4.0.1) on Microsoft.Testing.Platform — C# unit tests; bats 1.14.0 — e2e; `node:test` + jsdom — settings page |
| **Config file** | `global.json` (test runner selection); `.mise.toml` (tool pins). No new tool is pinned by this phase |
| **Quick run command** | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` (unit) · `bats e2e/NN-topic.bats` (one e2e file) |
| **Full suite command** | `mise run test` (unit + script + JS) · `mise run e2e` (all e2e) |
| **Estimated runtime** | unit ~seconds · `mise run e2e` minutes |

---

## Sampling Rate

- **After every task commit:** `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` for C# changes; the single relevant `bats e2e/NN-*.bats` file for e2e changes.
- **After every plan wave:** `mise run test` and `mise run e2e`.
- **Before `/gsd-verify-work`:** `mise run test` and `mise run e2e` green.
- **Max feedback latency:** under 60s for unit-level sampling.

---

## Per-Task Verification Map

Filled by the planner on 2026-09-20, once the eight plans existed. Every requirement below is owned by a named plan and task.

| Req ID | Behavior | Test Type | Automated Command | Owner | Status |
|--------|----------|-----------|--------------------|-------|--------|
| FPRT-04 | Records live in plugin-owned SQLite; reads take no plugin lock; a write commits durably | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` | 04-01 T1 | ✅ 26/26 |
| FPRT-04 | SQLite-specific failure modes: unopenable store, corrupt file, failed write on the login path | unit | `dotnet test … --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` | 04-01 T2 | ✅ 26/26 |
| FPRT-04 | The plugin loads inside Jellyfin with the host's SQLite, and one login's record round-trips | e2e | `bats e2e/30-migration-modes.bats` | 04-01 T1 | ✅ 7/7 |
| FPRT-04 | One-time JSON import, not repeated on a later start | unit | `dotnet test … --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` | 04-05 T1, T2 | ✅ 26/26 |
| FPRT-04 | The same import proven against the real server, including the start that must not import again | e2e | `bats e2e/80-fingerprint-store.bats` | 04-07 T2 | ✅ 2/2 |
| AUTH-05 | Sign-out fires whenever a token is readable, including a response with no user name | unit | `dotnet test … --filter "FullyQualifiedName~EmbyClientTests"` | 04-02 T1 | ✅ 27/27 |
| AUTH-05 | The sign-out names a user in every log entry, and never fires without a token | unit | `dotnet test … --filter "FullyQualifiedName~EmbyClientTests"` | 04-02 T2 | ✅ 27/27 |
| AUTH-05 | The unreadable-response limit is stated rather than claimed away | docs gate | `rg -q 'cannot end' docs/how-it-works.md` | 04-08 T1 | ✅ pass |
| TEST-05 (unit) | Race-loser path refused, no delete attempted, one reworded Error entry | unit | `dotnet test … --filter "FullyQualifiedName~EmbyAuthenticationProviderTests"` | 04-04 T1 | ✅ 34/34 |
| TEST-05 (unit) | A lone first login creates exactly one account; the winner's account shape is pinned | unit | `dotnet test … --filter "FullyQualifiedName~EmbyAuthenticationProviderTests"` | 04-04 T2 | ✅ 34/34 |
| TEST-05 (e2e) | Concurrent first logins: exactly one account per Emby user, no HTTP 500 | e2e | `bats e2e/60-concurrent-logins.bats` | 04-06 T2 | ✅ 3/3 |
| TEST-06 | Invalid settings saved on a running server: logins refused, Error-level log line, four cases | e2e | `bats e2e/70-invalid-settings.bats` | 04-06 T3 | ✅ 4/4 |
| PERF-01 | Concurrent readers on an expired snapshot send one Emby user list request | unit | `dotnet test … --filter "FullyQualifiedName~EmbyUserDirectoryTests"` | 04-03 T2 | ✅ 20/20 |
| PERF-01 | The stub handler can be driven concurrently and held open | unit | `dotnet test … --filter "FullyQualifiedName~TestDoublesTests"` | 04-03 T1 | ✅ 7/7 |
| PERF-01 | The cache-expiry boundary and the fast path are unchanged by the guard | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` | 04-03 T3 | ✅ 218/218 |
| PERF-02 | The two remaining costs are named in the docs with the reason each stays | docs gate | `rg -q 'inside a lock' docs/how-it-works.md` and `test ! -e docs/performance.md` | 04-08 T2 | ✅ pass |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Each item names the plan that creates it.

- [x] `Jellyfin.Plugin.EmbyAuth.csproj` — `Microsoft.Data.Sqlite.Core` **10.0.11** with `<ExcludeAssets>runtime</ExcludeAssets>` — **04-01 T1**
- [x] A unit-test seam for the new store: a per-test temporary `.db` file built through the existing `CreateStore(logger?)` helper, deleted with its write-ahead-log siblings in `Dispose`. An in-memory database cannot prove durability across a new store instance, which two of the carried-over tests depend on. `SqliteJellyfinDbContextFactory` (`TestDoubles.cs:329`) is **not** it; that backs `JellyfinDbContext` for `LoginMethodMove.cs:41` — **04-01 T1**
- [x] `EmbyVerifiedPasswordsTests.cs` — reworked against the SQLite store — **04-01 T1**, with the SQLite failure modes in **04-01 T2** and the import cases in **04-05 T1, T2**
- [x] `EmbyClientTests.cs` — the three-row theory split, and the token-bearing no-name fact — **04-02 T1**
- [x] `TestDoubles.cs` — `StubHttpMessageHandler` made thread-safe, with `HoldResponses`, `ReleaseResponses`, and `FirstRequestStarted` — **04-03 T1**
- [x] `EmbyUserDirectoryTests.cs` — concurrent readers on an expired snapshot, counting outgoing requests — **04-03 T2**
- [x] `e2e/setup_suite.bash` — four new Emby users, `bella`, `chris`, `dana`, `elton` — **04-06 T1**
- [x] `e2e/helpers.bash` — `jellyfin_log_lines`, reused by TEST-06's four cases, and `EMBY_INTERNAL_URL` — **04-06 T1**
- [x] `e2e/60-concurrent-logins.bats` — TEST-05 (e2e half) — **04-06 T2**
- [x] `e2e/70-invalid-settings.bats` — TEST-06 — **04-06 T3**
- [x] `e2e/helpers.bash` — the six fingerprint-store helpers that move the database between the stopped container and the host — **04-07 T1**
- [x] `e2e/80-fingerprint-store.bats` — the JSON-to-SQLite upgrade keeps a user ready to move, and a later start does not import again — **04-07 T2**

**Naming note:** the seeded list called the log helper `jellyfin_log_contains`. It ships as `jellyfin_log_lines`, which prints the matching lines so one helper serves both a presence check and the Error-level assertion TEST-06 needs.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| The documented sign-out limit is accurate | AUTH-05 | Prose accuracy about a response the plugin cannot parse | Read the statement in `docs/how-it-works.md` against `EmbyClient.cs`'s post-reorder behavior |
| The two remaining costs are described accurately and the reason each stays is true | PERF-02 | A claim about why no fix applies is a judgement, not an assertion a command can make | Check each against `EmbyAuthenticationProvider.cs:197`, `:233` and the 5-second timeout in `EmbyClient.cs` |
| A real upgrade keeps existing records | FPRT-04 | The e2e case covers the happy path; a real install may hold a file shaped differently | Start a server with a populated fingerprint JSON, upgrade, confirm the migration page is unchanged |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or a Wave 0 dependency
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers every MISSING reference above
- [x] No watch-mode flags
- [x] Feedback latency under 60s for unit-level sampling
- [ ] `nyquist_compliant: true` set in frontmatter — held false while the three Manual-Only rows stand

**Approval:** validated 2026-09-20, partial — every requirement has green automated coverage, three prose-accuracy judgements stay manual.

---

## Validation Audit 2026-09-20

| Metric | Count |
|--------|-------|
| Requirements audited | 6 |
| Map rows green | 16 / 16 |
| Gaps found | 0 |
| Resolved | 0 |
| Escalated | 0 |
| Manual-only remaining | 3 |

Every automated command in the Per-Task Verification Map was run directly during this audit, not inferred from a SUMMARY: `EmbyVerifiedPasswordsTests` 26/26, `EmbyClientTests` 27/27, `EmbyAuthenticationProviderTests` 34/34, `EmbyUserDirectoryTests` 20/20, `TestDoublesTests` 7/7, full unit suite 218/218, full e2e suite 41/41, and all three docs gates pass. Every Wave 0 artifact was confirmed present on disk.

`nyquist_compliant` stays `false` because the Manual-Only table is not empty. That records residual judgement, not missing coverage: all six requirements carry green automated verification, and the three remaining items ask whether prose is *accurate*, which no command asserts. Of the three, only "a real upgrade keeps existing records" needs a human at a server — the e2e case covers the happy path, and a real install may hold a file shaped differently.

One map row changed meaning during this audit. `EmbyVerifiedPasswordsTests.Database_DoesNotContainThePasswordHash` was green but could not fail: it read the database file without clearing the connection pools, so under WAL the row was still in the `-wal` sibling and the assertion passed whatever the store wrote, including a raw password hash. Fixed in `e0efc77` and proven by storing the raw hash and watching the assertion fail. A green row in this table is only as good as the mutation that was run against it.
