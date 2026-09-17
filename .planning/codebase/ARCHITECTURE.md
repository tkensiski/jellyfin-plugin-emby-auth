<!-- refreshed: 2026-09-16 -->
# Architecture

**Analysis Date:** 2026-09-16

**Scope:** This document describes commit `ecee1ed` on `main`.

## System Overview

Jellyfin calls the plugin through four separate entry points: the login method, the event consumer, the migration task, and the migration API. They share `EmbyVerifiedPasswords` and `DefaultLoginMethod`, and only `EmbyClient` contacts Emby.

```text
Jellyfin UserManager            Jellyfin SessionManager          Scheduled task                   Migration API (admin only)
(login, password change)        (event after a login)            (settings page, Dashboard, API)  (settings page)
        │                               │                               │                               │
        ▼                               ▼                               ▼                               ▼
EmbyAuthenticationProvider      MoveToDefaultLoginMethod         MoveEmbyUsersToDefaultTask       EmbyAuthController
 ├─ EmbyAuthSettings.TryCreate   ├─ EmbyVerifiedPasswords.Matches ├─ EmbyLoginMethodUsers.ListAsync ├─ GET  Migration
 ├─ EmbyVerifiedPasswords.Matches└─ DefaultLoginMethod.MoveAsync  │   └─ EmbyVerifiedPasswords     │   └─ EmbyLoginMethodUsers.ListAsync
 │    (JellyfinPasswordFirst only)                                │       .Matches                  └─ POST Migration/Run
 ├─ EmbyUserDirectory.GetStatusAsync ─► EmbyClient.GetUsersAsync  └─ DefaultLoginMethod.MoveAsync       └─ ITaskManager.QueueIfNotRunning
 │                                      ─► Emby GET /Users                                                  <MoveEmbyUsersToDefaultTask>
 ├─ EmbyClient.AuthenticateAsync ─► Emby POST /Users/AuthenticateByName, POST /Sessions/Logout
 ├─ LoginDecision.Decide
 ├─ AccountAccessPolicy
 ├─ IUserManager (CreateUserAsync, UpdateUserAsync, DeleteUserAsync)
 └─ EmbyVerifiedPasswords.Record ─► Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json
```

## Component Responsibilities

| Component | Visibility | Responsibility | File |
|-----------|------------|----------------|------|
| EmbyAuthenticationProvider | internal | The Emby login method. Refuses blank passwords, disabled accounts, and administrators, checks the password against Emby, creates or updates the account, and records the verified hash. Handles password changes in Jellyfin. | `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` |
| EmbyClient | internal | The only code that sends requests to Emby: login, sign-out, and user list. Sets a 5-second timeout on each client. | `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` |
| EmbyUserDirectory | internal | Keeps the Emby user list for 60 seconds (30 seconds after a failed read), so that the plugin sends a password to Emby only for an enabled Emby user with exactly that name, ignoring case. | `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs` |
| LoginDecision | internal | Pure function that returns `Deny`, `UseAccount`, or `CreateAccount` after Emby accepts a login. | `src/Jellyfin.Plugin.EmbyAuth/LoginDecision.cs` |
| EmbyVerifiedPasswords | public | Stores a SHA-256 fingerprint of each password hash that Emby verified, in a JSON file keyed by Jellyfin user ID. Public because `MoveEmbyUsersToDefaultTask` takes it in its constructor (`.claude/rules/plugin.md:22`). | `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` |
| AccountAccessPolicy | internal | Applies the `AccountAccess` setting: remote access and library access for new accounts, and removal of remote access for existing accounts. | `src/Jellyfin.Plugin.EmbyAuth/AccountAccessPolicy.cs` |
| DefaultLoginMethod | internal | Holds the Default login method ID and moves one user to it with a single-column `ExecuteUpdateAsync`. | `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs` |
| MoveToDefaultLoginMethod | internal | `IEventConsumer<AuthenticationResultEventArgs>`. In `MoveAfterFirstLogin` mode, moves the user to Default after a login if Emby verified the saved hash. | `src/Jellyfin.Plugin.EmbyAuth/MoveToDefaultLoginMethod.cs` |
| EmbyLoginMethodUsers | internal | Lists the users on the Emby login method, sorted by name, with ID, name, saved hash, and whether each user is ready to move. The migration task and the migration API share this list. | `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs` |
| MoveEmbyUsersToDefaultTask | public | `IScheduledTask` with no default trigger. Moves every ready user from the shared list. Runs with any migration behavior. | `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs` |
| EmbyAuthController | public | Admin-only migration API: `GET /EmbyAuth/Migration` returns the migration status, and `POST /EmbyAuth/Migration/Run` queues the migration task. Also holds the public `MigrationStatus` and `MigrationUser` records. | `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` |
| EmbyAuthSettings | internal | Validates `PluginConfiguration` into a typed record: server URL, API key, and the two enums. | `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs` |
| PluginConfiguration | public | Settings model, and the `MigrationMode` and `AccountAccess` enums. | `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` |
| EmbyAuthPlugin | public | `BasePlugin<PluginConfiguration>`: plugin ID, name, the static `Instance`, and the embedded settings page. | `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs` |
| PluginServiceRegistrator | public | Registers the services with Jellyfin's DI container. The scheduled task and the controller are not registered here; Jellyfin reaches both without it (see DI lifetimes). | `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` |

