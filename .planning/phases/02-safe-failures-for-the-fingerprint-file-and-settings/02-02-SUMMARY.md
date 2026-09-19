---
phase: 02-safe-failures-for-the-fingerprint-file-and-settings
plan: 02

subsystem: ui
tags: [jsdom, node-test, jellyfin-plugin-settings-page]

requires:
  - phase: 02-01
    provides: "tests/js/testHelpers.js (buildDom, stubApiClient, stubDashboard, flush, firePageshow, fireSubmit, clickSave), tests/js/configPage.test.js, and the load-failure .catch/disabled-Save pattern in configPage.html"
provides:
  - "UI-02: a failed settings save shows a fixed message whichever of its two requests rejects (the submit handler's own re-fetch or the update call), never repeats the rejection value or the configured Emby URL/API key, and hides the loading indicator on the failure path."
  - "TEST-04 complete: the settings-page test suite now covers a successful load, the save-failure paths, the migration list (one entry per user, server order, adjacency, empty, idempotency, markup safety), and Run migration now (success and failure) — 18 node:test cases total."
affects: [03]

actuals:
  tokens: 4374
  tasks: 3
  commits: 4

tech-stack:
  added: []
  patterns:
    - "getConfigFailsFromCall on the stub ApiClient: a call-count-aware form of getConfigFails, so one test can let an earlier call (e.g. the pageshow fetch) succeed while a later call (e.g. the submit handler's own re-fetch) rejects."
    - "buildDom().close() tears the jsdom window down so a pending setTimeout (Run migration now's 3-second reload) does not hold node --test open after the test that triggered it finishes."
    - "listItemTexts(document) reads the migration list as an ordered array of strings, for order- and count-sensitive assertions without inspecting markup."
    - "Break-then-restore teeth proofs for tests over already-working behavior (Phase 01's precedent, reused from 02-01 Task 3): break one line, confirm the expected test(s) go red, restore from a scratch-directory copy, diff to confirm byte-identical, re-run to confirm green — all before the commit, not as separate commits."

key-files:
  created: []
  modified:
    - tests/js/testHelpers.js
    - tests/js/configPage.test.js
    - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
    - docs/settings.md
    - docs/development.md
    - CLAUDE.md

key-decisions:
  - "getConfigFailsFromCall (a 1-based call-count threshold) rather than a second stub factory, per the plan's instruction to keep one stub factory and extend it — the submit handler's re-fetch failure test needed the pageshow fetch to succeed while the submit's own re-fetch rejected."
  - "docs/settings.md's Save-off sentence avoids markdown bold around the word Save (`turns Save off`, not `turns **Save** off`) so the plain-text acceptance-criteria regex (`turns .*Save off`) matches the literal file content, since the asterisks would otherwise split the searched substring."
  - "Task 2's ten new tests needed no RED-first cycle — they cover already-working behavior, so each of the four affected code paths (list-clearing, readiness text, migration-status .catch, run-migration .catch) was broken once, confirmed red, and restored, per this plan's own tdd_discipline section and 02-01's established precedent."

requirements-completed: [UI-02, TEST-04]

