# Phase 3: Migration Status and Target - Pattern Map

**Mapped:** 2026-09-19
**Files analyzed:** 13 (5 renames/modifications, 3 modifications, 5+ new test files)
**Analogs found:** 13 / 13 (all analogs are within-repo, most files are modified in place rather than net-new)

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs` (renamed from `DefaultLoginMethod.cs`) | service (static helper) | CRUD (single-column update) | itself (`DefaultLoginMethod.cs`) | exact — in-place rename, logic unchanged |
| `src/Jellyfin.Plugin.EmbyAuth/MoveAfterLogin.cs` (renamed from `MoveToDefaultLoginMethod.cs`) | event-driven consumer | event-driven | itself + `EmbyAuthenticationProvider.cs` (for the `Func<PluginConfiguration?>` DI seam, D-24) | exact — rename + one dependency swap |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyMigrationTask.cs` (renamed from `MoveEmbyUsersToDefaultTask.cs`) | service (scheduled task) | batch | itself | exact — rename + `Key`/`Name`/target-parameter changes |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs` | model / query helper | CRUD (read) | itself | exact — `ReadyToMove` bool becomes `MigrationUserState` enum |
| `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` | controller | request-response | itself | exact — response reshape (D-01–D-03), `+IUserManager` (D-13) |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` | service (file I/O) | file-I/O | itself | exact — log/XML-doc text only (D-08), availability signal added (FPRT-03) |
| `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` | config | CRUD | itself | exact — `+MigrationTarget`, `+PasswordSetTarget` properties |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs` | validation / model | request-response (validation) | itself | exact — `FindProblem` grows two more checks |
| `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` | config (DI wiring) | — | itself | exact — one new singleton registration (Pattern 3, D-24) |
| `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` | component (settings page) | request-response + polling | itself | exact — Migration section reshaped, polling loop added |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` | test double | — | itself (`FakeUserManager`) | exact — extend with `FakeTaskManager`, `FakeScheduledTaskWorker`, `FakeJellyfinDatabaseProvider`, fill in `GetAuthenticationProviders()` |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/LoginMethodMoveTests.cs` (new) | test | CRUD | `tests/.../EmbyVerifiedPasswordsTests.cs`, `tests/.../EmbyAuthSettingsTests.cs` | role-match — first test needing a real `JellyfinDbContext` |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs` (new) | test | request-response | `tests/.../EmbyAuthenticationProviderTests.cs` | role-match — xUnit v3 + `TestDoubles.cs` fakes convention |
| `e2e/50-migration-target.bats` (new) | test | event-driven / e2e | any existing `e2e/NN-topic.bats` file + `e2e/helpers.bash` | role-match — bats file structure and helper conventions |

## Pattern Assignments

### `src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs` (service, CRUD) — rename of `DefaultLoginMethod.cs`

**Analog:** `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs` (itself, in place)

**Full current file** (this IS the pattern to preserve — only the target ID becomes a parameter instead of the `ProviderId` constant):
```csharp
internal static class DefaultLoginMethod
{
    public const string ProviderId = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";

    public static async Task<bool> MoveAsync(JellyfinDbContext dbContext, Guid userId, string passwordHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        var moved = await dbContext.Users
            .Where(user => user.Id == userId
                && user.AuthenticationProviderId == EmbyAuthenticationProvider.ProviderId
                && user.Password == passwordHash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.AuthenticationProviderId, ProviderId), cancellationToken)
            .ConfigureAwait(false);
        return moved == 1;
    }
}
```

**Rename-time change:** `MoveAsync` needs a `targetProviderId` parameter (replacing the hardcoded `ProviderId` in `SetProperty`) since the target is now a setting, not a constant. Keep the `ExecuteUpdateAsync` single-column pattern exactly as-is — this is an explicit anti-pattern boundary per `.planning/codebase/ARCHITECTURE.md` (never a full-entity save here).

---

### `src/Jellyfin.Plugin.EmbyAuth/MoveAfterLogin.cs` (event-driven consumer) — rename of `MoveToDefaultLoginMethod.cs`

**Analog:** `src/Jellyfin.Plugin.EmbyAuth/MoveToDefaultLoginMethod.cs` (itself)

**Imports pattern** (lines 1-10):
```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
```