## Pattern Overview

**Overall:** A Jellyfin plugin that adds a login method named **Emby**. The login method checks passwords against Emby, saves a Jellyfin hash of each accepted password, and records a fingerprint of that hash. The user then moves to Jellyfin's Default login method, either after the login (event consumer) or when an administrator runs the migration, from the settings page or as a scheduled task.

**Key Characteristics:**
- Four entry points: the login method (`IAuthenticationProvider`, `IRequiresResolvedUser`), the event consumer, the scheduled task, and the migration API controller.
- All Emby traffic goes through `EmbyClient` (`EmbyClient.cs:31`). The migration task and the migration API never contact Emby (`docs/migration.md:25`).
- The user list check runs before the password goes to Emby (`EmbyAuthenticationProvider.cs:88-93`).
- Every path that moves a user to Default, or accepts a saved password without Emby, checks `EmbyVerifiedPasswords.Matches` (`EmbyAuthenticationProvider.cs:82`, `MoveToDefaultLoginMethod.cs:48`, and `EmbyLoginMethodUsers.cs:51` for the task).
- Invalid settings refuse every login on the Emby login method (`EmbyAuthenticationProvider.cs:144-153`).
- Log messages never contain a password or the API key (`EmbyClient.cs:34`, `docs/how-it-works.md:28`), checked by `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs:40-41` and `e2e/90-jellyfin-log.bats`.

## Layers

**Login method:**
- Purpose: Handle logins and password changes for users on the Emby login method, and logins for names with no Jellyfin account
- Location: `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs`
- Contains: `EmbyAuthenticationProvider`, which implements `IAuthenticationProvider` and `IRequiresResolvedUser`. The two-argument `Authenticate` throws `NotSupportedException` (line 56-57), because Jellyfin calls the overload with the resolved user.
- Depends on: `IServiceProvider` (resolves `IUserManager` per login), `ICryptoProvider`, `EmbyClient`, `EmbyUserDirectory`, `EmbyVerifiedPasswords`, `EmbyAuthSettings`, `EmbyAuthPlugin.Instance`, `LoginDecision`, `AccountAccessPolicy`, `DefaultLoginMethod.ProviderId`
- Used by: Jellyfin's `UserManager` (`.claude/rules/plugin.md:13`)

**Decision logic:**
- Purpose: Decide which account an Emby login applies to
- Location: `src/Jellyfin.Plugin.EmbyAuth/LoginDecision.cs`
- Contains: `LoginDecision.Decide` (line 40), the `JellyfinAccount` record, and the `LoginAction` enum
- Depends on: Nothing
- Used by: `EmbyAuthenticationProvider.Authenticate` (line 98)

**Settings:**
- Purpose: Hold and validate the plugin settings
- Location: `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs`, `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs`
- Contains: `PluginConfiguration`, `EmbyAuthSettings.TryCreate` (line 23) and `FindProblem` (line 36)
- Depends on: `MediaBrowser.Model.Plugins.BasePluginConfiguration`
- Used by: `EmbyAuthenticationProvider.GetSettings` (validated settings), `EmbyUserDirectory.GetStatusAsync` (takes `EmbyAuthSettings`), and `MoveToDefaultLoginMethod.OnEvent`, which reads `EmbyAuthPlugin.Instance?.Configuration.MigrationMode` directly (line 34)

