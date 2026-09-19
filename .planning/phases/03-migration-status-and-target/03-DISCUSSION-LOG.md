# Phase 3: Migration Status and Target - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-09-19
**Phase:** 3-Migration Status and Target
**Areas discussed:** Migration response and task state, The move-target setting (MIGR-01), E2E second login method, The manual-flip finding, Leftovers

---

## Migration response and task state

### Per-user state shape

| Option | Description | Selected |
|--------|-------------|----------|
| One state enum | `MigrationUser(Name, State)` with Ready / NeedsEmbyLogin / NoPassword. One field, one switch on the page. | ✓ |
| Keep ReadyToMove, add HasPassword | Additive; two booleans, with one impossible pair the page must not render. | |

**User's choice:** One state enum.

### Following the task run

| Option | Description | Selected |
|--------|-------------|----------|
| Poll the same endpoint | Re-request `GET /EmbyAuth/Migration` on an interval until the task is no longer running. Testable under jsdom with a fake timer. | ✓ |
| Jellyfin's ScheduledTasksInfo websocket | Subscribe as Jellyfin's own Scheduled Tasks dashboard does. No polling, live progress, but a socket API this repo has never used. | |

**User's choice:** Poll the same endpoint.

### Task-state payload

| Option | Description | Selected |
|--------|-------------|----------|
| State + progress + last end time | Closes the queue-to-start gap after `QueueIfNotRunning` by comparing the last end time against the moment the POST returned. | ✓ |
| State + progress only | Fewer fields; the stop condition rests on a timing assumption rather than a server fact. | |
| A running boolean only | Smallest thing satisfying the criterion's words. No progress, no failed-vs-completed distinction, same start-gap problem. | |

**User's choice:** State + progress + last end time.
**Notes:** Raised because `POST /EmbyAuth/Migration/Run` returns 204 immediately while the task may still report `Idle`, so a naive poll would stop early and render the old list.

### Read-failure display

| Option | Description | Selected |
|--------|-------------|----------|
| Message + Unknown state per user | Top-level flag drives the message; a fourth state, Unknown, keeps the accurate account names visible without claiming a readiness the plugin cannot determine. | ✓ |
| Message, and hide the user list | Nothing misleading can appear; the administrator also loses accurate names and counts. | |
| Message above the list, list unchanged | Smallest change; the list actively states something untrue about every account. | |

**User's choice:** Message + Unknown state per user.

### Poll limit

| Option | Description | Selected |
|--------|-------------|----------|
| Cap the start, follow the run | Poll every 2s; follow a Running task however long it takes; cap only the ~20s start window. | ✓ |
| One fixed cap for everything | One number, one rule, one test; a long migration ends with a stale list and a reload prompt. | |

**User's choice:** Cap the start, follow the run.

### AUTH-06 no-password warning

| Option | Description | Selected |
|--------|-------------|----------|
| Only when needed, target-aware | Renders only while a NoPassword account exists; makes the specific blank-password claim only when the target is Jellyfin's Default. | ✓ |
| Only when needed, one fixed text | Matches the criterion word for word; states something about Default on a server whose target is not Default. | |
| Always visible in the section | Static prose; no branching, always on screen even where no account is ever passwordless. | |

**User's choice:** Only when needed, target-aware.

---

## The move-target setting (MIGR-01)

**Notes:** The first framing of this area was rejected as unclear and re-explained in plain terms before the questions were re-put.

### Bad target at run time

**User's choice (free text):** Logins should keep working; the move should log an error and fail.

### Dropdown contents

**User's choice (free text):** Two fixed options plus the enabled others — "Move to Default" (always present) and "Remain on Emby Login", then any enabled method that is neither Default nor EmbyAuth. Label form: "Move to {plugin name}".
**Notes:** This introduced "Remain on Emby Login", a no-move target that neither MIGR-01 nor criterion 6 anticipates.

### The password-set path under "Remain"

