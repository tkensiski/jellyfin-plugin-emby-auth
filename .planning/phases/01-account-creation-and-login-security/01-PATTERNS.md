# Phase 1: Account Creation and Login Security - Pattern Map

**Mapped:** 2026-09-17
**Files analyzed:** 8 (2 new, 6 modified)
**Analogs found:** 8 / 8 (all modifications are in-place edits of files that are their own best analog; 2 new files use nearby sibling files as analogs)

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|--------------------|------|-----------|-----------------|---------------|
| `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` (edit in place) | controller/service (auth provider) | request-response | itself (existing file, edited) | exact |
| `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` (edit in place) | config/DI wiring | request-response | itself (existing file, edited) | exact |
| `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` (edit in place) | model/config | CRUD | itself (existing file, edited) | exact |
| `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` (edit in place) | UI config | request-response | itself (existing file, edited) | exact |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` (add `FakeUserManager`, fake `ICryptoProvider`) | test double/utility | event-driven (call recording) | `StubHttpMessageHandler` / `StubHttpClientFactory` in same file | exact |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` (new) | test | request-response | `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs` | role-match |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs` (edit one test case) | test | CRUD (config) | itself (existing file, edited) | exact |
| `docs/how-it-works.md`, `docs/settings.md` (edit in place) | docs | — | themselves | exact |

## Pattern Assignments

### `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` (controller/service, request-response)

**Analog:** itself — this file already contains every pattern this phase needs to extend, so no external analog is needed.

**Constructor / DI pattern to extend for D-10** (lines 27-34):
```csharp
internal sealed partial class EmbyAuthenticationProvider(
    IServiceProvider serviceProvider,
    ICryptoProvider cryptoProvider,
    EmbyClient embyClient,
    EmbyUserDirectory userDirectory,
    EmbyVerifiedPasswords verifiedPasswords,
    ILogger<EmbyAuthenticationProvider> logger)
    : IAuthenticationProvider, IRequiresResolvedUser