**Emby integration:**
- Purpose: Talk to Emby and keep a short-lived copy of the user list
- Location: `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs`, `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs`
- Contains: `EmbyClient.AuthenticateAsync` (line 50), `GetUsersAsync` (line 105), `SignOutAsync` (line 165); `EmbyUserDirectory.GetStatusAsync` (line 55) with `CacheDuration` 60 s (line 39) and `RetryDelay` 30 s (line 44)
- Depends on: `IHttpClientFactory` (`NamedClient.Default`), `TimeProvider`, `ILogger<T>`
- Used by: `EmbyAuthenticationProvider.Authenticate`

**Verified passwords:**
- Purpose: Record which saved hashes came from a password that Emby accepted
- Location: `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs`
- Contains: `Record` (line 41), `Matches` (line 73), lazy `Load` (line 90). The file is `Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json` in Jellyfin's plugin configuration folder (`PluginServiceRegistrator.cs:23, 31-33`).
- Depends on: File system, `ILogger<EmbyVerifiedPasswords>`
- Used by: `EmbyAuthenticationProvider`, `MoveToDefaultLoginMethod`, `EmbyLoginMethodUsers`; injected into `MoveEmbyUsersToDefaultTask` and `EmbyAuthController`, which pass it to `EmbyLoginMethodUsers.ListAsync`

**Account access:**
- Purpose: Apply the `AccountAccess` setting to a Jellyfin `User`
- Location: `src/Jellyfin.Plugin.EmbyAuth/AccountAccessPolicy.cs`
- Contains: `ApplyToNewAccount` (line 20), `ApplyToExistingAccount` (line 42)
- Depends on: Jellyfin's `User` entity and `PermissionKind`/`PreferenceKind`
- Used by: `EmbyAuthenticationProvider.CreateAccountAsync` (line 183) and `SavePasswordAsync` (line 212)

**Migration:**
- Purpose: Move users from the Emby login method to Default, and report who is ready
- Location: `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs`, `src/Jellyfin.Plugin.EmbyAuth/MoveToDefaultLoginMethod.cs`, `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs`, `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs`
- Contains:
  - `DefaultLoginMethod.MoveAsync` (line 31) — one `ExecuteUpdateAsync` that changes `AuthenticationProviderId` only while the user is on the Emby login method and still has the given hash
  - `MoveToDefaultLoginMethod.OnEvent` (line 31) — runs after each login in `MoveAfterFirstLogin` mode
  - `EmbyLoginMethodUsers.ListAsync` (line 32) — the users on the Emby login method, sorted by `Username` (line 41); a user is ready when the saved hash is not null and `Matches` accepts it (line 51)
  - `MoveEmbyUsersToDefaultTask.ExecuteAsync` (line 57) — runs when the task is started
- Depends on: `IDbContextFactory<JellyfinDbContext>`, `EmbyVerifiedPasswords`, `ILogger<T>`
- Used by: Jellyfin's event system (the consumer is registered in `PluginServiceRegistrator.cs:35`), Jellyfin's scheduled tasks (the task is discovered by type, see Architectural Constraints), and the migration API

**Migration API:**
- Purpose: Let an administrator see the migration status and start the migration from the settings page
- Location: `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` (namespace `Jellyfin.Plugin.EmbyAuth.Api`)
- Contains: `EmbyAuthController` (line 26), a `ControllerBase` with `[ApiController]`, `[Authorize(Policy = Policies.RequiresElevation)]`, `[Route("EmbyAuth")]`, and `[Produces]` JSON (lines 22-25); the `MigrationStatus` (line 66) and `MigrationUser` (line 76) records
- Depends on: `IDbContextFactory<JellyfinDbContext>`, `EmbyVerifiedPasswords`, `ITaskManager` (constructor, lines 26-29), `EmbyLoginMethodUsers`
- Used by: The settings page (`Configuration/configPage.html:63-105`)

## Data Flow

### Primary Request Path (Login)

