---
phase: "1"
slug: "account-creation-and-login-security"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-09-17"
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 (`xunit.v3` 4.0.1), Microsoft.Testing.Platform runner |
| **Config file** | none dedicated — runner selected by `global.json`; the test project is the config (`OutputType: Exe`) |
| **Quick run command** | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyAuthenticationProviderTests"` |
| **Full suite command** | `mise run test` (dotnet unit tests + `tests/scripts` bats) |
| **E2E command** | `mise run e2e` (needs Docker) |
| **Estimated runtime** | ~4 seconds wall clock for the unit suite |

**Measured baseline:** on 2026-09-17, `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` reported total 90, succeeded 90, failed 0, skipped 0, test duration 1s 125ms, 4.1s wall clock including the incremental build. The bats suites were not timed for this measurement.

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyAuthenticationProviderTests"`
- **After every plan wave:** Run `mise run test`
- **Before `/gsd-verify-work`:** `mise run test` and `mise run e2e` must both be green, and the AUTH-02 removal search must return no match
- **Max feedback latency:** 10 seconds (measured unit-suite wall clock is 4.1s; the 10s budget holds while the suite roughly doubles)

---

## Per-Task Verification Map

Task IDs are assigned by the planner. This table is seeded from the research Requirements → Test map and is completed by `/gsd-validate-phase` once PLAN.md files exist.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD | TBD | 0 | TEST-01 | — | `FakeUserManager` and fake `ICryptoProvider` exist and can construct the provider without `EmbyAuthPlugin.Instance` | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | TEST-01 / AUTH-04 | T-1-DoS | `UpdateUserAsync` throws `DbUpdateException` → login refused, no HTTP 500, account deleted | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyAuthenticationProviderTests"` | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | TEST-01 / AUTH-04 | T-1-DoS | `UpdateUserAsync` fails AND the cleanup `DeleteUserAsync` also fails → login still refused, no HTTP 500 | unit | same command | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | TEST-01 / AUTH-04 | T-1-DoS | `UpdateUserAsync` throws a type outside the old `when` filter (`InvalidOperationException`) → login refused, no HTTP 500 | unit | same command | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | TEST-01 | T-1-Info | Account checks (blank password, disabled, administrator), Emby login, account creation and update — happy paths | unit | same command | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | AUTH-03 | T-1-Race | New account gets the Emby-verified hash and `EmbyAuthenticationProvider.ProviderId` in the `UpdateUserAsync` call directly after `CreateUserAsync` | unit | same command | ❌ W0 | ⬜ pending |
| TBD | TBD | TBD | AUTH-01 | T-1-Bypass | Old Emby password refused at once after a change; new password accepted; saved hash follows | e2e | `bats e2e/30-migration-modes.bats` | ✅ | ⬜ pending |
| TBD | TBD | TBD | AUTH-01 | T-1-Bypass | User with a verified saved hash is refused during an Emby outage | e2e | `bats e2e/40-emby-outage.bats` | ✅ | ⬜ pending |
| TBD | TBD | TBD | AUTH-02 | T-1-Stale | No `JellyfinPasswordFirst` and no "Check the saved Jellyfin password first" in `src/`, `tests/`, `e2e/`, `docs/`, `README.md` | scripted search | `rg -n "JellyfinPasswordFirst\|Check the saved Jellyfin password first" src tests e2e docs README.md` | n/a | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` — new file, covers TEST-01, AUTH-03, AUTH-04
- [ ] `FakeUserManager` in `TestDoubles.cs` — new type (D-09), implements the 22-member `IUserManager` and can be told to throw from `CreateUserAsync`, `UpdateUserAsync`, and `DeleteUserAsync`
- [ ] Fake `ICryptoProvider` in `TestDoubles.cs` — new type (D-11)
- [ ] Settings-source constructor seam in `EmbyAuthenticationProvider.cs` plus its wiring in `PluginServiceRegistrator.cs` (D-10) — must land before `EmbyAuthenticationProviderTests.cs` can construct the provider without the static `EmbyAuthPlugin.Instance`
- [ ] Framework install: none needed — `xunit.v3`, `Microsoft.Extensions.DependencyInjection`, and `Microsoft.EntityFrameworkCore` already resolve in the test project

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| `docs/how-it-works.md` describes the brief moment between `CreateUserAsync` and the `UpdateUserAsync` that sets the hash | AUTH-03 | Prose accuracy against a documented residual race — a string match proves the text exists, not that it is correct | Read the account-creation section of `docs/how-it-works.md` and confirm it names the window, says the account has no usable password during it, and does not claim the window is eliminated |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 10s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