**The static-state read D-24 removes** (line 34):
```csharp
if (EmbyAuthPlugin.Instance?.Configuration.MigrationMode != MigrationMode.MoveAfterFirstLogin)
{
    return;
}
```

**Replacement pattern — copy `EmbyAuthenticationProvider`'s constructor-injected settings source** (`EmbyAuthenticationProvider.cs:29-36`, `139-148`):
```csharp
internal sealed partial class EmbyAuthenticationProvider(
    ...
    Func<PluginConfiguration?> configurationSource,
    ILogger<EmbyAuthenticationProvider> logger)
    ...
{
    private EmbyAuthSettings GetSettings()
    {
        if (EmbyAuthSettings.TryCreate(configurationSource(), out var settings, out var problem))
        {
            return settings;
        }
        LogSettingsInvalid(logger, problem);
        throw new AuthenticationException(problem);
    }
}
```
`MoveAfterLogin` takes the same `Func<PluginConfiguration?> configurationSource` constructor parameter and reads `configurationSource()?.MigrationMode` / `MigrationTarget` directly (no `EmbyAuthSettings.TryCreate` needed here since a missing/invalid migration target should skip the move, not throw — see D-14).

**Core event-consumer pattern to keep unchanged** (lines 30-58, the DB-read-then-move shape):
```csharp
public async Task OnEvent(AuthenticationResultEventArgs eventArgs)
{
    ArgumentNullException.ThrowIfNull(eventArgs);
    // ... settings check replaces the static read above ...
    var userId = eventArgs.User.Id;
    var dbContext = await dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
    await using (dbContext.ConfigureAwait(false))
    {
        var user = await dbContext.Users
            .Where(candidate => candidate.Id == userId && candidate.AuthenticationProviderId == EmbyAuthenticationProvider.ProviderId)
            .Select(candidate => new { candidate.Username, candidate.Password })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        if (user?.Password is null || !verifiedPasswords.Matches(userId, user.Password))
        {
            return;
        }

        if (await DefaultLoginMethod.MoveAsync(dbContext, userId, user.Password, CancellationToken.None).ConfigureAwait(false))
        {
            LogMovedToDefault(logger, user.Username);
        }
    }
}
```

**Logging pattern** (`[LoggerMessage]` partial at end of class, line 60-61) — keep the same shape, reword for the configurable target rather than "Default":
```csharp
[LoggerMessage(Level = LogLevel.Information, Message = "The plugin moved user {Username} to the Default login method. Jellyfin now checks the password of this user without Emby.")]
private static partial void LogMovedToDefault(ILogger logger, string username);
```

---

### `src/Jellyfin.Plugin.EmbyAuth/EmbyMigrationTask.cs` (service, batch) — rename of `MoveEmbyUsersToDefaultTask.cs`

**Analog:** `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs` (itself)

**IScheduledTask shape to keep** (lines 19-54) — `Name`, `Key`, `Description`, `Category`, `GetDefaultTriggers()` are all one-liner properties; only `Name` (D-20: "Finish the Emby migration") and `Key` (D-19: `EmbyAuthMigration`) change:
```csharp
public sealed partial class MoveEmbyUsersToDefaultTask : IScheduledTask
{
    public string Name => "Move Emby users to the Default login method";
    public string Key => "EmbyAuthMoveUsersToDefault";
    public string Description => "Moves each user on the Emby login method to the Default login method, ...";
    public string Category => "Emby Auth";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
```

**Core batch-execution pattern to keep** (lines 57-84) — progress reporting per candidate, summary log at the end; only the per-candidate move target becomes the resolved `MigrationTarget` setting instead of `DefaultLoginMethod.ProviderId`:
```csharp
public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(progress);
    var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
    await using (dbContext.ConfigureAwait(false))
    {
        var candidates = await EmbyLoginMethodUsers.ListAsync(dbContext, _verifiedPasswords, cancellationToken).ConfigureAwait(false);
        var moved = 0;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.ReadyToMove && await DefaultLoginMethod.MoveAsync(dbContext, candidate.Id, candidate.PasswordHash!, cancellationToken).ConfigureAwait(false))
            {
                moved++;
            }
            else
            {
                LogNotMoved(_logger, candidate.Username);
            }
            progress.Report(100.0 * (index + 1) / candidates.Count);
        }
        LogSummary(_logger, moved, candidates.Count - moved);
    }
}
```
Note: this file's `candidate.ReadyToMove` check needs to become `candidate.State == MigrationUserState.Ready` per D-01, and the move must skip entirely (log Error, per D-14) when `MigrationTarget` resolves to "Remain on Emby Login" or to a target Jellyfin no longer reports as enabled.

