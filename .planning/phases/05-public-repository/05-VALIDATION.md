---
phase: "5"
slug: "public-repository"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-09-20"
---

# Phase 5 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

Phase 5 changes shell scripts, tool configuration, workflow YAML, and prose. It changes nothing in `src/`, so the C# unit suite and the settings-page suite are regression cover only, and the repository rule that a `src/` change needs an end-to-end run does not fire for any task here.

| Property | Value |
|----------|-------|
| **Framework** | bats 1.14.0 for every test this phase adds (pinned, `.mise.toml:3`). `mise run test` also carries the pre-existing xUnit v3 and `node:test` suites, which this phase does not touch. |
| **Config file** | none — bats files are self-contained under `tests/scripts/` and are picked up by the existing `bats tests/scripts` entry in `[tasks.test]` |
| **Quick run command** | `bats tests/scripts` |
| **Full suite command** | `mise run test` |
| **Lint gate command** | `mise run lint` — the task REL-02 and REL-03 are verified through, run identically by CI's `lint` job and by the pre-commit hook |
| **Estimated runtime** | `bats tests/scripts` is dominated by `package.bats`'s `setup_file`, which runs a full `scripts/package.sh build`; `mise run test` adds a .NET build and `npm --prefix tests/js ci` on top of that. Neither was timed during this planning session. A single new bats file on its own runs in well under a second — measured this session against a working copy of `tests/scripts/gitleaks.bats`. |

---

## Sampling Rate

- **After every task commit:** `bats tests/scripts` — and `mise run lint` as well for any task that touches `.mise.toml`, `.gitleaks.toml`, `.pre-commit-config.yaml`, or a file under `.github/workflows/`
- **After every plan wave:** `mise run test` and `mise run lint`
- **Before `/gsd-verify-work`:** both green
- **Max feedback latency:** one task commit. Every task in this phase carries at least one `<automated>` command, so no task is ever more than its own commit away from a signal; there is no run of tasks with no automated check.
- **Not in the loop:** `mise run e2e`. No task changes `src/`, the plugin's behavior, or the e2e suite.

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 5-01-01 | 01 | 1 | REL-01 | T-05-01 / T-05-22 | A release cannot be built from a commit whose suite never passed, and the check-run query is sent as a GET so the gate can actually open | unit (bats, fake `gh` on `PATH`) + one un-faked `gh` probe | `bats tests/scripts/release-gate.bats` | ❌ W0 | ⬜ pending |
| 5-01-02 | 01 | 1 | REL-01 | T-05-02 / T-05-04 | Every non-success state refuses, and a failed API call refuses with a message provably distinct from "no check run", for both 403 and 422 | unit (bats) | `bats tests/scripts/release-gate.bats` | ❌ W0 | ⬜ pending |
| 5-01-03 | 01 | 1 | REL-01 | — | The documented release procedure matches what the gate actually refuses, so a maintainer is not left guessing | doc assertion + human-check | `rg -q 'ci-success' docs/development.md && rg -q 'cancelled' docs/development.md` | ✅ | ⬜ pending |
| 5-02-01 | 02 | 2 | REL-02 | T-05-07 / T-05-06 | The `ci-success` check-run name the gate matches on is byte-identical to today's, and an in-flight release is never cancelled part-way through uploading assets | integration (static analysis) | `actionlint && zizmor --offline --persona=pedantic .github/workflows` | ✅ | ⬜ pending |
| 5-02-02 | 02 | 2 | REL-02 | T-05-05 / T-05-08 | The pedantic persona is a standing gate, so a later workflow edit that drops a permission comment fails CI and the hook rather than waiting to be noticed | integration (lint task) | `mise run lint` | ✅ | ⬜ pending |
| 5-03-01 | 03 | 3 | REL-03 | T-05-09 / T-05-10 / T-05-11 | The full history is scanned for secrets on every lint run, suppression is by fixture value only, and the entry cannot swallow its own exit code | integration (lint task) | `mise run lint` | ✅ | ⬜ pending |
| 5-03-02 | 03 | 3 | REL-03 | T-05-12 | The scan is live: it fails on the repository's own fixture findings with the allowlist removed and passes with it restored | integration (break-then-restore) | `git diff --exit-code .gitleaks.toml && mise run lint` | ✅ | ⬜ pending |
| 5-03-03 | 03 | 3 | REL-03 | T-05-23 / T-05-24 | The configuration detects a real-shaped value and stays quiet on a clean one, so a widened allowlist cannot pass as a clean repository | unit (bats, temp dir outside the working tree) | `bats tests/scripts/gitleaks.bats` | ❌ W0 | ⬜ pending |
| 5-04-01 | 04 | 4 | DOCS-03 | T-05-13 | The README states the measured versions and the `targetAbi` floor, and claims no compatibility that was not measured | doc assertion + human-check | `rg -q '^## Compatibility$' README.md && rg -q '12\.1\.0\.0' README.md && rg -q '4\.10\.0\.40' README.md` | ✅ | ⬜ pending |
| 5-04-02 | 04 | 4 | DOCS-03 | T-05-15 | No Install sentence is false either before or after the visibility change | doc assertion + human-check | `rg -q -i 'repository is private\|you need access to it\|only when the release files are public' README.md; test $? -eq 1` | ✅ | ⬜ pending |
| 5-05-01 | 05 | 5 | PUB-01 | T-05-19 / T-05-21 | The audit reads only, reuses the one scan rather than defining a second, and prints `REVIEW` rather than `PASS` for anything it cannot judge | unit (bats, fake `gh` and `mise` on `PATH`) | `bats tests/scripts/pre-public-audit.bats` | ❌ W0 | ⬜ pending |
| 5-05-02 | 05 | 5 | PUB-01 | T-05-16 / T-05-17 / T-05-18 | Nothing about to become world-readable holds a secret, and the audit's own output publishes none | integration (live run) + manual review | `scripts/pre-public-audit.sh run` | ❌ W0 | ⬜ pending |
| 5-05-03 | 05 | 5 | PUB-03 | T-05-20 | The irreversible act is the maintainer's alone; no reply at the checkpoint causes it to be run | manual-only (blocking-human checkpoint) | `gh repo view --json visibility`, after the maintainer's own change | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

