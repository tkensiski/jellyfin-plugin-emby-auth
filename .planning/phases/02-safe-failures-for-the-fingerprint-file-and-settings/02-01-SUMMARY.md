---
phase: 02-safe-failures-for-the-fingerprint-file-and-settings
plan: 01

subsystem: ui
tags: [jsdom, node-test, jellyfin-plugin-settings-page]

requires: []
provides:
  - "The repository's first JavaScript toolchain: a pinned node in .mise.toml, tests/js/ with a committed jsdom@28.1.0 lockfile, and node --test wired into mise run test with no new CI job."
  - "UI-01: a failed settings load shows a fixed message, disables Save, survives repeated Save activations, re-enables on a later successful load, and leaks no configured value."
affects: [02-02]

actuals:
  tokens: 8972
  tasks: 3
  commits: 4

tech-stack:
  added: ["jsdom@28.1.0 (devDependency, tests/js/ only, never shipped)"]
  patterns:
    - "Settings-page tests execute the shipping configPage.html itself through jsdom's runScripts: 'dangerously', with ApiClient/Dashboard stubs injected via beforeParse — no extraction, no build step."
    - "Stub failure flags are plain mutable properties on the returned api object (getConfigFails etc.), so a test can flip a live stub from failing to succeeding without building a second window."
    - "RED-phase TDD commits use { skip: 'RED — unskipped in the GREEN commit' } on node:test, then unskip in the GREEN commit — the node:test analog of Phase 01's [Fact(Skip=...)] convention, required because the pre-commit test hook blocks any commit that leaves a test failing."

key-files:
  created:
    - tests/js/package.json
    - tests/js/package-lock.json
    - tests/js/testHelpers.js
    - tests/js/configPage.test.js
  modified:
    - .gitignore
    - .mise.toml
    - .pre-commit-config.yaml
    - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html

key-decisions:
  - "Task 1 legitimacy checkpoint approved by the maintainer on 2026-09-19: jsdom@28.1.0, pinned exact, taken as-specified. The gsd-tools SUS verdict reflects the latest dist-tag (30.1.0, one day old at research time), not the seven-month-old 28.1.0 pin; no postinstall script exists at any version, and the source repo is the canonical jsdom/jsdom."
  - "Three of Task 3's four new tests (Save off after a failed load, Save off after two activations, message never repeats a rejection value) already passed against Task 2's committed fix with no further page change — proven correct by Phase 01's established precedent: remove the already-working guard line, confirm the test goes red, restore it, rather than forcing an artificial pre-implementation RED for behavior that already exists."
  - "Only the fourth test (a later successful load re-enables Save) required a genuine RED-then-GREEN cycle, since that code path did not exist before Task 3."

requirements-completed: [UI-01, TEST-04]

coverage:
  - id: D1
    description: "A failed settings load shows a fixed inline message and disables Save, so the administrator cannot start a save that would overwrite stored settings with blanks"
    requirement: "UI-01"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#a failed settings load shows a message on the page"
        status: pass
      - kind: unit
        ref: "tests/js/configPage.test.js#Save is turned off after a failed settings load"
        status: pass
    human_judgment: false
  - id: D2
    description: "Save stays off across repeated activations after a failed load, and no update request is ever sent"
    requirement: "UI-01"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#activating Save twice after a failed load still sends nothing"
        status: pass
    human_judgment: false
  - id: D3
    description: "A later successful load re-populates the four inputs and re-enables Save"
    requirement: "UI-01"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#a later successful load turns Save back on"
        status: pass
    human_judgment: false
  - id: D4
    description: "The load-failure message never repeats a rejection value that carries the configured Emby URL or API key, and writes text, never markup"
    requirement: "UI-01"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#the load-failure message repeats neither the configured URL nor the API key"
        status: pass
    human_judgment: false
  - id: D5
    description: "The first JavaScript test harness in this repository: a pinned node, a jsdom-based node:test suite that runs the shipping configPage.html, wired into mise run test with no new CI job"
    requirement: "TEST-04"
    verification:
      - kind: unit
        ref: "mise run test (dotnet test + bats tests/scripts + npm --prefix tests/js ci + node --test)"
        status: pass
    human_judgment: false
  - id: D6
    description: "The message and message-readability quality reads clearly to an administrator in a real Jellyfin dashboard"
    human_judgment: true
    rationale: "The jsdom tests assert a message element holds specific text; whether the wording reads well to a person in the real dashboard chrome is a judgment call the automated suite cannot make (per 02-VALIDATION.md's Manual-Only Verifications)."