coverage:
  - id: D1
    description: "A failed settings save shows a fixed message whichever of the two requests fails (the submit handler's own re-fetch, or the update call), and the recorded update-call list stays empty when the re-fetch is the one that failed"
    requirement: "UI-02"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#a failed configuration update shows a message"
        status: pass
      - kind: unit
        ref: "tests/js/configPage.test.js#a failed re-fetch during save shows the same message"
        status: pass
    human_judgment: false
  - id: D2
    description: "The save-failure message never repeats the configured Emby URL or the API key, even when the rejection value embeds both, and writes text, never markup"
    requirement: "UI-02"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#the save-failure message repeats neither the configured URL nor the API key"
        status: pass
    human_judgment: false
  - id: D3
    description: "The loading indicator (Dashboard.hideLoadingMsg) is called after a failed save, so the page does not appear busy forever"
    requirement: "UI-02"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#a failed configuration update shows a message (hideLoadingCalls assertion)"
        status: pass
      - kind: unit
        ref: "tests/js/configPage.test.js#a failed re-fetch during save shows the same message (hideLoadingCalls assertion)"
        status: pass
    human_judgment: false
  - id: D4
    description: "A successful load fills the four settings inputs from the loaded configuration"
    requirement: "TEST-04"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#a successful load fills the four inputs"
        status: pass
    human_judgment: false
  - id: D5
    description: "The migration list renders one entry per user in server order, including two entries with the same name, and does not accumulate entries across repeated loads"
    requirement: "TEST-04"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#the migration list shows one entry per user"
        status: pass
      - kind: unit
        ref: "tests/js/configPage.test.js#two users with the same name each get their own entry"
        status: pass
      - kind: unit
        ref: "tests/js/configPage.test.js#the list keeps the server order"
        status: pass
      - kind: unit
        ref: "tests/js/configPage.test.js#an empty user list renders nothing and says so"
        status: pass
      - kind: unit
        ref: "tests/js/configPage.test.js#loading the migration status twice does not accumulate entries"
        status: pass
    human_judgment: false
  - id: D6
    description: "A migration list entry built from an Emby user name containing markup characters renders as text, not markup (T-02-02)"
    requirement: "TEST-04"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#a user name containing markup characters renders as text"
        status: pass
    human_judgment: false
  - id: D7
    description: "A failed migration-status read and a failed Run migration now each show their existing failure message; Run migration now sends exactly one POST and reports success"
    requirement: "TEST-04"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js#a failed migration status shows its message"
        status: pass
      - kind: unit
        ref: "tests/js/configPage.test.js#Run migration now sends the request and reports it"
        status: pass
      - kind: unit
        ref: "tests/js/configPage.test.js#a failed Run migration now shows its message"
        status: pass
    human_judgment: false
  - id: D8
    description: "Removing a load-failure or save-failure error handler turns a different test red (ROADMAP criterion 4) — proven by the four break-then-restore teeth proofs and Task 1's RED-then-GREEN cycle, all recorded below"
    requirement: "TEST-04"
    verification:
      - kind: unit
        ref: "tests/js/configPage.test.js (RED observed pre-fix; break-then-restore proofs recorded in this SUMMARY's Task Commits section)"
        status: pass
    human_judgment: false
  - id: D9
    description: "docs/settings.md, docs/development.md, and CLAUDE.md describe the new failure behavior and the settings-page test suite, without copying the exact message strings"
    requirement: null
    verification:
      - kind: other
        ref: "rg checks in this plan's Task 3 acceptance criteria, all passing"
        status: pass
    human_judgment: false
  - id: D10
    description: "The load-failure and save-failure messages read clearly to an administrator in a real Jellyfin dashboard, and neither message is visible to a user watching the page"
    human_judgment: true
    rationale: "The jsdom tests assert a message element holds specific text; whether that text reads well to a person in the real dashboard chrome is a judgment call the automated suite cannot make (02-VALIDATION.md's Manual-Only Verifications). Deferred to end-of-phase UAT per workflow.human_verify_mode=end-of-phase."

duration: 20min
completed: 2026-09-19
status: complete
---

# Phase 02 Plan 02: Save-failure handling and the complete settings-page test suite Summary

**A failed settings save now shows a fixed, credential-free message on both its rejection paths, and the settings-page test suite grew from 5 to 18 `node:test` cases, closing UI-02 and TEST-04.**

## Performance