*The pipe characters inside the 5-04-02 command are escaped for the table; the command as written in `05-04-PLAN.md` uses plain `|` alternation.*

---

## Wave 0 Requirements

Wave 0 here is not a separate execution wave. Every missing test file is created by the task that needs it, as the RED step of that task, before the code it covers exists.

- [ ] `tests/scripts/release-gate.bats` — 16 tests covering REL-01. Created by plan 01 Task 1 (Tests 1–4) and extended by Task 2 (Tests 5–16). Test 4 calls the real `gh`; the other fifteen use a fake `gh` on `PATH` per D-04.
- [ ] `tests/scripts/gitleaks.bats` — 2 tests covering REL-03's detection half. Created by plan 03 Task 3.
- [ ] `tests/scripts/pre-public-audit.bats` — 6 tests covering PUB-01's automatable slice. Created by plan 05 Task 1.
- [ ] `gitleaks = "8.30.1"` in `[tools]` — added by plan 03 Task 1. `tests/scripts/gitleaks.bats` needs the binary on `PATH`, which `mise run test` and the pre-commit hook both provide.
- Framework install: none. bats 1.14.0 is already pinned and already wired into `[tasks.test]` as `bats tests/scripts`, so all three new files are picked up with no change to `.mise.toml`'s task list, no change to the `shellcheck`/`shfmt` paths, and no change to `.pre-commit-config.yaml`.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| The Releases section tells a maintainer why a pushed tag produced no release and what to do about it | REL-01 | Whether prose is understandable to a reader who has never seen the gate is a judgment, not a string match | 05-01 Task 3 `<human-check>`: read the three numbered steps end to end as someone who has not seen the gate |
| `.gitleaks.toml` exempts no file, no directory, and no rule id | REL-03 | The property is about what the config does *not* say. A grep rejects known bad forms; only reading confirms narrowness | 05-03 Task 2 `<human-check>`: read the file end to end and confirm every suppression is a fixture value |
| The Compatibility section is readable by someone who has never heard the term `targetAbi` | DOCS-03 | Comprehension, not presence | 05-04 Task 1 `<human-check>`: read only that section, then state what the value controls, what happens below 12.1.0, what happens above it, and that above is untested rather than supported |
| No Install sentence is false in either visibility state | DOCS-03 | Requires reading the section twice under two different hypotheses about the world | 05-04 Task 2 `<human-check>`: read it once imagining the repository is private, once imagining it is public |
| Every `REVIEW` item in the audit is content a stranger may read | PUB-01 | The script surfaces evidence by design; judging the content of workflow logs, issue and PR text, and release notes is human work (D-13, RESEARCH.md Open Question 1) | 05-05 Task 2 `<human-check>`: read all six `REVIEW` items in full and record a one-line conclusion for each |
| The repository becomes public | PUB-03 | Irreversible for anyone who copies the repository in the interval. D-15 puts the command in the maintainer's hands and forbids any agent path, so this cannot close inside the execution run | 05-05 `checkpoint:decision` (`gate="blocking-human"`), then `gh repo view --json visibility` afterwards |
| The un-faked request-shape probe actually ran | REL-01 | The probe skips rather than fails when `gh` is absent or unauthenticated, and a skipped run leaves the 404 guard unexercised while the suite still reports green | Run `bats tests/scripts/release-gate.bats` once on an authenticated machine and confirm Test 4 reports `ok` rather than `ok # skip` |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency within one task commit
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