---

### `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs` (model/query, CRUD read)

**Analog:** itself — the file this phase modifies in place.

**Full current shape to extend** (lines 1-54) — the record and the static lister:
```csharp
internal sealed record EmbyLoginMethodUser(Guid Id, string Username, string? PasswordHash, bool ReadyToMove);

internal static class EmbyLoginMethodUsers
{
    public static async Task<IReadOnlyList<EmbyLoginMethodUser>> ListAsync(
        JellyfinDbContext dbContext,
        EmbyVerifiedPasswords verifiedPasswords,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(verifiedPasswords);
        var users = await dbContext.Users
            .Where(user => user.AuthenticationProviderId == EmbyAuthenticationProvider.ProviderId)
            .OrderBy(user => user.Username)
            .Select(user => new { user.Id, user.Username, user.Password })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return users
            .Select(user => new EmbyLoginMethodUser(
                user.Id,
                user.Username,
                user.Password,
                user.Password is not null && verifiedPasswords.Matches(user.Id, user.Password)))
            .ToList();
    }
}
```
**Change:** `ReadyToMove` (bool) becomes `State` (`MigrationUserState`). Per RESEARCH.md Open Question #1, `NoPassword` (when `user.Password is null`) takes precedence over `Unknown`; `Unknown` applies only when `verifiedPasswords` reports records unavailable (a new signal from `EmbyVerifiedPasswords`, see below) for a user who does have a password. Otherwise `Ready`/`NeedsEmbyLogin` follow today's `Matches` boolean exactly as before.

---

### `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` (controller, request-response)

**Analog:** itself.

**Imports pattern** (lines 1-12) — reuse verbatim, add `MediaBrowser.Controller.Library` for `IUserManager`:
```csharp
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
```

**Auth pattern (shared, apply unchanged)** (lines 22-25):
```csharp
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("EmbyAuth")]
[Produces(MediaTypeNames.Application.Json)]
```

**Core request-response pattern to extend** (lines 37-59) — `GetMigrationStatus` keeps the same `dbContextFactory.CreateDbContextAsync` + `await using` shape; `RunMigration` keeps `taskManager.QueueIfNotRunning<T>()` + `NoContent()`, but D-17 requires saving `MigrationTarget` before queuing:
```csharp
[HttpGet("Migration")]
[ProducesResponseType(StatusCodes.Status200OK)]
public async Task<ActionResult<MigrationStatus>> GetMigrationStatus(CancellationToken cancellationToken)
{
    var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
    await using (dbContext.ConfigureAwait(false))
    {
        var users = await EmbyLoginMethodUsers.ListAsync(dbContext, verifiedPasswords, cancellationToken).ConfigureAwait(false);
        return new MigrationStatus(users.Select(user => new MigrationUser(user.Username, user.ReadyToMove)).ToList());
    }
}

[HttpPost("Migration/Run")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
public ActionResult RunMigration()
{
    taskManager.QueueIfNotRunning<MoveEmbyUsersToDefaultTask>();
    return NoContent();
}
```

**Record/enum shape to add** (D-01–D-03, D-13; already drafted precisely in RESEARCH.md Pattern 4 — use as-is):
```csharp
public enum MigrationUserState { Ready, NeedsEmbyLogin, NoPassword, Unknown }
public sealed record MigrationTaskInfo(TaskState State, double? Progress, DateTime? LastEndTimeUtc, TaskCompletionStatus? LastResult);
public sealed record MigrationUser(string Name, MigrationUserState State);
public sealed record MigrationStatus(
    bool RecordsUnavailable,
    MigrationTaskInfo Task,
    IReadOnlyList<MigrationUser> Users,
    IReadOnlyList<NameIdPair> AvailableTargets);
```

**Finding the task worker (D-03) — pitfall-aware pattern** (RESEARCH.md Pitfall 5, Pattern 2):
```csharp
var worker = taskManager.ScheduledTasks.FirstOrDefault(w => w.ScheduledTask is EmbyMigrationTask);
// never match on worker.Id — that's a Jellyfin-assigned REST-route id, not the plugin's Key
```

