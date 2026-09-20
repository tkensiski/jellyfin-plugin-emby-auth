---
phase: 04-emby-traffic-under-load-and-failure
plan: 03
subsystem: performance
tags: [semaphoreslim, single-flight, cache-stampede, xunit-v3, concurrency]

requires:
  - phase: 04-emby-traffic-under-load-and-failure
    provides: "EmbyUserDirectory's volatile-snapshot cache shape (plan 04-01/existing code), unchanged by this plan"
provides:
  - "A single-flight guard on EmbyUserDirectory.GetStatusAsync's refresh branch: concurrent callers on a stale snapshot send one Emby request between them"
  - "A thread-safe, hold-capable StubHttpMessageHandler test double (HoldResponses/ReleaseResponses/FirstRequestStarted), reusable by any future concurrency test"
affects: [performance, testing]

actuals:
  tokens: 3761
  tasks: 3
  commits: 4

tech-stack:
  added: []
  patterns:
    - "SemaphoreSlim(1,1) with WaitAsync + an explicit post-acquire re-read/re-check, guarding only the refresh branch of an existing volatile-snapshot cache — the double-check is the correctness argument, not the semaphore alone"
    - "Deterministic concurrency tests via a stub HTTP handler that records a request synchronously then blocks on a test-controlled TaskCompletionSource (HoldResponses/ReleaseResponses/FirstRequestStarted), instead of sleeps or timing races"

key-files:
  created: []
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoublesTests.cs

key-decisions:
  - "EmbyUserDirectory now implements IDisposable to release the new SemaphoreSlim (CA1001), an unplanned Rule-3 fix — the DI container already disposes it as an AddSingleton, so this closes a real resource leak the compiler caught, not new scope"
  - "The fresh-snapshot fast-path test (SingleCaller_OnAFreshSnapshot_DoesNotWaitOnTheGuard) relies on an empty, held response queue: a caller that wrongly took the guard would hit either a hang or an immediate InvalidOperationException-turned-Unavailable, so the test fails deterministically either way rather than depending on a timeout"
  - "The two-settings test groups its ten callers contiguously (five then five) rather than interleaved, because SemaphoreSlim's async waiters release in FIFO order in this runtime; a contiguous grouping is what guarantees exactly two requests rather than a request per settings-flip mid-queue"

patterns-established:
  - "Single-flight guard around a volatile-snapshot cache: wrap only the refresh branch, never the fast-path read; re-read and re-evaluate the same staleness condition inside the guard before deciding to call out"

requirements-completed: [PERF-01]

coverage:
  - id: D1
    description: "Ten concurrent callers that find an expired snapshot send exactly one Emby user list request between them"
    requirement: PERF-01
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs#ConcurrentReaders_OnAnExpiredSnapshot_SendOneRequest"
        status: pass
    human_judgment: false
  - id: D2
    description: "Ten concurrent callers on a cold (never-populated) directory also send exactly one request"
    requirement: PERF-01
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs#ConcurrentReaders_OnAColdDirectory_SendOneRequest"
        status: pass
    human_judgment: false
  - id: D3
    description: "Concurrent callers with different settings (different API key) send two requests, never collapsed into one"
    requirement: PERF-01
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs#ConcurrentReaders_WithDifferentSettings_SendTwoRequests"
        status: pass
    human_judgment: false
  - id: D4
    description: "A failed refresh under the guard blocks nobody: every waiting caller returns Unavailable, and a later call after RetryDelay sends a second request"
    requirement: PERF-01
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs#ConcurrentReaders_AfterAFailedRefresh_AreNotBlocked"
        status: pass
    human_judgment: false
  - id: D5
    description: "A caller that finds a fresh snapshot never takes the guard (fast path unchanged)"
    requirement: PERF-01
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs#SingleCaller_OnAFreshSnapshot_DoesNotWaitOnTheGuard"
        status: pass
    human_judgment: false
  - id: D6
    description: "The existing cache-expiry boundary (UsesCachedList_WithinCacheDuration / RefreshesList_AfterCacheDuration) is unchanged by the guard"
    requirement: PERF-01
    verification:
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs#UsesCachedList_WithinCacheDuration"
        status: pass
      - kind: unit
        ref: "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs#RefreshesList_AfterCacheDuration"
        status: pass
    human_judgment: false
  - id: D7
    description: "The refresh path still works end-to-end through the real Jellyfin/Emby stack (login-lock retry, outage/recovery flow)"
    requirement: PERF-01
    verification:
      - kind: e2e
        ref: "bats e2e/40-emby-outage.bats"
        status: pass
    human_judgment: false

