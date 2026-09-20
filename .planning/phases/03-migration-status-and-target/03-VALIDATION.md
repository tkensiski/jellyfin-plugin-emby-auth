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
| 01-T1 | 03-01 | 1 | TEST-02, TEST-03 | T-03-SC | The SQLite seam can run `ExecuteUpdateAsync`, which the EF Core InMemory provider cannot | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~TestDoublesTests"` | ❌ W0 → created by this task | ⬜ pending |
| 01-T2 | 03-01 | 1 | FPRT-03 | T-03-06, T-03-07 | Read failure surfaces as an explicit unavailable state, never as "no users need migrating" | unit + jsdom | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyAuthControllerTests"` · `node --test` | ❌ W0 → created by 01-T1 | ⬜ pending |
| 01-T3 | 03-01 | 1 | TEST-02 | — | Move classes exercised against a real relational provider, before they are renamed | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~DefaultLoginMethodTests\|FullyQualifiedName~EmbyLoginMethodUsersTests"` | ❌ W0 → created by 01-T1 | ⬜ pending |
| 02-T1 | 03-02 | 2 | FPRT-01 | T-03-09 | Write failure never drops the verified-password record, and the log says so | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyVerifiedPasswordsTests"` | ✅ | ⬜ pending |
| 02-T2 | 03-02 | 2 | MIGR-02 | T-03-08 | `EmbyAuthenticationProvider` stays internal, and no type joins the exported surface unrecorded | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~TypeVisibilityTests"` | ❌ W0 | ⬜ pending |
| 02-T3 | 03-02 | 2 | DOCS-01, DOCS-05 | T-03-03 | The documented behaviour matches the code | doc-content | `rg -q -i 'blank password' docs/how-it-works.md` · `test "$(rg -c 'migration\.md#shut-down-emby' docs/how-it-works.md)" = "1"` | ✅ | ⬜ pending |
| 03-T1 | 03-03 | 2 | AUTH-06, FPRT-03 | — | A missing saved password is reported as such whatever the fingerprint file is doing | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyLoginMethodUsersTests"` | ❌ W0 | ⬜ pending |
| 03-T2 | 03-03 | 2 | UI-03, MIGR-01, TEST-03 | T-03-02, T-03-05, T-03-06 | The task state and the enabled-method list are server-derived, behind the elevation policy | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyAuthControllerTests"` | ❌ W0 | ⬜ pending |
| 03-T3 | 03-03 | 2 | AUTH-06 | T-03-10 | Every user-derived string is rendered with textContent | jsdom + e2e | `node --test` · `mise run e2e` | ✅ | ⬜ pending |
| 04-T1 | 03-04 | 3 | MIGR-01 | T-03-01 | A blank target is refused by shape validation without echoing the value | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyAuthSettingsTests"` | ✅ | ⬜ pending |
| 04-T2 | 03-04 | 3 | MIGR-01, TEST-02 | T-03-04, T-03-11, T-03-12 | The verified-password gate and the single-column update survive the generalization | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~LoginMethodMoveTests\|FullyQualifiedName~MoveAfterLoginTests"` | ❌ W0 | ⬜ pending |
| 04-T3 | 03-04 | 3 | MIGR-01, TEST-02, AUTH-06 | T-03-04, T-03-11 | The task moves only ready accounts, to the configured target | unit + e2e | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyMigrationTaskTests"` · `mise run e2e` | ❌ W0 | ⬜ pending |
| 05-T1 | 03-05 | 4 | UI-03 | — | The list reflects real task state, not a fixed 3 s guess, and never stacks requests | jsdom | `node --test` | ✅ | ⬜ pending |
| 05-T2 | 03-05 | 4 | MIGR-01, FPRT-03 | T-03-02, T-03-04, T-03-10 | The dropdown is fed only by the server-filtered list, and nothing is silently selected | jsdom | `node --test` | ✅ | ⬜ pending |
| 05-T3 | 03-05 | 4 | AUTH-06 | T-03-03, T-03-05 | No-password accounts are warned about in wording that claims nothing unverified | jsdom | `node --test` | ✅ | ⬜ pending |
| 06-T1 | 03-06 | 4 | MIGR-01 | T-03-01, T-03-02, T-03-05 | A target Jellyfin does not report as enabled is refused server-side, with nothing written | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~MigrationTargetValidationTests\|FullyQualifiedName~EmbyAuthPluginTests"` | ❌ W0 | ⬜ pending |
| 06-T2 | 03-06 | 4 | MIGR-01, AUTH-06 | T-03-03, T-03-04 | A password set in Jellyfin goes to the password-set target and is never refused | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyAuthenticationProviderTests"` | ✅ | ⬜ pending |
| 07-T1 | 03-07 | 5 | MIGR-01 | T-03-13, T-03-14 | The second login method is installed from a checksum-pinned download and is inert unconfigured | e2e | `mise run e2e` · `scripts/dev-env.sh up && scripts/dev-env.sh down` | ❌ W0 | ⬜ pending |
| 07-T2 | 03-07 | 5 | MIGR-01, AUTH-06 | T-03-01, T-03-03, T-03-06, T-03-15 | A real migration to a non-Default method hands over, and a real server refuses a bad target | e2e | `bats e2e/50-migration-target.bats` | ❌ W0 | ⬜ pending |
| 07-T3 | 03-07 | 5 | MIGR-01, AUTH-06 | — | The documentation matches the shipped settings, task name, and response | doc-content | `rg -q 'Migration target' docs/settings.md README.md` · `! rg -q 'ReadyToMove' docs/ README.md CHANGELOG.md` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — add the `IJellyfinDatabaseProvider` fake, the SQLite in-memory `JellyfinDbContext` factory, the `ITaskManager` fake, and the `IScheduledTaskWorker` fake; extend `FakeUserManager` with the enabled-login-method list — **plan 03-01, task 1**
- [ ] `tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj` — add `Microsoft.EntityFrameworkCore.Sqlite` `10.0.11` (matches the version `Jellyfin.Database.Implementations` `12.1.0` already resolves) — **plan 03-01, task 1**
- [ ] `tests/js/testHelpers.js` — a per-call scripted response for `getJSON`, and a helper pairing `mock.timers.tick` with `flush`, for the polling tests — **plan 03-05, task 1**
- [ ] `e2e/50-migration-target.bats` — new file for the non-Default target move, the no-password account assertion, and the server-side target refusal — **plan 03-07, task 2**
- [ ] `e2e/compose.yaml` / `e2e/setup_suite.bash` / `scripts/fetch-jellyfinsecurity.sh` — JellyfinSecurity plugin mount plus the pinned-zip download and checksum step — **plan 03-07, task 1**

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
