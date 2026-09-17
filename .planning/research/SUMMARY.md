# Project Research Summary

**Project:** Jellyfin Emby Auth v1.0.0
**Domain:** Jellyfin 12.1 authentication plugin (C#, .NET 10) — open-issue fixes, test gaps, load test, release gating, public plugin repository
**Researched:** 2026-09-17
**Confidence:** HIGH for Jellyfin behavior and library versions read from source or release pages; MEDIUM for workflow patterns; MEDIUM-HIGH for pitfalls (mix of code-confirmed and community-reported)

## Executive Summary

The milestone fixes and tests every open issue in `.planning/codebase/CONCERNS.md` and publishes v1.0.0 so that any Jellyfin administrator can install it from a manifest URL. The research finds no new entry points: six of the seven target changes modify existing components, and the release pipeline gains a merged, multi-version manifest (ARCHITECTURE.md).

The ordering constraint is test infrastructure. Changes 1, 2, and 4 touch code with no unit tests, and the repository rule is to write the test first, so an SQLite in-memory `JellyfinDbContext` seam and fakes for `IUserManager` and `ITaskManager` come first (ARCHITECTURE.md:175-186).

Three decisions are open for the maintainer (below). The most important one: ARCHITECTURE.md finds that the blank-password window during account creation cannot be closed to zero with the public v12.1 `IUserManager` API, which conflicts with the PROJECT.md password rule.

## Open Decisions

**1. Account creation gap vs. the password rule.**
- ARCHITECTURE.md:39-47: `CreateUserAsync` commits the account on the Default login method with no password and takes no lock; a concurrent blank-password login for that name can succeed before the plugin's `UpdateUserAsync`. It proposes minimizing the window, catching every failure as `AuthenticationException`, and documenting the residual race.
- Orchestrator verification (read in this session from `jellyfin/jellyfin` tag `v12.1`, not from the four research files): `IUserManager.CreateUserAsync(string name)` is the only create method (`MediaBrowser.Controller/Library/IUserManager.cs:90`); a new user gets the Default provider (`Jellyfin.Server.Implementations/Users/UserManager.cs:324-327`) and is committed at line 362 with no lock; the blank-password bypass is `DefaultAuthenticationProvider.cs:61-68`; the plugin's `UpdateUserAsync` runs inside the `Guid.Empty` login lock, where the nested-lock helper acquires no per-user lock (`UserManager.cs:550`, `1131-1141`).
- Candidate alternative, unverified by any test: create the account under a random temporary name, save the hash and the Emby login method, then rename it with `IUserManager.RenameUser` (`IUserManager.cs:72`). A new account is hidden from the public login-screen list by default (`Jellyfin.Data/UserEntityExtensions.cs:191`; `Jellyfin.Api/Controllers/UserController.cs:118`, `613`). Needs a spike: rename side effects, and the case where the real name is taken before the rename.

**2. Release gating.**
- FEATURES.md: have `release.yml` run lint, unit, script, and e2e itself before packaging.
- STACK.md and PITFALLS.md:46-48: have `release.yml` confirm through the GitHub API that the tagged commit has a passing `ci-success` check run. PITFALLS.md argues against re-running, because re-running does not prove the merged commit passed and adds e2e time (the e2e job timeout is 30 minutes, `ci.yml:49`).
- All three agree that a tag ruleset cannot require a CI result; it can only restrict who pushes `v*` tags.

**3. Manifest hosting.**
- STACK.md:67: GitHub Pages, published by a separate workflow that runs when `manifest.json` changes (common convention; a decoupled deploy avoids a failed Pages step stranding the catalog, per `GeiserX/smart-covers`). Pages serves through a CDN with its own TTL (PITFALLS.md:140).
- Alternative: a committed `manifest.json` on a branch served from `raw.githubusercontent.com` (simpler; about 5 minutes of cache per IP with no bypass, PITFALLS.md:140).
- PITFALLS.md:143 also notes that a per-release asset is immutable; this conflicts with the chosen "one manifest that lists every version at a stable URL" and is recorded here, not adopted.

## Key Findings

### Recommended Stack

From STACK.md (versions and confidence as stated there):
- **Microsoft.EntityFrameworkCore.Sqlite 10.0.11** with an open `:memory:` connection for `JellyfinDbContext` tests — the InMemory provider cannot run `ExecuteUpdateAsync`, which `DefaultLoginMethod.MoveAsync` uses (HIGH).
- **NSubstitute 6.2.0** for `IUserManager` and `ITaskManager` fakes; not Moq (HIGH).
- `EmbyAuthController` can be constructed directly in unit tests; no test server needed (HIGH).
- **Node 24.21.0 LTS + `node:test` + jsdom 29.1.1** to run the real `configPage.html` script (HIGH on versions; MEDIUM on whether `pageshow` fires under jsdom).
- **k6 2.2.0** as a mise-pinned CLI for the load test (HIGH); **toxiproxy 2.12.0** for latency injection (MEDIUM — last release 2025-03-18; ARCHITECTURE.md:147 also marks it unverified against this repo's toolchain).
- **gitleaks 8.30.1** for the full-history scan (MEDIUM — re-check the latest patch at implementation time).
- GitHub Pages actions: resolve current release SHAs at implementation time (LOW on exact versions).

### Expected Features

From FEATURES.md:
- **Table stakes:** manifest fields that match `PluginManifest`; checksum as plain hex MD5 with no algorithm prefix (Jellyfin compares case-insensitively; a `sha256:` prefix fails every install); public repository and release files, because Jellyfin fetches without credentials; settings page errors for load and save; a migration list that follows the real task state instead of a fixed 3-second reload; a release that ships only CI-verified commits.
- **Migration status:** Jellyfin exposes task `State` and progress; ARCHITECTURE.md:105 prefers reading `ITaskManager.ScheduledTasks` inside `GET /EmbyAuth/Migration` over a second call to `GET /ScheduledTasks/{taskId}`.
- **Anti-features / deferred:** official Jellyfin repository submission, signed releases, automated semantic-release tooling, continuous permission sync.

### Architecture Approach

From ARCHITECTURE.md:
- **Change 1 (account creation):** see Open Decision 1. Two first logins for the same new name are already safe: both wait on `Guid.Empty`, and the second gets the duplicate-name `ArgumentException`, which the plugin catches. The exception handling around `UpdateUserAsync` and `DeleteUserAsync` must be widened so no failure returns HTTP 500.
- **Change 2 (fingerprint read failure):** keep earlier records and expose a "read failed" signal; design it together with change 4's `MigrationStatus` change so the API shape changes once.
- **Change 3 (Emby sign-out):** sign out whenever a token is present, before the user-name check; small and isolated.
- **Change 4 (settings page):** `.catch` handlers with visible messages; poll migration state from the extended `MigrationStatus`.
- **Change 5 (test seams):** shared SQLite `IDbContextFactory<JellyfinDbContext>` helper; `IServiceProvider`/`IUserManager` fake for the provider; `ITaskManager` fake with settable `ScheduledTasks` for the controller; JS harness in parallel.
- **Change 6 (load test):** fault-injection proxy beside the existing logging `emby-proxy`, or custom code if no suitable pinned tool passes review.
- **Change 7 (release):** CI gate, then a merged manifest that exists before `v1.0.0` is tagged.

### Critical Pitfalls

From PITFALLS.md:
1. **Irreversible exposure:** scan full history before the visibility change; rewriting history after forks exist does not remove blobs.
2. **Release skips lint and e2e** and does not check that the tagged commit passed `ci-success` (`release.yml:6-9`, `32-41`).
3. **EF Core InMemory** cannot execute `ExecuteUpdateAsync`; use SQLite in-memory.
4. **Account-creation fix must not use a broad full-entity save** that reintroduces the concurrent-overwrite class `DefaultLoginMethod.MoveAsync` avoids (see `jellyfin#16353`).
5. **`targetAbi` is a minimum**, not a maximum (Jellyfin issue #11331): untested newer Jellyfin versions can still install the plugin.
6. **Manifest caching** delays a new version for minutes on either hosting option.
7. **Load test and lockout:** Jellyfin's default `LoginAttemptsBeforeLockout` is 3; use a distinct account pool and test lockout as a separate scenario; Docker CPU limits can distort latency.
8. **A tag pushed with `GITHUB_TOKEN`** does not trigger `release.yml`.

## Implications for Roadmap

Build order from ARCHITECTURE.md:175-186:
1. DB test seam, then the remaining unit test seams (provider, controller, task).
2. Emby sign-out fix (change 3).
3. Account creation fix (change 1) — after Open Decision 1.
4. Fingerprint read-failure handling (change 2), designed with change 4's status shape.
5. Settings page errors and migration polling (change 4), after the JS harness.
6. Load test harness (change 6), in parallel with 2-5; its numbers feed change 1 and the three performance items.
7. Release pipeline: CI gate, then merged manifest (change 7) — independent; the gate lands before the repository goes public, the manifest before the `v1.0.0` tag.
8. Public release: full-history scan as a hard gate, then visibility change, then `v1.0.0`.

Research flags for phase planning: JS harness (`pageshow` under jsdom), load-test tooling (toxiproxy pin and compose wiring), Pages action SHAs, and a spike for the temporary-name account creation if chosen.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH / MEDIUM | Versions cited to release pages; toxiproxy and gitleaks patch level MEDIUM; Pages action SHAs LOW |
| Features | HIGH / MEDIUM | Manifest and task API behavior read from Jellyfin source; workflow conventions from community examples |
| Architecture | HIGH | Jellyfin v12.1 internals read from source; unverified: whether `IScheduledTaskWorker.Id` equals the task `Key` for the `GET` route |
| Pitfalls | MEDIUM-HIGH | Items 1-5 and 7 confirmed from code or official sources; others community-reported |

### Gaps to Address

- Practical duration of the account-creation window under load (ARCHITECTURE.md:40 — unverified).
- Whether tags are always pushed from `main` after a merged PR (assumed by the CI-gate design; needs maintainer confirmation).
- `pageshow` behavior under jsdom.
- toxiproxy suitability as a pinned dependency.

## Sources

- `.planning/research/STACK.md` — tool and library versions, alternatives, release pages
- `.planning/research/FEATURES.md` — manifest schema, catalog install behavior, dashboard patterns, release expectations
- `.planning/research/ARCHITECTURE.md` — Jellyfin v12.1 internals, per-change boundaries, build order
- `.planning/research/PITFALLS.md` — public release, manifest, workflow, testing, load test, and authentication pitfalls
- `jellyfin/jellyfin` tag `v12.1` (commit `ee91c75e`): `IUserManager.cs`, `UserManager.cs`, `DefaultAuthenticationProvider.cs`, `UserEntityExtensions.cs`, `UserController.cs` — read by the orchestrator for Open Decision 1

---
*Research completed: 2026-09-17*
*Ready for roadmap: yes, after the three open decisions*
