---
gsd_state_version: 1.0
milestone: v0.9.0.0
current_phase: 5
current_phase_name: Public Repository
status: planning
stopped_at: Phase 04 complete, ready to plan Phase 5
last_updated: "2026-09-20T22:38:27.394Z"
last_activity: 2026-09-20
last_activity_desc: Phase 04 complete, transitioned to Phase 5
state_head: e0efc7753af4e4dfa32a0b8b69ba33da038d93c3
progress:
  total_phases: 6
  completed_phases: 4
  total_plans: 22
  completed_plans: 22
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-09-19)

**Core value:** A user moves from Emby to Jellyfin without a password reset, and no password that Emby did not verify ever opens an account.
**Current focus:** Phase 04 — The Fingerprint Store, and Emby Traffic Under Failure

## Current Position

Phase: 5 — Public Repository
Plan: Not started
Status: Ready to plan
Last activity: 2026-09-20 — Phase 04 complete, transitioned to Phase 5

Progress: [███░░░░░░░] 2/6 phases complete — 7 plans executed

## Performance Metrics

**Velocity:**

- Total plans completed: 22
- Average duration: -
- Total execution time: 0.0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01 | 4 | - | - |
| 02 | 3 | - | - |
| 03 | 7 | - | - |
| 04 | 8 | - | - |

**Recent Trend:**

- Last 5 plans: -
- Trend: -

*Updated after each plan completion*
**Per-Plan Metrics:**