| Option | Description | Selected |
|--------|-------------|----------|
| Remain means remain, everywhere | One rule, no exceptions; an administrator's newly set password is saved but inert until the target changes. | |
| The password-set path always moves to Default | Keeps the per-account escape route; introduces a hidden fallback to a method the setting never named. | |
| A second destination setting | A `PasswordSetTarget` dropdown with the same options plus "Same as the migration target" as the default. | ✓ |

**User's choice:** A second destination setting.
**Notes:** Asked for as "can we allow a destination selection for password-set path? otherwise remain means remain in this case." The cost was stated before selection: MIGR-01 and criterion 6 say "setting" in the singular and must be reworded.

### MigrationMode overlap

| Option | Description | Selected |
|--------|-------------|----------|
| Keep both, explain the pair | MigrationMode answers when, the target answers where; the page explains the combination. | ✓ |
| Keep both, and grey out MigrationMode | Clearer on screen; a second piece of page state to cover in jsdom. | |
| Drop KeepEmbyInCharge | Fewer overlapping settings; a second breaking settings change this milestone. | |

**User's choice:** Keep both, explain the pair.

### Save-time validation

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, refuse the save | Dropdown and save both check the enabled list, so an API call cannot store a nonexistent target. | ✓ |
| No, accept any value | Dropdown is the only guard; a typo is discoverable only from the log after a login. | |

**User's choice:** Yes, refuse the save.

### Task Key

| Option | Description | Selected |
|--------|-------------|----------|
| Change it now | Never released, no default triggers, three lines move; after v1.0.0 it breaks real installs. Follows Phase 1 D-07's reasoning. | ✓ |
| Keep the Key, rename everything else | Nothing keyed by string can break; "Default" survives in one administrator-visible place. | |

**User's choice:** Change it now.

### Naming

| Option | Description | Selected |
|--------|-------------|----------|
| Migration wording | `LoginMethodMove`, `MoveAfterLogin`, `EmbyMigrationTask`, Key `EmbyAuthMigration`, task Name "Finish the Emby migration". | ✓ |
| Target wording | `TargetLoginMethod`, `MoveAfterLogin`, `MoveEmbyUsersTask`, Key `EmbyAuthMoveUsers`. | |
| You decide at plan time | Record only the rule; let the planner pick names. | |

**User's choice:** Migration wording.

---

## E2E second login method

**Notes:** This area required two corrections of record. First, a web search reaching only v2.5.22 suggested JellyfinSecurity could not load on Jellyfin 12, which cast doubt on PROJECT.md's 2026-09-18 measurement. The user corrected this — v2.6.0 (2026-09-11) added Jellyfin 12 support — and the doubt was retracted. The user then asked for re-verification and noted v2.6.1; the GitHub API confirmed v2.6.1 (2026-09-14) with a `-jf12` zip of 24,539,922 bytes and published checksums. Second, the question itself was rejected as unclear and re-explained before being re-put.

### Source of the third login method

| Option | Description | Selected |
|--------|-------------|----------|
| JellyfinSecurity in its own stack | Real plugin, isolated container, interference risk removed. Costs a second container and its wizard setup. | |
| JellyfinSecurity in the shared stack | Simplest wiring; rests on the unverified assumption that the plugin is inert at defaults. | ✓ |
| A test-only provider in this repo | Small, self-contained, cannot disturb the pipeline; a stand-in rather than a real plugin. | |
| Test-only provider, JellyfinSecurity in docs | Both automated coverage and the real-plugin claim; maintains a provider nobody ships. | |

**User's choice:** JellyfinSecurity in the shared stack.
**Notes:** Chosen on a principled argument rather than cost — "Both EmbyAuth and Jellyfinsecurity plugins need to run side by side to allow a migration to end on Jellyfinsecurity." The shared container is the production topology, so any interference found is a real finding about a supported path.

### CI coverage

| Option | Description | Selected |
|--------|-------------|----------|
| Documentation only | No CI cost; the claim rests on a manual check that can go stale. | |
| An opt-in e2e test | Real coverage run by hand before a release; no download on every CI run. | |
| A full e2e test in CI | Strongest guarantee; every run pays the ~23 MiB download. | ✓ |

