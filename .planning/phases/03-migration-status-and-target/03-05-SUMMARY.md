---
phase: 03-migration-status-and-target
plan: 05
subsystem: ui
tags: [jsdom, node-test, settings-page, polling, jellyfin-plugin]

# Dependency graph
requires:
  - phase: 03-01
    provides: "MigrationStatus.RecordsUnavailable and the settings-page warning it needs — this plan's summary-message priority chain keeps it ahead of the new task-absent and unknown-target conditions"
  - phase: 03-03
    provides: "MigrationUserState, MigrationTaskInfo (State/Progress/LastEndTimeUtc/LastResult), and AvailableTargets on GET /EmbyAuth/Migration — the response shape this plan's poll loop, dropdown, and warning all render"
  - phase: 03-04
    provides: "PluginConfiguration.MigrationTarget/PasswordSetTarget, the RemainOnEmbyLoginMethod sentinel, and the EmbyAuthMigration task key this plan's Run handler saves and queues"
provides:
  - "A setInterval-driven poll loop replacing the fixed 3-second reload, following a run to completion by comparing the last observed end time rather than a browser clock, capped to ~20s only while the task has never left Idle"
  - "The EmbyAuthMigrationTarget dropdown above Run migration now, built from AvailableTargets, saved before the task is queued, and refusing to run on an unknown or missing target"
  - "The EmbyAuthMigrationWarning no-saved-password warning and the PasswordSetTarget dropdown in the settings form"
affects: [03-06, 03-07]

# Actuals (#2632)
actuals:
  tokens: 12032
  tasks: 3
  commits: 3

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "A window-level fake setInterval/clearInterval installed per test (tests/js/testHelpers.js installFakeInterval), because jsdom implements window.setInterval internally by chaining Node's bare setTimeout — node:test's mock.timers (which mocks the global setInterval function, not setTimeout) never intercepts it"
    - "A stable stop condition that compares the task's LastEndTimeUtc to a value remembered before the run request, never a browser clock to a server timestamp, closing the window where QueueIfNotRunning returns 204 while the task is still Idle"
    - "A shared appendEmbyAuthTargetChoices helper builds the Default/Remain/rest option list once, reused by both the migration-target and password-set-target dropdowns, with the latter prepending an empty-valued 'Same as the migration target' entry"

key-files:
  created: []
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
    - tests/js/testHelpers.js
    - tests/js/configPage.test.js

key-decisions:
  - "Replaced RESEARCH.md's suggested node:test mock.timers approach with a direct window.setInterval/clearInterval override, discovered necessary only after mock.timers demonstrably failed to intercept jsdom's internally setTimeout-chained interval implementation"
  - "Changed stubApiClient's task default from null to a healthy idle-never-run fixture, and availableTargets' default from an empty list to one holding Jellyfin's Default method, so the two new summary-message conditions this plan adds (task absent, unknown migration target) do not spuriously fire for every pre-existing test that never mentions Task or AvailableTargets"
  - "The migration-target-unknown warning overrides the routine per-user summary text but not a RecordsUnavailable warning, since a data-integrity read failure is more foundational than a misconfigured destination"
  - "Task 1's Run-migration tests needed firePageshow added before clickRunMigration once Task 2 made a successful Run depend on a populated, valid target dropdown — anticipated by the plan's own read_first note that Task 2 touches the existing Run-migration tests"

patterns-established:
  - "Fake interval installed at the window level in beforeParse, before the page's own script runs, so a bare setInterval call inside vm-executed page script resolves to the test's controllable fake rather than a real timer"

requirements-completed: [UI-03, MIGR-01, AUTH-06, FPRT-03]