**Filtering enabled login methods (D-11, D-13, Pattern 5)**:
```csharp
var targets = userManager.GetAuthenticationProviders()
    .Where(p => p.Id != EmbyAuthenticationProvider.ProviderId)
    .ToList();
```

**XML doc / record doc pattern to keep** (lines 62-76) — every public record and property gets a `<summary>`/`<param>` doc, matching existing style.

---

### `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` (service, file-I/O)

**Analog:** itself — the whole class stays; only three sentences and one new availability signal are added.

**The `Record()` order to preserve exactly (D-09)** (lines 41-70) — cache assignment (line 58) before the write (lines 61-63):
```csharp
public void Record(Guid userId, string passwordHash)
{
    ...
    lock (_lock)
    {
        var fingerprints = Load();
        if (fingerprints is null) { return; }
        if (fingerprints.TryGetValue(userId, out var existing) && existing == fingerprint) { return; }

        fingerprints[userId] = fingerprint;
        try
        {
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(fingerprints));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(_logger, ex, _filePath);
        }
    }
}
```

**Log message to correct (D-08)** — current text at line 128 is wrong and must be replaced:
```csharp
// WRONG (current): "...The plugin does not move this user to the Default login method until the user logs in again through Emby."
// CORRECT (D-08): the in-memory record survives; only the on-disk copy is lost until the next successful write or restart.
[LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot write {FilePath}. The plugin keeps the record in memory and still moves this user; the record is lost only if Jellyfin restarts before the next successful write.")]
private static partial void LogWriteFailed(ILogger logger, Exception exception, string filePath);
```
Also fix the XML `<summary>` on `Record()` (line 37) — same wrong claim.

**FPRT-03 availability signal (new)** — `Load()` already returns `null` distinctly on a failed read (lines 99-123); expose that without changing `Matches(Guid, string?)`'s two-value signature (existing callers at `EmbyAuthenticationProvider.cs:82` — wait, actually not called there; called at `MoveToDefaultLoginMethod.cs:48` and inside `EmbyLoginMethodUsers.ListAsync`). Add a method callable once per `ListAsync` invocation, e.g. `bool RecordsAvailable()` that calls `Load() is not null` under the same lock — reuses the existing private `Load()` cache, no behavior change to `Matches`.

---

### `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` (config)

**Analog:** itself.

**Enum + property pattern to copy for the two new settings** (lines 9-20, 46-68):
```csharp
public enum MigrationMode
{
    /// <summary>...</summary>
    MoveAfterFirstLogin,
    /// <summary>...</summary>
    KeepEmbyInCharge,
}

public class PluginConfiguration : BasePluginConfiguration
{
    public MigrationMode MigrationMode { get; set; } = MigrationMode.MoveAfterFirstLogin;
    public AccountAccess AccountAccess { get; set; } = AccountAccess.CopyEmbyRemoteAccess;
}
```
`MigrationTarget` and `PasswordSetTarget` are **strings** (a `NameIdPair.Id`), not enums, since the set of valid values is dynamic (D-11, D-13) — follow the `EmbyServerUrl`/`EmbyApiKey` string-property style (lines 49-57) rather than the enum style, with the same `[SuppressMessage]` consideration only if a `Uri`-like type is ever considered (it is not needed here).

---

### `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs` (validation)

**Analog:** itself.

**`FindProblem` chain pattern to extend** (lines 36-76) — each new validation is one more early return with a plain-language, value-free message:
```csharp
private static string? FindProblem(PluginConfiguration? configuration, out Uri? url)
{
    ...
    if (!Enum.IsDefined(configuration.MigrationMode))
    {
        return "The migration behavior setting is not valid.";
    }
    ...
    return null;
}
```
D-14's target validation follows this same shape but needs `IUserManager.GetAuthenticationProviders()` at save time — this is validated in the controller's save path (or a settings-save endpoint), not inside this static, dependency-free class; `EmbyAuthSettings` stays free of `IUserManager` and only validates shape (non-empty, is-a-string), while `EmbyAuthController` cross-checks the value against the live enabled-provider list per D-14's "server-side refusal at save time."

---

### `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` (DI wiring)

**Analog:** itself.