**User's choice:** A full e2e test in CI.
**Notes:** Conditioned on scope — "as long as our e2e test is just performing a basic password auth in jellyfin security. Which is all we would have from a emby migration." Reinforced afterwards: "We don't need to validate every path available in jellyfin security... what happens after the migration is done is all handled by jellyfinsecurity or another plugin for that matter." This became the test's scope line.

---

## The manual-flip finding

| Option | Description | Selected |
|--------|-------------|----------|
| Close it — already covered | AUTH-06 warns on the settings page and DOCS-05 documents the behaviour; nothing can prevent the action. | ✓ |
| Log it at Error | Visible in the log; needs new persistent state and must not fire on the plugin's own moves. | |
| Show it on the settings page | Visible where migration is managed; same new state plus new page state. | |
| Push it past v1.0.0 | Record as a known limitation; revisit as an upstream Jellyfin request. | |

**User's choice:** Close it — already covered.
**Notes:** The item arrived from 02-CONTEXT as assigned to Phase 3, but no requirement was ever added to ROADMAP.md or REQUIREMENTS.md, and AUTH-06's 2026-09-18 rewrite ruled out the random-password mitigation that was the original proposal.

---

## Leftovers

### Manual run destination

**User's choice (free text):** "manual migration should allow a selection of where to migrate to, which is selected before clicking the manual migration button."

| Option | Description | Selected |
|--------|-------------|----------|
| Save the pick, then run | No new state, no duplicated move logic, identical from the Dashboard; a one-off run also changes the saved target. | ✓ |
| A one-shot run target | No settings mutation; needs shared out-of-band state and a stale-stash rule. | |
| The controller does the move itself | Clean parameter passing; duplicates the move loop and breaks the UI-03 task-state polling. | |

**User's choice:** Save the pick, then run.
**Notes:** Raised because Jellyfin's scheduled task API accepts no run parameters — `IScheduledTask.ExecuteAsync` takes only a progress reporter and a cancellation token.

### Where the target control lives

| Option | Description | Selected |
|--------|-------------|----------|
| In the Migration section | One control above Run migration now; no two dropdowns that can disagree. | ✓ |
| In the settings form, read-only beside the button | Nothing saves as a side effect; changing destination needs a scroll and a save first. | |

**User's choice:** In the Migration section.

### Fingerprint write failure

| Option | Description | Selected |
|--------|-------------|----------|
| Log only, as FPRT-01 says | The in-memory record is real and those users are ready now; the next successful write clears it. | ✓ |
| Show it on the page too | Actionable — finish now, do not restart — but beyond FPRT-01, so REQUIREMENTS.md gains a sentence. | |

**User's choice:** Log only.
**Notes:** Asked twice. The first attempt drew "I thought we dropped the fingerprint file?" and the second "I don't remember", so the file's purpose, its write path, and the FPRT-01 mismatch were re-explained from the top before the question was re-put. The user's selection carried a standing objection — "this is why I'd rather not even need this file" — which is recorded in CONTEXT.md §Specific Ideas rather than reopening Phase 2 D-01.

---

## Claude's Discretion

- Standing up the SQLite in-memory `JellyfinDbContext` for TEST-02 and TEST-03, including whether EF Core's InMemory provider is genuinely unusable given `ExecuteUpdateAsync`.
- The shape of the `ITaskManager` fake, following the hand-written-double precedent.
- Whether MIGR-02's visibility test guards only `EmbyAuthenticationProvider` or every internal type.
- Where JellyfinSecurity is installed in the e2e suite lifecycle, given its install needs a Jellyfin restart.
- Whether the task `Key` change earns a `CHANGELOG.md` entry.
- Exact message and documentation wording throughout.
- Test file names and their split across files.

## Deferred Ideas

- Drop `MigrationMode.KeepEmbyInCharge` — rejected for this milestone; revisit at a major version.
- A one-shot run destination that does not change the saved setting — rejected; revisit if the side effect proves confusing.
- Greying out `MigrationMode` while the target is "Remain on Emby Login" — rejected in favour of prose.
- Logging or flagging the manual flip — rejected; revisit only if Jellyfin adds a veto.
- Retiring the fingerprint file — blocked upstream, not by this plugin.