coverage:
  - id: D1
    description: "The migration list follows a run to its end however long it takes, gives up with a message naming the Jellyfin log if no run begins within ~20s, never stacks a second request, stops when the page is hidden, and reports an unregistered task as absent rather than as an idle never-run task"
    requirement: "UI-03"
    verification:
      - kind: unit
        ref: "configPage.test.js#Run migration now begins polling that issues a second request after one interval"
        status: pass
      - kind: unit
        ref: "configPage.test.js#polling continues across three consecutive Running responses"
        status: pass
      - kind: unit
        ref: "configPage.test.js#polling continues when Idle repeats the pre-run end time"
        status: pass
      - kind: unit
        ref: "configPage.test.js#polling stops once Idle reports a new end time"
        status: pass
      - kind: unit
        ref: "configPage.test.js#polling gives up after about 20 seconds when no run begins"
        status: pass
      - kind: unit
        ref: "configPage.test.js#the page never stacks a second migration-status request while one is pending"
        status: pass
      - kind: unit
        ref: "configPage.test.js#polling starts on load when the task is already running"
        status: pass
      - kind: unit
        ref: "configPage.test.js#pagehide stops polling"
        status: pass
      - kind: unit
        ref: "configPage.test.js#the section reports the migration task as absent when the response carries no task"
        status: pass
    human_judgment: false
  - id: D2
    description: "An administrator picks the migration destination from a dropdown above Run migration now, fed only by Jellyfin-enabled login methods; Run migration now saves the pick before queueing and refuses to start on an unknown or missing target; a settings-form save leaves the migration target untouched"
    requirement: "MIGR-01"
    verification:
      - kind: unit
        ref: "configPage.test.js#the migration target dropdown orders Default, Remain, then the rest"
        status: pass
      - kind: unit
        ref: "configPage.test.js#with only Default enabled the dropdown offers exactly two options"
        status: pass
      - kind: unit
        ref: "configPage.test.js#an unavailable saved target selects nothing and names the Jellyfin log"
        status: pass
      - kind: unit
        ref: "configPage.test.js#Run migration now refuses to start when the saved target is unavailable"
        status: pass
      - kind: unit
        ref: "configPage.test.js#picking a target and clicking Run saves it before queueing the task"
        status: pass
      - kind: unit
        ref: "configPage.test.js#a failed save before Run stops the migration from starting"
        status: pass
      - kind: unit
        ref: "configPage.test.js#a target name with markup characters renders as an option with text only"
        status: pass
      - kind: unit
        ref: "configPage.test.js#submitting the settings form leaves the migration target unchanged"
        status: pass
      - kind: e2e
        ref: "mise run e2e (27/27 passing)"
        status: pass
    human_judgment: false
  - id: D3
    description: "Accounts with no saved password are named and warned about only while at least one is in that state, in wording that follows the configured destination and never claims the plugin refuses the move; the settings form carries a password-set target defaulting to 'Same as the migration target'"
    requirement: "AUTH-06"
    verification:
      - kind: unit
        ref: "configPage.test.js#a no-saved-password account with the Default target gets the blank-password warning"
        status: pass
      - kind: unit
        ref: "configPage.test.js#a no-saved-password account with another target gets the cannot-tell warning"
        status: pass
      - kind: unit
        ref: "configPage.test.js#a no-saved-password account with the Remain target gets no warning"
        status: pass
      - kind: unit
        ref: "configPage.test.js#no no-saved-password accounts means no warning, whatever the target"
        status: pass
      - kind: unit
        ref: "configPage.test.js#the no-saved-password warning never claims the plugin refuses the move"
        status: pass
      - kind: unit
        ref: "configPage.test.js#the password-set dropdown defaults to Same as the migration target"
        status: pass
      - kind: unit
        ref: "configPage.test.js#submitting the settings form saves the picked password-set target"
        status: pass
    human_judgment: false

# Metrics
duration: 24min
completed: 2026-09-20
status: complete
---

# Phase 3 Plan 5: The Settings Page Layout Summary

**A setInterval poll loop that follows the migration task to completion, a migration-target dropdown that saves before Run migration now queues, and a no-saved-password warning that never claims more than it has verified**

## Performance

- **Duration:** 24 min
- **Started:** 2026-09-20T05:05:00Z
- **Completed:** 2026-09-20T05:29:00Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments

- Deleted the fixed 3-second reload and replaced it with a 2-second `setInterval` poll that follows a run for as long as it takes, capped to ~20 polls-with-no-change only during the start window, and stopped on `pagehide`, on a failed Run request, or once the task reports Idle with a new `LastEndTimeUtc`.
- The `EmbyAuthMigrationTarget` dropdown, built from the server-filtered `AvailableTargets`, lets an administrator pick the migration destination; `Run migration now` saves that pick through the plugin configuration API before it queues `EmbyMigrationTask`, and refuses to run when nothing valid is picked.
- The `EmbyAuthMigrationWarning` element names no-saved-password accounts and warns in wording that follows the configured target — the specific blank-password claim only for Jellyfin's Default method, "cannot tell" for any other, nothing at all for Remain on Emby Login — and the settings form's new `PasswordSetTarget` dropdown defaults to "Same as the migration target".
- FPRT-03 (the records-unavailable warning, shipped in plan 01) keeps its priority ahead of the two new summary-message conditions this plan adds; no plan-05 change touches its behavior.

## Task Commits

Each task was committed atomically:

1. **Task 1: The list follows the task to the end of its run** — `44c3227` (feat)
2. **Task 2: The migration target dropdown, and Run migration now saving it first** — `3ada8e3` (feat)
3. **Task 3: AUTH-06's warning, and the password-set target** — `785bc9c` (feat)

_All three tasks carried `tdd="true"`. Each was proven by writing the test and the implementation together, running the full acceptance-criteria gate list and `mise run test` before committing — following the same convention 03-03/03-04 used for changes too structural to split into a meaningful intermediate red without contortion._

## Files Created/Modified

- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` — the poll loop, the two new dropdowns, the no-saved-password warning, and the corrected field descriptions
- `tests/js/testHelpers.js` — `installFakeInterval`, `DEFAULT_TASK`/`DEFAULT_AVAILABLE_TARGETS` stub defaults, `taskSequence`/`migrationStatusHangs` options, `tickPoll`, `selectOptions`, and the exported `DEFAULT_PROVIDER_ID`/`REMAIN_ON_EMBY_LOGIN_METHOD` constants
- `tests/js/configPage.test.js` — 24 new jsdom tests across the three tasks, plus `firePageshow` added to the pre-existing Run-migration tests once Task 2 made a successful Run depend on a populated target dropdown

## Decisions Made

- **Abandoned RESEARCH.md's `node:test` `mock.timers` recommendation for the poll-loop tests.** A first implementation using `t.mock.timers.enable({ apis: ['setInterval'] })` failed every polling test with a 0-vs-1 call-count mismatch. Reading `jsdom/lib/jsdom/browser/Window.js` showed `window.setInterval` is implemented by chaining calls to the bare `setTimeout` (`timerInitializationSteps`), never a native `setInterval` — so `mock.timers`, which mocks the global `setInterval` function, never intercepts it. Mocking `setTimeout` instead would have also frozen `flush()`'s own real-timer-based macrotask boundaries. The fix installs a small fake `setInterval`/`clearInterval` directly on the jsdom `window` object in `beforeParse`, before the page's script runs, leaving Node's real timers (and `flush()`) untouched.
- **Changed two stub defaults that predate this plan.** `stubApiClient`'s `task` option defaulted to `null` and `availableTargets` to `[]` (chosen by plans 01/03, before the page rendered anything from either field). Once this plan's `loadEmbyAuthMigration` started treating a null task as "absent" and an unmatched saved target as "not enabled", every pre-existing test that omitted those options started tripping the new conditions. Changed the defaults to a healthy idle-never-run task and a list holding only Jellyfin's Default method; tests of the absent/unknown conditions pass an explicit override.
- **The unknown-target warning does not override a RecordsUnavailable warning.** Both are top-of-section conditions rendered through the same `#EmbyAuthMigrationSummary` element; a fingerprint-file read failure is a more foundational data-integrity concern than a misconfigured destination, so `loadEmbyAuthMigration` only shows the unknown-target message when records are available.
- **Fixed the stale field description under Run migration now**, which still named the pre-plan-04 task ("Move Emby users to the Default login method") and unconditionally "the Default login method" — now reads "the migration target above" and the current task name, "Finish the Emby migration".

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The RESEARCH.md-recommended `node:test` mock-timer approach does not intercept jsdom's poll interval**
- **Found during:** Task 1, first test run
- **Issue:** Every new polling test failed with a call-count mismatch (e.g. `0 !== 1`) after ticking the mocked clock; `t.mock.timers.enable({ apis: ['setInterval'] })` had no effect on the page's actual `setInterval` calls.
- **Fix:** Installed a direct `window.setInterval`/`window.clearInterval` fake in `testHelpers.js`'s `buildDom` (via a new `installFakeInterval` helper), returned as `interval` from `buildDom` and driven by a new `tickPoll(interval, times)` helper. `flush()` and all other real-timer usage are unaffected.
- **Files modified:** `tests/js/testHelpers.js`, `tests/js/configPage.test.js`
- **Verification:** All 9 Task 1 polling tests pass; full suite green.
- **Committed in:** `44c3227` (Task 1)

