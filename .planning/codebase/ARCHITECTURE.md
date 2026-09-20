---
last_mapped_commit: fb0fd999638d88fabb37bd9d449c23226a5b2f8b
---

<!-- refreshed: 2026-09-20 -->
# Architecture

**Analysis Date:** 2026-09-20

**Scope:** Full repository at commit fb0fd99 (branch: gsd/phase-04-emby-traffic-under-load-and-failure)

## System Overview

A Jellyfin 12.1 plugin that authenticates users against an Emby server on first login, saves the password in Jellyfin, and optionally moves the user off the Emby login method.

```text
┌──────────────────────────────────────────────────────────────────────────┐
│                          Jellyfin Entry Points                           │
├──────────────────────┬─────────────────────────┬──────────────────────────┤
│   Login Method       │   Event Consumer        │  Scheduled Task          │
│ IAuthenticationProvider  IEventConsumer      IScheduledTask             │
│ (EmbyAuthenticationProvider) (MoveAfterLogin) (EmbyMigrationTask)       │
│ `src/.../EmbyAuth... `  `src/.../MoveAfter... `  `src/.../EmbyMigrat...  `│
└──────────────┬───────┴────────────┬────────────┴──────────────┬───────────┘
               │                    │                          │
               ▼                    ▼                          ▼
       ┌───────────────────────────────────────────────────────────────┐
       │            Core Authentication & Migration Flow               │
       │                                                               │
       │  EmbyAuthenticationProvider (login entry)                    │
       │  ├─ EmbyUserDirectory (check if user exists in Emby)         │
       │  ├─ EmbyClient (contacts Emby server)                        │
       │  ├─ LoginDecision (pure function: decide action)             │
       │  ├─ AccountAccessPolicy (apply permissions)                  │
       │  └─ EmbyVerifiedPasswords (record verified hashes)           │
       │                                                               │
       │  MoveAfterLogin (event consumer: moves after login)           │
       │  └─ LoginMethodMove (database operation: move user)           │
       │                                                               │
       │  EmbyMigrationTask (admin-run migration)                      │
       │  ├─ EmbyLoginMethodUsers (list users on Emby method)          │
       │  └─ LoginMethodMove (batch move operation)                    │
       └───────────────────────────────────────────────────────────────┘
               │
               ▼
       ┌──────────────────┐
       │  Jellyfin DB     │
       │  (User table)    │
       └──────────────────┘
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| **EmbyAuthenticationProvider** | Login method entry point; calls Emby, saves password, creates or logs in users, records verified hashes | `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` |
| **EmbyClient** | Only code that sends HTTP requests to Emby (`AuthenticateByName`, `GetUsers`, `Sessions/Logout`) | `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` |
| **EmbyUserDirectory** | Cached copy of Emby user list; skips sending password to Emby for users that don't exist there | `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs` |
| **LoginDecision** | Pure function: decides whether to deny login, use existing Jellyfin account, or create new account | `src/Jellyfin.Plugin.EmbyAuth/LoginDecision.cs` |
| **AccountAccessPolicy** | Sets or adjusts Jellyfin account permissions based on Emby's remote access setting and `AccountAccess` setting | `src/Jellyfin.Plugin.EmbyAuth/AccountAccessPolicy.cs` |
| **EmbyVerifiedPasswords** | File-based record of password hashes that Emby verified; prevents moving users with passwords an admin set on a pre-created account | `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` |
| **MoveAfterLogin** | Event consumer: moves user to configured migration target after login if `MoveAfterFirstLogin` mode is enabled and Emby verified the password | `src/Jellyfin.Plugin.EmbyAuth/MoveAfterLogin.cs` |
| **LoginMethodMove** | Database operation: moves a single user off Emby login method using `ExecuteUpdateAsync` with condition checks | `src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs` |
| **EmbyMigrationTask** | Scheduled task: moves all ready users to configured migration target; can be run manually or on a schedule | `src/Jellyfin.Plugin.EmbyAuth/EmbyMigrationTask.cs` |
| **EmbyLoginMethodUsers** | Lists users currently on Emby login method with their readiness state (Ready, NeedsEmbyLogin, NoPassword, Unknown) | `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs` |
| **PluginConfiguration** | Settings model with `MigrationMode`, `AccountAccess`, and move targets | `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` |
| **EmbyAuthSettings** | Validated settings record; validates URL, API key, and other required settings | `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs` |
| **MigrationTargetValidation** | Checks move targets against Jellyfin's live enabled-provider list | `src/Jellyfin.Plugin.EmbyAuth/MigrationTargetValidation.cs` |
| **EmbyAuthController** | Admin-only API endpoints: `GET /EmbyAuth/Migration` (status), `POST /EmbyAuth/Migration/Run` (trigger task) | `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` |
| **EmbyAuthPlugin** | Plugin lifecycle and settings validation; refuses settings saves if move targets are not enabled | `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs` |
| **PluginServiceRegistrator** | Wires all components into the DI container; registers the four entry points | `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` |

## Pattern Overview

**Overall:** Layered authentication bridge with guarded state and event-driven migration

**Key Characteristics:**
- **Four Jellyfin entry points:** Login method (password validation), event consumer (move after login), scheduled task (batch migration), API controller (migration status and control)
- **Separation of concerns:** `EmbyClient` is the only code contacting Emby; login decision is a pure function; database moves are conditional operations
- **Shared state managed explicitly:** Volatile snapshot swap in `EmbyUserDirectory` for cache coherency; lock-guarded file I/O in `EmbyVerifiedPasswords`
- **One-column database updates:** `ExecuteUpdateAsync` with conditions prevents overwriting concurrent changes (e.g., admin disabling a user)
- **Jellyfin's login lock:** Plugin runs inside Jellyfin's login lock (all unknown-user logins serialize on `Guid.Empty`); must complete quickly and avoid nested user-manager calls

## Layers

**Jellyfin Integration (Entry Points):**
- **Location:** `src/Jellyfin.Plugin.EmbyAuth/`
- **Contains:** `EmbyAuthPlugin`, `PluginServiceRegistrator`, `EmbyAuthenticationProvider`, `MoveAfterLogin`, `EmbyMigrationTask`, `EmbyAuthController`
- **Depends on:** Everything below
- **Used by:** Jellyfin itself (via assembly scanning and routing)
- **Responsibility:** Register with Jellyfin's authentication, event, and task discovery; handle Jellyfin lifecycle

**Domain Logic (Account Rules & State):**
- **Location:** `src/Jellyfin.Plugin.EmbyAuth/` (main directory)
- **Contains:** `LoginDecision`, `AccountAccessPolicy`, `MigrationTargetValidation`
- **Depends on:** Configuration types
- **Used by:** `EmbyAuthenticationProvider`, `MoveAfterLogin`, `EmbyMigrationTask`, `PluginServiceRegistrator`
- **Responsibility:** Make decisions about accounts, apply permissions, validate configuration

**State Management (Caching & Persistence):**
- **Location:** `src/Jellyfin.Plugin.EmbyAuth/`
- **Contains:** `EmbyUserDirectory`, `EmbyVerifiedPasswords`
- **Depends on:** `EmbyClient`
- **Used by:** `EmbyAuthenticationProvider`, `MoveAfterLogin`, `EmbyMigrationTask`, `EmbyAuthController`
- **Responsibility:** Cache Emby user list with TTL; persist and check verified password fingerprints

**External Integration (Emby & Database):**
- **Location:** `src/Jellyfin.Plugin.EmbyAuth/` (main) + `LoginMethodMove`
- **Contains:** `EmbyClient`, `LoginMethodMove`, `EmbyLoginMethodUsers`
- **Depends on:** Jellyfin DB context and HTTP client factory
- **Used by:** All layers above
- **Responsibility:** HTTP communication to Emby; conditional database updates; listing Jellyfin users

**Configuration (Settings):**
- **Location:** `src/Jellyfin.Plugin.EmbyAuth/Configuration/`
- **Contains:** `PluginConfiguration`, `EmbyAuthSettings`
- **Depends on:** None
- **Used by:** All layers for settings access and validation
- **Responsibility:** Define and validate all plugin settings

## Data Flow

### Primary Request Path: User Login

1. **Entry:** Jellyfin calls `EmbyAuthenticationProvider.Authenticate(username, password, resolvedUser)` inside the login lock (`Guid.Empty` for unknown users) (`EmbyAuthenticationProvider.cs:69`)
2. **Validation:** Check blank password, disabled user, administrator status (`EmbyAuthenticationProvider.cs:71-86`)
3. **User existence:** `EmbyUserDirectory.GetStatusAsync()` checks cached Emby user list, skipping Emby call if user not found or unavailable (`EmbyAuthenticationProvider.cs:89`, `EmbyUserDirectory.cs:55`)
4. **Emby authentication:** `EmbyClient.AuthenticateAsync()` sends credentials to Emby (`EmbyAuthenticationProvider.cs:96`, `EmbyClient.cs:50`)
   - Returns `EmbyLogin` record with name and remote-access setting, or null if rejected
   - Ends Emby session with `Sessions/Logout` (`EmbyClient.cs:94`)
5. **Account decision:** `LoginDecision.Decide()` pure function decides: Deny (name mismatch or account on different method), UseAccount (existing account), or CreateAccount (`EmbyAuthenticationProvider.cs:99`, `LoginDecision.cs:40`)
6. **Account handling:**
   - **CreateAccount path:** Create user via `IUserManager.CreateUserAsync()`, set provider to Emby method, save password hash, apply permissions (`EmbyAuthenticationProvider.cs:176-224`)
   - **UseAccount path:** Save password hash, apply permissions to existing account (`EmbyAuthenticationProvider.cs:226-242`)
7. **Record verified hash:** `EmbyVerifiedPasswords.Record()` stores SHA-256 fingerprint of the password hash that Emby just accepted (`EmbyAuthenticationProvider.cs:112`)
8. **Return:** Jellyfin completes login and sets user's login method to Emby (`EmbyAuthenticationProvider.cs:113`)

**Where Jellyfin's login lock sits:** Steps 1-8 all execute inside Jellyfin's login lock. The plugin minimizes time in the lock by: checking user list cache first, using a 5-second HTTP timeout, failing fast on errors, and keeping logic pure.

### Secondary Path: Move After First Login (Event-Driven)

1. **Trigger:** Jellyfin's `SessionManager` publishes `AuthenticationResultEventArgs` after login completes and lock is released (`MoveAfterLogin.cs` remarks)
2. **Guard:** Check `MigrationMode.MoveAfterFirstLogin` setting (`MoveAfterLogin.cs:37`)
3. **Target:** Resolve migration target to login method ID (`LoginMethodMove.ResolveMigrationTarget()`, `MoveAfterLogin.cs:42`)
4. **Database query:** Query Jellyfin database for user still on Emby method with password (`MoveAfterLogin.cs:58-62`)
5. **Verify password:** Check `EmbyVerifiedPasswords.Matches()` — only move if Emby verified this password (`MoveAfterLogin.cs:63`)
6. **Conditional move:** `LoginMethodMove.MoveAsync()` updates one column with condition checks (user still on Emby method and password unchanged) (`MoveAfterLogin.cs:68`, `LoginMethodMove.cs:33`)
7. **Outcome:** User moved or stays on Emby method; login still succeeds (event consumer exceptions are logged and swallowed by Jellyfin)

### Tertiary Path: Manual/Scheduled Migration

1. **Entry:** Administrator runs "Finish the Emby migration" task via settings page, Scheduled Tasks, or API
2. **Target:** Resolve migration target (`EmbyMigrationTask.cs:65`)
3. **List candidates:** `EmbyLoginMethodUsers.ListAsync()` queries all users on Emby method, checks `EmbyVerifiedPasswords.RecordsAvailable()` and `Matches()` to determine readiness state (`EmbyMigrationTask.cs:83`)
4. **Iterate and move:** For each user with `MigrationUserState.Ready`, call `LoginMethodMove.MoveAsync()` with condition checks (`EmbyMigrationTask.cs:93-107`)
5. **Report:** Log summary of moved/remaining users and progress

### Settings Change Path

1. **Entry:** Administrator changes settings via settings page (calls plugin API) or direct Jellyfin plugin API
2. **Validation:** `EmbyAuthPlugin.UpdateConfiguration()` calls `MigrationTargetValidation.FindProblem()` against live enabled provider list (`EmbyAuthPlugin.cs:79`)
3. **Guard:** Refuses save with `ArgumentException` if migration target or password-set target is not an enabled login method (`EmbyAuthPlugin.cs:84`)
4. **Persist:** Base `UpdateConfiguration()` saves settings if validation passes

### Password Change Path (Administrator or User)

1. **Entry:** Jellyfin calls `ChangePassword(user, newPassword)` inside login lock (`EmbyAuthenticationProvider.cs:132`)
2. **Reset:** If empty password, clear saved hash and stay on Emby method (`EmbyAuthenticationProvider.cs:135-139`)
3. **New password:** Hash and save new password (`EmbyAuthenticationProvider.cs:142`)
4. **Move logic:** Resolve password-set target (falls back to migration target if empty) (`LoginMethodMove.ResolvePasswordSetTarget()`, `EmbyAuthenticationProvider.cs:143`)
5. **Outcome:**
   - **MoveTargetKind.Move:** Set `AuthenticationProviderId` to target method (user moves on this call, not in event) (`EmbyAuthenticationProvider.cs:147`)
   - **MoveTargetKind.Remain:** Leave on Emby method (`EmbyAuthenticationProvider.cs:151`)
   - **MoveTargetKind.Invalid:** Leave unchanged, log error (`EmbyAuthenticationProvider.cs:154`)

**State Management:**
- **Emby user list:** `EmbyUserDirectory` holds volatile snapshot; cache TTL is 60 seconds (live update on settings change), retry delay 30 seconds on fetch failure (`EmbyUserDirectory.cs:39, 44`)
- **Verified passwords:** `EmbyVerifiedPasswords` reads JSON file once and caches; non-sticky failed reads retry on next call (`EmbyVerifiedPasswords.cs:116-140`)
- **Lock in login:** All login flow (steps 1-8 in Primary Request Path) serializes inside Jellyfin's lock; must complete in ~seconds, not minutes
- **Lock in password save:** `EmbyVerifiedPasswords.Record()` holds lock only for I/O (`EmbyVerifiedPasswords.cs:45-69`)

## Key Abstractions

**JellyfinAccount:**
- **Purpose:** Hold the username and authentication provider ID of a Jellyfin user; used in login decision logic
- **Examples:** `LoginDecision.cs:10`
- **Pattern:** Record type; passed by value; immutable

**EmbyLogin:**
- **Purpose:** Hold the response from Emby after accepting a login (username and remote-access setting)
- **Examples:** `EmbyClient.cs:21`
- **Pattern:** Record type; returned by Emby client; parsed from JSON response

**EmbyUserStatus:**
- **Purpose:** Report the state of an Emby user (Active, Disabled, NotFound, Unavailable)
- **Examples:** `EmbyUserDirectory.cs:13-26`
- **Pattern:** Enum; returned by `GetStatusAsync()` to decide whether to send password

**MigrationUserState:**
- **Purpose:** Report readiness of a user on Emby method for migration (Ready, NeedsEmbyLogin, NoPassword, Unknown)
- **Examples:** `EmbyLoginMethodUsers.cs:20-42`
- **Pattern:** Enum; determines whether migration task moves user; calculated from DB and fingerprint file

**MoveTarget:**
- **Purpose:** Represent a resolved move target (Move to X, Remain, or Invalid)
- **Examples:** `LoginMethodMove.cs:109`
- **Pattern:** Record struct; discriminates on Kind field; ProviderId is null unless Kind == Move

**LoginAction:**
- **Purpose:** Outcome of login decision (Deny, UseAccount, CreateAccount)
- **Examples:** `LoginDecision.cs:15-25`
- **Pattern:** Enum; returned by pure `Decide()` function

## Entry Points

**Login Method (IAuthenticationProvider):**
- **Location:** `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs`
- **Triggers:** Jellyfin calls when a user attempts login on the Emby method, or when Emby is the fallback for an unknown username
- **Responsibilities:** 
  - Validate credentials against Emby
  - Create or update Jellyfin accounts
  - Save password hashes
  - Apply account permissions
  - Record verified passwords
- **Registered by:** `PluginServiceRegistrator.RegisterServices()` line 37
- **Jellyfin discovery:** Automatic via `GetExports<IAuthenticationProvider>()`

**Event Consumer (IEventConsumer<AuthenticationResultEventArgs>):**
- **Location:** `src/Jellyfin.Plugin.EmbyAuth/MoveAfterLogin.cs`
- **Triggers:** `SessionManager` publishes after any login completes (including Quick Connect), outside the login lock
- **Responsibilities:** Move user to configured target if `MoveAfterFirstLogin` mode and password verified by Emby
- **Registered by:** `PluginServiceRegistrator.RegisterServices()` line 45
- **Jellyfin discovery:** Automatic via scoped DI registration

**Scheduled Task (IScheduledTask):**
- **Location:** `src/Jellyfin.Plugin.EmbyAuth/EmbyMigrationTask.cs`
- **Triggers:** Administrator runs manually; no default trigger configured
- **Responsibilities:** Batch-move all ready users to configured target; report progress
- **Registered by:** Jellyfin discovers via `Assembly.GetExportedTypes()` reflection (must be `public`)
- **Jellyfin UI:** Accessible at Dashboard > Advanced > Scheduled Tasks

**API Controller (ControllerBase):**
- **Location:** `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs`
- **Endpoints:**
  - `GET /EmbyAuth/Migration` — Return migration status (users, task state, available targets)
  - `POST /EmbyAuth/Migration/Run` — Queue migration task if not running
- **Authorization:** `RequiresElevation` policy (administrators only)
- **Registered by:** Jellyfin auto-discovers and routes via `[ApiController]` and `[Route]` attributes
- **Client:** Called by settings page JavaScript (`Configuration/configPage.html`)

**Settings Page (IHasWebPages):**
- **Location:** `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html`
- **Triggers:** Administrator navigates to Plugins > Emby Auth in Dashboard
- **Responsibilities:** Display current settings, validate inputs, call migration API, show user readiness
- **Registered by:** `EmbyAuthPlugin.GetPages()` line 54
- **Framework:** Server-side embedded HTML; uses Jellyfin's `Dashboard` and `ApiClient` globals

## Architectural Constraints

- **Threading:** Single-threaded event loop per request (async/await); `EmbyUserDirectory` uses volatile field for lock-free snapshot updates; `EmbyVerifiedPasswords` uses `Lock` for file I/O
- **Global state:** 
  - `EmbyAuthPlugin.Instance` static singleton (read-only after construction)
  - `EmbyUserDirectory._snapshot` volatile field holds cached copy
  - `EmbyVerifiedPasswords._fingerprints` holds in-memory cache (lazy-loaded)
  - No other shared mutable state
- **Login lock:** All login requests serialize inside Jellyfin's `UserManager` lock on `Guid.Empty` for unknown users; known users do not hold the lock. Plugin must not call `IUserManager.GetUserByName()` recursively or make blocking calls
- **Circular imports:** None — structure is acyclic (Jellyfin → Plugin → Domain → External)
- **Jellyfin dependency:** Plugin depends on Jellyfin 12.1 types; reverse dependency is implicit (Jellyfin discovers and invokes the plugin)

## Anti-Patterns

### Accessing IUserManager in Constructors

**What happens:** Plugin constructors or event handlers hold a reference to `IUserManager`

**Why it's wrong:** `IUserManager` depends on every login method, including this plugin. A cycle in the DI graph breaks construction.

**Do this instead:** Resolve `IUserManager` on each login attempt (inside `Authenticate()`), not in the constructor. `EmbyAuthenticationProvider.cs:107` does this correctly.

### Storing Settings in Static State

**What happens:** Plugin reads configuration once at startup and caches it

**Why it's wrong:** Administrator can change settings without restarting Jellyfin. Settings changes must be read on every login.

**Do this instead:** Accept `Func<PluginConfiguration?>` in the constructor and call it on each login. `EmbyAuthenticationProvider.cs:41` does this correctly.

### Moving Users Without Password Verification

**What happens:** Plugin moves a user to Default after reading the password from the database, without checking that Emby verified it

**Why it's wrong:** An administrator can set a password on a pre-created account without Emby seeing it. Moving such an account to Default makes it accessible with that password, even though Emby never accepted it. Default method allows blank-password access to accounts without passwords.

**Do this instead:** Before moving, call `EmbyVerifiedPasswords.Matches()` and only move if it returns true. `MoveAfterLogin.cs:63` does this correctly.

### Overwriting User Rows With UpdateUserAsync

**What happens:** Plugin calls `IUserManager.UpdateUserAsync()` after changing the login method

**Why it's wrong:** While the update is in flight, an administrator might disable the user or make other changes. `UpdateUserAsync()` overwrites the entire row, losing concurrent changes. The move is not atomic relative to other updates.

**Do this instead:** Use `ExecuteUpdateAsync()` with condition checks on one column only. `LoginMethodMove.MoveAsync()` line 37-41 does this correctly.

## Error Handling

**Strategy:** Fail fast with clear, actionable messages. Never silently ignore errors.

**Patterns:**
- **Login method:** Convert expected failures (Emby rejects login, user disabled) to `AuthenticationException`; convert unexpected exceptions (HTTP error, JSON parse error) to `AuthenticationException` with inner exception for logging
- **Account creation:** Log specific reason if creation or save fails; attempt cleanup delete before throwing
- **Password file I/O:** Log read/write failures at Error level; reads fail non-sticky (retry next call); writes fail soft (keep in-memory, lose on restart only)
- **Settings validation:** Refuse save with `ArgumentException` (synchronous, prevents state change); log validation errors at Error level
- **Event consumer:** Log errors and swallow (Jellyfin's `EventManager` does this anyway; login succeeds even if move fails)
- **Migration task:** Log per-user outcomes and summary; task completes with progress report even if some users fail to move

## Cross-Cutting Concerns

**Logging:**
- Logger pattern: Inject `ILogger<T>` into constructor
- Message format: Actions (created user, moved user), failures (login rejected, save failed), warnings (user list unavailable, settings invalid)
- Security: Never log passwords, password hashes, or API keys
- Structured logging: Use `LoggerMessage` partial methods for efficiency and structured fields

**Validation:**
- Settings: `EmbyAuthSettings.TryCreate()` validates URL, API key, enums (pure function)
- Targets: `MigrationTargetValidation.FindProblem()` validates against live provider list (takes provider list as parameter)
- Settings save: `EmbyAuthPlugin.UpdateConfiguration()` calls validation and refuses save

**Authentication:**
- Jellyfin provides login lock, user manager, and password hasher
- Plugin accepts password, validates against Emby, saves hash in Jellyfin's format
- Admin can reset password (clears hash, user stays on Emby method) or set new password (moves per configuration)

---

*Architecture analysis: 2026-09-20*