1. **Jellyfin calls the login method.** For a known user name, Jellyfin calls only the login method assigned to the user. For an unknown name, it calls every enabled login method (`.claude/rules/plugin.md:13`).
2. **Account checks** (`EmbyAuthenticationProvider.cs:62-77`): refuse a blank password, a disabled account, or an administrator account. Emby is not contacted.
3. **Settings** (`EmbyAuthenticationProvider.cs:79`, `144-153`): validate the settings. If they are not valid, log at Error and refuse.
4. **Saved password, `JellyfinPasswordFirst` only** (`EmbyAuthenticationProvider.cs:80-86`): if the account has a saved hash, `EmbyVerifiedPasswords.Matches` accepts that hash, and the typed password verifies against it, accept the login without Emby.
5. **User list** (`EmbyAuthenticationProvider.cs:88-93`, `EmbyUserDirectory.cs:55-83`): read the cached list, or fetch it with `EmbyClient.GetUsersAsync` if there is no snapshot, the server URL or API key changed, or the snapshot expired. Unless the status is `Active`, log at Information and refuse without sending the password.
6. **Emby login** (`EmbyAuthenticationProvider.cs:95-96`, `EmbyClient.cs:50-96`): `POST /Users/AuthenticateByName` with a buffered body. On success with a user name, end the Emby session with `POST /Sessions/Logout` if Emby returned an access token, then return `EmbyLogin`. A non-success status, no response within 5 seconds, an unreadable response, or a response without a user name returns `null`, and the login is refused.
7. **Decision** (`EmbyAuthenticationProvider.cs:98-103`, `LoginDecision.cs:40-55`): `Deny` if the typed name and the Emby name differ (ignoring case), or if the existing account uses another login method. `CreateAccount` if no account exists. Otherwise `UseAccount`.
8. **Save** (`EmbyAuthenticationProvider.cs:105-109`): hash the password and resolve `IUserManager`.
   - New account (`CreateAccountAsync`, line 168): `CreateUserAsync`, set the Emby login method and the hash, apply `AccountAccessPolicy.ApplyToNewAccount`, then `UpdateUserAsync`. If that save does not complete, the `finally` block deletes the account (line 197-203).
   - Existing account (`SavePasswordAsync`, line 209): set the hash, apply `AccountAccessPolicy.ApplyToExistingAccount`, then `UpdateUserAsync`.
9. **Record** (`EmbyAuthenticationProvider.cs:111`, `EmbyVerifiedPasswords.cs:41-65`): store the fingerprint under a lock. If the fingerprint is new, write a `.tmp` file and move it over the JSON file.
10. **Return** `ProviderAuthenticationResult` to Jellyfin. Jellyfin sets the user's login method to the method that accepted the login (`.claude/rules/plugin.md:18`).
11. **Event** (`MoveToDefaultLoginMethod.cs:31-58`): `SessionManager` publishes `AuthenticationResultEventArgs` after the login completes. In `MoveAfterFirstLogin` mode, the consumer loads the user if it is on the Emby login method, checks `EmbyVerifiedPasswords.Matches`, and calls `DefaultLoginMethod.MoveAsync`. A Quick Connect login publishes the same event, and the `Matches` check stops a move without a verified password (`.claude/rules/plugin.md:19`).

### Password Change Path

1. `UserManager.ChangePassword` calls the assigned login method, then saves the user (`.claude/rules/plugin.md:23`).
2. `EmbyAuthenticationProvider.ChangePassword` (`EmbyAuthenticationProvider.cs:125-139`):
   - New password: set the hash and set `AuthenticationProviderId` to `DefaultLoginMethod.ProviderId`. The user moves to Default at once.
   - Empty password (reset): set `Password` to `null`. The user stays on the Emby login method, so Emby checks the next login.

### Migration Status Path (settings page)

1. On `pageshow`, the settings page loads the plugin configuration, then calls `loadEmbyAuthMigration` (`configPage.html:82-94`).
2. `loadEmbyAuthMigration` sends `GET EmbyAuth/Migration` with `ApiClient.getJSON` (`configPage.html:66`).
3. `EmbyAuthController.GetMigrationStatus` (`EmbyAuthController.cs:37-47`) creates a database context, calls `EmbyLoginMethodUsers.ListAsync`, and returns `MigrationStatus` with one `MigrationUser(Name, ReadyToMove)` per user. The response has no user IDs or hashes (line 45).
4. The page shows the count of users and of ready users, and one list item per user, built with `textContent` (`configPage.html:67-76`, `.claude/rules/plugin.md:50`). If the request fails, it shows an error line (`configPage.html:77-79`).

### Migration Task Path

