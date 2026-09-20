---
phase: 04-emby-traffic-under-load-and-failure
plan: 02
subsystem: auth
tags: [emby-client, sign-out, session-hygiene, xunit]

# Dependency graph
requires:
  - phase: 04-emby-traffic-under-load-and-failure
    provides: "04-01's SQLite fingerprint store (sibling plan, same wave; not touched here)"
provides:
  - "EmbyClient.AuthenticateAsync ends the Emby session for every token it can read, including a response with no user name"
  - "SignOutAsync's embyUserName parameter is string?, falling back to the typed username so every sign-out log entry names someone"
affects: [04-08]

# Actuals (#2632)
actuals:
  tokens: 1588
  tasks: 2
  commits: 3

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Compile-error red for a nullable-reference-type regression (Phase 03's MIGR-02 precedent), used here for the SignOutAsync fallback"

key-files:
  created: []
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs

key-decisions:
  - "SignOutAsync gained a username parameter rather than computing the fallback name at the AuthenticateAsync call site, so the fallback (string.IsNullOrEmpty(embyUserName) ? username : embyUserName) stays next to the log calls that need it"
  - "The charset test (Login_ReturnsNull_WhenEmbyResponseHasAnInvalidCharset) gained an explicit Assert.Single(handler.Requests), turning D-02's documented limit into an assertion rather than an implication"

patterns-established:
  - "Break-then-restore proof for already-working coverage: remove the guard/fallback under test, run the suite, confirm the new test (and any collateral existing test) goes red, then git checkout -- the exact committed file rather than hand-reverting the edit"

requirements-completed: [AUTH-05]

coverage:
  - id: D1
    description: "A login response carrying an AccessToken and no user name sends the sign-out (login + Sessions/Logout) and still refuses the Jellyfin login"
    requirement: "AUTH-05"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs#Login_EndsTheEmbySession_WhenTheResponseHasATokenAndNoUserName"
        status: pass
      - kind: e2e
        ref: "bats:e2e/10-login-checks.bats#the plugin ends its Emby session after each login"
        status: pass
    human_judgment: false
  - id: D2
    description: "A response with no token, and one the plugin cannot parse, each still send exactly one request (D-02's documented limit, not a defect)"
    requirement: "AUTH-05"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs#Login_ReturnsNull_WhenEmbyResponseHasNoUserName"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs#Login_ReturnsNull_WhenEmbyResponseHasAnInvalidCharset"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs#Login_DoesNotEndASession_WhenEmbyReturnsNoToken"
        status: pass
    human_judgment: false
  - id: D3
    description: "The sign-out log always names someone (the Emby name, or the typed name when Emby's response carries none), and never logs the access token"
    requirement: "AUTH-05"
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs#Login_NamesTheTypedUserName_WhenTheSignOutFailsForAResponseWithoutAUserName"
        status: pass
    human_judgment: false

# Metrics
duration: 8min
completed: 2026-09-20
status: complete
---

# Phase 4 Plan 2: Every Readable Emby Token Ends Its Session Summary

**`EmbyClient.AuthenticateAsync` now signs out of Emby before checking the user name, so a login response carrying a token and no name still ends its Emby session while the Jellyfin login stays refused.**

## Performance

- **Duration:** 8 min
- **Started:** 2026-09-20T19:43:00Z
- **Completed:** 2026-09-20T19:51:00Z
- **Tasks:** 2 completed
- **Files modified:** 2

## Accomplishments

- `AuthenticateAsync` calls `SignOutAsync` ahead of the user-name check, so every readable `AccessToken` ends its Emby session — including the response `{"User":{"Name":""},"AccessToken":"t"}` that previously returned before the sign-out was reachable.
- `SignOutAsync`'s `embyUserName` parameter widened to `string?`, falling back to the typed `username` argument, so `LogSignOutRejected` and `LogSignOutFailed` always name someone even when Emby's response carries no name.
- The three-row `Login_ReturnsNull_WhenEmbyResponseHasNoUserName` theory split: the token-bearing row moved to its own fact asserting two requests; the two no-token rows kept `Assert.Single`. The charset test gained the same explicit `Assert.Single`, naming D-02's documented limit (a body the plugin cannot parse hides its token) as an assertion.
- Two new facts pin the widened path: the sign-out log names the typed user name when the sign-out itself fails, and a response with no token never attempts a sign-out.

## Task Commits

Each task was committed atomically:

1. **Task 1: Every readable token ends its Emby session** — `aad59aa` (test, RED) then `95c0058` (feat, GREEN)
2. **Task 2: The whole sign-out path stays green and leaks nothing** — `4ddc331` (test)

_TDD task (Task 1) produced the required test→feat commit pair; no refactor commit was needed._

## Files Created/Modified

- `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` — `SignOutAsync` call moved ahead of the user-name check in `AuthenticateAsync`; `SignOutAsync`'s `embyUserName` parameter widened to `string?` with a `username` fallback parameter.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs` — new facts `Login_EndsTheEmbySession_WhenTheResponseHasATokenAndNoUserName`, `Login_NamesTheTypedUserName_WhenTheSignOutFailsForAResponseWithoutAUserName`, `Login_DoesNotEndASession_WhenEmbyReturnsNoToken`; the three-row theory reduced to two rows; the charset test gained an explicit single-request assertion and both tests gained XML doc comments stating what each proves.

## Decisions Made

- **SignOutAsync gained a `username` parameter** rather than computing the fallback name at the `AuthenticateAsync` call site. This keeps the fallback expression (`string.IsNullOrEmpty(embyUserName) ? username : embyUserName`) next to the two log calls that consume it, and matches the plan's instruction to widen `SignOutAsync`'s own `embyUserName` parameter to `string?`.
- **The charset test's `Assert.Single(handler.Requests)` is new**, not carried over — it was implicit before this plan (the row shared a theory with the no-token cases) and is now explicit, matching the plan's instruction to make D-02's limit "an explicit, named test instead of an accident."

## Deviations from Plan

None - plan executed exactly as written.

Task 2's break-then-restore proof for the `SignOutAsync` fallback produced a **compile error** (`CS8604: Possible null reference argument`) rather than a runtime test failure — removing the fallback (`var signOutUserName = embyUserName;`) leaves a `string?` flowing into `LogSignOutRejected`/`LogSignOutFailed`'s non-nullable `string username` parameter, and the project's `AllEnabledByDefault` analysis mode with warnings-as-errors turns that into a build failure before any test can run. This is not a deviation from the plan's instruction ("remove the fallback ... watch each go red") — a compile-time red is still a witnessed red, and Phase 03's MIGR-02 established the same precedent (03-02-SUMMARY.md) for a nullable/visibility regression that a runtime test cannot reach. The second break (removing the empty-token guard) failed at runtime as expected, with `InvalidOperationException: No stub response for POST .../Sessions/Logout` — this failure also caught an existing test (`Login_ReturnsNull_WhenEmbyResponseHasNoUserName(json: "{}")`), not only the new one, confirming the guard is load-bearing for both.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

AUTH-05's code half is complete: every access token `EmbyClient` can read now ends its Emby session, the one token it cannot read (hidden inside an unparsable body) is covered by a named test stating the limit, and no recovery code exists for it. `docs/how-it-works.md` still claims the plugin ends every session unconditionally — plan 04-08 owns correcting that, per this plan's `<planner_notes>` and the requirement's shared declaration between 04-02 and 04-08 in `.planning/REQUIREMENTS.md`. No blockers for 04-08 or any other sibling plan in this phase.

## Self-Check: PASSED

- FOUND: src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs
- FOUND: tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs
- FOUND: aad59aa (test, RED)
- FOUND: 95c0058 (feat, GREEN)
- FOUND: 4ddc331 (test, Task 2 coverage)
- All acceptance criteria for both tasks re-verified passing.
- Plan-level `<verification>`: `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` — 200/200 pass, no warnings. `mise run lint` — clean. `bats e2e/10-login-checks.bats` — 10/10 pass, including "the plugin ends its Emby session after each login".

---
*Phase: 04-emby-traffic-under-load-and-failure*
*Completed: 2026-09-20*
