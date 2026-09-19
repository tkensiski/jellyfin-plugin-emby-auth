---
phase: "02"
slug: "safe-failures-for-the-fingerprint-file-and-settings"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: validated
nyquist_compliant: true
wave_0_complete: true
created: "2026-09-18"
validated: "2026-09-19"
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
| Task 1 | 02-01 | 1 | TEST-04 | T-02-SC | The first npm install in this repository stops for a human, in every mode | checkpoint (`blocking-human`) | none — human decision | n/a | ✅ green |
| Task 2 | 02-01 | 1 | UI-01, TEST-04 | T-02-01, T-02-03, T-02-04 | The load-failure message is a fixed string; the installed dependency tree is not tracked | unit (`node:test` + jsdom) | `node --test` | ✅ | ✅ green |
| Task 3 | 02-01 | 1 | UI-01, TEST-04 | T-02-01, T-02-02, T-02-03 | A rejection value carrying a `user:password@host` URL and an API key reaches no page message | unit (`node:test` + jsdom) | `node --test` | ✅ | ✅ green |
| Task 1 | 02-02 | 2 | UI-02 | T-02-01, T-02-03 | The save-failure message is a fixed string and never repeats the Emby URL or the API key | unit (`node:test` + jsdom) | `node --test` | ✅ | ✅ green |
| Task 2 | 02-02 | 2 | TEST-04 | T-02-02 | A migration list entry built from an Emby user name renders as text, not markup | unit (`node:test` + jsdom) | `node --test` | ✅ | ✅ green |
| Task 3 | 02-02 | 2 | UI-01, UI-02 | T-02-05 | The documentation states the limit of the guarantee and copies no message string | docs + human check | `mise run lint` | ✅ | ✅ green |
| Task 1 | 02-03 | 2 | FPRT-02 | T-02-06, T-02-07, T-02-08 | A failed read never causes a write, and `Matches` is false for every user while it fails | unit (xUnit) | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` | ✅ | ✅ green |
| Task 2 | 02-03 | 2 | FPRT-02 | T-02-10 | The documentation does not claim the plugin prevents or eliminates data loss | docs + human check | `rg` gates in the task `<verify>` | ✅ | ✅ green |

**Covering tests, measured 2026-09-19** (`mise run test`: .NET `Passed!`, `bats tests/scripts`, `node --test` 30/30):

| Requirement | Covering tests |
|-------------|----------------|
| UI-01 | `a failed settings load shows a message on the page`, `Save is turned off after a failed settings load`, `activating Save twice after a failed load still sends nothing`, `a later successful load turns Save back on`, `the load-failure message repeats neither the configured URL nor the API key` |
| UI-02 | `a failed configuration update shows a message`, `a failed re-fetch during save shows the same message`, `the save-failure message repeats neither the configured URL nor the API key`, `attempting a save disables Save at once`, `Save stays off after a failed save`, `a successful save turns Save back on` |
| TEST-04 | `a user name containing markup characters renders as text`, `two users with the same name each get their own entry`, `the list keeps the server order`, `loading the migration status twice does not accumulate entries` |
| FPRT-02 | `UnreadableFile_MatchesNothing_AndLogsAnError`, `UnreadableFile_KeepsItsRecords_WhenALoginIsRecorded`, `UnreadableFile_IsReadAgain_WhenItBecomesReadable`, `ConcurrentRecords_AreNotWritten_WhenTheFileIsUnreadable` (`EmbyVerifiedPasswordsTests.cs`) |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

**Full-suite command after 02-01 lands:** `mise run test` runs four entries — `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx`, `bats tests/scripts`, `npm --prefix tests/js ci`, and `node --test`. `node --test` takes no path argument.

---

## Wave 0 Requirements

- [x] `.mise.toml` — pin `node`, and add the `node --test` line to `[tasks.test]` (`node = "24.21.0"`, `.mise.toml:11`; `node --test`, `.mise.toml:35`)
- [x] `tests/js/package.json` and `tests/js/package-lock.json` — hold the `jsdom` dependency
- [x] `tests/js/testHelpers.js` — `buildDom()`, `flush()`, the `ApiClient` and `Dashboard` stubs, and the four event helpers
- [x] `tests/js/configPage.test.js` — the TEST-04 suite
- [x] `.gitignore` — `node_modules` listed
- [x] `.pre-commit-config.yaml` — `tests/js` matched by the `test` hook

All of Wave 0 lands in plan 02-01, Task 2, except the pre-commit pattern, which lands in plan 02-01, Task 3.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| The load-failure and save-failure messages read clearly to an administrator in a real Jellyfin dashboard | UI-01, UI-02 | The jsdom tests assert that a message element holds text; whether that text reads well to a person is a judgment a test cannot make | `scripts/dev-env.sh up`, open the plugin settings page on port 28196, stop Jellyfin's access to the plugin configuration, reload the page, and read the message |
| The fingerprint-file read and write bullets read as actionable | FPRT-02 | Whether the wording is actionable is a judgment call, not something a regex match settles | Read the read-failure and write-failure bullets in `docs/how-it-works.md` |

**Both entries were executed and passed on 2026-09-19**, recorded as tests 1 and 2 in `02-UAT.md`. The first failed its initial run: the message reached the administrator in the wrong place and a disabled Save kept its enabled styling. Quick tasks `260919-208` and `260919-inm` closed that gap (`1f47767`, `325855e`), and the tester confirmed the result in a real dashboard.

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 30s — measured `mise run test` well under the 30s budget
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** validated 2026-09-19

## Validation Audit 2026-09-19

| Metric | Count |
|--------|-------|
| Gaps found | 0 |
| Resolved | 0 |
| Escalated | 0 |

No auditor was spawned, because the audit found no MISSING or PARTIAL requirement. Every requirement in the Per-Task Verification Map resolves to a named test that runs green; the covering tests are listed above the Wave 0 section.

**One judgment recorded, so a later reader does not have to re-derive it.** Plan 02-01 Task 1 carries no automated command — it is a `blocking-human` checkpoint on the first `npm install` in the repository, a one-time execution-time gate rather than an ongoing verification of a requirement. `nyquist_compliant: true` is set on the basis that all four requirements this phase claims (UI-01, UI-02, TEST-04, FPRT-02) have automated verification. The checkpoint is not a fifth requirement.