1. The task starts in one of three ways (`MoveEmbyUsersToDefaultTask.cs:17`):
   - **Run migration now** on the settings page sends `POST EmbyAuth/Migration/Run` (`configPage.html:96-105`). `EmbyAuthController.RunMigration` calls `ITaskManager.QueueIfNotRunning<MoveEmbyUsersToDefaultTask>()` and returns 204 (`EmbyAuthController.cs:53-59`). The page reloads the status after 3 seconds (`configPage.html:100-101`).
   - Dashboard > Advanced > Scheduled Tasks (`.claude/rules/plugin.md:51`).
   - `POST /ScheduledTasks/Running/{id}` (`.claude/rules/e2e.md:32`).
2. The key is `EmbyAuthMoveUsersToDefault` (`MoveEmbyUsersToDefaultTask.cs:45`). `GetDefaultTriggers` returns no triggers (line 54).
3. `ExecuteAsync` (line 57) gets the users from `EmbyLoginMethodUsers.ListAsync` (line 63).
4. For each user with `ReadyToMove`, it calls `DefaultLoginMethod.MoveAsync` with the saved hash (lines 69-70). It logs each user that stays (line 76), reports progress (line 79), and logs a summary (line 82).

**State Management:**
- **Jellyfin database** — accounts, `AuthenticationProviderId`, and password hashes. Written through `IUserManager` and `DefaultLoginMethod.MoveAsync`; read by `MoveToDefaultLoginMethod` and `EmbyLoginMethodUsers`.
- **Verified passwords file** — JSON map of Jellyfin user ID to the SHA-256 fingerprint of the hash. Loaded once on first use and kept in memory (`EmbyVerifiedPasswords.cs:90-113`).
- **Emby user list** — in-memory `Snapshot` record (`EmbyUserDirectory.cs:88`) with server URL, API key, users, and expiry time.
- **Settings** — `PluginConfiguration`, saved by Jellyfin with `XmlSerializer` (`PluginConfiguration.cs:57`).

## Key Abstractions

**EmbyLogin** (`EmbyClient.cs:21`):
- Purpose: A login that Emby accepted
- Fields: `Name` (the Emby user name), `EnableRemoteAccess`
- Produced by: `EmbyClient.AuthenticateAsync`

**EmbyUser** (`EmbyClient.cs:28`):
- Purpose: An entry in the Emby user list
- Fields: `Name`, `IsDisabled`
- Produced by: `EmbyClient.GetUsersAsync`, kept in `EmbyUserDirectory`

**JellyfinAccount** (`LoginDecision.cs:10`):
- Purpose: The account data that `LoginDecision.Decide` needs
- Fields: `Username`, `AuthenticationProviderId`

**LoginAction** (`LoginDecision.cs:15`):
- `Deny`, `UseAccount`, `CreateAccount`
- Returned by: `LoginDecision.Decide`

**EmbyUserStatus** (`EmbyUserDirectory.cs:13`):
- `Active` — an enabled Emby user has exactly this name, ignoring case
- `Disabled` — the Emby user with this name is disabled
- `NotFound` — no Emby user has this name
- `Unavailable` — the plugin cannot read the Emby user list
- Returned by: `EmbyUserDirectory.GetStatusAsync`

**EmbyLoginMethodUser** (`EmbyLoginMethodUsers.cs:18`, internal):
- Purpose: A user on the Emby login method, as the migration sees the user
- Fields: `Id`, `Username`, `PasswordHash` (nullable), `ReadyToMove`
- Produced by: `EmbyLoginMethodUsers.ListAsync`

**MigrationStatus** and **MigrationUser** (`EmbyAuthController.cs:66`, `EmbyAuthController.cs:76`, public):
- Purpose: The response of `GET /EmbyAuth/Migration`
- Fields: `MigrationStatus.Users`; `MigrationUser.Name`, `MigrationUser.ReadyToMove`. Jellyfin serializes them with PascalCase names (`.claude/rules/plugin.md:49`).

**MigrationMode** (`PluginConfiguration.cs:9`):
- `MoveAfterFirstLogin` (default) — Emby checks the first login, then the event consumer moves the user to Default
- `KeepEmbyInCharge` — Emby checks every login until the migration task runs
- `JellyfinPasswordFirst` — Jellyfin checks the saved password first, Emby only if it does not match, until the migration task runs