**Existing registration style to extend** (lines 27-44):
```csharp
serviceCollection.AddSingleton<IAuthenticationProvider>(services => new EmbyAuthenticationProvider(
    services,
    services.GetRequiredService<ICryptoProvider>(),
    services.GetRequiredService<EmbyClient>(),
    services.GetRequiredService<EmbyUserDirectory>(),
    services.GetRequiredService<EmbyVerifiedPasswords>(),
    () => EmbyAuthPlugin.Instance?.Configuration,
    services.GetRequiredService<ILogger<EmbyAuthenticationProvider>>()));
serviceCollection.AddScoped<IEventConsumer<AuthenticationResultEventArgs>, MoveToDefaultLoginMethod>();
```

**D-24's recommended change (RESEARCH.md Pattern 3)** — register the `Func<PluginConfiguration?>` itself as a reusable singleton so both `EmbyAuthenticationProvider` and `MoveAfterLogin` take it as an ordinary constructor parameter:
```csharp
serviceCollection.AddSingleton<Func<PluginConfiguration?>>(_ => () => EmbyAuthPlugin.Instance?.Configuration);
```

---

### `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` (component, request-response + polling)

**Analog:** itself (`loadEmbyAuthMigration`, `showEmbyAuthMessage`, lines 59-141 per CONTEXT.md's Canonical References).

**Reusable message helper (already exists, reuse for D-07/D-14/D-18):**
`showEmbyAuthMessage(element, text, isFailure)` at `configPage.html:75-85` — every new Migration-section message (RecordsUnavailable, no-password warning, target-refused-at-save) renders through this, never a bespoke DOM write.

**`textContent`-only rule (never `innerHTML`)** — `configPage.html:98` is the existing example; every new user-name or state string in the reshaped list follows this.

**Removed pattern (D-06):** delete `setTimeout(loadEmbyAuthMigration, 3000)` at line 137 outright; replace with the `setInterval`-driven poll (D-04, 2s cadence) and the 20s start-window cap (D-05) — no existing analog in this repo for a poll loop, so this is genuinely new client logic; RESEARCH.md Pattern 6 gives the `node:test` mock-timer test shape to pair with it.

---

### `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` (test doubles)

**Analog:** itself — `FakeUserManager` (lines 94-232).

**Hand-written-double convention to extend (every unimplemented member throws, matching members are filled in):**
```csharp
public sealed class FakeUserManager : IUserManager
{
    // ...
    public NameIdPair[] GetAuthenticationProviders() => throw new NotImplementedException();
    // TEST-03 (D-13) needs this filled in with a settable field, e.g.:
    //   public NameIdPair[] AuthenticationProviders { get; set; } = [];
    //   public NameIdPair[] GetAuthenticationProviders() => AuthenticationProviders;
}
```
Apply the identical shape to the new `FakeTaskManager`/`FakeScheduledTaskWorker` (RESEARCH.md Pattern 2 gives the full member list already reflected against `MediaBrowser.Model.Tasks`) and `FakeJellyfinDatabaseProvider`/`SqliteJellyfinDbContextFactory` (RESEARCH.md Pattern 1 gives the full class already written) — these three additions are drop-in per the research document and should be copied into `TestDoubles.cs` beside `FakeUserManager`, not into new files, following this repo's one-shared-doubles-file convention (per RESEARCH.md's Recommended Project Structure).

---

## Shared Patterns

### `[LoggerMessage]` partial logging convention
**Source:** every production class in this repo (e.g. `EmbyVerifiedPasswords.cs:125-129`, `MoveToDefaultLoginMethod.cs:60-61`, `MoveEmbyUsersToDefaultTask.cs:86-90`)
**Apply to:** every new/changed log call in `LoginMethodMove.cs`, `MoveAfterLogin.cs`, `EmbyMigrationTask.cs`, `EmbyAuthController.cs`
```csharp
[LoggerMessage(Level = LogLevel.Error, Message = "...")]
private static partial void LogSomething(ILogger logger, ...);
```
Log methods are static partials at the end of the class, name + status codes only in the message — never repeat the Emby URL or API key (`.claude/rules/plugin.md` §Settings).

