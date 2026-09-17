# Phase 1: Account Creation and Login Security - Research

**Researched:** 2026-09-17
**Domain:** Jellyfin 12.1 plugin authentication (C#/.NET 10) — exception-safety in account creation, and unit-test seams for a class with no prior tests
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Account creation and failure handling**

- **D-01:** Account creation keeps Jellyfin's own steps: `IUserManager.CreateUserAsync(name)`, then the Emby-verified hash and `EmbyAuthenticationProvider.ProviderId` in the `UpdateUserAsync` call directly after it. The plugin adds only the login method and the hash. The maintainer rejected an insert of the account row through `JellyfinDbContext`, which would have removed the moment without a password but would also copy Jellyfin's creation steps (`InternalId`, default permissions, the user name rule at `UserManager.cs:963`) and skip `UserCreatedEventArgs`.
- **D-02:** The plugin never compensates for a failure inside Jellyfin's user store. Every exception type from `CreateUserAsync` and from `UpdateUserAsync` is caught, logged at Error, and refused with `AuthenticationException`, so no login returns HTTP 500. There is no account lookup by name, no single-column move to the Emby login method, and no retry. Jellyfin's own `POST /Users/New` also leaves a half-made account (`Jellyfin.Api/Controllers/UserController.cs:520-533`, tag `v12.1`), and a failure in Jellyfin's store is outside the plugin's control. — **Reversibility:** reversible — the compensating writes were never written; adding one later touches one method.
- **D-03:** The existing best-effort delete of the new account stays (`EmbyAuthenticationProvider.cs:197-203`). A failure of `DeleteUserAsync` is caught and logged; it never escapes and never triggers a second cleanup attempt.
- **D-04:** The Error log for a failed save or a failed delete names the account, so an administrator can find a half-made account. It never contains the password or the API key.

**Removal of `JellyfinPasswordFirst`**

- **D-05:** `MigrationMode.JellyfinPasswordFirst` and the saved-password branch (`EmbyAuthenticationProvider.cs:80-86`) are deleted, with the settings page option, the `docs/settings.md` row, the `docs/how-it-works.md` step 2, and the test that uses the value. No compatibility path and no renamed replacement. — **Reversibility:** one-way — the value disappears from the settings contract; an install that stored it loses its settings (see D-07).
- **D-06:** The two end-to-end tests that use the value are rewritten in place, not moved to a new file, so they reuse the Emby users that `e2e/setup_suite.bash` already creates:
  - `e2e/30-migration-modes.bats:43` becomes a `KeepEmbyInCharge` test: after the password of the Emby user changes, the old password is refused at once, the new password is accepted, and the saved hash follows the new password.
  - `e2e/40-emby-outage.bats:43` becomes: while Emby is unreachable, the user with a verified saved hash (`uma`) is refused on the Emby login method.
- **D-07:** An install that still holds `MigrationMode = JellyfinPasswordFirst` in its settings file is accepted as broken by the change. Jellyfin catches a failed deserialization, builds a default configuration, and saves it over the file (`MediaBrowser.Common/Plugins/BasePluginOfT.cs:184-198`, tag `v12.1`), so that install loses the Emby URL and the API key and refuses every login until an administrator types them again. `CHANGELOG.md` states this. The plugin was never public, so only the maintainer's own servers can be affected.

**Hash and fingerprint writes**

- **D-08:** Every login that Emby accepts keeps saving a fresh hash on the account and recording its fingerprint. No branch that skips the write when the saved hash already verifies the typed password. The write cost belongs to PERF-02 in Phase 4, which measures it first.

**Unit test seams (TEST-01)**

- **D-09:** `IUserManager` is replaced by a hand-written `FakeUserManager` in `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs`. It implements `CreateUserAsync`, `UpdateUserAsync`, and `DeleteUserAsync`, throws `NotImplementedException` for every other member, and can fail each method with a chosen exception type. No mocking library: the repository has none, and `.planning/research/STACK.md:15` recommends NSubstitute only to avoid maintaining such a fake. — **Reversibility:** reversible — a mocking library can replace the fake later.
- **D-10:** `EmbyAuthenticationProvider` stops reading the static `EmbyAuthPlugin.Instance` (`EmbyAuthenticationProvider.cs:146`). It takes a settings source in its constructor, and `PluginServiceRegistrator` passes a source that reads `EmbyAuthPlugin.Instance?.Configuration`. Tests then set the settings with no static state, so parallel test classes cannot affect each other. `MoveToDefaultLoginMethod.cs:34` reads the same static and can use the seam when Phase 3 tests that class. — **Reversibility:** costly — the constructor signature and the service registration change together.
- **D-11:** `ICryptoProvider` also needs a test double, because the Jellyfin implementation is not in the `Jellyfin.Controller` package. Keep it in `TestDoubles.cs` beside the others.

### Claude's Discretion

- The exact shape of the settings source in D-10 (`Func<PluginConfiguration?>` or a small interface).
- Log message wording for each failure path, within the rule that no message holds a password or the API key.
- The split of the unit tests over test classes and the test names, following the naming pattern in `.planning/codebase/TESTING.md`.
- The wording in `docs/how-it-works.md` for the moment when a new account has no password, as long as it states the risk plainly and names the cause (Jellyfin has no create-with-password call).

### Deferred Ideas (OUT OF SCOPE)

- **Insert the account row directly, with the password set, through `JellyfinDbContext`** — removes the moment without a password completely. Rejected for v1: it copies Jellyfin's creation steps and skips `UserCreatedEventArgs`. Revisit only if Jellyfin adds a create-with-password call.
- **A compensating write when the save fails** (single-column move to the Emby login method, an account lookup by name, or a retry queue) — considered and rejected by D-02. Recorded so that a later phase does not rediscover it as new.
- **Skip the hash and fingerprint write when the password did not change** — belongs to PERF-02 in Phase 4, after the load test measures the cost.
- **Settings that survive an unknown stored value** — rejected by D-07; it would keep a compatibility path that AUTH-02 forbids.

### Required Roadmap Correction (carried from CONTEXT.md)

Phase 1 success criterion 1 in `.planning/ROADMAP.md:29` currently states the plugin leaves "no enabled account ... on the Default login method without a password" in every failure case. D-02 and D-03 narrow this: when Jellyfin's save AND its delete both fail, the plugin logs at Error and stops — it does not retry outside `IUserManager`. The planner must update `.planning/ROADMAP.md` success criterion 1, `.claude/rules/plugin.md:33-36`, and `.planning/PROJECT.md:74`'s constraint wording to say: every failure path refuses the login and never returns HTTP 500, and the plugin always *attempts* the delete — but when both the save and the delete fail, the account can stay on Default without a password, logged at Error, matching Jellyfin's own `POST /Users/New` contract.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| AUTH-04 | No failure while creating or saving an account (failed save, failed cleanup delete, or an unexpected exception type) returns HTTP 500 or leaves an enabled account on the Default login method without a password (as narrowed above). | Verified exact exception types `CreateUserAsync`/`UpdateUserAsync`/`DeleteUserAsync` throw in Jellyfin 12.1 source (see Code Examples, Common Pitfalls). Confirms the current `catch (Exception ex) when (ex is DbUpdateException or ResourceNotFoundException)` clauses must widen to catch every exception type, and that the `finally` block's `DeleteUserAsync` call today has **no** surrounding try/catch at all. |
| AUTH-03 | New account gets the Emby-verified hash and the Emby login method in the `UpdateUserAsync` call directly after `CreateUserAsync`; `docs/how-it-works.md` describes the brief moment before that save. | Confirmed this pattern already exists in `CreateAccountAsync` (`EmbyAuthenticationProvider.cs:168-207`) and needs documentation, not a code reorder. Verified Jellyfin's own `CreateUserAsync` has no atomic create-with-password call (see Architecture Patterns). |
| AUTH-01 | While on the Emby login method, only Emby decides a login; the saved hash is refreshed after every accepted login. | Verified `verifiedPasswords.Record(user.Id, passwordHash)` already runs unconditionally after every Emby-accepted login (`EmbyAuthenticationProvider.cs:111`), independent of `MigrationMode` — so `uma` in the rewritten `40-emby-outage.bats` gets a verified hash from an ordinary `KeepEmbyInCharge` login, no `JellyfinPasswordFirst` needed (see Common Pitfalls). |
| AUTH-02 | `JellyfinPasswordFirst` / "Check the saved Jellyfin password first" removed from settings, page, docs, and tests. | Full enumeration of every reference across the repo (see Common Pitfalls: Dead Code) — 9 non-generated locations, including one line of *dead code* (`SavedPasswordMatches`) that removal creates but D-05 does not explicitly call out. |
| TEST-01 | Unit tests cover `EmbyAuthenticationProvider`: account checks, Emby login, account creation/update, and every AUTH-04 failure path. | Verified exact `IUserManager`/`ICryptoProvider` member lists via reflection against the pinned `Jellyfin.Controller`/`Jellyfin.Model` 12.1.0 packages (see Code Examples) — gives the planner the complete `NotImplementedException` surface the `FakeUserManager` must cover. |
</phase_requirements>

## Summary

This phase is a narrow, well-scoped fix inside one method family (`EmbyAuthenticationProvider.CreateAccountAsync`/`SavePasswordAsync`) plus a repo-wide deletion of one enum value and its dependent branch. CONTEXT.md already resolved every design decision (D-01 through D-11); this research verifies the Jellyfin-internal facts those decisions depend on against the actual pinned package (`Jellyfin.Controller`/`Jellyfin.Model` 12.1.0) and the actual Jellyfin 12.1 server source, and fills in the concrete shape of the two new test doubles the plan will need to write first.

The key verified finding is the exact exception surface of the three `IUserManager` calls the plugin makes. `CreateUserAsync` only ever throws `ArgumentException` (already handled). `UpdateUserAsync` throws `ResourceNotFoundException` if the row disappeared, or lets `SaveChangesAsync` throw `DbUpdateException` (already handled) — but the current code's `when (ex is DbUpdateException or ResourceNotFoundException)` filter is not exhaustive against "an unexpected exception type," which is explicitly one of the three required test scenarios. `DeleteUserAsync` can additionally throw `InvalidOperationException` ("last user") or `ArgumentException` ("last admin") — neither reachable for a just-created non-admin account, but the cleanup call today has **no catch at all** around it, so any exception there — expected or not — already escapes as HTTP 500. Both gaps must close for AUTH-04.

**Primary recommendation:** Build the two test doubles (`FakeUserManager`, a fake `ICryptoProvider`) and the settings-source constructor seam (D-10) first, write the three failing AUTH-04 tests against them, then widen both `catch` clauses in `EmbyAuthenticationProvider.cs` to unconditional `catch (Exception ex)` and wrap the orphaned `DeleteUserAsync` call in its own try/catch — in that order, matching the phase's stated work order (seam and tests, then AUTH-04, AUTH-03, AUTH-01, AUTH-02).

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Password verification against Emby | API/Backend (plugin `EmbyAuthenticationProvider`) | — (external service: Emby, via `EmbyClient`) | The plugin is itself a backend authentication provider inside Jellyfin's server process; there is no browser/client tier in this phase (`UI hint: no`). |
| Account creation, hash/login-method save, cleanup-on-failure | API/Backend (plugin) | Database/Storage (Jellyfin's own EF Core `JellyfinDbContext`, opaque behind `IUserManager`) | The plugin never touches `JellyfinDbContext` directly for account creation (D-01); all writes go through Jellyfin's own `IUserManager`, which owns the actual persistence. |
| Verified-password fingerprint recording | API/Backend (plugin `EmbyVerifiedPasswords`) | Database/Storage (a plugin-owned JSON file, not the Jellyfin DB) | Out of scope for this phase's *changes* (fingerprint failure handling is Phase 2/3), but every login path in scope calls into it (D-08), so tests must account for it. |
| Settings source (removal of `JellyfinPasswordFirst`, D-10 seam) | API/Backend (plugin configuration) | — | Plugin settings live in Jellyfin's `PluginConfiguration`, saved via `XmlSerializer`; no UI-tier change in this phase (the settings page HTML edit is a one-line `<option>` removal, not new UI logic). |
| Unit test doubles (`FakeUserManager`, fake `ICryptoProvider`) | Test infrastructure (not a runtime tier) | — | Exists only inside `tests/`; never shipped, never touches a runtime tier. |

## Project Constraints (from CLAUDE.md)

Extracted from `/Users/tkensiski/projects/jellyfin-plugin-emby-auth/CLAUDE.md` (repo root) — binding for every task in this phase:

- **Test-first, prove-it-fails:** "Write the test first. Then break the code once and watch the test fail." Applies directly to TEST-01/AUTH-04: each new `EmbyAuthenticationProviderTests.cs` case must be written and shown red against the *current* narrow catch clause before the catch is widened.
- **E2E required for Jellyfin/Emby-dependent changes:** "A change that depends on Jellyfin or Emby behavior needs an end-to-end test. Run `mise run e2e` for every change to `src/`." AUTH-01/AUTH-02's `docs/how-it-works.md` and `e2e/*.bats` rewrites are covered by D-06; every `src/` edit in this phase (the two widened catches) still requires `mise run e2e` before merge.
- **Pre-commit hook:** "Run `prek run` before each commit."
- **No secrets in logs:** "Never put a password or the API key in a log or exception message." Binds D-04's Error-log wording — account name only, never a hash or password.
- **Zero-warnings / analyzer discipline:** "Warnings are errors, and the plugin project uses `AnalysisMode` `AllEnabledByDefault`. Fix a warning. Suppress it only with a `Justification`." Directly relevant: deleting the `JellyfinPasswordFirst` branch leaves `SavedPasswordMatches` and `LogSavedPasswordUnreadable` as unused private members — the analyzer will fail the build (IDE0051/CA1822-class warnings-as-errors) unless both are deleted in the same change (see Common Pitfalls).
- **Version pinning:** "Pin exact versions. Look up the current stable version before a bump." Not triggered — this phase adds no new package.
- **Jellyfin version bump discipline:** not triggered — no Jellyfin/Emby version change in this phase.

The `.claude/CLAUDE.md` (workflow-enforcement layer) requires this work to proceed through a GSD command (`/gsd-execute-phase`), not direct edits — already satisfied by the calling workflow.

## Standard Stack

This phase adds **no new package** to either `src/` or `tests/` projects. It exercises only what is already pinned and already resolvable.

### Core (existing, re-verified for this phase)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Jellyfin.Controller` | 12.1.0 `[VERIFIED: src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj:11]` | `IUserManager`, `IAuthenticationProvider`, `IRequiresResolvedUser` | Already the pinned Jellyfin server API surface; this phase only consumes existing interfaces. |
| `Jellyfin.Model` | 12.1.0 (transitive via `Jellyfin.Controller`) `[VERIFIED: reflection against MediaBrowser.Model.dll — see Code Examples]` | `ICryptoProvider`, `PasswordHash` | Same reasoning. |
| `xunit.v3` | 4.0.1 `[VERIFIED: tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj:10]` | Unit test framework, Microsoft.Testing.Platform runner | Existing, only framework in the repo. |
| `Microsoft.Extensions.DependencyInjection` | 10.0.11 (transitive, already resolves in the test project) `[VERIFIED: tests/Jellyfin.Plugin.EmbyAuth.Tests/obj/project.assets.json]` | Build a real `ServiceCollection`/`IServiceProvider` in tests for D-10's constructor seam | No new package needed — confirmed present via the test project's own dependency lock file. |
| `Microsoft.EntityFrameworkCore` | 10.0.11 (transitive, already resolves in the test project) `[VERIFIED: tests/Jellyfin.Plugin.EmbyAuth.Tests/obj/project.assets.json]` | `DbUpdateException` type, for `FakeUserManager` to throw a realistic "expected" exception | No new package needed for the existing catch-clause exception type. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Hand-written `FakeUserManager` (D-09) | NSubstitute 6.2.0 `[CITED: .planning/research/STACK.md:15]` | `.planning/research/STACK.md` recommends NSubstitute specifically because `IUserManager` has 22 members and a throw-by-default hand stub is tedious to extend. D-09 explicitly rejects this for v1 (no mocking library in the repo today, and the plugin only calls 3 of the 22 members) but flags it as reversible — a future phase adding more `IUserManager` calls should revisit NSubstitute rather than keep hand-extending `FakeUserManager`. |

**Installation:** None required — this phase adds no `dotnet add package` or `npm install` step.

## Package Legitimacy Audit

**Not applicable.** This phase introduces zero new external packages to either the `src/` or `tests/` projects. All types used (`IUserManager`, `ICryptoProvider`, `PasswordHash`, `DbUpdateException`, `ServiceCollection`) come from packages already pinned in the committed `.csproj` files or already resolved transitively (confirmed by inspecting `tests/Jellyfin.Plugin.EmbyAuth.Tests/obj/project.assets.json`). No `checkpoint:human-verify` task is needed for a package install in this phase.

## Architecture Patterns

### System Architecture Diagram

```
Jellyfin UserManager.AuthenticateWithProvider
        │  catch(AuthenticationException) ONLY — anything else becomes HTTP 500
        │  [VERIFIED: jellyfin/jellyfin UserManager.cs:1070-1090, tag v12.1]
        ▼
EmbyAuthenticationProvider.Authenticate(username, password, resolvedUser)
        │
        ├─ 1. Account checks (blank password / disabled / admin)         — unchanged, in scope for TEST-01 coverage only
        ├─ 2. GetSettings() — validate PluginConfiguration                — D-10 changes its *source*, not its logic
        ├─ 3. userDirectory.GetStatusAsync → Active/Disabled/NotFound/Unavailable
        ├─ 4. embyClient.AuthenticateAsync(username, password) → EmbyLogin?
        ├─ 5. LoginDecision.Decide → Deny / CreateAccount / UseAccount
        ├─ 6a. CreateAccountAsync (LoginAction.CreateAccount)             ◄── AUTH-03/AUTH-04 fix lands here
        │       │
        │       ├─ userManager.CreateUserAsync(name)                     — throws only ArgumentException (handled)
        │       │       [VERIFIED: UserManager.cs:339-368, tag v12.1]
        │       ├─ set user.Password, user.AuthenticationProviderId,
        │       │   AccountAccessPolicy.ApplyToNewAccount(...)           — in-memory only, cannot fail
        │       ├─ userManager.UpdateUserAsync(user)                     — throws ResourceNotFoundException
        │       │       [VERIFIED: UserManager.cs:213-225, tag v12.1]      or lets SaveChangesAsync throw DbUpdateException
        │       │                                                          — TODAY: catch filter excludes "unexpected" types → HTTP 500
        │       └─ finally: userManager.DeleteUserAsync(user.Id)         — TODAY: NO try/catch at all → any exception → HTTP 500
        │               [VERIFIED: EmbyAuthenticationProvider.cs:197-203] can itself throw ResourceNotFoundException,
        │               InvalidOperationException, or ArgumentException [VERIFIED: UserManager.cs:371-416, tag v12.1]
        ├─ 6b. SavePasswordAsync (LoginAction.UseAccount)                 — existing account, same UpdateUserAsync exception surface
        └─ 7. verifiedPasswords.Record(user.Id, passwordHash)             — unconditional, every accepted Emby login (AUTH-01)
        ▼
ProviderAuthenticationResult → Jellyfin sets user.AuthenticationProviderId = this provider
        ▼ (async, after login completes)
MoveToDefaultLoginMethod (event consumer) — out of scope, unaffected by this phase
```

### Recommended Project Structure

No new files or folders beyond one new test file — this phase edits in place:

```
src/Jellyfin.Plugin.EmbyAuth/
├── EmbyAuthenticationProvider.cs       # widen 2 catch clauses, add try/catch around DeleteUserAsync,
│                                       # remove JellyfinPasswordFirst branch + SavedPasswordMatches + its log method,
│                                       # constructor takes settings source (D-10)
├── PluginServiceRegistrator.cs         # pass the settings source into EmbyAuthenticationProvider (D-10)
├── Configuration/
│   ├── PluginConfiguration.cs          # remove MigrationMode.JellyfinPasswordFirst
│   └── configPage.html                 # remove the <option value="JellyfinPasswordFirst"> row
tests/Jellyfin.Plugin.EmbyAuth.Tests/
├── TestDoubles.cs                      # add FakeUserManager, fake ICryptoProvider (D-09, D-11)
├── EmbyAuthenticationProviderTests.cs  # NEW — TEST-01, does not exist today
└── EmbyAuthSettingsTests.cs            # change the JellyfinPasswordFirst test case to a remaining enum member
e2e/
├── 30-migration-modes.bats             # rewrite the JellyfinPasswordFirst test → KeepEmbyInCharge (D-06)
└── 40-emby-outage.bats                 # rewrite setup_file + the JellyfinPasswordFirst test (D-06)
docs/
├── how-it-works.md                     # remove step 2 (saved-password), describe the AUTH-03 window
└── settings.md                         # remove the JellyfinPasswordFirst row
```

### Pattern 1: Settings-source constructor injection (D-10)

**What:** Replace the static-field read `EmbyAuthPlugin.Instance?.Configuration` inside `GetSettings()` with a constructor-injected delegate.
**When to use:** Whenever a type under test needs to control plugin settings without touching global static state (also reusable by Phase 3 for `MoveToDefaultLoginMethod`).
**Example (shape, per D-10's "Claude's Discretion" — a `Func<PluginConfiguration?>` is the minimal option; document the choice in the plan, not here):**
```csharp
// Constructor parameter, replacing the direct read of EmbyAuthPlugin.Instance:
internal sealed partial class EmbyAuthenticationProvider(
    IServiceProvider serviceProvider,
    ICryptoProvider cryptoProvider,
    EmbyClient embyClient,
    EmbyUserDirectory userDirectory,
    EmbyVerifiedPasswords verifiedPasswords,
    Func<PluginConfiguration?> configurationSource,   // NEW — D-10
    ILogger<EmbyAuthenticationProvider> logger)
    : IAuthenticationProvider, IRequiresResolvedUser
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
    // ...
}
```
```csharp
// PluginServiceRegistrator.cs — production wiring:
serviceCollection.AddSingleton<IAuthenticationProvider>(services => new EmbyAuthenticationProvider(
    services,
    services.GetRequiredService<ICryptoProvider>(),
    services.GetRequiredService<EmbyClient>(),
    services.GetRequiredService<EmbyUserDirectory>(),
    services.GetRequiredService<EmbyVerifiedPasswords>(),
    () => EmbyAuthPlugin.Instance?.Configuration,
    services.GetRequiredService<ILogger<EmbyAuthenticationProvider>>()));
```
`[ASSUMED]` — the exact constructor/registration shape above is illustrative; D-10 leaves the concrete form (delegate vs. small interface) to the planner's discretion. Not tool-verified because it does not exist in the codebase yet.

### Pattern 2: Fake collaborators via real `ServiceCollection`, not a fake `IServiceProvider`

**What:** Build a real `Microsoft.Extensions.DependencyInjection.ServiceCollection`, register `FakeUserManager` as `IUserManager`, call `.BuildServiceProvider()`, and pass the result as the constructor's `IServiceProvider serviceProvider` argument.
**When to use:** For every `EmbyAuthenticationProviderTests.cs` case that reaches `CreateAccountAsync`/`SavePasswordAsync`, since the constructor resolves `IUserManager` via `serviceProvider.GetRequiredService<IUserManager>()` (`EmbyAuthenticationProvider.cs:106`) rather than accepting it directly.
**Example:**
```csharp
// Source: Microsoft.Extensions.DependencyInjection, already a transitive dependency
// [VERIFIED: tests/Jellyfin.Plugin.EmbyAuth.Tests/obj/project.assets.json — Microsoft.Extensions.DependencyInjection/10.0.11]
var services = new ServiceCollection();
services.AddSingleton<IUserManager>(new FakeUserManager());
var provider = services.BuildServiceProvider();
```

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Mocking `IUserManager` with call verification/argument matching | A bespoke assertion DSL on top of `FakeUserManager` | Plain fields the fake records (`LastCreatedName`, `LastUpdatedUser`, `LastDeletedId`) and `Assert.Equal` against them | Matches the repo's existing `StubHttpMessageHandler.Requests` pattern (`TestDoubles.cs:17`) — a list/field the test reads directly, no matcher library. |
| A generic "throw the Nth exception" fake framework | A per-call configurable exception field on `FakeUserManager` (e.g. `public Exception? CreateUserThrows`, `UpdateUserThrows`, `DeleteUserThrows`) | D-09 already specifies this shape ("can fail each method with a chosen exception type"); building anything more general is premature abstraction for a fake used by one test file. |
| A DI container abstraction to avoid `ServiceCollection` in tests | `IServiceProvider` implemented by hand (a `switch` on `Type`) | `new ServiceCollection().AddSingleton<IUserManager>(fake).BuildServiceProvider()` | The real container is already a transitive dependency (see Standard Stack) and is one line; a hand-rolled `IServiceProvider` would need to also implement `GetService(Type)` correctly for generic resolution, which is more code than the real thing. |

**Key insight:** Every "don't hand-roll" item above resolves to "use the thing that's already a transitive dependency, in the plainest possible shape" — this phase has no genuinely new infrastructure problem, only test-double shape decisions the repo's existing `TestDoubles.cs` conventions already answer.

## Common Pitfalls

### Pitfall 1: Deleting the `JellyfinPasswordFirst` branch leaves dead code that fails the build

**What goes wrong:** `SavedPasswordMatches` (`EmbyAuthenticationProvider.cs:155-166`) and `LogSavedPasswordUnreadable` (`EmbyAuthenticationProvider.cs:239-240`) are called *only* from the branch D-05 deletes (`EmbyAuthenticationProvider.cs:80-86`). `[VERIFIED: EmbyAuthenticationProvider.cs:83 " && SavedPasswordMatches(resolvedUser.Username, savedHash, password))" — grep confirms no other call site in src/ or tests/]`. Deleting only lines 80-86 leaves both members unused.
**Why it happens:** D-05's bullet list of removal targets (branch, enum value, settings page option, two docs rows, one test) does not mention the two now-orphaned private members, because they are an indirect consequence, not a direct reference to the value.
**How to avoid:** In the same change that removes the branch, delete `SavedPasswordMatches` and `LogSavedPasswordUnreadable` together. Because `Directory.Build.props` sets warnings-as-errors and the plugin project uses `AnalysisMode: AllEnabledByDefault` `[CITED: CLAUDE.md "Warnings are errors, and the plugin project uses AnalysisMode AllEnabledByDefault"]`, an unused private member is caught at build time (`dotnet format --verify-no-changes` / `dotnet test`), not silently — but only once the branch is actually removed, so this should surface immediately if missed, not later.
**Warning signs:** `mise run test` or `mise run lint` fails after removing only the `if` block, citing an unused-member analyzer rule.

### Pitfall 2: The cleanup delete has no exception handling at all today — not a narrow catch, a missing one

**What goes wrong:** The current `finally` block:
```csharp
finally
{
    if (!saved)
    {
        await userManager.DeleteUserAsync(user.Id).ConfigureAwait(false);
    }
}
```
has zero try/catch around `DeleteUserAsync`. `[VERIFIED: EmbyAuthenticationProvider.cs:197-203]`. Any exception here — `ResourceNotFoundException`, `InvalidOperationException`, `ArgumentException` `[VERIFIED: jellyfin/jellyfin UserManager.cs:371-416, tag v12.1, quoted below]`, or anything else — propagates out of the `finally`, out of `Authenticate`, and is not `AuthenticationException`, so Jellyfin's caller does not catch it and the login returns HTTP 500 `[VERIFIED: jellyfin/jellyfin UserManager.cs:1070-1090, tag v12.1 — "catch (AuthenticationException ex)" is the only catch clause in AuthenticateWithProvider]`.

Verbatim quote of the three throw sites inside `DeleteUserAsync` (`UserManager.cs:371-416`, tag `v12.1`):
```csharp
if (user is null)
{
    throw new ResourceNotFoundException(nameof(userId));
}

var userCount = await dbContext.Users.CountAsync().ConfigureAwait(false);
if (userCount == 1)
{
    throw new InvalidOperationException(string.Format(...));
}

if (user.HasPermission(PermissionKind.IsAdministrator)
    && await dbContext.Users.CountAsync(i => i.Permissions.Any(p => p.Kind == PermissionKind.IsAdministrator && p.Value)).ConfigureAwait(false) == 1)
{
    throw new ArgumentException(string.Format(...), nameof(userId));
}
```
The last two conditions (`InvalidOperationException`, `ArgumentException`) are unreachable for a just-created non-administrator account in a system that already has at least one other user — but the *first* (`ResourceNotFoundException`, e.g. a concurrent delete of the same row) is plausible, and the test scenario in Success Criterion 1 ("make the cleanup delete fail as well") does not need a realistic trigger — a `FakeUserManager` configured to throw any of these proves the fix.
**Why it happens:** The original code correctly identified that the save could fail and added a `finally`-based cleanup, but treated the cleanup call itself as infallible.
**How to avoid:** Wrap the `DeleteUserAsync` call in its own `try { } catch (Exception ex) { LogDeleteFailed(logger, ex, embyLogin.Name); }` inside the `finally` block, so a delete failure is logged (D-04) but never escapes, and the method still proceeds to `throw new AuthenticationException(...)` from the outer `catch` for the original save failure.
**Warning signs:** A unit test that configures `FakeUserManager.DeleteUserThrows` and asserts the login is refused with `AuthenticationException` (not an unhandled exception) will fail against today's code with the raw exception surfacing from the test's `await` — the test itself is the detector.

### Pitfall 3: Widening the catch on `UpdateUserAsync` without a corresponding "unexpected type" test proves nothing

**What goes wrong:** Success Criterion 1 requires a test that throws "an unexpected exception type" from the save. If the plan only tests `DbUpdateException` and `ResourceNotFoundException` (the two types already caught), it re-confirms existing behavior and never exercises the actual fix (removing the `when` filter).
**Why it happens:** It is easy to reuse the two already-known exception types for all three required scenarios ("save fails," "cleanup fails," "unexpected type") since they are already imported and referenced in the file.
**How to avoid:** Pick a third exception type that is neither `DbUpdateException` nor `ResourceNotFoundException` for the "unexpected type" test — e.g., `InvalidOperationException` or a plain `Exception`. Confirm the test fails against the current `when (ex is DbUpdateException or ResourceNotFoundException)` filter (it will — the exception escapes uncaught) before widening the catch.
**Warning signs:** All three AUTH-04 tests pass without any production-code change — a sign the "unexpected type" case did not actually hit the narrow filter.

### Pitfall 4: Assuming `JellyfinPasswordFirst` removal breaks the `uma`/verified-hash e2e scenario

**What goes wrong:** It looks like D-06's rewrite of `40-emby-outage.bats` needs `JellyfinPasswordFirst` mode to create a verified-hash user, since that is the only mode the *current* test uses to reach that state.
**Why it happens:** In the current test, `JellyfinPasswordFirst` is set before `uma`'s first login only because that mode happens to be the one under test elsewhere in the same file — not because verified-hash recording depends on it.
**How to avoid:** `verifiedPasswords.Record(user.Id, passwordHash)` (`EmbyAuthenticationProvider.cs:111`) runs after **every** Emby-accepted login regardless of `MigrationMode` `[VERIFIED: EmbyAuthenticationProvider.cs:79-113 — the Record call at line 111 is outside any MigrationMode-conditioned branch]`. Setting `KeepEmbyInCharge` before `uma`'s login (so `uma` stays on the Emby method rather than moving to Default) achieves the same verified-hash state without touching the removed mode.
**Warning signs:** A rewritten `40-emby-outage.bats` that still references `MigrationMode` in a way that couples the outage scenario to a migration-behavior choice unrelated to what's being tested (Emby unreachability).

### Pitfall 5: `EmbyAuthSettings.Rejects_UnknownMigrationMode` test already covers the D-07 "unknown enum value" path — don't duplicate it

**What goes wrong:** D-07 documents that an install with a stored `JellyfinPasswordFirst` value gets a default configuration from Jellyfin's own deserialization-failure handling — this could be mistaken for a gap needing a new "reject unknown migration mode" test.
**Why it happens:** D-07's scenario (an *enum value removed from the C# type* causes .NET's `XmlSerializer` to fail deserialization entirely) is different from `EmbyAuthSettingsTests.Rejects_UnknownMigrationMode` (`EmbyAuthSettingsTests.cs:111-117`), which tests an *in-range* `PluginConfiguration` object with `(MigrationMode)99` — a value that deserializes fine as an int but fails `Enum.IsDefined`.
**How to avoid:** No new unit test is needed for D-07 itself — it is a documentation-only decision (state it in `CHANGELOG.md`, per D-07). `EmbyAuthSettingsTests.CarriesTheChosenMigrationModeAndAccountAccess` (`EmbyAuthSettingsTests.cs:34-42`) is the only existing test that references the value being removed and needs its `mode:` argument changed to a remaining enum member (e.g., `KeepEmbyInCharge`).
**Warning signs:** A plan task titled "add a test for the JellyfinPasswordFirst settings-file-rejection behavior" — that behavior lives entirely in Jellyfin core (`BasePluginOfT.cs:184-198`) and is untestable from this repo.

## Code Examples

### `IUserManager` — the exact interface `FakeUserManager` must implement

Obtained by reflecting the pinned package assembly directly (not training-data recall):
```
// Source: MediaBrowser.Controller.dll, from NuGet package Jellyfin.Controller 12.1.0
// [VERIFIED: reflection dump against ~/.nuget/packages/jellyfin.controller/12.1.0/lib/net10.0/MediaBrowser.Controller.dll,
//  the exact version pinned in src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj:11]
public interface IUserManager
{
    event EventHandler<GenericEventArgs<User>> OnUserUpdated;
    IEnumerable<User> GetUsers();
    IEnumerable<Guid> GetUsersIds();
    Task InitializeAsync();
    User GetUserById(Guid id);
    User GetFirstUser();
    User GetUserByName(string name);
    Task RenameUser(Guid userId, string oldName, string newName);
    Task UpdateUserAsync(User user);                       // ← used by the plugin
    Task<User> CreateUserAsync(string name);                // ← used by the plugin
    Task DeleteUserAsync(Guid userId);                      // ← used by the plugin
    Task ResetPassword(Guid userId);
    Task ChangePassword(Guid userId, string newPassword);
    UserDto GetUserDto(User user, string remoteEndPoint);
    Task<ProviderAuthenticationResult> AuthenticateUser(string username, string password, string remoteEndPoint, bool isUserSession);
    Task<ForgotPasswordResult> StartForgotPasswordProcess(string enteredUsername, bool isInNetwork);
    Task<PinRedeemResult> RedeemPasswordResetPin(string pin);
    NameIdPair[] GetAuthenticationProviders();
    NameIdPair[] GetPasswordResetProviders();
    Task UpdateConfigurationAsync(Guid userId, UserConfiguration config);
    Task UpdatePolicyAsync(Guid userId, UserPolicy policy);
    Task ClearProfileImageAsync(User user);
}
```
(Return-type generic arguments and exact parameter names for the non-plugin-used members were normalized by the reflection dump; the member list and the three used signatures — `CreateUserAsync(string)`, `UpdateUserAsync(User)`, `DeleteUserAsync(Guid)` — are exact.)

### `FakeUserManager` skeleton (D-09)

```csharp
// tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs
public sealed class FakeUserManager : IUserManager
{
    public Exception? CreateUserThrows { get; set; }
    public Exception? UpdateUserThrows { get; set; }
    public Exception? DeleteUserThrows { get; set; }

    public User? LastCreated { get; private set; }
    public User? LastUpdated { get; private set; }
    public Guid? LastDeletedId { get; private set; }

    public Task<User> CreateUserAsync(string name)
    {
        if (CreateUserThrows is not null)
        {
            throw CreateUserThrows;
        }

        // A real Jellyfin User needs the two provider IDs (default + reset) that
        // UserManager.CreateUserInternalAsync supplies — see AccountAccessPolicyTests.cs:12-18
        // for the repo's existing pattern for building one.
        var user = new User(name, "provider", "reset-provider");
        user.AddDefaultPermissions();
        user.AddDefaultPreferences();
        LastCreated = user;
        return Task.FromResult(user);
    }

    public Task UpdateUserAsync(User user)
    {
        if (UpdateUserThrows is not null)
        {
            throw UpdateUserThrows;
        }

        LastUpdated = user;
        return Task.CompletedTask;
    }

    public Task DeleteUserAsync(Guid userId)
    {
        if (DeleteUserThrows is not null)
        {
            throw DeleteUserThrows;
        }

        LastDeletedId = userId;
        return Task.CompletedTask;
    }

    // Every other IUserManager member: throw NotImplementedException (D-09).
    public IEnumerable<User> GetUsers() => throw new NotImplementedException();
    // ... (19 more members, one line each)
}
```
`[ASSUMED]` for the skeleton's exact field/property names — D-09 fixes the *behavior* (throws-by-default, per-method configurable failure), not the fake's internal naming, which is planner/implementer discretion.

### `ICryptoProvider` — the interface a fake must implement (D-11)

```
// Source: MediaBrowser.Model.dll, from NuGet package Jellyfin.Model 12.1.0
// [VERIFIED: reflection dump against ~/.nuget/packages/jellyfin.model/12.1.0/lib/net10.0/MediaBrowser.Model.dll]
public interface ICryptoProvider
{
    string DefaultHashMethod { get; }
    PasswordHash CreatePasswordHash(ReadOnlySpan<char> password);
    bool Verify(PasswordHash hash, ReadOnlySpan<char> password);
    byte[] GenerateSalt();
    byte[] GenerateSalt(int length);
}
```
A minimal fake only needs `CreatePasswordHash` (called in both `CreateAccountAsync`/`SavePasswordAsync` and `ChangePassword`) to return a deterministic, distinguishable `PasswordHash` per input password — e.g. `new PasswordHash("fake", Encoding.UTF8.GetBytes(password.ToString()))` — so tests can assert the exact hash string landed in `UpdateUserAsync`'s argument without needing real cryptographic verification.

### Widened catch — the AUTH-04 fix shape

```csharp
// EmbyAuthenticationProvider.cs — CreateAccountAsync, illustrative shape only
var saved = false;
try
{
    await userManager.UpdateUserAsync(user).ConfigureAwait(false);
    saved = true;
}
catch (Exception ex)                                    // was: when (ex is DbUpdateException or ResourceNotFoundException)
{
    LogSaveFailed(logger, ex, embyLogin.Name);
    throw new AuthenticationException(InvalidLogin, ex);
}
finally
{
    if (!saved)
    {
        try
        {
            await userManager.DeleteUserAsync(user.Id).ConfigureAwait(false);
        }
        catch (Exception ex)                             // NEW — today there is no catch here at all
        {
            LogDeleteFailed(logger, ex, embyLogin.Name);
        }
    }
}
```
`[ASSUMED]` for this exact refactor shape (log method names, structure) — it follows directly from D-02/D-03/D-04 and the verified exception surface above, but the concrete diff is an implementation decision for the plan, not a fact this research verified against a running build.

## State of the Art

Not applicable in the "old vs. new library version" sense — this phase changes application logic and test scaffolding, not a dependency version. The one relevant "state of the art" shift is internal to the plugin's own security model: moving from "some exception types are caught" (implicit allow-list) to "every exception type is caught, only `AuthenticationException` is re-thrown" (explicit fail-closed default), matching the Architectural Constraint already documented for the rest of the codebase (`.planning/codebase/ARCHITECTURE.md:265`, "Jellyfin catches only `AuthenticationException` from a login method").

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | The exact constructor/DI shape for the D-10 settings source (`Func<PluginConfiguration?>` vs. a small interface) | Architecture Patterns, Pattern 1 | Low — D-10 explicitly leaves this to discretion; either shape satisfies the requirement (no static read, injectable in tests). |
| A2 | The exact field/property names and internal shape of `FakeUserManager` and the fake `ICryptoProvider` | Code Examples | Low — D-09/D-11 fix behavior, not naming; any shape that throws-by-default and allows per-method failure injection satisfies the decision. |
| A3 | The exact refactor diff for the widened `catch` clauses and the new `try/catch` around `DeleteUserAsync` | Code Examples | Low — the verified exception surface (Pitfall 2/3) constrains *what* must be caught; the precise code shape is an implementation choice within that constraint. |

**No assumption in this table affects a security-relevant decision** — all touch only test/implementation shape, not the auth semantics D-01 through D-11 already lock.

## Open Questions

1. **Does AUTH-04's "creating or saving an account" phrase also require widening the catch in `SavePasswordAsync` (existing-account path), not just `CreateAccountAsync`?**
   - What we know: CONTEXT.md's D-02/D-03/D-04 and the phase's Success Criterion 1 discuss only the *creation* path (`CreateUserAsync` + its cleanup delete). `SavePasswordAsync`'s `UpdateUserAsync` call (`EmbyAuthenticationProvider.cs:209-225`) has the identical narrow `when (ex is DbUpdateException or ResourceNotFoundException)` filter and the identical HTTP-500 exposure for an unexpected exception type, but touches an *existing* account with no blank-password window.
   - What's unclear: whether the phase's success criteria are satisfied by fixing `CreateAccountAsync` alone, or whether `SavePasswordAsync` must be widened too for full "saving an account" coverage and for the general HTTP-500 prohibition (AUTH-04's requirement text does not literally scope this to new accounts).
   - Recommendation: Widen both — the fix is identical in shape (`catch (Exception ex)` with no `when`), costs nothing extra, and closes an HTTP-500 gap on the existing-account path that current AUTH-04 phrasing arguably already covers. Flag this explicitly in the plan so the discuss/plan-review step can confirm scope before implementation, rather than leaving it a silent inclusion.
2. **Should `.planning/PROJECT.md:74`'s constraint wording be corrected in this phase, alongside `.planning/ROADMAP.md` and `.claude/rules/plugin.md`?**
   - What we know: CONTEXT.md's `roadmap_change_required` section explicitly names all three documents as needing the same wording correction (the "when both save and delete fail" caveat).
   - What's unclear: whether the plan should treat this as a Phase 1 documentation task or a separate, immediate pre-planning edit (CONTEXT.md says "make this change before planning").
   - Recommendation: Include the three-document correction as an explicit early task in the Phase 1 plan (not assumed already done), since this RESEARCH.md is being written after CONTEXT.md and the correction's completion status was not verified this session — `git grep` for the exact current wording of `.planning/ROADMAP.md:29` and `.claude/rules/plugin.md:33-36` before finalizing the plan's task list.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | `dotnet test`, `dotnet build` for all unit test work | ✓ | 10.0.401 `[VERIFIED: dotnet --version]` | — |
| Docker | `mise run e2e` (D-06's bats rewrites) | ✓ | daemon reachable, `docker info` succeeded `[VERIFIED: docker info]` | — |
| bats | e2e/script test execution | ✓ | 1.14.0 `[VERIFIED: bats --version]` | — |
| mise | pinned tool installation, all `mise run` tasks | ✓ | 2026.8.3 (a newer 2026.9.10 is available but not required) `[VERIFIED: mise --version]` | — |

**Missing dependencies with no fallback:** none.
**Missing dependencies with fallback:** none — every tool this phase needs is already installed and pinned.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3, package `xunit.v3` 4.0.1, Microsoft.Testing.Platform runner `[VERIFIED: tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj:10; .planning/codebase/TESTING.md:9-11]` |
| Config file | none dedicated — runner selected by `global.json:3`; test project itself is the config (`OutputType: Exe`) |
| Quick run command | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter "FullyQualifiedName~EmbyAuthenticationProviderTests"` |
| Full suite command | `mise run test` (dotnet unit tests + `tests/scripts` bats), plus `mise run e2e` for the D-06 bats rewrites |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| TEST-01 / AUTH-04 | `UpdateUserAsync` fails with a caught type (`DbUpdateException`) → login refused, no HTTP 500, account deleted | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter EmbyAuthenticationProviderTests` | ❌ Wave 0 |
| TEST-01 / AUTH-04 | `UpdateUserAsync` fails AND the cleanup `DeleteUserAsync` also fails → login still refused, no HTTP 500 | unit | same command | ❌ Wave 0 |
| TEST-01 / AUTH-04 | `UpdateUserAsync` fails with an exception type not in the old `when` filter (e.g. `InvalidOperationException`) → login refused, no HTTP 500 | unit | same command | ❌ Wave 0 |
| TEST-01 | Account checks (blank password, disabled, administrator), Emby login, account creation, account update — happy paths | unit | same command | ❌ Wave 0 |
| AUTH-03 | New account gets the hash + `EmbyAuthenticationProvider.ProviderId` in the `UpdateUserAsync` call directly after `CreateUserAsync` | unit | same command (assert `FakeUserManager.LastUpdated.Password`/`.AuthenticationProviderId`) | ❌ Wave 0 |
| AUTH-01 | Old Emby password refused at once after a change, new password accepted, saved hash follows | e2e | `bats e2e/30-migration-modes.bats` | ✅ (file exists, test rewritten per D-06) |
| AUTH-01 | User with a verified saved hash is refused during an Emby outage | e2e | `bats e2e/40-emby-outage.bats` | ✅ (file exists, test rewritten per D-06) |
| AUTH-02 | No `JellyfinPasswordFirst` / "Check the saved Jellyfin password first" anywhere in `src/`, `tests/`, `e2e/`, `docs/`, `README.md` | manual/scripted grep | `grep -rn "JellyfinPasswordFirst\|Check the saved Jellyfin password first" src tests e2e docs README.md` (excluding `obj/`/`bin/`) | n/a — a repo-wide search, not a unit test |

### Sampling Rate
- **Per task commit:** `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter EmbyAuthenticationProviderTests` (fast — the whole existing suite runs in under a second per `.planning/codebase/TESTING.md:30`, so filtering is a convenience, not a necessity)
- **Per wave merge:** `mise run test` (dotnet unit tests + script tests)
- **Phase gate:** `mise run e2e` full suite green, plus the AUTH-02 grep check, before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs` — new file, covers TEST-01/AUTH-03/AUTH-04
- [ ] `FakeUserManager` in `TestDoubles.cs` — new type, D-09
- [ ] A fake `ICryptoProvider` in `TestDoubles.cs` — new type, D-11
- [ ] The D-10 settings-source constructor seam in `EmbyAuthenticationProvider.cs` and its wiring in `PluginServiceRegistrator.cs` — must land before `EmbyAuthenticationProviderTests.cs` can construct the provider without a static `EmbyAuthPlugin.Instance`
- [ ] Framework install: none — `xunit.v3`, `Microsoft.Extensions.DependencyInjection`, and `Microsoft.EntityFrameworkCore` are all already resolvable (see Standard Stack)

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-------------------|
| V2 Authentication | Yes | Delegate all Emby-method password verification to Emby (`EmbyClient.AuthenticateAsync`) — the plugin never verifies a password itself; already the existing pattern, unchanged by this phase. |
| V3 Session Management | No | Session issuance is entirely Jellyfin core's responsibility; the plugin only returns `ProviderAuthenticationResult`. |
| V4 Access Control | Partial | The administrator-refusal check (`resolvedUser.HasPermission(PermissionKind.IsAdministrator)`) is in scope for TEST-01 coverage but not changed by this phase. |
| V5 Input Validation | No new surface | No new external input in this phase (no new HTTP endpoint, no new settings field). |
| V6 Cryptography | Yes — never hand-roll | `ICryptoProvider.CreatePasswordHash`/`Verify` (Jellyfin's own hasher) — the plugin never implements its own hashing; the phase's fake `ICryptoProvider` for tests must not be mistaken for a production alternative. |
| V7 Error Handling and Logging | Yes — this phase's core change | Fail-closed: every exception from `IUserManager` becomes `AuthenticationException`, never a raw exception; every Error-level log names the account but never a password or API key (D-04, `[CITED: CLAUDE.md "Never put a password or the API key in a log or exception message"]`). |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Blank-password bypass during the account-creation window (a concurrent request lands after `CreateUserAsync` commits a Default-method, no-password row, but before `UpdateUserAsync` sets the hash) | Spoofing / Elevation of Privilege | Cannot be closed to zero with the public v12.1 `IUserManager` API `[CITED: .planning/research/ARCHITECTURE.md:15-52, Change 1]` — minimize the window (already near-optimal) and document the residual race in `docs/how-it-works.md` (AUTH-03) rather than claim it is eliminated. Out of this phase's power to fully close; in scope only to document and to ensure failure paths never *widen* it (AUTH-04). |
| Information disclosure via exception message or log on a failed save/delete | Information Disclosure | D-04: Error log names the account only, never the password or hash; `AuthenticationException` messages use the constant `InvalidLogin` ("Invalid username or password"), never echoing the underlying exception's message to the client (only to the log, as the `ex` parameter of the `[LoggerMessage]` call). |
| Denial of service via HTTP 500 instead of a clean 401 on a transient Jellyfin datastore failure | Denial of Service (soft) | AUTH-04's entire purpose: convert every `IUserManager` exception to `AuthenticationException`, which Jellyfin turns into a normal 401, not a 500 that could indicate an unhandled-exception surface to a prober. |
| Dead validated-mode value (`JellyfinPasswordFirst`) left reachable via a stale settings file after removal | Tampering (indirect) | D-07: accepted as an out-of-band break (Jellyfin's own settings-deserialization-failure handling resets to defaults) rather than a compatibility shim, because a compatibility path would keep the very "accept a saved password without asking Emby" behavior AUTH-01/AUTH-02 exist to remove. |

## Sources

### Primary (HIGH confidence)
- `jellyfin/jellyfin` tag `v12.1`, `Jellyfin.Server.Implementations/Users/UserManager.cs` — fetched verbatim via `curl` and read directly with the Read tool this session; `CreateUserAsync` (lines 338-368), `UpdateUserAsync` (lines 213-260+), `DeleteUserAsync` (lines 370-416), `ThrowIfInvalidUsername` (lines 963-971), `AuthenticateWithProvider` (lines 1064-1090) all quoted verbatim above.
- Reflection dump against the pinned `Jellyfin.Controller`/`Jellyfin.Model` 12.1.0 NuGet packages (`~/.nuget/packages/jellyfin.controller/12.1.0/lib/net10.0/MediaBrowser.Controller.dll`, `~/.nuget/packages/jellyfin.model/12.1.0/lib/net10.0/MediaBrowser.Model.dll`) — exact `IUserManager` and `ICryptoProvider` member lists.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/obj/project.assets.json` — confirms `Microsoft.Extensions.DependencyInjection` 10.0.11 and `Microsoft.EntityFrameworkCore` 10.0.11 already resolve transitively in the test project, no new package needed.
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs`, `TestDoubles.cs`, `EmbyUserDirectoryTests.cs`, `EmbyAuthSettingsTests.cs`, `PluginConfiguration.cs`, `EmbyAuthSettings.cs`, `LoginDecision.cs`, `AccountAccessPolicy.cs`, `PluginServiceRegistrator.cs`, `DefaultLoginMethod.cs`, `EmbyUserDirectory.cs`, `EmbyVerifiedPasswords.cs` — all read directly this session.
- `.claude/rules/plugin.md`, `.claude/rules/e2e.md`, `docs/how-it-works.md`, `docs/settings.md`, `e2e/30-migration-modes.bats`, `e2e/40-emby-outage.bats` — all read directly this session.
- `.planning/codebase/TESTING.md`, `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/CONCERNS.md`, `.planning/research/ARCHITECTURE.md`, `.planning/research/STACK.md`, `.planning/research/PITFALLS.md`, `.planning/PROJECT.md`, `.planning/REQUIREMENTS.md`, `.planning/STATE.md` — all read directly this session.

### Secondary (MEDIUM confidence)
- `[jellyfin/jellyfin#16353]` and the `IEntityFrameworkCoreLockingBehavior`/`ExecuteUpdateAsync`-on-InMemory findings from `.planning/research/PITFALLS.md` — relevant background on Jellyfin's own concurrency issues, but the specific `ExecuteUpdateAsync`/EF-InMemory pitfall does not apply to this phase's scope (no `JellyfinDbContext` code touched here; that is Phase 3's `TEST-02`).

### Tertiary (LOW confidence)
- None used as the basis for any claim in this document.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages, every transitive dependency confirmed present via the test project's own lock file.
- Architecture (exception surface, catch-clause fix): HIGH — verified against the actual Jellyfin 12.1 tagged source, quoted verbatim.
- Pitfalls: HIGH — each pitfall traces to a specific, quoted line range in either the pinned package, the Jellyfin source, or this repo's own files.

**Research date:** 2026-09-17
**Valid until:** Effectively pinned to Jellyfin 12.1.0 / the current commit — re-verify only if the Jellyfin or `Jellyfin.Controller`/`Jellyfin.Model` package versions change (per `CLAUDE.md`'s version-bump rule, that would also change the e2e image tag and target framework, well outside this phase's scope).