```
Add a `Func<PluginConfiguration?> configurationSource` parameter here (primary constructor style, matching the existing parameter list), and use it in `GetSettings()` (line 146) in place of `EmbyAuthPlugin.Instance?.Configuration`.

**Current narrow catch on save — the two spots D-02/AUTH-04 widen** (lines 192, 218):
```csharp
catch (Exception ex) when (ex is DbUpdateException or ResourceNotFoundException)
{
    LogSaveFailed(logger, ex, embyLogin.Name);
    throw new AuthenticationException(InvalidLogin, ex);
}
```
Widen both instances (`CreateAccountAsync` line 192 and `SavePasswordAsync` line 218) to unconditional `catch (Exception ex)` — drop the `when` filter, keep the log call and the rethrow shape unchanged.

**Cleanup delete with no catch today — the AUTH-04 gap** (lines 197-203):
```csharp
finally
{
    if (!saved)
    {
        await userManager.DeleteUserAsync(user.Id).ConfigureAwait(false);
    }
}
```
Wrap the delete in its own try/catch, following the exact log-then-continue shape already used for save failures at lines 192-196:
```csharp
finally
{
    if (!saved)
    {
        try
        {
            await userManager.DeleteUserAsync(user.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogDeleteFailed(logger, ex, embyLogin.Name);
        }
    }
}
```

**`[LoggerMessage]` partial pattern to copy for the new `LogDeleteFailed`** (lines 245-249, immediately adjacent Error-level examples):
```csharp
[LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot create an account for Emby user {EmbyUserName}. If Jellyfin does not allow this user name, rename the user on Emby.")]
private static partial void LogCreateAccountFailed(ILogger logger, Exception exception, string embyUserName);

[LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot save the password for user {Username}. The plugin refused the login.")]
private static partial void LogSaveFailed(ILogger logger, Exception exception, string username);
```
Add `LogDeleteFailed` in the same block, same signature shape `(ILogger logger, Exception exception, string embyUserName)`, message naming only the account (D-04 — never a password or hash).

**Branch and dead code to delete for D-05** (lines 80-86, 155-166, 239-240):
```csharp
if (settings.MigrationMode == MigrationMode.JellyfinPasswordFirst
    && resolvedUser?.Password is { } savedHash
    && verifiedPasswords.Matches(resolvedUser.Id, savedHash)
    && SavedPasswordMatches(resolvedUser.Username, savedHash, password))
{
    return new ProviderAuthenticationResult { Username = resolvedUser.Username };
}
```
Delete this block, then delete `SavedPasswordMatches` (lines 155-166) and `LogSavedPasswordUnreadable` (lines 239-240) — both become unused once the block is gone (Pitfall 1 in RESEARCH.md), and the analyzer (`AnalysisMode: AllEnabledByDefault`) fails the build on an unused private member.

**Error-handling pattern (project-wide convention this file establishes):** every refusal throws `AuthenticationException(InvalidLogin)`, and settings-specific failures throw `AuthenticationException(problem)` (line 152) — no other exception type ever leaves `Authenticate`. This is the pattern the widened catches must preserve.

---

### `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` (DI wiring)

**Analog:** itself (lines 26-36) — the existing registration for `EmbyAuthenticationProvider`:
```csharp
serviceCollection.AddSingleton<IAuthenticationProvider, EmbyAuthenticationProvider>();
```
D-10 needs this to become a factory registration that supplies the settings-source delegate, following the existing factory-registration style already used two lines above for `EmbyVerifiedPasswords` (lines 31-33):
```csharp
serviceCollection.AddSingleton(services => new EmbyVerifiedPasswords(
    Path.Combine(services.GetRequiredService<IApplicationPaths>().PluginConfigurationsPath, VerifiedPasswordsFileName),
    services.GetRequiredService<ILogger<EmbyVerifiedPasswords>>()));
```
Copy this factory-lambda shape for `EmbyAuthenticationProvider`, adding `() => EmbyAuthPlugin.Instance?.Configuration` as the new argument (per RESEARCH.md Pattern 1).

---

### `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` (new `FakeUserManager`, fake `ICryptoProvider`)

**Analog:** `StubHttpMessageHandler` / `StubHttpClientFactory` in the same file (lines 13-52) — the established shape for a hand-written test double in this repo: a class implementing the real interface, public fields/properties the test reads directly (no matcher library), and a `throw new InvalidOperationException(...)` default for the unconfigured case.

**Recording-field pattern to copy** (lines 13-23):
```csharp
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];
    ...
```
`FakeUserManager` follows the same shape: public settable `Exception? CreateUserThrows / UpdateUserThrows / DeleteUserThrows`, and public get-only `User? LastCreated`, `User? LastUpdated`, `Guid? LastDeletedId` fields the test asserts against directly — see RESEARCH.md's `FakeUserManager` skeleton (Code Examples section) for the exact per-method shape; it already follows this file's convention.

**Building a real `User` for `CreateUserAsync`'s return value** — copy from `AccountAccessPolicyTests.cs:12-18`:
```csharp
private static User JellyfinDefaultUser()
{
    var user = new User("alice", "provider", "reset-provider");
    user.AddDefaultPermissions();
    user.AddDefaultPreferences();
    return user;
}
```

**Throw-by-default for every unused interface member** — `IUserManager` has 22 members (see RESEARCH.md Code Examples for the full list); every member besides `CreateUserAsync`, `UpdateUserAsync`, `DeleteUserAsync` should be a one-line `=> throw new NotImplementedException();`, matching D-09.

**Fake `ICryptoProvider`:** only `CreatePasswordHash` needs a real body (deterministic, distinguishable per input, e.g. `new PasswordHash("fake", Encoding.UTF8.GetBytes(password.ToString()))`); every other member throws `NotImplementedException`, same convention.

---

### `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` (new file, TEST-01/AUTH-03/AUTH-04)

**Analog:** `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs` — the repo's pattern for a test class against a plugin type with real collaborators plus one stub.

**Imports pattern** (lines 1-11):
```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;
```

**Settings-construction and factory-method pattern** (lines 13-29):
```csharp
private static readonly EmbyAuthSettings Settings =
    new(new Uri("http://emby:8096"), "key-1", MigrationMode.MoveAfterFirstLogin, AccountAccess.CopyEmbyRemoteAccess);

private readonly ManualTimeProvider _clock = new();
...
private EmbyUserDirectory CreateDirectory(StubHttpMessageHandler handler) =>
    new(new EmbyClient(new StubHttpClientFactory(handler), NullLogger<EmbyClient>.Instance), _clock, NullLogger<EmbyUserDirectory>.Instance);
```
`EmbyAuthenticationProviderTests` follows the same shape: a private `CreateProvider(...)` factory method that builds a real `EmbyClient`/`EmbyUserDirectory`/`EmbyVerifiedPasswords` (temp file) plus a `FakeUserManager` wired into a real `ServiceCollection` (RESEARCH.md Pattern 2), a fake `ICryptoProvider`, and `NullLogger<EmbyAuthenticationProvider>.Instance`.

**Theory/InlineData pattern for parameterized assertions** (lines 31-39, 41-51):
```csharp
[Theory]
[InlineData("Alice")]
[InlineData("alice")]
public async Task ReturnsActive_WhenEmbyHasTheUser(string username)
{
    var directory = CreateDirectory(new StubHttpMessageHandler().Then(UserList));

    Assert.Equal(EmbyUserStatus.Active, await directory.GetStatusAsync(Settings, username, CancellationToken.None));
}
```
Use this shape for the three AUTH-04 failure-path tests (`UpdateUserThrows = new DbUpdateException(...)`, `new ResourceNotFoundException(...)`, `new InvalidOperationException(...)` — the third must be a type outside the old `when` filter per RESEARCH.md Pitfall 3) and for the delete-also-fails test (`DeleteUserThrows` set alongside `UpdateUserThrows`). Each test asserts `await Assert.ThrowsAsync<AuthenticationException>(...)`, not an unhandled exception.

**Deterministic HTTP stubbing pattern (`StubHttpMessageHandler.Then(...)`)** (lines 20-26, 36):
```csharp
private static HttpResponseMessage UserList() => new(HttpStatusCode.OK)
{
    Content = new StringContent(
        """[{"Name":"Alice","Policy":{"IsDisabled":false}},{"Name":"ivy","Policy":{"IsDisabled":true}}]""",
        Encoding.UTF8,
        "application/json"),
};
```
Reuse this to stub the Emby user-list and authenticate responses that `EmbyClient`/`EmbyUserDirectory` need before `EmbyAuthenticationProvider.Authenticate` reaches `CreateAccountAsync`/`SavePasswordAsync`.

---

### `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs` (edit one test case, D-05)

**Analog:** itself. Change the `mode:` argument of `CarriesTheChosenMigrationModeAndAccountAccess` (repo line range 34-42 per RESEARCH.md) from `MigrationMode.JellyfinPasswordFirst` to a remaining enum member, e.g. `MigrationMode.KeepEmbyInCharge` or `MigrationMode.MoveAfterFirstLogin` (matches `EmbyUserDirectoryTests.cs:16`, which already uses `MigrationMode.MoveAfterFirstLogin`). No new test needed for D-07 — `Rejects_UnknownMigrationMode` (lines 111-117) already covers the unrelated out-of-range-int scenario (RESEARCH.md Pitfall 5).

---

## Shared Patterns

### Fail-closed exception handling (applies to every widened catch in this phase)
**Source:** `EmbyAuthenticationProvider.cs:144-153` (`GetSettings`) and `:175-179` (existing `CreateUserAsync` catch)
```csharp
catch (ArgumentException ex)
{
    LogCreateAccountFailed(logger, ex, embyLogin.Name);
    throw new AuthenticationException(InvalidLogin, ex);
}
```
**Apply to:** the widened `UpdateUserAsync` catches and the new `DeleteUserAsync` catch — always log at Error with the account name, then either rethrow `AuthenticationException(InvalidLogin, ex)` (save failure, escapes) or swallow after logging (delete failure, per D-03, never escapes).

### `[LoggerMessage]` partial method convention
**Source:** `EmbyAuthenticationProvider.cs:227-258` — every log call in the class is a `private static partial void LogXxx(ILogger logger, ...)` at the bottom of the file, one line each, message text never includes a password, hash, or API key.
**Apply to:** the new `LogDeleteFailed` message.

### Test-double throw-by-default with per-call configurable failure
**Source:** `TestDoubles.cs:13-47` (`StubHttpMessageHandler`)
**Apply to:** `FakeUserManager` and the fake `ICryptoProvider` — public settable exception fields, public read-only "last call" fields, `NotImplementedException` for every unimplemented member. No mocking library (D-09).

### Real `ServiceCollection` instead of a hand-rolled `IServiceProvider`
**Source:** RESEARCH.md Pattern 2, confirmed against `Microsoft.Extensions.DependencyInjection` 10.0.11 already resolving transitively in the test project (`tests/Jellyfin.Plugin.EmbyAuth.Tests/obj/project.assets.json` — build artifact, not cited as an analog path).
**Apply to:** `EmbyAuthenticationProviderTests.cs`'s provider-construction helper, since the constructor resolves `IUserManager` via `serviceProvider.GetRequiredService<IUserManager>()` (`EmbyAuthenticationProvider.cs:106`).

## No Analog Found

None. Every file in this phase is either an in-place edit of an existing file (its own best analog) or a new test file/type with a direct sibling analog in the same test project.

## Metadata

**Analog search scope:** `src/Jellyfin.Plugin.EmbyAuth/`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/`
**Files scanned:** `EmbyAuthenticationProvider.cs`, `PluginServiceRegistrator.cs`, `TestDoubles.cs`, `EmbyUserDirectoryTests.cs`, `AccountAccessPolicyTests.cs`, `EmbyAuthSettingsTests.cs` (all confirmed git-tracked via `git ls-files`)
**Pattern extraction date:** 2026-09-17