**AccountAccess** (`PluginConfiguration.cs:31`):
- `CopyEmbyRemoteAccess` (default) — new accounts get Jellyfin's default permissions and copy Emby's remote access setting; existing accounts lose remote access when Emby does not allow it
- `JellyfinDefaults` — new accounts get Jellyfin's default permissions; Emby's remote access setting is ignored
- `NoLibraries` — like `CopyEmbyRemoteAccess`, but new accounts get no library access until an administrator grants it

## Entry Points

**EmbyAuthenticationProvider.Authenticate(string, string, User?):**
- Location: `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:60`
- Triggers: Jellyfin's `UserManager`, for a user on the Emby login method or a name with no Jellyfin account
- Responsibilities: Account checks, settings, saved-password check, user list, Emby login, decision, save, fingerprint

**EmbyAuthenticationProvider.ChangePassword:**
- Location: `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:125`
- Triggers: `UserManager.ChangePassword` for a user on the Emby login method
- Responsibilities: A new password moves the user to Default; a reset keeps the user on the Emby login method

**MoveToDefaultLoginMethod.OnEvent:**
- Location: `src/Jellyfin.Plugin.EmbyAuth/MoveToDefaultLoginMethod.cs:31`
- Triggers: `AuthenticationResultEventArgs`, which `SessionManager` publishes after a login, including a Quick Connect login
- Responsibilities: In `MoveAfterFirstLogin` mode, move the user to Default if Emby verified the saved hash

**MoveEmbyUsersToDefaultTask.ExecuteAsync:**
- Location: `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs:57`
- Triggers: **Run migration now** (through the migration API), Dashboard > Advanced > Scheduled Tasks, or `POST /ScheduledTasks/Running/{id}`
- Responsibilities: Move every ready user on the Emby login method

**EmbyAuthController.GetMigrationStatus (`GET /EmbyAuth/Migration`):**
- Location: `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs:37-39`
- Triggers: The settings page, or an administrator's API call
- Responsibilities: Return each user on the Emby login method with `ReadyToMove`

**EmbyAuthController.RunMigration (`POST /EmbyAuth/Migration/Run`):**
- Location: `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs:53-55`
- Triggers: **Run migration now** on the settings page, or an administrator's API call
- Responsibilities: Queue the migration task unless it already runs; return 204

**EmbyAuthPlugin.GetPages:**
- Location: `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs:43`
- Triggers: Jellyfin's dashboard
- Responsibilities: Serve the embedded `Configuration/configPage.html`

## Architectural Constraints

