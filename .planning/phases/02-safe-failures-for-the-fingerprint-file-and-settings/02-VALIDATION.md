---
phase: "02"
slug: "safe-failures-for-the-fingerprint-file-and-settings"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-09-18"
---

# Phase 02 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

This phase spans two test layers. The C# layer exists; the JavaScript layer is a Wave 0 deliverable.

| Property | Value |
|----------|-------|
| **Framework (C#)** | xUnit v3 4.0.1 on Microsoft.Testing.Platform (`tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj:10`) |
| **Framework (JS, new)** | Node built-in `node:test` + `node:assert/strict` with jsdom — no config file, CLI-flag driven |
| **Config file** | none — Wave 0 adds `tests/js/package.json` and the `.mise.toml` node pin |
| **Quick run command (C#)** | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` |
| **Quick run command (JS)** | `node --test` — run from repo root with **no path argument** (a path argument fails with `MODULE_NOT_FOUND` on Node 24) |
| **Full suite command** | `mise run test` |
| **Estimated runtime** | ~1s C# suite; ~4s JS suite; full `mise run test` under 30s |

---

## Sampling Rate

- **After every task commit:** Run the affected layer's quick command — `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` for a C# change, `node --test` for a JavaScript change.
- **After every plan wave:** Run `mise run test` (`dotnet test` + `bats tests/scripts` + `node --test`).
- **Before `/gsd-verify-work`:** `mise run test` green, plus `mise run e2e`. Both `EmbyVerifiedPasswords.cs` and `configPage.html` are under `src/`, so the repository rule "a change to `src/` needs an end-to-end test run" applies to every plan in this phase.
- **Max feedback latency:** 30 seconds.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| Task 1 | 02-01 | 1 | TEST-04 | T-02-SC | The first npm install in this repository stops for a human, in every mode | checkpoint (`blocking-human`) | none — human decision | n/a | ⬜ pending |
| Task 2 | 02-01 | 1 | UI-01, TEST-04 | T-02-01, T-02-03, T-02-04 | The load-failure message is a fixed string; the installed dependency tree is not tracked | unit (`node:test` + jsdom) | `node --test` | ❌ created here | ⬜ pending |
| Task 3 | 02-01 | 1 | UI-01, TEST-04 | T-02-01, T-02-02, T-02-03 | A rejection value carrying a `user:password@host` URL and an API key reaches no page message | unit (`node:test` + jsdom) | `node --test` | ✅ after 02-01 Task 2 | ⬜ pending |
| Task 1 | 02-02 | 2 | UI-02 | T-02-01, T-02-03 | The save-failure message is a fixed string and never repeats the Emby URL or the API key | unit (`node:test` + jsdom) | `node --test` | ✅ | ⬜ pending |
| Task 2 | 02-02 | 2 | TEST-04 | T-02-02 | A migration list entry built from an Emby user name renders as text, not markup | unit (`node:test` + jsdom) | `node --test` | ✅ | ⬜ pending |
| Task 3 | 02-02 | 2 | UI-01, UI-02 | T-02-05 | The documentation states the limit of the guarantee and copies no message string | docs + human check | `mise run lint` | ✅ | ⬜ pending |
| Task 1 | 02-03 | 2 | FPRT-02 | T-02-06, T-02-07, T-02-08 | A failed read never causes a write, and `Matches` is false for every user while it fails | unit (xUnit) | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` | ✅ | ⬜ pending |
| Task 2 | 02-03 | 2 | FPRT-02 | T-02-10 | The documentation does not claim the plugin prevents or eliminates data loss | docs + human check | `rg` gates in the task `<verify>` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

**Full-suite command after 02-01 lands:** `mise run test` runs four entries — `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx`, `bats tests/scripts`, `npm --prefix tests/js ci`, and `node --test`. `node --test` takes no path argument.

---

## Wave 0 Requirements

- [ ] `.mise.toml` — pin `node`, and add the `node --test` line to `[tasks.test]`
- [ ] `tests/js/package.json` and `tests/js/package-lock.json` — hold the `jsdom` dependency
- [ ] `tests/js/testHelpers.js` — `buildDom()`, `flush()`, the `ApiClient` and `Dashboard` stubs, and the four event helpers
- [ ] `tests/js/configPage.test.js` — the TEST-04 suite
- [ ] `.gitignore` — add `node_modules/`, which it does not list today
- [ ] `.pre-commit-config.yaml` — add `^tests/js/` to the `test` hook's `files` pattern, which matches nothing under that directory today

All of Wave 0 lands in plan 02-01, Task 2, except the pre-commit pattern, which lands in plan 02-01, Task 3.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| The load-failure and save-failure messages read clearly to an administrator in a real Jellyfin dashboard | UI-01, UI-02 | The jsdom tests assert that a message element holds text; whether that text reads well to a person is a judgment a test cannot make | `scripts/dev-env.sh up`, open the plugin settings page on port 28196, stop Jellyfin's access to the plugin configuration, reload the page, and read the message |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