### `IDbContextFactory<JellyfinDbContext>` create-and-dispose-per-call
**Source:** `EmbyAuthController.cs:41-42`, `MoveToDefaultLoginMethod.cs:40-41`, `MoveEmbyUsersToDefaultTask.cs:60-61`
**Apply to:** every new/changed method that touches the database
```csharp
var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
await using (dbContext.ConfigureAwait(false))
{
    // ...
}
```

### `EmbyVerifiedPasswords.Matches` gate before any move
**Source:** `.claude/rules/plugin.md` §Verified passwords; `MoveToDefaultLoginMethod.cs:48`, `EmbyLoginMethodUsers.cs:51`
**Apply to:** `LoginMethodMove`/`MoveAfterLogin`, `EmbyMigrationTask`, `EmbyLoginMethodUsers` — every path that moves a user off Emby must check this first; never bypassed even when the target changes.

### Single-column `ExecuteUpdateAsync`, never a full-entity save
**Source:** `DefaultLoginMethod.cs:34-39`; called out explicitly in `.planning/codebase/ARCHITECTURE.md` as an anti-pattern boundary
**Apply to:** `LoginMethodMove.MoveAsync` — stays a one-column, one-statement update even as the target column value becomes a parameter instead of a constant.

### Authorization on every controller route
**Source:** `EmbyAuthController.cs:22-25`; `.claude/rules/plugin.md` §API and settings page
**Apply to:** any new route added to `EmbyAuthController` (e.g. an `AvailableTargets`-only route, if the planner chooses a second endpoint over Pattern 4's fold-in) — keep `[Authorize(Policy = Policies.RequiresElevation)]` and add an e2e 403 test for it.

### Hand-written test doubles, no mocking library
**Source:** `TestDoubles.cs` entire file; Phase 1 D-09
**Apply to:** `FakeTaskManager`, `FakeScheduledTaskWorker`, `FakeJellyfinDatabaseProvider` — every unimplemented interface member throws `NotImplementedException()`, matching members are filled in minimally.

### SQLite-in-memory `JellyfinDbContext` factory (net-new seam, no prior analog in this repo)
**Source:** RESEARCH.md Pattern 1 (full code already drafted, reflection-verified against `Jellyfin.Database.Implementations 12.1.0`)
**Apply to:** `LoginMethodMoveTests.cs`, `EmbyLoginMethodUsersTests.cs`, `EmbyMigrationTaskTests.cs`, `EmbyAuthControllerTests.cs` — one held-open `SqliteConnection`, reuse `NoLockBehavior` (public, from `Jellyfin.Database.Implementations.Locking`), one small hand-written `FakeJellyfinDatabaseProvider`.

## No Analog Found

| File | Role | Data Flow | Reason |
|---|---|---|---|
| Poll loop in `configPage.html` (D-04/D-05: `setInterval`, 2s cadence, 20s start-window cap) | component (client polling) | streaming-like (repeated poll) | No existing page in this repo polls anything; `.mise.toml`-pinned Node's `node:test` mock timers (RESEARCH.md Pattern 6) is the closest infrastructure analog, not a UI pattern. |
| `FakeTaskManager`/`FakeScheduledTaskWorker` (`ITaskManager`/`IScheduledTaskWorker`) | test double | event-driven (task state) | No prior test in this repo has faked Jellyfin's task-manager surface; `FakeUserManager`'s *shape* (throw-by-default, fill in what's needed) is the analog, not its content. Full member list already reflected in RESEARCH.md Pattern 2. |
| `e2e/50-migration-target.bats` (JellyfinSecurity hand-over, D-21–D-23) | test (e2e) | event-driven | No existing e2e file installs a second real Jellyfin plugin; `e2e/setup_suite.bash` and `e2e/helpers.bash` give the shared-container and task-lookup conventions to extend, but the JellyfinSecurity install/checksum step itself is genuinely new. |

## Metadata

**Analog search scope:** `src/Jellyfin.Plugin.EmbyAuth/`, `src/Jellyfin.Plugin.EmbyAuth/Api/`, `src/Jellyfin.Plugin.EmbyAuth/Configuration/`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/`, `tests/js/`, `e2e/`
**Files scanned:** 17 source files, 8 test files, plus CONTEXT.md/RESEARCH.md's already-reflected external assembly members (`Jellyfin.Controller`, `Jellyfin.Model`, `Jellyfin.Database.Implementations` 12.1.0)
**Pattern extraction date:** 2026-09-19
