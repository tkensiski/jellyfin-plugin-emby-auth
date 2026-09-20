---
phase: "3"
slug: "migration-status-and-target"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-09-19"
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 `4.0.1` (unit), bats `1.14.0` (e2e/script), `node:test` (settings page) |
| **Config file** | `Jellyfin.Plugin.EmbyAuth.slnx`; `global.json` selects Microsoft.Testing.Platform |
| **Quick run command** | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` |
| **Full suite command** | `mise run test`, then `mise run e2e` |
| **Estimated runtime** | ~1 s for the unit run (786 ms measured baseline). `mise run e2e` is Docker-backed and was not measured. |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx`, plus `node --test tests/js` for any settings-page change.
- **After every plan wave:** Run `mise run test` and `mise run e2e`. CLAUDE.md requires an end-to-end test for every change to `src/`, and this phase changes `src/` in most waves — so e2e runs at each wave boundary, not only at the phase gate.
- **Before `/gsd-verify-work`:** Full suite green, including the new migration-target e2e file.
- **Max feedback latency:** ~1 s for the per-task unit run.

---

## Per-Task Verification Map

Task IDs are assigned by the planner. Seed rows below carry the requirement-to-command map from `03-RESEARCH.md`; the planner fills `Task ID`, `Plan`, `Wave`, and `Threat Ref`.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD | TBD | TBD | FPRT-01 | — | Write failure never drops the verified-password record | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter EmbyVerifiedPasswordsTests` | ✅ | ⬜ pending |
| TBD | TBD | TBD | FPRT-03 | — | Read failure surfaces as an explicit unavailable state, never as "no users need migrating" | unit + jsdom | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` · `node --test tests/js` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | UI-03 | — | Migration list reflects real task state, not a fixed 3 s guess | jsdom | `node --test tests/js` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | MIGR-01 | TBD | Target login method restricted to methods Jellyfin reports as enabled; anything else refused | unit + e2e | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` · `bats e2e/50-migration-target.bats` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | MIGR-02 | TBD | `EmbyAuthenticationProvider` stays internal | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter EmbyAuthenticationProviderVisibilityTests` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | AUTH-06 | TBD | No-password accounts are named and warned about, never blocked and never given an untypeable password | unit + jsdom + e2e | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` · `node --test tests/js` · `bats e2e/50-migration-target.bats` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | TEST-02 | — | Move classes exercised against a real relational provider | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "LoginMethodMoveTests\|EmbyLoginMethodUsersTests\|EmbyMigrationTaskTests"` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | TEST-03 | — | Controller status, run request, and task state covered | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter EmbyAuthControllerTests` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | DOCS-01 | — | N/A | doc-content | see Manual-Only below | ✅ | ⬜ pending |
| TBD | TBD | TBD | DOCS-05 | — | N/A | doc-content | see Manual-Only below | ❌ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — add the `IJellyfinDatabaseProvider` fake, the SQLite in-memory `JellyfinDbContext` factory, the `ITaskManager` fake, and the `IScheduledTaskWorker` fake; extend `FakeUserManager` with the enabled-login-method list
- [ ] `tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj` — add `Microsoft.EntityFrameworkCore.Sqlite` `10.0.11` (matches the version `Jellyfin.Database.Implementations` `12.1.0` already resolves)
- [ ] `tests/js/testHelpers.js` — mock-timer helpers for the polling tests, if `flush()` alone is not enough
- [ ] `e2e/50-migration-target.bats` — new file for the non-Default target move and the no-password account assertion
- [ ] `e2e/compose.yaml` / `e2e/setup_suite.bash` — JellyfinSecurity plugin mount plus the pinned-zip download and checksum step

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| `docs/how-it-works.md` points to the shutdown step in `docs/migration.md` | DOCS-01 | Prose accuracy, not behavior. The sentence already exists (added in Phase 2) — this is a verification, not new authoring. | Read `docs/how-it-works.md` and confirm the pointer names the shutdown step in `docs/migration.md`. A plain-text `rg` acceptance criterion can automate the presence check, as Phase 2 did for `docs/settings.md`. |
| `docs/how-it-works.md` states the blank-password behavior of Jellyfin's Default login method and connects it to the deleted account after a failed password save, the settings-page warning, and the plugin's interim role | DOCS-05 | Prose accuracy and the correctness of the connection between four separate behaviors cannot be asserted by a command. | Read the section and confirm all four links are stated. An `rg` criterion can check the presence of the statement, not that the connection is correct. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 5 s for the per-task unit run
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