- **Duration:** 20 min (includes a human-action checkpoint pause for 1Password commit-signing confirmation, per this plan's sequential_execution constraints)
- **Started:** 2026-09-19T07:16:14Z
- **Completed:** 2026-09-19T07:36:01Z
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments

- Closed UI-02: the submit handler's `updatePluginConfiguration` promise now returns out of the outer `.then`, so one `.catch` covers both the handler's own re-fetch and the update call — whichever fails, `#EmbyAuthMigrationSummary` gets the fixed string `Jellyfin cannot save the plugin settings. See the Jellyfin log.`, never the rejection value or the configured URL/API key.
- Added a `.finally` that calls `Dashboard.hideLoadingMsg()` on the submit chain, so the loading indicator no longer stays up after a failed save.
- Grew the settings-page suite from 5 to 18 `node:test` cases: 3 new save-failure tests (Task 1), 10 new tests covering a successful load, the migration list (one entry per user, server order, adjacency, empty, idempotency, markup safety), and Run migration now, success and failure (Task 2).
- Extended `tests/js/testHelpers.js` with `getConfigFailsFromCall` (call-count-aware config failure), `buildDom().close()` (tears down a jsdom window so Run migration now's 3-second `setTimeout` does not hold the test runner open), and `listItemTexts()` (reads the migration list as an ordered array of strings).
- Updated `docs/settings.md`, `docs/development.md`, and `CLAUDE.md` to describe the new failure behavior and the settings-page test suite, without copying the exact message strings.

## Task Commits

Each task was committed atomically. Task 1 followed the RED/GREEN TDD cycle; Task 2 used the break-then-restore discipline this plan's `tdd_discipline` section specifies for tests over already-working behavior.

1. **Task 1: A failed settings save shows a message** — RED: `6998cb5` (`test`), GREEN: `c68ced8` (`feat`)
2. **Task 2: Cover the successful load, the migration list, and Run migration now** — `404fd0a` (`test`)
3. **Task 3: Bring the documentation up to the new behaviour** — `43ff594` (`docs`)

**Plan metadata:** committed separately after this SUMMARY.

_Note: this plan paused once, at Task 1's RED-phase commit, on a `checkpoint:human-action` — `git commit` (SSH-signed through 1Password) failed twice with `error: 1Password: failed to fill whole buffer`, a Touch ID approval that did not complete. Per the project's global instruction on 1Password prompts, the plan stopped rather than retry blindly, bypass signing, or run `op whoami`. The maintainer confirmed they were at the keyboard; the identical commit then succeeded on retry with no code changes. This is documented as normal flow, not a deviation._

## Files Created/Modified

- `tests/js/testHelpers.js` — `getConfigFailsFromCall` (Task 1); `buildDom().close()` and `listItemTexts()` (Task 2).
- `tests/js/configPage.test.js` — 3 save-failure tests (Task 1); 10 tests for load, the migration list, and Run migration now (Task 2). 8 → 18 total.
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` — the submit handler's `.catch`/`.finally` (UI-02, Task 1). No change from Task 2 — its ten new tests cover already-working code.
- `docs/settings.md` — a paragraph describing the load-failure and save-failure behavior, without the exact message strings.
- `docs/development.md` — the `mise run test` row updated to name all four things it runs; a new "Settings-page tests" section naming `tests/js/`, the runner, and the no-path-argument rule.
- `CLAUDE.md` — the `mise run test` line in `## Commands` updated to match; `tests/js/` added to `## Layout`.

## Decisions Made

- **`getConfigFailsFromCall`, not a second stub factory.** The plan required the config fetch to succeed once (the submit handler's own re-fetch is call 2) and then fail — a call-count-aware flag on the existing `stubApiClient` factory does this without a second factory shape, per the plan's explicit instruction to extend the existing one.
- **`docs/settings.md` avoids markdown bold around "Save" in the off/on sentence.** The plan's acceptance-criteria regex (`turns .*Save off|Save is turned off|turns off .*Save`) needs the literal substring `Save off` contiguous in the file; `**Save**` breaks that with asterisks between the word and " off". Plain text (`turns Save off`) satisfies both the regex and ordinary prose.
- **Task 2's ten tests used break-then-restore, not RED-first**, because they cover behavior that already worked before this plan (the migration list and Run migration now, both already implemented and already `.catch`-wrapped) — exactly the case this plan's own `tdd_discipline` section and 02-01's Task 3 precedent call for.

## Deviations from Plan

None — plan executed exactly as written. The commit-signing checkpoint (see Task Commits note) is documented as normal flow per the authentication-gates protocol, not a deviation.

## RED-Phase and Teeth-Proof Observations (TDD discipline)

**Task 1, all three new tests:** observed red as unhandled promise rejections against the unfixed submit handler (no `.catch` anywhere), matching the exact signature `02-RESEARCH.md` Pitfall 4 predicts. Committed skipped (`{ skip: 'RED — unskipped in the GREEN commit' }`), then unskipped after the `.catch`/`.finally` was added; reran green (8/8).

**Task 2, four break-then-restore teeth proofs, each confirmed by a scratch-copy diff before restoring:**

- Removed `list.replaceChildren()` in `loadEmbyAuthMigration` → `loading the migration status twice does not accumulate entries` went red (17 pass, 1 fail).
- Inverted the `ReadyToMove` check in the list-item text assignment (`!user.ReadyToMove` instead of `user.ReadyToMove`) → three tests asserting readiness text went red: `the migration list shows one entry per user`, `the list keeps the server order`, `a user name containing markup characters renders as text` (15 pass, 3 fail).
- Removed the `.catch` on the migration-status request → `a failed migration status shows its message` went red (17 pass, 1 fail).
- Removed the `.catch` on the Run-migration POST → `a failed Run migration now shows its message` went red (17 pass, 1 fail).

Each break was restored from a scratch-directory copy (`diff` confirmed byte-identical) before the next break and before the final commit; a full `node --test` run after the last restore confirmed 18/18 green, and two consecutive runs reported the same pass count (no shared DOM/stub state between tests).

## Issues Encountered

- `git commit` (SSH-signed through 1Password) failed twice mid-Task-1 with `error: 1Password: failed to fill whole buffer` — a Touch ID approval that did not complete, not a sign-out. Resolved via a `checkpoint:human-action` (see Task Commits note); no code or configuration change was needed, and no retry loop or signing bypass was used.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- `mise run test` (4 entries: `dotnet test`, `bats tests/scripts`, `npm --prefix tests/js ci`, `node --test`), `mise run lint`, and `mise run e2e` (27/27) all pass after every task commit and at plan completion.
- TEST-04 is now fully complete (shared with plan 02-01, which built the harness and the load-failure half; this plan completed the save-failure half and the migration-section coverage).
- Plan 02-03 (FPRT-02, `EmbyVerifiedPasswords.cs`) is unaffected — this plan touched no file it declares, and this plan's own `files_modified` list was not exceeded.
- No blockers for 02-03 or Phase 3 (UI-03's migration-list rewrite): D-11 held — no test in this suite asserts the 3-second reload delay.

## Self-Check: PASSED

All modified files verified present on disk with the expected content (`tests/js/testHelpers.js`, `tests/js/configPage.test.js`, `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html`, `docs/settings.md`, `docs/development.md`, `CLAUDE.md`). All four task commits (`6998cb5`, `c68ced8`, `404fd0a`, `43ff594`) verified present in `git log`. Re-ran `node --test` (18/18 pass, twice consecutively with the same pass count), `mise run test`, `mise run lint`, and `mise run e2e` (27/27 pass) at plan completion — all green.

---
*Phase: 02-safe-failures-for-the-fingerprint-file-and-settings*
*Completed: 2026-09-19*