- **Threading:** Jellyfin calls login methods inside a lock, and all logins for unknown names share the lock key `Guid.Empty` (`.claude/rules/plugin.md:14`). `EmbyUserDirectory` replaces an immutable `Snapshot` through a `volatile` field (`EmbyUserDirectory.cs:46, 58-64`), so a reader always sees a whole snapshot. Two logins that miss the cache at the same time can each request the user list; the last write wins. `EmbyVerifiedPasswords` guards the dictionary and the file with one `Lock` (`EmbyVerifiedPasswords.cs:22, 45, 81`).
- **Global state:** `EmbyAuthPlugin.Instance` is a static, nullable property set in the plugin constructor (`EmbyAuthPlugin.cs:25, 31`). Callers use `?.` (`EmbyAuthenticationProvider.cs:146`, `MoveToDefaultLoginMethod.cs:34`).
- **DI lifetimes:** `PluginServiceRegistrator.RegisterServices` (`PluginServiceRegistrator.cs:26-36`) adds `TimeProvider.System` with `TryAddSingleton`, adds `EmbyClient`, `EmbyUserDirectory`, `EmbyVerifiedPasswords`, and the `IAuthenticationProvider` as singletons, and adds `MoveToDefaultLoginMethod` as a scoped `IEventConsumer<AuthenticationResultEventArgs>`. `rg` finds no registration of `EmbyAuthController` or `MoveEmbyUsersToDefaultTask` in `src/`. Jellyfin still reaches both: the task is discovered by type (see Type visibility), and the e2e tests call both controller routes and get 403, 204, and the status list (`e2e/30-migration-modes.bats:95-120`). No file in this repo states how Jellyfin discovers a plugin controller.
- **IUserManager resolution:** `IUserManager` depends on all login methods, so the provider resolves it from `IServiceProvider` at login time, never in a constructor (`EmbyAuthenticationProvider.cs:21, 106`, `.claude/rules/plugin.md:16`).
- **Type visibility:** Jellyfin discovers scheduled tasks with `Assembly.GetExportedTypes()`, so `MoveEmbyUsersToDefaultTask` and every type in its constructor are public. Other plugin types stay internal (`.claude/rules/plugin.md:22`). `EmbyAuthController`, `MigrationStatus`, and `MigrationUser` are also public (`EmbyAuthController.cs:26, 66, 76`); `EmbyLoginMethodUsers` and `EmbyLoginMethodUser` are internal (`EmbyLoginMethodUsers.cs:18, 23`). Tests reach internal types through `InternalsVisibleTo` (`Jellyfin.Plugin.EmbyAuth.csproj:19`).
- **API authorization:** Every plugin API controller uses `[Authorize(Policy = Policies.RequiresElevation)]`, so only administrators can call it (`.claude/rules/plugin.md:48`, `EmbyAuthController.cs:23`). `e2e/30-migration-modes.bats:95-110` checks that a regular user gets 403 on both routes.
- **Mutual reference:** `EmbyAuthenticationProvider.ChangePassword` uses `DefaultLoginMethod.ProviderId`, and `DefaultLoginMethod.MoveAsync` and `EmbyLoginMethodUsers.ListAsync` use `EmbyAuthenticationProvider.ProviderId` (`EmbyAuthenticationProvider.cs:136`, `DefaultLoginMethod.cs:36`, `EmbyLoginMethodUsers.cs:40`).
- **Database writes:** `DefaultLoginMethod.MoveAsync` changes one column in one statement, so it does not overwrite concurrent changes to the user (`DefaultLoginMethod.cs:23-25, 34-38`).
- **Exceptions:** Jellyfin catches only `AuthenticationException` from a login method; any other exception becomes an HTTP 500 (`.claude/rules/plugin.md:15`). `EventManager` logs and ignores an exception from an event consumer, so a failed move does not fail the login (`.claude/rules/plugin.md:20`).
- **Event ordering:** After a login, Jellyfin sets the user's login method to the method that accepted the login. The provider therefore cannot move the user to Default itself; `MoveToDefaultLoginMethod` does it after the login completes (`MoveToDefaultLoginMethod.cs:17-18`, `.claude/rules/plugin.md:18`).

## Anti-Patterns

These rules come from `.claude/rules/plugin.md` and `CLAUDE.md`.

### Full account save to move a user

**What happens:** A move to Default saves the whole user with `UpdateUserAsync`.
**Why it's wrong:** It can overwrite a concurrent change, for example an administrator disabling the user (`.claude/rules/plugin.md:21`).
**Do this instead:** Use `DefaultLoginMethod.MoveAsync` (`src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs:31`).

### Move or accept a saved password without the fingerprint check

**What happens:** A code path moves a user to Default, or accepts a saved hash without Emby, and skips `EmbyVerifiedPasswords.Matches`.
**Why it's wrong:** A password that an administrator set on a pre-created account would then work (`.claude/rules/plugin.md:28`).
**Do this instead:** Call `EmbyVerifiedPasswords.Matches` first, as `EmbyAuthenticationProvider.cs:82` and `MoveToDefaultLoginMethod.cs:48` do, or use `ReadyToMove` from `EmbyLoginMethodUsers.ListAsync` (`EmbyLoginMethodUsers.cs:51`), as `MoveEmbyUsersToDefaultTask.cs:69` does.

### Other exceptions from the login method

**What happens:** An expected failure escapes as an exception other than `AuthenticationException`.
**Why it's wrong:** Jellyfin returns HTTP 500 for the login request.
**Do this instead:** Catch the expected exception and throw `AuthenticationException`, as in `EmbyAuthenticationProvider.cs:175-179` and `192-196`.

### A Default account without a password

**What happens:** A user is left on, or moved to, the Default login method with no password.
**Why it's wrong:** The Default login method accepts a blank password for an account without a password (`.claude/rules/plugin.md:33`).
**Do this instead:** Save the hash immediately after `CreateUserAsync` and delete the account if the save fails (`EmbyAuthenticationProvider.cs:181-203`). On a password reset, keep the user on the Emby login method (`EmbyAuthenticationProvider.cs:128-133`).

### A password or the API key in a log or exception message