| Plan | Duration | Tasks | Files |
|------|----------|-------|-------|
| Phase 01 P01 | 26min | 2 tasks | 4 files |
| Phase 01 P02 | 19min | 3 tasks | 2 files |
| Phase 01-account-creation-and-login-security P03 | 25min | 2 tasks | 6 files |
| Phase 01 P04 | 20min | 3 tasks | 6 files |
| Phase 02 P01 | 22min | 2 tasks | 8 files |
| Phase 02 P02 | 20min | 3 tasks | 6 files |
| Phase 02 P03 | 28min | 2 tasks | 3 files |
| Phase 03 P01 | 34min | 3 tasks | 11 files |
| Phase 03 P02 | 16min | 3 tasks | 5 files |
| Phase 03 P03 | 25min | 3 tasks | 10 files |
| Phase 03-migration-status-and-target P04 | 36min | 3 tasks | 22 files |
| Phase 03-migration-status-and-target P05 | 24min | 3 tasks | 3 files |
| Phase 03 P06 | 25min | 2 tasks | 6 files |
| Phase 03 P07 | 23min | 3 tasks | 10 files |
| Phase 04 P01 | 18min | 2 tasks | 8 files |
| Phase 04 P02 | 8min | 2 tasks | 2 files |
| Phase 04 P03 | 55min | 3 tasks | 4 files |
| Phase 04 P04 | ~7min | 2 tasks | 2 files |
| Phase 04 P05 | 35min | 2 tasks | 8 files |
| Phase 04 P06 | 50min | 3 tasks | 4 files |
| Phase 04 P07 | 45min | 2 tasks | 3 files |
| Phase 04-emby-traffic-under-load-and-failure P08 | 35 min | 2 tasks | 2 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- Roadmap: The security fixes go first (maintainer priority, 2026-09-17). Phase 1 has AUTH-04, AUTH-03, AUTH-01, AUTH-02, and TEST-01. Phase 2 has FPRT-02, UI-01, UI-02, and TEST-04.
- Roadmap: FPRT-03 and UI-03 share Phase 3, so the `GET /EmbyAuth/Migration` response changes once.
- Roadmap: PUB-04 gets its second catalog version from a rehearsal release, version 0.9.0.0, before version 1.0.0.0 (Phase 6).
- Roadmap: The release tags follow the existing `v<version>` rule (`docs/development.md:30`), so PUB-05 publishes tag `v1.0.0.0`.
- Roadmap: The Pages manifest comes after the repository is public (Phase 6), because Jellyfin downloads the manifest and the zip without GitHub credentials (`.planning/codebase/CONCERNS.md:133`).
- [Phase 01]: Restructured CreateAccountAsync to log exactly one Error entry per failure (save vs. delete), resolving an internal plan inconsistency between the illustrative two-catch shape and the explicit 'exactly one Error entry' acceptance criterion — The delete is attempted at most once and never retried (D-03); logging both failures independently would duplicate the same incident
- [Phase 01]: RED-phase TDD commits use [Fact(Skip=...)] then unskip in the GREEN commit, because the repo's pre-commit hook blocks any commit that leaves a test failing — Preserves the required test-then-feat commit ordering and CLAUDE.md's no-hook-bypass rule; RED was independently confirmed locally before each Skip
- [Phase 01]: [Phase 01]: Broke and restored one guard/write per named group (blank-password guard, Emby-status guard, LoginAction.Deny guard, hash write, fingerprint ordering) to prove each new AUTH-03/AUTH-01 test would catch a regression, since these tests cover already-working behavior rather than new code
- [Phase 01]: D-05 confirmed: deleted MigrationMode.JellyfinPasswordFirst with no compatibility path — Human selected 'proceed' at the checkpoint, accepting that an install still holding the value loses its Emby URL and API key at next load (D-07)
- [Phase 01]: Phase 01: User-directed scope change at the 01-04 Task 3 checkpoint — removed the settings-page screenshot entirely (README.md and CLAUDE.md references stripped too) instead of retaking it — Avoids re-establishing the screenshot-maintenance rule in CLAUDE.md for a single README image
- [Phase 02]: Approved jsdom@28.1.0 at the Task 1 legitimacy checkpoint (2026-09-19) — SUS/too-new verdict reflected the latest dist-tag (30.1.0), not the seven-month-old 28.1.0 pin; no postinstall script at any version, canonical jsdom/jsdom repo, test-only dependency never shipped
- [Phase 02]: Three of Task 3's four new tests proven by remove-and-restore against Task 2's already-working fix, rather than an artificial pre-implementation red — Following Phase 01's established precedent for tests that cover already-working behavior rather than new code
- [Phase 02]: getConfigFailsFromCall (call-count-aware config failure flag) added to the ApiClient stub instead of a second stub factory, so a single test can let the pageshow fetch succeed while the submit handler's own re-fetch rejects
- [Phase 02]: docs/settings.md's Save-off sentence avoids markdown bold around "Save" so the plain-text acceptance-criteria regex matches the literal file content
- [Phase 02]: Task 2's ten new settings-page tests used break-then-restore rather than RED-first, since they cover already-working migration-list and Run-migration-now behavior — per this plan's tdd_discipline section and 02-01's established precedent
- [Phase 02]: [Phase 02]: Load() returns Dictionary<Guid, string>? — null only on a failed read — so Record and Matches can tell a failed read from a genuinely empty file without a second field or flag
- [Phase 02]: [Phase 02]: No read-failure flag added to EmbyVerifiedPasswords in Phase 2, per the plan's explicit instruction — Phase 3 adds it alongside the single GET /EmbyAuth/Migration response change FPRT-03 and UI-03 share
- [Phase 02]: [Phase 02]: Matches() kept as a single return expression using Load() is { } fingerprints && ... pattern matching, avoiding the ?.-with-out-var construct
- [Phase 03]: Task 3's TEST-02 coverage tests were proven by temporarily breaking DefaultLoginMethod.MoveAsync's guard and EmbyLoginMethodUsers.ListAsync's filter/ordering, confirming each new test caught the regression, then restoring both files to their exact committed state — Both classes already worked correctly in production and needed coverage, not a fix, matching the phase 01/02 break-and-restore precedent for already-working behavior
- [Phase 03]: The second-target-provider theory row in DefaultLoginMethodTests.cs is deliberately skipped and named for plan 04 task 2 to unskip — DefaultLoginMethod.MoveAsync has no target parameter today and always writes DefaultLoginMethod.ProviderId; the assumption-delta decision in 03-01-PLAN.md records this as the invariant test that goes red once the move target generalizes
- [Phase 03]: [Phase 03]: MIGR-02's TypeVisibilityTests guard covers the whole plugin assembly's exported surface, not only EmbyAuthenticationProvider, resolving the plan's discretion question — a guard on one class catches one mistake, and the rule in .claude/rules/plugin.md is already assembly-wide
- [Phase 03]: [Phase 03]: Confirmed MIGR-02's red run via a compile-error demonstration rather than a passing-build failing-test result — making EmbyAuthenticationProvider public alone fails to compile (CS0051), and making its two internal dependencies public as well cascades into further compile errors instead of converging on a green build; all three files reverted to byte-identical content
- [Phase 03]: [Phase 03]: NoPassword takes precedence over Unknown on MigrationUserState, and the task's scheduled-task worker is found by ScheduledTask type, never by IScheduledTaskWorker.Id — AUTH-06 requires every no-password account named even while the fingerprint file cannot be read; Id is a Jellyfin-assigned REST-route identifier, not the plugin's own task Key, so an Id comparison would silently match nothing
- [Phase 03]: [Phase 03] Task 1's RED commit uses Skip-marked shape-validation tests rather than a non-compiling commit, because prek's pre-commit hook runs mise run test and would block a build failure — Same convention Phase 01 established for the identical hook constraint (STATE.md)
- [Phase 03]: [Phase 03] MoveTargetKind (Move/Remain/Invalid) replaces a nullable string for a resolved move target — Remain (deliberate, silent) and Invalid (misconfiguration, one Error log) need different treatment; collapsing them would either spam the log or hide a real misconfiguration
- [Phase 03]: [Phase 03] LoginMethodMove.ResolveMigrationTarget/ResolvePasswordSetTarget do not check whether Jellyfin currently reports the configured target as enabled; a target that disappears after being saved still resolves to Move and gets written unconditionally — Deliberately deferred to plan 06's save-time refusal per this plan's own Task 1 action text; the runtime skip-and-log behavior for an already-saved, now-disappeared target is not yet implemented anywhere
- [Phase 03-migration-status-and-target]: [Phase 03] RESEARCH.md's node:test mock.timers approach does not work for the polling tests -- jsdom implements window.setInterval by chaining Node's own setTimeout, which mock.timers (mocking the bare setInterval function) never intercepts — Confirmed via jsdom/lib/jsdom/browser/Window.js's timerInitializationSteps, which calls the bare setTimeout even for window.setInterval; fixed with a direct window.setInterval/clearInterval fake installed in beforeParse instead
- [Phase 03-migration-status-and-target]: [Phase 03] Changed stubApiClient's task default from null to an idle never-run fixture, and availableTargets' default from empty to Default-only, so this plan's new task-absent and unknown-target summary conditions do not trip every unrelated pre-existing test — Those defaults predated this plan (01/03) from before the page ever rendered anything from either field; tests of the absent/unknown conditions now pass an explicit override instead
- [Phase 03]: [Phase 03] Task 1's server-side refusal (EmbyAuthPlugin.UpdateConfiguration) closes the save-time migration-target gap only; ChangePassword/MoveAfterLogin/EmbyMigrationTask still resolve a stale-but-non-blank target to Move with no live enabled-list check — Task 2's action text explicitly branches only on MoveTargetKind and explicitly takes no new IUserManager dependency; closing the full resolve-time gap would need IUserManager added to three files not listed in this plan's files_modified — documented as an explicit out-of-scope decision in 03-06-SUMMARY.md per the 03-04 executor's carry-over instruction
- [Phase 03]: [Phase 03-migration-status-and-target]: [Phase 03] The mounted JellyfinSecurity plugin folder is JellyfinSecurity_2.6.1.1, matching what the unpacked meta.json reports, not the release tag's 2.6.1.0 — the plan's own instruction to confirm against meta.json and use what it says covers this mismatch
- [Phase 03]: [Phase 03-migration-status-and-target]: [Phase 03] fetch-jellyfinsecurity.sh's clean action uses trash (macOS-only) safely, because no mise task or CI job ever calls it — only fetch, which needs no destructive delete, runs in any automated path
- [Phase 04]: [Phase 04] Checkpoint resolved proceed: fingerprint store moves to SQLite, one-way for data written after the upgrade; plugin project gains a pinned Microsoft.Data.Sqlite.Core reference — Removes the lock-around-I/O contention and the three documented failure modes of the JSON store (lost record on read failure, every user reported unverified during a read failure, in-memory record lost on restart)
- [Phase 04]: [Phase 04] Task 1's RED phase proved nothing against the pre-rewrite JSON store (storage-engine swap behind an unchanged API); red was proven instead by break-then-restore against the finished SQLite implementation, per human direction — Unskipping all 14 rewritten tests against the unmodified JSON store passed all 14 — no behavior in the <behavior> block is false before the rewrite and true after it
- [Phase 04]: SignOutAsync gained a username parameter rather than computing the fallback name at the AuthenticateAsync call site, keeping the string.IsNullOrEmpty(embyUserName) ? username : embyUserName fallback next to the two log calls that consume it — Matches the plan's instruction to widen SignOutAsync's own embyUserName parameter to string?
- [Phase 04]: Task 2's break-then-restore proof for the SignOutAsync fallback produced a compile-time CS8604 red rather than a runtime test failure, the same precedent Phase 03's MIGR-02 established — A nullable-reference-type check catches a missing fallback before any test can run; still a witnessed red, and the second break (removing the empty-token guard) did fail at runtime as expected
- [Phase 04]: [Phase 04] EmbyUserDirectory now implements IDisposable to release the new SemaphoreSlim (CA1001), an unplanned Rule-3 fix -- the DI container already disposes it as an AddSingleton, so this closes a real resource leak the compiler caught, not new scope
- [Phase 04]: [Phase 04] The fresh-snapshot fast-path test relies on an empty, held response queue so a wrongly-guarded caller fails deterministically (hang or InvalidOperationException-turned-Unavailable) rather than depending on a timeout
- [Phase 04]: [Phase 04] The two-settings concurrency test groups its ten callers contiguously (five then five) rather than interleaved, since SemaphoreSlim's async waiters release in FIFO order in this runtime -- a contiguous grouping is what guarantees exactly two requests
- [Phase 04]: [Phase 04] LogCreateAccountFailed's reworded message states both causes of a failed account creation (a benign concurrent race, or a name Jellyfin rejects) rather than branching on the exception's message string — Jellyfin throws ArgumentException for a duplicate name and an invalid name alike (D-04); the exact wording is the contract plan 04-06 asserts against a live server, recorded verbatim in 04-04-SUMMARY.md
- [Phase 04]: [Phase 04] TEST-05 stays Pending in REQUIREMENTS.md after 04-04, by design — 04-06 also declares TEST-05 for the end-to-end half, and the shared-ID convention (#2388) blocks Complete until every declaring plan has a SUMMARY
- [Phase 04]: [Phase 04] Task 1's RED commit ships the widened three-arg constructor wired to ImportLegacyRecords, with the method body an inert stub, rather than a pre-implementation compile error — Widening the constructor's argument count is signature-incompatible, so no test file referencing the new shape can compile against the old two-arg constructor at all; confirmed red locally against the stub before committing, per repo convention
- [Phase 04]: [Phase 04] Task 2 landed no separate commit; its two tests joined Task 1's RED/GREEN pair since they belong to the same test file and TDD cycle — Task 2's own acceptance criterion requires src/ unchanged for its contribution; its value is the three break-then-restore proofs recorded in 04-05-SUMMARY.md, not new production code
- [Phase 04]: The second bella concurrent-login burst tolerates 200 or 401 (never 500) rather than strict all-200, after mise run e2e deterministically reproduced a 401 caused by JellyfinSecurity's TwoFactorAuthProvider's own IP-based app-password rate limiter tripping under genuine 5-way concurrency — Traced via the live Jellyfin log to a third-party plugin mounted for Phase 3's MIGR-01/02 verification, unrelated to this plugin; the exactly-one-account and never-500 invariants stayed strict
- [Phase 04]: [Phase 04] docker compose exec cannot remove a file from a stopped container; fingerprint_store_remove and fingerprint_store_push's wal/shm cleanup instead run rm -f through a throwaway container sharing the stopped Jellyfin container's volumes via docker run --volumes-from, using nginx:1.30.5-alpine (already pinned for emby-proxy) — Confirmed empirically: docker compose exec refuses a stopped service (service is not running); --volumes-from attaches regardless of running state, and reusing the already-pinned image adds no new dependency
- [Phase 04]: [Phase 04] 80-fingerprint-store.bats builds its legacy JSON fixture from a real record the plugin just wrote, never a hand-authored one — A fingerprint is a SHA-256 of a freshly salted Jellyfin password hash, so it cannot be constructed any other way and still mean anything as a genuine pre-upgrade file
- [Phase 04-emby-traffic-under-load-and-failure]: Task 1 and Task 2's docs/how-it-works.md edits landed in one commit (3ddb2b5), since both tasks touch the same file and the workspace CLAUDE.md asks for batched edits to a single file — Minimizes pre-commit hook runs; CHANGELOG.md, the Task-2-only artifact, still got its own commit (e042269)
- [Phase 04-emby-traffic-under-load-and-failure]: The read-failure Limits entry says the migration list reports readiness unknown, not needing an Emby login, correcting the plan's own must_haves wording against EmbyLoginMethodUsers.DetermineState — RecordsAvailable()==false maps to MigrationUserState.Unknown, not NeedsEmbyLogin (EmbyLoginMethodUsers.cs:98-103); docs/migration.md already documents this as readiness unknown, so the plan's literal phrase would have misstated a verified-but-unreported user as needing to log in again

### Pending Todos

None yet.

### Blockers/Concerns

- Research flags for phase planning (`.planning/research/SUMMARY.md:88`): the fault-injection tool for the load test (Phase 4), the assumption that tags are pushed from `main` behind the CI gate (Phase 5), Pages action SHAs (Phase 6). The `pageshow`-under-jsdom flag is resolved — Phase 2 shipped the suite.
- [Phase 3]: **FPRT-01 must fix a stated-behavior mismatch, not only add a guarantee.** `EmbyVerifiedPasswords.Record()` assigns into the cache at `EmbyVerifiedPasswords.cs:58` before the write at `:62-63`, and `Load()` returns the `_fingerprints` field by reference, so a failed write leaves the cache holding a record the disk does not. `MoveToDefaultLoginMethod` then reads that cache (`MoveToDefaultLoginMethod.cs:48`) after the login, so in `MoveAfterFirstLogin` mode the user **is** moved to Default despite the write failure. Two shipped sentences say the opposite: the log message at `EmbyVerifiedPasswords.cs:128` and `docs/how-it-works.md:49`. Not a privilege escalation — Emby verified that password moments earlier — but Phase 3 must either correct both sentences or move the cache mutation after a successful write. Traced and recorded as O-1 in `02-SECURITY.md`; carried from WR-01 in `02-REVIEW.md`.

### Quick Tasks Completed

| # | Description | Date | Commit | Directory |
|---|-------------|------|--------|-----------|
| 260919-208 | Move settings failure message next to Save and show Save as disabled | 2026-09-19 | 1f47767 | [260919-208-move-settings-failure-message-next-to-sa](./quick/260919-208-move-settings-failure-message-next-to-sa/) |
| 260919-inm | Add a warning icon to the four failure messages on the settings page | 2026-09-19 | 325855e | [260919-inm-add-a-warning-icon-to-the-four-failure-m](./quick/260919-inm-add-a-warning-icon-to-the-four-failure-m/) |
| 260920-3lw | Move jsdom to devDependencies in tests/js | 2026-09-20 | b88045b | [260920-3lw-move-jsdom-to-devdependencies-in-tests-j](./quick/260920-3lw-move-jsdom-to-devdependencies-in-tests-j/) |

## Deferred Items

Items acknowledged and deferred at milestone close, most recent first:

| Category | Item | Status | Deferred At | Milestone |
|----------|------|--------|-------------|-----------|
| *(none)* | | | | |

## Session Continuity

Last session: 2026-09-20T22:09:29.102Z
Stopped at: Phase 04 complete, ready to plan Phase 5
Resume file: None
