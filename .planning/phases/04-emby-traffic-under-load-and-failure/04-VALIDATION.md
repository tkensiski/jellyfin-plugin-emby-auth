---
phase: "04"
slug: "emby-traffic-under-load-and-failure"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-09-20"
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

| Req ID | Behavior | Test Type | Automated Command | Owner | File Exists? |
|--------|----------|-----------|--------------------|-------|--------------|
| FPRT-04 | Records live in plugin-owned SQLite; reads take no plugin lock; a write commits durably | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` | 04-01 T1 | ⚠ Exists but is written against the file store — reworked, not extended |
| FPRT-04 | SQLite-specific failure modes: unopenable store, corrupt file, failed write on the login path | unit | `dotnet test … --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` | 04-01 T2 | ❌ W0 — new bodies; the JSON-specific failure tests are deleted |
| FPRT-04 | The plugin loads inside Jellyfin with the host's SQLite, and one login's record round-trips | e2e | `bats e2e/30-migration-modes.bats` | 04-01 T1 | ✅ Existing file; it is the tracer's end-to-end verify |
| FPRT-04 | One-time JSON import, not repeated on a later start | unit | `dotnet test … --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` | 04-05 T1, T2 | ❌ W0 — new |
| FPRT-04 | The same import proven against the real server, including the start that must not import again | e2e | `bats e2e/80-fingerprint-store.bats` | 04-07 T2 | ❌ W0 — new file |
| AUTH-05 | Sign-out fires whenever a token is readable, including a response with no user name | unit | `dotnet test … --filter "FullyQualifiedName~EmbyClientTests"` | 04-02 T1 | ❌ W0 — the existing theory at `EmbyClientTests.cs:179-192` splits |
| AUTH-05 | The sign-out names a user in every log entry, and never fires without a token | unit | `dotnet test … --filter "FullyQualifiedName~EmbyClientTests"` | 04-02 T2 | ❌ W0 — new |
| AUTH-05 | The unreadable-response limit is stated rather than claimed away | docs gate | `rg -q 'cannot end' docs/how-it-works.md` | 04-08 T1 | ❌ W0 — doc edit |
| TEST-05 (unit) | Race-loser path refused, no delete attempted, one reworded Error entry | unit | `dotnet test … --filter "FullyQualifiedName~EmbyAuthenticationProviderTests"` | 04-04 T1 | ⚠ Exists at `EmbyAuthenticationProviderTests.cs:196-207`; renamed and extended |
| TEST-05 (unit) | A lone first login creates exactly one account; the winner's account shape is pinned | unit | `dotnet test … --filter "FullyQualifiedName~EmbyAuthenticationProviderTests"` | 04-04 T2 | ❌ W0 — new |
| TEST-05 (e2e) | Concurrent first logins: exactly one account per Emby user, no HTTP 500 | e2e | `bats e2e/60-concurrent-logins.bats` | 04-06 T2 | ❌ W0 — new file |
| TEST-06 | Invalid settings saved on a running server: logins refused, Error-level log line, four cases | e2e | `bats e2e/70-invalid-settings.bats` | 04-06 T3 | ❌ W0 — new file |
| PERF-01 | Concurrent readers on an expired snapshot send one Emby user list request | unit | `dotnet test … --filter "FullyQualifiedName~EmbyUserDirectoryTests"` | 04-03 T2 | ⚠ File exists; the single-flight cases are new |
| PERF-01 | The stub handler can be driven concurrently and held open | unit | `dotnet test … --filter "FullyQualifiedName~TestDoublesTests"` | 04-03 T1 | ❌ W0 — new members and new tests |
| PERF-01 | The cache-expiry boundary and the fast path are unchanged by the guard | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` | 04-03 T3 | ✅ Existing tests must pass with bodies unchanged |
| PERF-02 | The two remaining costs are named in the docs with the reason each stays | docs gate | `rg -q 'inside a lock' docs/how-it-works.md` and `test ! -e docs/performance.md` | 04-08 T2 | ❌ W0 — doc edit |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Each item names the plan that creates it.

- [ ] `Jellyfin.Plugin.EmbyAuth.csproj` — `Microsoft.Data.Sqlite.Core` **10.0.11** with `<ExcludeAssets>runtime</ExcludeAssets>` — **04-01 T1**
- [ ] A unit-test seam for the new store: a per-test temporary `.db` file built through the existing `CreateStore(logger?)` helper, deleted with its write-ahead-log siblings in `Dispose`. An in-memory database cannot prove durability across a new store instance, which two of the carried-over tests depend on. `SqliteJellyfinDbContextFactory` (`TestDoubles.cs:329`) is **not** it; that backs `JellyfinDbContext` for `LoginMethodMove.cs:41` — **04-01 T1**
- [ ] `EmbyVerifiedPasswordsTests.cs` — reworked against the SQLite store — **04-01 T1**, with the SQLite failure modes in **04-01 T2** and the import cases in **04-05 T1, T2**
- [ ] `EmbyClientTests.cs` — the three-row theory split, and the token-bearing no-name fact — **04-02 T1**
- [ ] `TestDoubles.cs` — `StubHttpMessageHandler` made thread-safe, with `HoldResponses`, `ReleaseResponses`, and `FirstRequestStarted` — **04-03 T1**
- [ ] `EmbyUserDirectoryTests.cs` — concurrent readers on an expired snapshot, counting outgoing requests — **04-03 T2**
- [ ] `e2e/setup_suite.bash` — four new Emby users, `bella`, `chris`, `dana`, `elton` — **04-06 T1**
- [ ] `e2e/helpers.bash` — `jellyfin_log_lines`, reused by TEST-06's four cases, and `EMBY_INTERNAL_URL` — **04-06 T1**
- [ ] `e2e/60-concurrent-logins.bats` — TEST-05 (e2e half) — **04-06 T2**
- [ ] `e2e/70-invalid-settings.bats` — TEST-06 — **04-06 T3**
- [ ] `e2e/helpers.bash` — the six fingerprint-store helpers that move the database between the stopped container and the host — **04-07 T1**
- [ ] `e2e/80-fingerprint-store.bats` — the JSON-to-SQLite upgrade keeps a user ready to move, and a later start does not import again — **04-07 T2**

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

- [ ] All tasks have `<automated>` verify or a Wave 0 dependency
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers every MISSING reference above
- [ ] No watch-mode flags
- [ ] Feedback latency under 60s for unit-level sampling
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