**What happens:** A log template or exception message includes a secret, or a settings message repeats a configured value.
**Why it's wrong:** The server URL can contain credentials (`.claude/rules/plugin.md:56`), and `CLAUDE.md:46` forbids a password or the API key in a log or exception message.
**Do this instead:** Use `[LoggerMessage]` templates with names and status codes only (`EmbyClient.cs:188-207`). Settings problems describe the field, not the value (`EmbyAuthSettings.cs:21, 36-76`).

### User names in page HTML

**What happens:** The settings page inserts a user name with `innerHTML`.
**Why it's wrong:** `.claude/rules/plugin.md:50` requires `textContent` for page content built from user names.
**Do this instead:** Create the element and set `textContent`, as `configPage.html:73-75` does.

## Error Handling

**Strategy:** Fail closed. Every refused login throws `AuthenticationException("Invalid username or password")` (`EmbyAuthenticationProvider.cs:36`), except invalid settings, which throw `AuthenticationException` with the settings problem (line 152). Emby failures return `null` from `EmbyClient`, and the provider refuses the login.

**Patterns:**
- Invalid settings: Error log, login refused (`EmbyAuthenticationProvider.cs:144-153`).
- Emby refuses the login: Information log (`EmbyClient.cs:68-72`).
- Emby unreachable or timed out (`HttpRequestException`, or `TaskCanceledException` not caused by the caller's token): Warning log (`EmbyClient.cs:76-80, 148-149`).
- Unreadable Emby response (`JsonException`, `InvalidOperationException`) or no user name: Warning log (`EmbyClient.cs:81-91, 152-153`).
- User list refused or unreadable: Warning log; `EmbyUserDirectory` caches the failure for 30 seconds and returns `Unavailable` (`EmbyUserDirectory.cs:62-68`).
- Sign-out failure: Warning log; the login still succeeds (`EmbyClient.cs:177-185`).
- Saved hash cannot be parsed (`FormatException`, `NotSupportedException`): Warning log; the provider asks Emby (`EmbyAuthenticationProvider.cs:155-166`).
- Jellyfin rejects the user name (`ArgumentException` from `CreateUserAsync`): Error log, login refused; no account exists (`EmbyAuthenticationProvider.cs:175-179`).
- Account save fails (`DbUpdateException`, `ResourceNotFoundException`): Error log, login refused; a new account is deleted (`EmbyAuthenticationProvider.cs:192-203, 218-222`).
- Fingerprint file read fails (`JsonException`, `IOException`, `UnauthorizedAccessException`): Error log; the in-memory map stays empty, and the next record replaces the file (`EmbyVerifiedPasswords.cs:103-110, 115`).
- Fingerprint file write fails (`IOException`, `UnauthorizedAccessException`): Error log (`EmbyVerifiedPasswords.cs:54-63`).
- Migration API: the controller has no exception handling of its own (`EmbyAuthController.cs:37-59`). The settings page shows "Jellyfin cannot read the migration status" or "Jellyfin did not start the migration" when a request fails (`configPage.html:77-79, 102-104`).

## Cross-Cutting Concerns

**Logging:** Every class that logs declares `private static partial void Log...` methods with `[LoggerMessage]` near the end of the class. Levels: Information for normal events and ordinary refusals, Warning for Emby problems, an administrator on the Emby login method, and name conflicts, Error for settings, account creation or save failures, and fingerprint file failures. `EmbyAuthController` and `EmbyLoginMethodUsers` do not log.

**Validation:** `EmbyAuthSettings.FindProblem` (`EmbyAuthSettings.cs:36-76`) checks that the URL is set, is an absolute http or https URL, and has no user info; that the API key is set; and that both enums are defined. The user name must match an Emby user name exactly, ignoring case, before the password goes to Emby (`EmbyUserDirectory.cs:76`), and Emby's returned name must match the typed name, ignoring case (`LoginDecision.cs:42`).

**Authentication and authorization:** Emby checks the password for a user on the Emby login method, except in `JellyfinPasswordFirst` mode when the saved hash is verified and matches. The plugin never authenticates an administrator through Emby (`EmbyAuthenticationProvider.cs:73-77`). A password set in Jellyfin moves the user to Default; a password reset keeps the user on the Emby login method. The migration API requires an administrator (`EmbyAuthController.cs:23`).

---

*Architecture analysis: 2026-09-16*
