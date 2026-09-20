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

Filled by the planner once task IDs exist. The requirement-to-test mapping it must satisfy:

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|--------------------|--------------|
| FPRT-04 | Records live in plugin-owned SQLite; reads take no plugin lock; a write commits durably | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` | ⚠ Exists but is written against the file store — must be reworked, not extended |
| FPRT-04 | The plugin loads inside Jellyfin with the host's SQLite | e2e | any `bats e2e/NN-*.bats` that performs a login — a bind failure stops the plugin loading at all | ✅ Existing suite covers it implicitly; make it explicit |
| FPRT-04 | One-time JSON import, not repeated on a later start | unit + e2e | `dotnet test … --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` · new bats case | ❌ W0 — new |
| AUTH-05 | Sign-out fires whenever a token is readable, including a response with no user name | unit | `dotnet test … --filter "FullyQualifiedName~EmbyClientTests"` | ❌ W0 — the existing theory at `EmbyClientTests.cs:179-192` must split |
| TEST-05 (unit) | Race-loser path refused, no delete attempted | unit | `dotnet test … --filter RefusesTheLogin_WhenCreateUserFailsBecauseJellyfinRejectsTheName` | ✅ Exists — `EmbyAuthenticationProviderTests.cs:196-207` |
| TEST-05 (e2e) | Concurrent first logins: exactly one account per Emby user, no HTTP 500 | e2e | `bats e2e/60-concurrent-logins.bats` | ❌ W0 — new file |
| TEST-06 | Invalid settings saved on a running server: logins refused, Error-level log line | e2e | `bats e2e/70-invalid-settings.bats` | ❌ W0 — new file |
| PERF-01 | Concurrent readers on an expired snapshot send one Emby user list request | unit | `dotnet test … --filter "FullyQualifiedName~EmbyUserDirectoryTests"` | ⚠ File exists; the single-flight case is new |
| PERF-02 | The two remaining costs are named in the docs with the reason each stays | docs gate | not a command — `docs/how-it-works.md` review | ❌ W0 — doc edit |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `Jellyfin.Plugin.EmbyAuth.csproj` — `Microsoft.Data.Sqlite.Core` **10.0.11** with `<ExcludeAssets>runtime</ExcludeAssets>`
- [ ] A unit-test seam for the new store — a temporary-file database or an injected connection factory. `SqliteJellyfinDbContextFactory` (`TestDoubles.cs:329`) is **not** it; that backs `JellyfinDbContext` for `LoginMethodMove.cs:41`
- [ ] `EmbyVerifiedPasswordsTests.cs` — reworked against the SQLite store, including the import case
- [ ] `e2e/60-concurrent-logins.bats` — TEST-05 (e2e half)
- [ ] `e2e/70-invalid-settings.bats` — TEST-06
- [ ] `e2e/helpers.bash` — a `jellyfin_log_contains` helper, reused by TEST-06's four cases
- [ ] An e2e case proving the JSON-to-SQLite upgrade keeps a user ready to move
- [ ] `EmbyUserDirectoryTests.cs` — a concurrent-readers-on-expired-snapshot case counting outgoing requests

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
