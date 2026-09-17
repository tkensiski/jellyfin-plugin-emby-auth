# Architecture Research — v1.0.0 Target Changes

**Domain:** Jellyfin 12.1 authentication plugin (C#, .NET 10)
**Researched:** 2026-09-17
**Confidence:** HIGH for Jellyfin-internals claims (verified against `jellyfin/jellyfin` tag `v12.1` source); MEDIUM for tooling recommendations not yet used in this repo (marked inline)

This document does not re-describe the existing system — see `.planning/codebase/ARCHITECTURE.md` for that. It covers only how the seven target changes from `.planning/PROJECT.md` (Active requirements, referencing `.planning/codebase/CONCERNS.md` item numbers) fit into the existing component boundaries, and what order to build them in.

## How the Target Changes Integrate

The existing system has four entry points (login method, event consumer, scheduled task, migration API controller) sharing two services (`EmbyVerifiedPasswords`, `DefaultLoginMethod`). None of the seven changes adds a new entry point or a new shared service. Six of the seven changes modify existing components in place; the seventh (release pipeline) adds one new artifact (a merged manifest) and one new CI/release ordering constraint. The riskiest change (1: no blank-password window) is the only one that depends on undocumented Jellyfin locking behavior, so it is covered first and in the most depth — the other six changes rest on the existing, already-documented component boundaries in `.planning/codebase/ARCHITECTURE.md`.

---

## Change 1 — Account creation without a blank-password window

**Components:** `EmbyAuthenticationProvider.CreateAccountAsync` (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:168-207`). No new component. The boundary is unchanged: this method is still the only place that calls `IUserManager.CreateUserAsync`, `UpdateUserAsync`, and `DeleteUserAsync`.

### What Jellyfin 12.1 actually does (verified against `jellyfin/jellyfin` tag `v12.1`)

**`IUserManager.CreateUserAsync(string name)` has no password parameter and is not atomic with a password save.** `Jellyfin.Server.Implementations/Users/UserManager.cs:339-368` (`CreateUserAsync`) creates the row via `CreateUserInternalAsync` (`UserManager.cs:317-336`), which sets `AuthenticationProviderId` to `DefaultAuthenticationProvider`'s type name (`UserManager.cs:326`) and does not set a password, then commits with one `SaveChangesAsync` (`UserManager.cs:362`). Jellyfin's public `IUserManager` interface offers no "create with password" call; `CreateUserInternalAsync` is `internal` to `Jellyfin.Server.Implementations` and not exposed to plugins. **This means the plugin cannot close the window using only the public v12.1 API — this is a Jellyfin API constraint, not a plugin oversight.** `CreateUserAsync` itself takes no lock (no `_userLock.LockAsync` call anywhere in its body, confirmed by reading the full method).

**Jellyfin's Default login method accepts a blank password only when the saved password is also blank.** `Jellyfin.Server.Implementations/Users/DefaultAuthenticationProvider.cs:61-68`:
```csharp
// As long as jellyfin supports password-less users, we need this little block here to accommodate
if (string.IsNullOrEmpty(resolvedUser.Password) && string.IsNullOrEmpty(password))
{
    return Task.FromResult(new ProviderAuthenticationResult { Username = username });
}
```
This confirms `.claude/rules/plugin.md:33` exactly: a freshly created account (Default provider, `Password == null`) accepts an empty-string password from anyone who knows the username, for as long as the row stays in that state.

**The race is real and I traced its exact mechanics.** `UserManager.AuthenticateUser` (`UserManager.cs:536-708`) resolves the user with `GetUserByName` *before* acquiring any lock (line 549), then locks on `user?.Id ?? Guid.Empty` (line 550):
```csharp
var user = GetUserByName(username);
using (await _userLock.LockAsync(user?.Id ?? Guid.Empty).ConfigureAwait(false))
```
- For an **unknown name** (no account yet), the lock key is always `Guid.Empty` — this is the shared key documented in `.claude/rules/plugin.md:14`. **Two concurrent first logins for the same brand-new Emby username are fully serialized**, because both resolve to `Guid.Empty` before locking. The second one only reaches `AuthenticateLocalUser` after the first's entire `AuthenticateUser` call (including the plugin's `CreateUserAsync` + `UpdateUserAsync`) has released the lock. When the second one finally runs, its `user` variable is still `null` (the post-lock reload only happens `if (user is not null)`, `UserManager.cs:557-560`), so it goes through `EmbyAuthenticationProvider.Authenticate` again with `resolvedUser: null`; `CreateAccountAsync` calls `CreateUserAsync` a second time, which now throws `ArgumentException` because the name exists (`UserManager.cs:352-356`) — a case the plugin already catches (`EmbyAuthenticationProvider.cs:175-179`) and turns into a normal refused login. **This specific race — two concurrent Emby-method logins for the same new name — is already handled correctly by the existing code. No fix is needed here.**
- The exploitable race is different: **a concurrent request with a *blank* password for the *same username*, timed to land after the legitimate request's `CreateUserAsync` commits but before its `UpdateUserAsync` call acquires the per-user lock.** If that second request's `GetUserByName` call (line 549, unlocked, no-tracking) runs after the row exists, it resolves a non-null `user` with `AuthenticationProviderId = Default`, `Password = null`. Its lock key is then `user.Id`, not `Guid.Empty` — a key nothing else holds yet, since the legitimate request is still between `CreateUserAsync` (no lock) and its own upcoming `UpdateUserAsync` call (which needs `_userLock.LockAsync(user.Id)`, `UserManager.cs:213-215`). The blank-password request acquires `user.Id` immediately, reloads the row (still Default, no password), and `GetAuthenticationProviders(user)` (`UserManager.cs:988-1013`) filters to exactly `[DefaultAuthenticationProvider]` because `user.AuthenticationProviderId` is set. `DefaultAuthenticationProvider.Authenticate` then hits the password-less bypass quoted above and **succeeds**. Jellyfin's later `IsDisabled` check (`UserManager.cs:623-631`) does not block this, because a freshly created account is not disabled by default (`user.AddDefaultPermissions()`, `UserManager.cs:332`, does not set `IsDisabled`).
- **This window cannot be closed to zero with the public v12.1 `IUserManager` API.** It can only be minimized: call `UpdateUserAsync` with the password and the plugin's `AuthenticationProviderId` as the very next `await` after `CreateUserAsync` returns, before any other work (the current code already does this almost optimally — `EmbyAuthenticationProvider.cs:181-189` sets the password and the provider ID, then calls `AccountAccessPolicy.ApplyToNewAccount` before `UpdateUserAsync`; moving `ApplyToNewAccount` after the first `UpdateUserAsync` and applying access as a second, immediate follow-up call would shave a small amount of synchronous CPU time off the window, but the fundamental gap between two independently-awaited EF Core operations remains). **Unverified:** the practical duration of this window under load — this is exactly what the load-test harness (change 6) should measure, since it is also the plugin's own bottleneck concern about `CreateUserAsync`/`UpdateUserAsync` pairs during concurrent first logins.

### What this means for the fix

The realistic, achievable version of "never leave a Default account without a password" given Jellyfin 12.1's constraints is:
1. Keep the window as short as code allows (no reordering needed beyond what exists; confirm with a regression test that no avoidable work sits between `CreateUserAsync` and the first `UpdateUserAsync`).
2. **Fix the actual gap CONCERNS.md flags precisely** — exception handling around the cleanup path. Today, `CreateAccountAsync`'s `finally` block calls `DeleteUserAsync` only when `UpdateUserAsync` failed with `DbUpdateException` or `ResourceNotFoundException` (`EmbyAuthenticationProvider.cs:192, 197-203`); any other exception from `UpdateUserAsync`, or any exception from `DeleteUserAsync` itself, is not `AuthenticationException` and escapes as HTTP 500 per Jellyfin's contract (`.claude/rules/plugin.md:15`, confirmed against `AuthenticateWithProvider`, `UserManager.cs:1038-1059`, which only catches `AuthenticationException` from a provider). Widen the catch around the save/delete pair so every exception path both attempts cleanup and always throws `AuthenticationException`, never lets a raw exception through.
3. Document the residual race (item above) as a known, Jellyfin-API-level limitation in `docs/how-it-works.md` and in `PITFALLS.md`, rather than promising it is impossible — the promise in PROJECT.md ("never leave a Default account without a password") should be read as "minimized to the shortest window the public API allows, with tests proving the failure paths never regress it," not "provably zero."

**Data flow (unchanged path, new failure handling):** `EmbyAuthenticationProvider.Authenticate` → `IUserManager.CreateUserAsync` (Jellyfin DB, no lock) → in-memory field sets → `IUserManager.UpdateUserAsync` (Jellyfin DB, per-user lock) → on failure, `IUserManager.DeleteUserAsync` (Jellyfin DB, per-user lock) → `AuthenticationException` back to Jellyfin's `UserManager.AuthenticateWithProvider`, which is the only caller that Jellyfin allows to catch it (`UserManager.cs:1038-1059`).

**Build order dependency:** This change needs unit test seams (change 5) to exist first, because CONCERNS.md's test gap for this exact code path (`tests/.../EmbyAuthenticationProviderTests.cs` does not exist) is the only way to assert the widened exception handling without standing up a full e2e failure injection. Do change 5's `EmbyAuthenticationProvider` seam before change 1's exception-handling fix, write the failing test first (fake `IUserManager` that throws an unexpected exception from `UpdateUserAsync`, then from `DeleteUserAsync`), then fix the code.

---

## Change 2 — Fingerprint file keeps earlier records after a failed read

**Components:** `EmbyVerifiedPasswords` (`src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs`) only. No boundary change — this class is still the sole owner of the fingerprint file and the only thing every other component (`EmbyAuthenticationProvider`, `MoveToDefaultLoginMethod`, `EmbyLoginMethodUsers`) calls into for verification.

**Current behavior (confirmed by reading, `EmbyVerifiedPasswords.cs:90-113`):** `Load()` sets `_fingerprints = []` (line 97) *before* attempting the read. If `File.ReadAllText`/`JsonSerializer.Deserialize` throws, the catch (lines 107-110) leaves that empty dictionary in place for the rest of the process lifetime — `_fingerprints is not null` (line 92) short-circuits every future `Load()` call, so the file is never retried, and the next successful `Record()` call overwrites the file with only the new entry (lines 56-58), permanently discarding every earlier record.

**The fix is entirely internal to this one class — no data flow change to callers.** `Matches` and `Record` both already funnel through `Load()`, so any of these approaches keeps the same public surface (`Record(Guid, string)`, `Matches(Guid, string?)`) that `EmbyAuthenticationProvider`, `MoveToDefaultLoginMethod`, and `EmbyLoginMethodUsers` depend on:
- Distinguish "file does not exist" (a normal empty-state case, keep `_fingerprints = []`) from "file exists but could not be read" (do not memoize an empty dictionary — leave `_fingerprints` null so the next call retries the read, or set a distinct in-memory flag that `Matches`/`Record` consult to refuse writes until a successful read, per the maintainer's decision already logged in `PROJECT.md` Key Decisions: "A failed fingerprint write keeps the in-memory record; fix the docs and log, add a test").
- Surface the cause to the administrator. Today only the Jellyfin log gets the exception (`EmbyVerifiedPasswords.cs:109`, `LogReadFailed`); the settings page has no visibility into it at all. This is the one place change 2 and change 5 (settings page error messages) meet: `EmbyLoginMethodUsers.ListAsync` and `EmbyAuthController.GetMigrationStatus` would need a way to know "the fingerprint file could not be read" so the migration status response (and therefore the settings page) can show it, rather than silently showing every affected user as "needs one login while Emby runs" with no explanation (`configPage.html:74`, flagged in CONCERNS.md). The natural seam is a new read-only property on `EmbyVerifiedPasswords` (e.g., `bool LastReadFailed`) that `EmbyLoginMethodUsers.ListAsync` or `EmbyAuthController.GetMigrationStatus` can read and fold into `MigrationStatus`.

**Build order:** Independent of every other change except its overlap with change 5's `MigrationStatus` shape (see below) — build change 2's file-handling fix first (it's the smaller, self-contained piece with an existing partial test, `EmbyVerifiedPasswordsTests.cs:103`), then extend `MigrationStatus` once in change 5 to carry both the fingerprint-read failure and the task-completion state, rather than shipping two separate API shape changes.

---

## Change 3 — Emby sign-out whenever Emby returns an access token

**Components:** `EmbyClient.AuthenticateAsync` / `EmbyClient.SignOutAsync` (`src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:50-96, 165-186`) only. `EmbyClient` remains the only component that talks to Emby (`docs/migration.md:25`, confirmed unchanged by this fix).

**Current gap (confirmed by reading, `EmbyClient.cs:87-95`):**
```csharp
var embyUserName = login?.User?.Name;
if (string.IsNullOrEmpty(embyUserName))
{
    LogResponseWithoutUserName(logger, baseUrl);
    return null;                       // returns before SignOutAsync — token is never used
}
await SignOutAsync(client, baseUrl, embyUserName, login?.AccessToken, cancellationToken).ConfigureAwait(false);
```
When Emby's response has a token but no user name, the method returns `null` at line 91 without reaching the sign-out call at line 94. `Login_ReturnsNull_WhenEmbyResponseHasNoUserName` (`tests/.../EmbyClientTests.cs:181-190`) currently *asserts* this — only one request is sent — so the existing test encodes the bug and must change as part of the fix, not just the production code.

**The fix is a reordering within `AuthenticateAsync`, no new component:** move the token-presence check ahead of the user-name check, so sign-out happens whenever `login?.AccessToken` is non-empty, regardless of whether a user name is present. `SignOutAsync` already accepts `embyUserName` only for its log message (`EmbyClient.cs:165, 179, 184` — `LogSignOutRejected`/`LogSignOutFailed` take `string embyUserName`), so a missing user name needs a fallback value for the log call (for example the typed `username` parameter already in scope in `AuthenticateAsync`, not the Emby-returned one) — this keeps the "never log a password or the API key" rule intact (`.claude/rules/plugin.md:56`) since the typed username is not a secret.

**Data flow:** No change to callers. `EmbyAuthenticationProvider.Authenticate` still only sees the `EmbyLogin?` return value; sign-out remains entirely internal to `EmbyClient`.

**Build order:** Small and isolated — no dependency on any other change. Good candidate for an early, low-risk win once test seams for `EmbyClient` (already exist via `TestDoubles.cs`'s `StubHttpMessageHandler`) confirm the reordering with a new/updated unit test asserting two requests are sent when the response has a token but no user name.

---

## Change 4 — Settings page: error messages and a migration list that updates after the task finishes

**Components:** `Configuration/configPage.html` (client-side JS, `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:58-123`) and `EmbyAuthController` (`src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs`). This is the one change that touches the API response shape, so it is the natural place to also carry change 2's fingerprint-read-failure signal.

**Current gaps (confirmed by reading):**
- `pageshow` has `.finally` but no `.catch` (`configPage.html:85-93`) — a failed `getPluginConfiguration` leaves the form blank with no message.
- The submit handler has no `.catch` on `getPluginConfiguration` or `updatePluginConfiguration` (`configPage.html:110-118`).
- After "Run migration now," the page reloads the list exactly once, on a fixed 3-second timer (`configPage.html:100-101`), regardless of whether the task has actually finished. `POST /EmbyAuth/Migration/Run` returns 204 as soon as the task is *queued or already running* (`EmbyAuthController.cs:53-59`), not when it completes.

**Two implementation paths for "updates after the task finishes," both verified against Jellyfin 12.1 source:**

1. **Poll Jellyfin's own `GET /ScheduledTasks/{taskId}` API.** `Jellyfin.Api/Controllers/ScheduledTasksController.cs:72-82` (tag `v12.1`) exposes this route, admin-only via the class-level `[Authorize(Policy = Policies.RequiresElevation)]` (`ScheduledTasksController.cs:16`) — the same policy the plugin's own controller already uses. It returns a `TaskInfo` (`MediaBrowser.Model/Tasks/TaskInfo.cs`) with `State` (`TaskState` enum), `CurrentProgressPercentage`, and `Key`. `.claude/rules/e2e.md:32` and the e2e tests already use the task's `Key` (`EmbyAuthMoveUsersToDefault`) as the `{taskId}` route value for `POST /ScheduledTasks/Running/{id}`, so the same identifier should work for the `GET` route (**unverified**: whether `IScheduledTaskWorker.Id`, which `GetTask` matches against, `ScheduledTasksController.cs:76-77`, is always equal to `Key` for a plugin task in this exact build — the e2e suite's existing successful use of `Key` for the `POST` route on the same controller is the strongest available evidence, but no test in this repo currently calls the `GET` route).
2. **Extend the plugin's own `GET /EmbyAuth/Migration` response using `ITaskManager`, which `EmbyAuthController` already has injected.** `MediaBrowser.Model/Tasks/ITaskManager.cs` exposes `IReadOnlyList<IScheduledTaskWorker> ScheduledTasks`, and `IScheduledTaskWorker` (`MediaBrowser.Model/Tasks/IScheduledTaskWorker.cs`) carries `State` and `CurrentProgress` directly, in-process — no second HTTP call, no second authorization check, no second identifier to keep in sync with the task's `Key`. `EmbyAuthController.GetMigrationStatus` (`EmbyAuthController.cs:37-47`) can find the worker with `taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask is MoveEmbyUsersToDefaultTask)` and fold `State`/`CurrentProgress` into `MigrationStatus`.

**Recommendation: path 2.** It keeps the migration API self-contained (one endpoint the settings page already polls), avoids a second authorization round trip, and avoids the unverified `Id`-vs-`Key` question entirely, since it reads the worker object directly rather than routing through a string identifier. This does mean `MigrationStatus` (`EmbyAuthController.cs:66-76`, currently `public sealed record MigrationStatus(IReadOnlyList<MigrationUser> Users)`) grows a field — for example `TaskState TaskState` — which is a wire-format change to a `public` record; check `.claude/rules/plugin.md:49` (Jellyfin serializes with PascalCase) still holds and update the e2e test that asserts the shape (`e2e/30-migration-modes.bats:95-120` per `.planning/codebase/ARCHITECTURE.md`).

**Data flow:** `configPage.html` → `GET EmbyAuth/Migration` (unchanged endpoint, extended response) → `EmbyAuthController.GetMigrationStatus` → `EmbyLoginMethodUsers.ListAsync` (unchanged) **and** `taskManager.ScheduledTasks` (new read) → `MigrationStatus` (extended) → page replaces the fixed 3-second `setTimeout` with a short poll loop that stops when `TaskState` returns to `Idle` (or a configured max attempts, so a page left open does not poll forever).

**Build order:** Do change 2 (fingerprint read-failure signal) before this, since both extend `MigrationStatus`/`GetMigrationStatus` — one combined API-shape change is cleaner than two. Change 5's settings-page JS test harness (see next section) should exist before writing the polling loop, so the polling logic itself is test-driven rather than verified only through the slower e2e suite.

---

## Change 5 — Test seams

**Components affected:** every component that currently has no unit test — `EmbyAuthenticationProvider`, `MoveToDefaultLoginMethod`, `MoveEmbyUsersToDefaultTask`, `DefaultLoginMethod`, `EmbyLoginMethodUsers`, `EmbyAuthController` — plus a new, separate harness for `configPage.html`. This change adds no new production component; it adds fakes/test doubles and, for the JS, a new test tool dependency.

**Why each is currently untestable in isolation, and the seam to add (confirmed by reading each constructor):**

| Component | Blocking dependency | Seam |
|---|---|---|
| `EmbyAuthenticationProvider` | Resolves `IUserManager` from `IServiceProvider` at login time (`EmbyAuthenticationProvider.cs:21, 106`), per `.claude/rules/plugin.md:16` — this is required because `IUserManager` itself depends on every registered `IAuthenticationProvider`, so a constructor-time dependency would be circular in real Jellyfin, but a unit test has no such cycle | Build a fake `IServiceProvider` (or a minimal `IServiceCollection`/`ServiceProvider` from `Microsoft.Extensions.DependencyInjection`) whose `GetRequiredService<IUserManager>()` returns a hand-written fake `IUserManager`. `IUserManager` is a MediaBrowser.Controller interface with many members; only `CreateUserAsync`, `UpdateUserAsync`, `DeleteUserAsync` are called by this class, so the fake can throw `NotImplementedException` for the rest |
| `MoveToDefaultLoginMethod` | `IDbContextFactory<JellyfinDbContext>` (`MoveToDefaultLoginMethod.cs:26`) | `Jellyfin.Database.Implementations` supports EF Core's in-memory or SQLite in-memory provider for `JellyfinDbContext`; `IDbContextFactory<T>` has a standard `PooledDbContextFactory`/manual factory wrapper for tests. This is the same seam every other DB-touching class below needs — build it once, reuse it |
| `MoveEmbyUsersToDefaultTask` | Same `IDbContextFactory<JellyfinDbContext>` seam, plus it is `public` (required by Jellyfin's `Assembly.GetExportedTypes()` scheduled-task discovery, `.claude/rules/plugin.md:22`) so no `InternalsVisibleTo` gymnastics are needed — it is already visible to the test project |
| `DefaultLoginMethod` | `JellyfinDbContext` directly (not the factory) via its static `MoveAsync(JellyfinDbContext, ...)` signature (`DefaultLoginMethod.cs:31`) | Same in-memory/SQLite `JellyfinDbContext` seam, constructed directly rather than through a factory since the method takes the context as a parameter |
| `EmbyLoginMethodUsers` | Same `JellyfinDbContext` parameter pattern as `DefaultLoginMethod` | Same seam |
| `EmbyAuthController` | `IDbContextFactory<JellyfinDbContext>`, `EmbyVerifiedPasswords` (already constructible directly with a temp file path and `CapturingLogger`), `ITaskManager` (`EmbyAuthController.cs:26-29`) | Same DB seam, plus a fake `ITaskManager` — only `QueueIfNotRunning<T>()` is called today (`EmbyAuthController.cs:57`); change 4 above adds a read of `ScheduledTasks`, so the fake needs a settable `IReadOnlyList<IScheduledTaskWorker>` too |

**The one seam every DB-touching class shares (`IDbContextFactory<JellyfinDbContext>`/`JellyfinDbContext`) does not exist yet in this repo's test project** — `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` currently has no DB double, only `StubHttpMessageHandler`, `StubHttpClientFactory`, `ManualTimeProvider`, and `CapturingLogger<T>`. Building this one shared helper (an EF Core in-memory or SQLite in-memory `JellyfinDbContext` factory, seeded with whatever `User` rows a test needs) unblocks five of the six production-code test gaps at once. **Build this seam first, before any of the five DB-touching components' tests, and before change 1's exception-handling fix**, since that fix needs `EmbyAuthenticationProviderTests.cs` to exist to prove the widened catch blocks.

**Settings page JavaScript (`configPage.html:58-123`):** This is plain inline `<script>` in an HTML file with no build step and no existing test tooling in this repo (`mise.toml`'s `lint`/`test` tasks are dotnet/shellcheck-only; `package.json` does not exist in this repo). To unit test it without changing production behavior, extract the testable logic to a separate, tested surface:
- The functions (`loadEmbyAuthMigration`, the `pageshow`/submit/click handlers) already reference `ApiClient`, `Dashboard`, and `document.querySelector` globals that Jellyfin's dashboard provides at runtime. A Node-based test (for example `node:test` plus `jsdom`, or Vitest with a jsdom environment — **MEDIUM confidence, general JS tooling knowledge, not verified against this repo's existing toolchain**) can load the script body in a DOM with stub `ApiClient`/`Dashboard` globals and assert the `.catch` branches set the expected `textContent`. This requires either extracting the script to a standalone `.js` file that `configPage.html` includes (a structural change, testable in isolation) or a test harness that parses the inline `<script>` out of the HTML and `eval`s it into a jsdom `window` — the former is cleaner and matches "test seams ... without changing public behavior," since Jellyfin serves whatever `GetPages` returns (`EmbyAuthPlugin.cs:43`) regardless of whether the JS lives inline or in a separate file the HTML `<script src>`-includes.
- Whichever tool is chosen needs a `mise.toml` tool pin and a CI job addition (`ci.yml` currently has `lint`, `test`, `e2e` — a fourth job, or folding it into `test`, needs a decision before change 5's JS piece can land in CI, which affects change 7's "release only after CI passed" ordering).

**Build order:** (a) shared `JellyfinDbContext` test seam → (b) the five DB-touching unit test suites, in any order → (c) `EmbyAuthenticationProvider` test seam (needs its own `IServiceProvider`/`IUserManager` fake, independent of (a)) → (d) settings-page JS extraction + test tool choice, which can happen in parallel with (a)-(c) since it touches a different file with no shared dependency. Change 1's exception-handling fix and change 4's `MigrationStatus` extension both depend on (a)-(c) existing first (write the failing test, then the fix).

---

## Change 6 — Load test harness

**Components:** New, additive to `e2e/` — no production code component. `.claude/rules/e2e.md` already documents the compose-based harness (`e2e/compose.yaml`, `e2e/emby-proxy.conf`) this extends.

**What exists today (confirmed by reading `e2e/compose.yaml`, `e2e/emby-proxy.conf`):** `emby-proxy` is a stock `nginx:1.30.5-alpine` reverse proxy whose only job is request logging (`log_format requests`, `emby-proxy.conf:5-9`) — it has no latency-injection or fault-injection configuration today. Plain nginx has no built-in "delay this response" or "drop this connection" directive without a third-party module not present in the stock `nginx:alpine` image.

**Two additive options for slow/faulty Emby simulation, matching CONCERNS.md's three unmeasured bottlenecks (Emby calls inside the login lock, user list refresh, fingerprint file writes):**
1. **A dedicated fault-injection proxy alongside (not replacing) the existing logging `emby-proxy`** — a widely used, purpose-built tool for exactly this (inject latency, timeouts, bandwidth limits, connection resets, via a scriptable admin API) is Toxiproxy (Shopify). **MEDIUM confidence — general tooling knowledge, not verified against this repo or read from Toxiproxy's own docs in this session; the maintainer should confirm it is available as a pinned, mise-installable binary or a compose service image before committing to it.** This would sit as a third compose service between Jellyfin and `emby-proxy` (or between `emby-proxy` and `emby`), toggled per-test via its HTTP admin API — matching this repo's existing pattern of one Docker Compose file with per-file setup (`.claude/rules/e2e.md:11`).
2. **A slow/faulty mode built directly into `emby-proxy.conf`**, e.g., an nginx `location` block wrapping requests through a small Lua or a second lightweight service (a lightweight custom script proxy) that can be told to delay or drop — more work to build than adopting an existing tool, but no new dependency to pin/audit for the security-conscious defaults this project already holds (`zizmor`, pinned tool versions).

**Recommendation:** option 1 if a suitably small, pinnable Toxiproxy (or equivalent) image passes the same scrutiny as `emby/embyserver` and `nginx` already receive in `compose.yaml` (pinned digest/tag); otherwise option 2 keeps the harness dependency-free at the cost of more custom code to maintain. This is a decision for the phase that implements change 6, not something this research can settle without the maintainer's tooling preference.

**Data flow (new):** Jellyfin → (existing) `emby-proxy` (logging, unchanged) → new fault-injection layer → Emby. Load test scripts drive concurrent logins against Jellyfin's existing login endpoint (same one the current e2e `.bats` files already exercise) while the fault-injection layer is configured to add latency/drop connections, and the test asserts on: (a) how long concurrent first logins take when Emby is slow (change 1's window-duration question), (b) whether the user list cache's lack of a single-flight guard (`EmbyUserDirectory.cs:58-64`, confirmed no request coalescing) causes duplicate Emby calls under concurrent cache misses, and (c) fingerprint file write latency under concurrent `Record()` calls holding the single `Lock` (`EmbyVerifiedPasswords.cs:22, 45`).

**Build order:** Depends on nothing from changes 1-5 to *build*, but its *results* directly inform change 1's window-duration claims and CONCERNS.md's three "unmeasured" performance items — so it should run early enough that its numbers can inform whether change 1's minimal-window mitigation is sufficient, but it is not a hard prerequisite for any other change. Reasonable to build in parallel with changes 2-5.

---

## Change 7 — Release pipeline

**Components:** `.github/workflows/release.yml`, `.github/workflows/ci.yml`, `scripts/package.sh`. No production plugin code.

**Current gap 1 — releases skip lint and e2e (confirmed by reading both workflow files):** `release.yml` triggers on any pushed `v*` tag (`release.yml:8-9`) and runs `scripts/package.sh check-tag`, `mise run test`, `mise run package` (`release.yml:32-41`) — no `mise run lint`, no `mise run e2e`, and no check that the tagged commit passed `ci.yml` at all. `ci.yml` runs all three (`lint`, `test`, `e2e`) as required jobs gated by a `ci-success` job (`ci.yml:64-81`) on pull requests and pushes to `main` (`ci.yml:5-9`), but a tag push is a separate event that `ci.yml`'s `on:` block does not currently match, so tagging a commit does not itself trigger `ci-success` to run against that exact tag ref.

**Fix approach, no new component, workflow-only:** Make `release.yml` depend on `ci-success` having already succeeded for the exact tagged commit, rather than re-running the checks (which would duplicate CI and slow releases) or trusting the tag blindly (today's gap). The two standard GitHub Actions patterns for this:
- `workflow_run` trigger on `ci.yml`'s completion, filtered to the tag ref, gated on `conclusion == 'success'`.
- A required-status-check branch/tag protection rule combined with `gh api` polling the commit's check-run status for `ci-success` before `release.yml` proceeds (heavier, but works if tags are pushed against commits that already went through a PR's `ci.yml` run on `main`, which is this repo's normal flow — tag `main` after merge, not a feature branch).
Given this repo tags releases from `main` after a PR merged (implied by the existing `check-tag` step comparing to `Directory.Build.props`, and `ci.yml`'s `push: branches: [main]` trigger), the simplest correct fix is likely: `release.yml` triggers on tag push as today, then its first step queries the GitHub Checks API for the tagged commit's `ci-success` conclusion and fails the job if it is not `success` — this avoids a second full CI run and avoids restructuring `ci.yml`'s triggers. **This is a workflow-design decision, not something this research can finalize without the maintainer confirming the tag-from-main assumption holds** (it is implied but not stated as a rule anywhere read in this session).

**Current gap 2 — one manifest per release, not one manifest for every release:** `scripts/package.sh build` (`scripts/package.sh:49-105`) writes a fresh `manifest.json` per invocation containing exactly one `versions` entry (`package.sh:87-101`) and uploads it as a release asset (`release.yml:44`, `gh release create ... artifacts/release/manifest.json`). PROJECT.md's Active requirement ("One `manifest.json` at a stable URL lists every released version") needs a manifest that *accumulates* entries across releases at a URL that does not change between releases — a GitHub Release asset URL is per-tag and does not qualify. **New component needed:** a persistence layer for the merged manifest, separate from the per-release build artifact `scripts/package.sh` already produces. The two standard patterns for third-party Jellyfin plugin repositories, per `PROJECT.md`'s Context section (`jellyfin.org/docs/general/server/plugins`; `Kevinjil/jellyfin-plugin-repo-action`; `LizardByte/jellyfin-plugin-repo`), are a dedicated `gh-pages` branch or a fixed file on a long-lived branch, both serving the merged `manifest.json` at a URL like `https://<owner>.github.io/<repo>/manifest.json` or a `raw.githubusercontent.com` URL pinned to a branch (not a tag). `release.yml` would need a new step, after `scripts/package.sh package` succeeds, that fetches the current published manifest, merges in the new version's entry (matching this repo's existing `jq`-based JSON construction style in `scripts/package.sh`), and pushes/publishes it — this is additive to `release.yml`, not a replacement of `scripts/package.sh build`'s per-release manifest (which can stay as a release asset for reference, or be dropped in favor of only the merged one, a naming decision for the implementing phase).

**Build order:** Independent of changes 1-6 (different files entirely — workflows and scripts vs. plugin source). The "release only after CI passed" fix (gap 1) should land before the "public repository, real release" milestone-completion steps in PROJECT.md's Active list, since a broken or incomplete gate is worse to discover after the repository goes public. The merged-manifest fix (gap 2) is required before v1.0.0 can actually be installed via the documented "add the manifest URL" flow (PROJECT.md Active: "A Jellyfin administrator adds the manifest URL, installs the plugin from the catalog, and receives updates") — build it before the `v1.0.0` tag is pushed, not after, since the first real administrator install needs the merged manifest to already exist and already contain (at minimum) the `v1.0.0` entry.

---

## Suggested Build Order Across All Seven Changes

1. **Change 5, DB test seam** (`IDbContextFactory<JellyfinDbContext>` fake/in-memory helper) — nothing else that touches the database can be test-driven without it.
2. **Change 5, remaining unit test seams** (`EmbyAuthenticationProvider`'s `IServiceProvider`/`IUserManager` fake; `ITaskManager` fake for `EmbyAuthController`) — in parallel with step 1 where independent.
3. **Change 3** (Emby sign-out reordering) — small, isolated, existing `EmbyClient` test seam already supports it; good early win once the general test-writing rhythm from steps 1-2 is established.
4. **Change 1** (blank-password window: exception-handling fix, using the seam from step 2; window-duration documentation, informed by change 6's numbers once available).
5. **Change 2** (fingerprint read-failure handling) — self-contained, but design its new "did the read fail" signal together with change 4's `MigrationStatus` extension so the API shape changes once, not twice.
6. **Change 4** (settings page errors + migration polling) — needs change 2's signal decided, needs `MigrationStatus`/`GetMigrationStatus` changes, benefits from change 5's JS test harness existing first so the new polling loop is test-driven.
7. **Change 6** (load test harness) — can run in parallel with steps 3-6; its output (measured window durations, cache-miss duplicate-request counts, fingerprint-write latency) should feed back into step 4's window-duration documentation and CONCERNS.md's "unmeasured" performance items before those items are marked resolved.
8. **Change 7** (release pipeline: CI-gate fix, then merged manifest) — independent of 1-6, but the merged manifest must exist before the `v1.0.0` tag is pushed, and the CI-gate fix should land before the repository goes public.

The dependency that most constrains ordering is change 5's DB seam: changes 1, 2, and 4 all touch code paths that currently have zero unit test coverage, and CLAUDE.md's "write the test first" rule means none of those three fixes can start correctly until the seam exists.

## Sources

- `jellyfin/jellyfin` GitHub repository, tag `v12.1` (confirmed via `git ls-remote`/GitHub tags API; matches the Docker image tag `jellyfin/jellyfin:12.1.20260915-010956` pinned in `e2e/compose.yaml`):
  - `Jellyfin.Server.Implementations/Users/UserManager.cs` — `CreateUserAsync` (339-368), `CreateUserInternalAsync` (317-336), `AuthenticateUser` (536-708), `GetAuthenticationProviders` (988-1013), `AuthenticateLocalUser`/`AuthenticateWithProvider` (1038-1059+), `UpdateUserAsync` (213-250+), `DeleteUserAsync` (371-416), `LockHelper` (~1130+)
  - `Jellyfin.Server.Implementations/Users/DefaultAuthenticationProvider.cs` — `Authenticate(string, string, User?)` (48-94), the password-less bypass (61-68)
  - `Jellyfin.Api/Controllers/ScheduledTasksController.cs` — `GetTask` (72-82), `[Authorize(Policy = Policies.RequiresElevation)]` at class level (16)
  - `MediaBrowser.Model/Tasks/TaskInfo.cs`, `IScheduledTaskWorker.cs`, `ITaskManager.cs`
- This repository at commit `ecee1ed` (per `.planning/codebase/ARCHITECTURE.md`) plus the current working tree read directly for this research: `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs`, `EmbyVerifiedPasswords.cs`, `EmbyClient.cs`, `LoginDecision.cs`, `DefaultLoginMethod.cs`, `MoveToDefaultLoginMethod.cs`, `EmbyLoginMethodUsers.cs`, `MoveEmbyUsersToDefaultTask.cs`, `PluginServiceRegistrator.cs`, `Api/EmbyAuthController.cs`, `Configuration/configPage.html`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs`, `e2e/compose.yaml`, `e2e/emby-proxy.conf`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `scripts/package.sh`
- `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/CONCERNS.md`, `.planning/PROJECT.md`, `.claude/rules/plugin.md`, `.claude/rules/e2e.md` (this repository, read in full for this research)
- Toxiproxy as a candidate fault-injection tool for change 6: general tooling knowledge, **not verified in this session against its current documentation or against this repo's toolchain constraints** — flagged MEDIUM confidence in the relevant section above; confirm before adopting.

---
*Architecture research for: Jellyfin Emby Auth plugin, v1.0.0 milestone*
*Researched: 2026-09-17*