duration: 55min
completed: 2026-09-20
status: complete
---

# Phase 4 Plan 3: Single-Flight Guard on the Emby User List Refresh Summary

**A `SemaphoreSlim(1,1)` with an explicit post-acquire re-read collapses a burst of concurrent user-list refreshes into one Emby request, proven by a thread-safe stub handler that can hold a response open on demand.**

## Performance

- **Duration:** 55 min
- **Started:** 2026-09-20T20:05:00Z
- **Completed:** 2026-09-20T21:00:00Z
- **Tasks:** 3 completed
- **Files modified:** 4

## Accomplishments

- `StubHttpMessageHandler` records requests under a `Lock` (safe for concurrent callers) and can hold a response open via `HoldResponses()`/`ReleaseResponses()`/`FirstRequestStarted`, so a concurrency test can force true overlap instead of hoping for a race to land the right way.
- `EmbyUserDirectory.GetStatusAsync` gained a `SemaphoreSlim`-guarded refresh branch: the fast path (fresh snapshot) is untouched — still a lock-free volatile read — and only the stale branch waits on the guard, re-reads the snapshot, and re-evaluates the same four-part staleness condition before deciding whether to call Emby.
- Four new concurrency tests drive real overlap (ten tasks, a held response, a start counter) and assert exact request counts: one for an expired snapshot, one for a cold directory, two for differing settings, and one-then-a-later-second for a failed-then-retried refresh.
- Proved the guard is load-bearing by breaking it twice against the committed implementation and watching the targeted test go red each time, then restoring the file byte-for-byte.

## Task Commits

Each task was committed atomically:

1. **Task 1: The stub handler becomes thread-safe and can hold a response open** — `fe533a2` (feat)
2. **Task 2: Concurrent readers on an expired snapshot send one request** — `78f382f` (test, RED) then `8d91a7a` (feat, GREEN)
3. **Task 3: The existing cache behavior is unchanged, and the guard is proven load-bearing** — `46c899d` (test)

**Plan metadata:** pending (this commit)

_Note: Task 2 is `tdd="true"` and produced the required test → feat commit pair; no refactor commit was needed._

## Files Created/Modified

- `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs` — new `_refreshGuard` field, guarded refresh branch with post-acquire re-read, `IDisposable` implementation (CA1001), updated XML doc
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs` — five new facts: four concurrency tests plus the fresh-snapshot fast-path test
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — `StubHttpMessageHandler` gained a private `Lock`, `HoldResponses()`, `ReleaseResponses()`, `FirstRequestStarted`
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoublesTests.cs` — three new facts covering the handler's concurrency, hold, and release behavior

## Decisions Made

- Followed the plan's discretion on test file names and split exactly as written; no open discretion items remained for this plan.
- `EmbyUserDirectory` implementing `IDisposable` was not called out in the plan text, but is a direct, minimal consequence of adding a `SemaphoreSlim` field (CA1001, warnings-as-errors) — see Deviations below.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] `EmbyUserDirectory` needed `IDisposable` to compile under CA1001**

- **Found during:** Task 2 (GREEN phase, first build after adding `_refreshGuard`)
- **Issue:** `CA1001: Type 'EmbyUserDirectory' owns disposable field(s) '_refreshGuard' but is not disposable`. The project builds with warnings as errors, so this blocked every subsequent step.
- **Fix:** Added `: IDisposable` to the class declaration and a `public void Dispose() => _refreshGuard.Dispose();` member. `EmbyUserDirectory` is registered with `AddSingleton<EmbyUserDirectory>()` in `PluginServiceRegistrator`, so the DI container already disposes it at host shutdown — no other wiring change was needed.
- **Files modified:** `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs`
- **Verification:** `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` builds and passes with zero warnings.
- **Committed in:** `8d91a7a` (part of the Task 2 GREEN commit)