duration: 22min
completed: 2026-09-19
status: complete
---

# Phase 02 Plan 01: Settings-load safe failure and the JavaScript test harness Summary

**A pinned Node/jsdom test harness that runs the shipping `configPage.html` directly, and a failed settings load that now shows a fixed message, disables Save, and recovers on the next successful load — proved by five `node:test` cases wired into `mise run test`.**

## Performance

- **Duration:** 22 min (this continuation, from the approved Task 1 checkpoint)
- **Started:** 2026-09-19T06:58:00Z
- **Completed:** 2026-09-19T07:20:00Z
- **Tasks:** 2 (Task 1 was the checkpoint, resolved in a prior turn)
- **Files modified:** 8 (4 created, 4 modified)

## Accomplishments

- Built the repository's first JavaScript toolchain: `node = "24.21.0"` pinned in `.mise.toml`, `tests/js/package.json` with `jsdom@28.1.0` pinned exact and a committed lockfile, `.gitignore` excluding `node_modules/`, and `node --test` wired into `mise run test` with no new CI job.
- `tests/js/testHelpers.js` loads the real `configPage.html` through jsdom's `runScripts: 'dangerously'`, injecting `ApiClient`/`Dashboard` stubs via `beforeParse` — the tests execute the shipping artifact, not a copy.
- Closed UI-01: `configPage.html`'s `pageshow` chain gained a `.catch` that shows a fixed message, disables Save, and a success-branch line that re-enables Save on a later successful load.
- Five `node:test` cases in `tests/js/configPage.test.js` prove: the message appears, Save disables and stays disabled across repeated activations, Save re-enables on recovery, and the message never repeats a rejection value carrying the configured Emby URL or API key.
- `.pre-commit-config.yaml`'s `test` hook now also matches `tests/js/`, so editing a test file runs the suite locally, closing the one gap the existing `.html` pattern left.

## Task Commits

Each task was committed atomically. Task 2 and Task 3 each followed the RED/GREEN TDD cycle:

1. **Task 2: One end-to-end path** — RED: `cfe00ad` (`test`), GREEN: `26cb44e` (`feat`)
2. **Task 3: Expand the load-failure slice** — RED: `27c3d8c` (`test`), GREEN: `187a135` (`feat`)

**Plan metadata:** committed separately after this SUMMARY.

_Note: Task 1 (the `jsdom` legitimacy checkpoint) produced no commit — it is a `gate="blocking-human"` decision, approved by the maintainer before Task 2's install._

## Files Created/Modified