**2. [Rule 3 - Blocking] Pre-existing stub defaults collided with this plan's new summary-message conditions**
- **Found during:** Task 1, before adding the "task absent" branch
- **Issue:** `stubApiClient`'s default `task: null` and `availableTargets: []` (set by plans 01/03) made every unrelated test that omitted those options trip the new "task not registered" and (in Task 2) "target not enabled" messages, overriding the summary text those tests actually asserted on.
- **Fix:** Changed the defaults to `DEFAULT_TASK` (an idle, never-run worker) and `DEFAULT_AVAILABLE_TARGETS` (Jellyfin's Default method only); `DEFAULT_CONFIG` gained matching `MigrationTarget`/`PasswordSetTarget` defaults. Tests of the absent/unknown conditions pass explicit overrides (`task: null`, an unmatched `MigrationTarget`).
- **Files modified:** `tests/js/testHelpers.js`
- **Verification:** All 43 pre-existing tests plus the new tests pass unchanged in intent.
- **Committed in:** `44c3227` (Task 1), extended in `3ada8e3` (Task 2)

**3. [Rule 1 - Bug] Corrected the stale Run-migration field description**
- **Found during:** Task 2
- **Issue:** The field description under Run migration now still said "Moves each user marked 'ready' to the Default login method" and named the task "Move Emby users to the Default login method" — both wrong since plan 04 generalized the destination and renamed the task to "Finish the Emby migration".
- **Fix:** Reworded to "Moves each user marked 'ready' to the migration target above" and the current task name.
- **Files modified:** `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html`
- **Verification:** Read against the current task Name in `EmbyMigrationTask.cs`.
- **Committed in:** `3ada8e3` (Task 2)

---

**Total deviations:** 3 auto-fixed (1 bug in the test infrastructure approach, 1 blocking test-default collision, 1 doc-accuracy bug). **Impact:** All three were necessary corrections surfaced by implementing the plan as written; none added scope beyond what the plan specified.

## Issues Encountered

None beyond the deviations documented above.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- The settings page now renders everything plans 01, 03, and 04 built: the records-unavailable warning, the four-state migration list, the task's live progress, the migration-target and password-set-target dropdowns, and the no-saved-password warning.
- Plan 06 adds the server-side save-time refusal for an unavailable migration target — this plan's client-side refusal (`Run migration now` checking the dropdown value) is explicitly defense in depth, not the control, per the plan's own threat register (T-03-02).
- `LoginMethodMove.ResolveMigrationTarget`/`ResolvePasswordSetTarget` still do not check whether Jellyfin currently reports a saved target as enabled at read time (noted by plan 04 as deferred to plan 06); this plan's browser-side "not enabled" detection reads `AvailableTargets` fresh on every poll, so the settings page already reflects a disappeared target even though the server-side resolver does not yet.
- No blockers for 03-06 or 03-07.

---
*Phase: 03-migration-status-and-target*
*Completed: 2026-09-20*

## Self-Check: PASSED

All three modified files exist on disk, and all three task commits (`44c3227`, `3ada8e3`, `785bc9c`) are present in `git log`. `node --test` (58/58), `mise run test` (dotnet 179/179, bats 8/8, node 58/58), `mise run lint` (dotnet format, shellcheck, shfmt, actionlint, zizmor), and `mise run e2e` (27/27) all ran green after the final task. Every acceptance-criteria grep gate for all three tasks was re-run and passed.