---

**Total deviations:** 1 auto-fixed (1 blocking).
**Impact on plan:** Necessary to compile under the repo's warnings-as-errors policy. No scope creep — the fix is the minimal `IDisposable` shape for a field the plan itself introduced.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## TDD Discipline

Task 2's RED/GREEN commit pair, confirmed locally before adding `Skip` markers:

- **RED** (`78f382f`): all four new concurrency tests ran against the unguarded code. Each caller issued its own request. `ConcurrentReaders_OnAnExpiredSnapshot_SendOneRequest` got 3 requests against an expected 2; `ConcurrentReaders_OnAColdDirectory_SendOneRequest` and `ConcurrentReaders_WithDifferentSettings_SendTwoRequests` got 2 and 4 against expected 1 and 2; `ConcurrentReaders_AfterAFailedRefresh_AreNotBlocked` recorded 2 requests against an expected 1. The stub's exhausted-queue `InvalidOperationException` (thrown once the one-or-two queued responses ran out) is caught inside `EmbyClient.GetUsersAsync`'s `IsUnreadable` branch and turned into a `null` user list, so the observed red is an assertion mismatch rather than an unhandled exception — recorded here as the plan's acceptance criterion requires.
- **GREEN** (`8d91a7a`): the `SemaphoreSlim` guard with the post-acquire re-read made all four pass; the fifth (`SingleCaller_OnAFreshSnapshot_DoesNotWaitOnTheGuard`, Task 3) was added afterward and passed immediately since it exercises the untouched fast path.

Task 3's load-bearing proof (not a TDD gate, but the same break-then-restore discipline the project uses for already-working-code coverage):

- **Break 1 — remove `await _refreshGuard.WaitAsync(cancellationToken).ConfigureAwait(false);`, keep the `finally { _refreshGuard.Release(); }`:** the very first stale call (the test's own priming call) threw `System.Threading.SemaphoreFullException` — releasing a `SemaphoreSlim(1,1)` that was never waited on exceeds its max count. Confirmed, then restored the file byte-for-byte (`git checkout --` against the just-committed `8d91a7a` content, diffed against a saved copy of that commit's blob to confirm an exact match).
- **Break 2 — remove the re-read (`snapshot = _snapshot;`) and re-check inside the guard, keep the wait/release:** `ConcurrentReaders_OnAnExpiredSnapshot_SendOneRequest` went red with 11 requests instead of the expected 2 — the guard still serializes the ten waiters one at a time, but each one unconditionally re-fetches instead of seeing a prior waiter's already-fresh snapshot, so the burst is serialized rather than collapsed. This is the failure the plan calls out as the one that matters: a guard without the re-read is invisible to a test that only counts eventual success, but visible immediately to a test that counts requests. Confirmed, then restored the file byte-for-byte (same verification as break 1).

## Next Phase Readiness

- PERF-01 is fully met: the single-flight guard exists, is proven by exact-count unit tests and confirmed load-bearing by two independent breaks, and the fast path is provably unaffected.
- No latency assertion exists anywhere in the new tests (`rg -q 'Stopwatch|ElapsedMilliseconds' tests/.../EmbyUserDirectoryTests.cs` finds nothing), per the plan's explicit prohibition.
- `mise run lint` and `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` are both clean (208 tests, 0 warnings). `bats e2e/40-emby-outage.bats` passes (3/3), exercising the guarded refresh path through the real Jellyfin/Emby stack.
- Nothing in this plan touches the fingerprint store (04-01) or the sign-out reorder (04-02); no interaction expected with the remaining phase-4 plans.

---
*Phase: 04-emby-traffic-under-load-and-failure*
*Completed: 2026-09-20*