- `tests/js/package.json` — new. `jellyfin-plugin-emby-auth-page-tests`, `private: true`, `type: "commonjs"`, one devDependency `jsdom` at `28.1.0` with no range operator.
- `tests/js/package-lock.json` — new, committed. Enforced by `npm --prefix tests/js ci` in `mise run test`.
- `tests/js/testHelpers.js` — new. `buildDom`, `stubApiClient`, `stubDashboard`, `flush`, `firePageshow`, `fireSubmit`, `clickSave`, `clickRunMigration`.
- `tests/js/configPage.test.js` — new. Five `node:test` cases covering the full UI-01 load-failure behavior.
- `.mise.toml` — `node = "24.21.0"` added to `[tools]`; `npm --prefix tests/js ci` and `node --test` (no path argument) added to `[tasks.test]`.
- `.gitignore` — `node_modules/` added.
- `.pre-commit-config.yaml` — the `test` hook's `files` pattern gained `|^tests/js/`.
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` — a `.catch` added to the `pageshow` chain (fixed message, `disabled = true`), and `disabled = false` added to the success branch.

## Decisions Made

- **Task 1 checkpoint approved.** The maintainer approved `jsdom@28.1.0` on 2026-09-19, taking the version exactly as specified. Supporting facts re-verified independently by the orchestrator against `registry.npmjs.org/jsdom` before approval: `28.1.0` exists (published 2026-02-15, ~7 months old, MIT), the `SUS`/`too-new` verdict reflects the `latest` dist-tag (`30.1.0`, published one day before the research ran) and not the pin, the repository is the canonical `github.com/jsdom/jsdom`, no version declares a `postinstall`/`preinstall`/`install` script, `engines.node` (`^20.19.0 || ^22.12.0 || >=24.0.0`) is satisfied by the `24.21.0` pin, and the release zip contains only the plugin DLL and `meta.json` — this dependency is test-only and never reaches a Jellyfin user.
- **Three of Task 3's four new tests needed no new implementation.** "Save is turned off after a failed load," "activating Save twice after a failed load still sends nothing," and "the load-failure message repeats neither the URL nor the API key" all passed against Task 2's already-committed fix. Rather than force an artificial red by writing them before Task 2's code existed (which would be historically false — Task 2 already implemented the behavior they cover), the plan's own acceptance criteria call for the Phase 01 precedent instead: remove the already-working guard line, confirm the test catches the regression, restore it. Only the fourth test, Save re-enabling on a later successful load, needed a genuine RED-then-GREEN cycle, because that code path did not exist until this task.
- **`node = "24.21.0"`** — the current Active LTS 24.x release, satisfying `jsdom@28.1.0`'s `engines.node` floor. Installed locally via `mise install node@24.21.0` before the first test run.

## Deviations from Plan

None — plan executed exactly as written. The dual-mode TDD handling for Task 3 (three tests proven by remove-and-restore rather than a pre-implementation red) is the behavior the plan's own acceptance criteria specify, not a deviation from it.

## RED-Phase Observations (TDD discipline)

**Task 2, "a failed settings load shows a message on the page":** observed red as an unhandled promise rejection, exactly as `02-RESEARCH.md` Pitfall 4 predicted — the stack trace ran through jsdom-internal `callTheUserObjectsOperation` / `EventTarget-impl.js` frames, not a plain assertion failure, because `pageshow`'s chain had `.finally` and no `.catch`. Committed skipped, then unskipped after the `.catch` was added; reran green.

**Task 3, "a later successful load turns Save back on":** observed red as a plain assertion failure (`AssertionError: true !== false` on the `disabled` property) — the four other Task 3 tests passed unskipped on first write, confirming Task 2's fix already covered them. Committed the one genuinely-new test skipped, then unskipped after the success-branch `disabled = false` line was added; reran green (5/5).

**Teeth proofs (both tasks), each removed and restored exactly, confirmed by `diff` against a scratch copy before restoring:**
- Task 2: removing the `.catch` block turned the one test red again (unhandled rejection).
- Task 3: removing only the `disabled = true` line turned 4 of 5 tests red (the message test, both disabled-Save tests, and the re-enable test, which failed at its first assertion since the button was never disabled to begin with).
- Task 3: removing the entire `.catch` block turned all 5 tests red (4 assertion failures plus an unhandled-rejection warning from the fifth, whose async activity outlived the test).

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- `tests/js/` and its harness are in place for plan 02-02 (UI-02, the save-failure path, and the migration-list markup test) to extend directly — no further toolchain work needed.
- `mise run test` now runs four entries (`dotnet test`, `bats tests/scripts`, `npm --prefix tests/js ci`, `node --test`); CI's `test` job and `ci-success` are unchanged, confirmed by `mise run lint` and a full `mise run e2e` pass (27/27) after each task.
- No blockers for 02-02 or 02-03.

## Self-Check: PASSED

All created files verified present on disk (`tests/js/package.json`, `tests/js/package-lock.json`, `tests/js/testHelpers.js`, `tests/js/configPage.test.js`). All four task commits (`cfe00ad`, `26cb44e`, `27c3d8c`, `187a135`) verified present in `git log`. Re-ran `node --test` (5/5 pass), `mise run test`, `mise run lint`, and `mise run e2e` (27/27 pass) at plan completion — all green.

---
*Phase: 02-safe-failures-for-the-fingerprint-file-and-settings*
*Completed: 2026-09-19*
