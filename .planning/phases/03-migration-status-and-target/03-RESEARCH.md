# Phase 3: Migration Status and Target - Research

**Researched:** 2026-09-19
**Domain:** ASP.NET Core / EF Core plugin internals (Jellyfin 12.1), xUnit v3 unit testing against `JellyfinDbContext`, jsdom settings-page polling, bats e2e with a second real Jellyfin plugin
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**The migration response**
- **D-01:** `MigrationUser` carries one state enum instead of the `ReadyToMove` boolean: `Ready`, `NeedsEmbyLogin`, `NoPassword`, `Unknown`. AUTH-06 adds a third real state, and today's boolean renders an account with no saved password and an account whose password Emby never verified identically. One field means one switch on the page. The record is public but the plugin has never been released, so no consumer breaks.
- **D-02:** One change to `MigrationStatus` carries everything the Migration section needs: a top-level `RecordsUnavailable` flag for the read failure (FPRT-03), a task record (UI-03), and the per-user state (AUTH-06). This is the single response change that ROADMAP criterion 2 requires.
- **D-03:** The task record carries the task state, the progress, the last end time, and the last result. The last end time is not decoration: `POST /EmbyAuth/Migration/Run` calls `QueueIfNotRunning` and returns 204 at once, so there is a window where the task is queued and its state is still `Idle`. A page that stopped at the first `Idle` would render the old list. The page records when the POST returned and stops only when the task is `Idle` **and** the last run ended after that moment.
- **D-04:** The page polls `GET /EmbyAuth/Migration` every 2 seconds. No websocket: the `ScheduledTasksInfo` socket API has never been used in this repository and the jsdom harness would need a socket stub. Polling reuses the one endpoint and the existing `ApiClient.getJSON` stub.
- **D-05:** Polling follows a `Running` task for as long as it runs — the server is stating it is still working, and a migration of many accounts can outlast any fixed window. Only the start window is capped: if no run has begun after about 20 seconds, the page stops and shows a message pointing to the Jellyfin log. Each failure mode gets the treatment it needs instead of one number serving both.
- **D-06:** `setTimeout(loadEmbyAuthMigration, 3000)` at `configPage.html:137` is deleted. Phase 2 D-11 deliberately left it untested so this change churns no existing test.

**Failure reporting**
- **D-07:** While the fingerprint file cannot be read, every user renders with state `Unknown` and the section shows the `RecordsUnavailable` message pointing to the Jellyfin log. The account names come from the database and stay accurate; only readiness is unknowable. Rendering `NeedsEmbyLogin` for everyone — which is what `Matches` returning `false` would otherwise produce — would state something untrue about every account at once.
- **D-08:** A failed fingerprint **write** stays in the Jellyfin log only, as FPRT-01 specifies. Nothing changes on the page: the in-memory record is real, those users genuinely are ready to move now, and the next successful write clears the condition. The log message at `EmbyVerifiedPasswords.cs:128`, the XML doc, and `docs/how-it-works.md:49` all currently say the opposite of what the code does and are corrected to say the record stays in memory until Jellyfin restarts.
- **D-09:** `Record()` keeps its current order — the cache assignment at `EmbyVerifiedPasswords.cs:58` stays before the write at `:62-63`. This resolves the STATE.md blocker in favour of correcting the sentences rather than moving the mutation. Locked before this discussion by PROJECT.md §Key Decisions: Emby did verify that password moments earlier, so the rule holds and a disk error does not block the migration.

**The move-target settings**
- **D-10:** Two settings, not one. `MigrationTarget` governs the after-login move and the migration task. `PasswordSetTarget` governs the move that happens when an administrator sets a password in Jellyfin (`EmbyAuthenticationProvider.cs:130-132`). `PasswordSetTarget` offers the same choices plus a first entry, "Same as the migration target", which is its default. This contradicts MIGR-01 as written; see Required Roadmap Changes below.
- **D-11:** The target dropdown offers two fixed entries plus a dynamic list. Fixed: **Move to Default** (always present) and **Remain on Emby Login**. Dynamic: one entry per enabled login method that is neither Jellyfin's Default nor this plugin's own Emby method. Labels read "Move to {plugin name}". `EmbyAuthenticationProvider` reports `IsEnabled => true` and `Name => "Emby"`, so it does appear in Jellyfin's enabled list and must be filtered out.
- **D-12:** **Remain on Emby Login** means no path moves anyone — not the after-login move, not the migration task, not the password-set path when `PasswordSetTarget` resolves to it. There is no hidden fallback to Default.
- **D-13:** The list of enabled login methods comes from the plugin's own API. `EmbyAuthController` takes `IUserManager`, calls `GetAuthenticationProviders()`, drops its own Emby method, and returns the remaining name and ID pairs. Filtering is server-side and unit-testable through the `FakeUserManager` that TEST-03 needs anyway (`TestDoubles.cs:220`).
- **D-14:** A target that Jellyfin does not report as enabled is refused at save time with a message. At run time a target that has since disappeared does **not** refuse logins. Emby still checks every password; only the move is skipped, with an Error log, and the Migration section reports it. This deliberately differs from every other invalid setting, which refuses all logins.
- **D-15:** `MigrationMode` and `MigrationTarget` both stay. `MigrationMode` answers *when* a move happens, `MigrationTarget` answers *where*. The settings page states the combination plainly, including that `MigrationMode` has no effect while the target is "Remain on Emby Login". `KeepEmbyInCharge` is not removed.

**The settings page layout**
- **D-16:** The `MigrationTarget` dropdown lives in the **Migration section**, directly above **Run migration now** — not in the settings form. The settings form keeps `PasswordSetTarget`.
- **D-17:** Clicking **Run migration now** saves the picked destination first, then queues the task. Jellyfin's scheduled task API accepts no run parameters. Consequence to document: a one-off run to a different destination also changes where automatic after-login moves go from then on.
- **D-18:** The AUTH-06 no-password warning renders only while at least one account is in the `NoPassword` state, and its wording follows the configured target: specific claim for Default (verified at `DefaultAuthenticationProvider.cs:61-68`), "cannot tell" for any other method, and no warning at all for "Remain on Emby Login".

**Renaming**
- **D-19:** The task `Key` changes from `EmbyAuthMoveUsersToDefault` to `EmbyAuthMigration`. Three lines move: `MoveEmbyUsersToDefaultTask.cs:45` and `e2e/helpers.bash:165,167`.
- **D-20:** Migration wording for the renames:
  - `DefaultLoginMethod` → `LoginMethodMove`
  - `MoveToDefaultLoginMethod` → `MoveAfterLogin`
  - `MoveEmbyUsersToDefaultTask` → `EmbyMigrationTask`
  - task `Name` "Move Emby users to the Default login method" → "Finish the Emby migration"
  - settings properties `MigrationTarget` and `PasswordSetTarget`

**Tests**
- **D-21:** The criterion 7 e2e test uses **JellyfinSecurity v2.6.1**, the `-jf12` zip, pinned and verified against the published `sha256`. No test-only provider is built.
- **D-22:** JellyfinSecurity is installed into the **shared** Jellyfin container that all e2e files already use, not an isolated one. If JellyfinSecurity turns out not to be inert at defaults and disturbs Emby-method logins, that is a finding about a supported path, not a reason to isolate the test.
- **D-23:** The test runs in CI on every pull request, and its scope stops at the hand-over: an Emby login, the password saved, the migration to JellyfinSecurity, and that same password logging the user in without a challenge.
- **D-24:** `MoveAfterLogin` stops reading the static `EmbyAuthPlugin.Instance` (`MoveToDefaultLoginMethod.cs:34`) and takes the settings source that Phase 1 D-10 added.

**The manual-flip finding — closed**
- **D-25:** The Phase 2 finding that an administrator can flip an Emby-method user with no password to Default by hand is **closed with no code in this phase**. Phase 2 verified Jellyfin 12.1 offers no veto on that path. The mitigation is already covered: AUTH-06 warns on the settings page, and DOCS-05 states the behavior in `docs/how-it-works.md`.

### Claude's Discretion
- How the SQLite in-memory `JellyfinDbContext` is stood up for TEST-02 and TEST-03 — needs an `IJellyfinDatabaseProvider`, an `IDbContextFactory<JellyfinDbContext>` double, and a pinned EF Core SQLite package. Confirm that EF Core's InMemory provider is genuinely unusable before adding the dependency, since `LoginMethodMove.MoveAsync` uses `ExecuteUpdateAsync`. **Resolved by this research — see Common Pitfalls.**
- The shape of the `ITaskManager` fake for TEST-03, following the hand-written-double precedent. No mocking library.
- Whether MIGR-02's visibility test guards only `EmbyAuthenticationProvider` or every type that must stay internal.
- Where JellyfinSecurity is installed in the e2e suite lifecycle, given its install needs a Jellyfin restart and `e2e/setup_suite.bash` already waits up to 180 seconds per server.
- Whether the task `Key` change earns a `CHANGELOG.md` entry, following Phase 1's precedent for a settings-visible change.
- Exact wording throughout, within the rule that no message repeats the Emby URL or the API key, and that the plugin never claims behavior it has not verified.
- Test file names and the split across files, following `.planning/codebase/TESTING.md`.

### Deferred Ideas (OUT OF SCOPE)
- **Drop `MigrationMode.KeepEmbyInCharge`** — rejected for this milestone; revisit at a major version.
- **A one-shot run destination that does not change the saved setting** — rejected; Jellyfin's scheduled task API accepts no run parameters.
- **Greying out `MigrationMode` while the target is "Remain on Emby Login"** — the page explains the combination in prose instead.
- **Logging or flagging the manual flip to another login method** — rejected for this phase; needs new persistent state.
- **Retiring the fingerprint file** — blocked upstream, not by this plugin.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| FPRT-01 | Fingerprint write failure stays in-memory only; log/XML-doc/`how-it-works.md` corrected; unit test covers it | `EmbyVerifiedPasswords.cs:59-68` already has the right *behavior* — only the three sentences at `:128`, the XML doc above `Record`, and `how-it-works.md:49` are wrong. See Code Examples. |
| FPRT-03 | Read failure surfaces in the Migration section, pointing to the Jellyfin log | Needs a new way for `EmbyVerifiedPasswords` to expose "records unavailable" distinct from "no match" — see Common Pitfalls #1 and Architecture Patterns. |
| UI-03 | Migration list updates when the task finishes, not after a fixed 3s | `ITaskManager`/`IScheduledTaskWorker` reflected this session — see Architecture Patterns #2 and Code Examples. |
| MIGR-01 | Move-to setting restricted to Jellyfin-enabled login methods, refused otherwise, used by every move path | `IUserManager.GetAuthenticationProviders()` returns `NameIdPair[]` (`Name`, `Id` both `string`) — reflected this session. See Code Examples. Roadmap wording needs the D-10/D-11 split — see Required Roadmap Changes. |
| MIGR-02 | `EmbyAuthenticationProvider` stays internal; a test fails if it becomes public | Reflection-based test pattern in Code Examples. |
| AUTH-06 | No-password accounts named and warned about, never blocked or given an invented password | `EmbyLoginMethodUsers.cs:46-52` already computes `PasswordHash is null`; the four-state precedence needs a decision — see Open Questions #1. |
| TEST-02 | Unit tests for the move classes, `EmbyLoginMethodUsers`, and `EmbyMigrationTask` against an SQLite in-memory `JellyfinDbContext` | `JellyfinDbContext` constructor signature and `IJellyfinDatabaseProvider`/`IEntityFrameworkCoreLockingBehavior` requirements reflected this session — see Architecture Patterns #1 and Common Pitfalls #2-4. |
| TEST-03 | Unit tests for `EmbyAuthController`: migration status, run request, task state | Same DB seam, plus a hand-written `ITaskManager`/`IScheduledTaskWorker` fake — see Code Examples. |
| DOCS-01 | `how-it-works.md` points to the `migration.md` shutdown step that finds users who reverted to Emby | **Already present** in the codebase (`how-it-works.md:50`, added in commit `20ba502`, Phase 2). See Common Pitfalls #7 — verify only, do not re-author. |
| DOCS-05 | `how-it-works.md` states the Default-login blank-password behavior and connects it to AUTH-04, AUTH-06, and the interim-tool framing | Net-new prose; no existing content covers this generally (only the account-creation-window case is covered today). |
</phase_requirements>

## Summary

This phase touches four already-working files (`EmbyVerifiedPasswords`, the migration API, the settings page, and the migration task/event-consumer trio) plus two brand-new unit-test seams that nothing in the repository has needed until now: a real `JellyfinDbContext` running on SQLite in-memory, and a hand-written `ITaskManager`/`IScheduledTaskWorker` fake. Both seams are buildable with the packages and patterns already in the dependency tree — no new production dependency is needed, only one new **test-only** NuGet package (`Microsoft.EntityFrameworkCore.Sqlite`, pinned to the exact EF Core version, `10.0.11`, that `Jellyfin.Database.Implementations 12.1.0` already pulls in transitively).

Reflecting against the actual installed `Jellyfin.Controller`/`Jellyfin.Model` 12.1.0 and `Jellyfin.Database.Implementations` 12.1.0 assemblies this session settled the two open technical questions from CONTEXT.md's "Claude's Discretion": (1) EF Core's InMemory provider genuinely cannot run `ExecuteUpdateAsync` — this is a documented, unfixed EF Core limitation, not a configuration problem, so SQLite in-memory is mandatory, not just preferred; and (2) `JellyfinDbContext`'s constructor takes `IJellyfinDatabaseProvider` and `IEntityFrameworkCoreLockingBehavior` as required dependencies — the locking behavior can reuse Jellyfin's own public `NoLockBehavior` class outright, but the database provider needs a small hand-written fake since no public no-op implementation ships in the referenced packages.

`ITaskManager` and its supporting types (`IScheduledTaskWorker`, `TaskState`, `TaskResult`, `TaskCompletionStatus`) live in `MediaBrowser.Model.Tasks`, not `MediaBrowser.Controller` — reflected this session, with every member listed in Code Examples. This directly informs the `MigrationStatus.Task` shape D-03 calls for: `State`, `CurrentProgress`, and `LastExecutionResult.{EndTimeUtc,Status}` are all present on `IScheduledTaskWorker` with no translation needed.

Two pre-existing findings change the phase's actual workload versus what CONTEXT.md implies: DOCS-01's pointer sentence already exists verbatim in `how-it-works.md` (added in Phase 2, commit `20ba502`) — this requirement needs a verification task, not new authoring. And the JellyfinSecurity "is it inert by default" question CONTEXT.md flagged as unsettled remains genuinely unsettled from documentation alone — the README states `<Enabled>false</Enabled>` fully disables enforcement, but does not state the plugin's *shipped* default for that flag. This phase's e2e work should include an explicit smoke check (install JellyfinSecurity with zero configuration, confirm an uninvolved user's ordinary login is unaffected) before building the full migration hand-over test on top of it.

**Primary recommendation:** Build the SQLite-in-memory test seam and the `ITaskManager` fake first (as ROADMAP already orders), reusing `NoLockBehavior` from `Jellyfin.Database.Implementations` and writing one small `FakeJellyfinDatabaseProvider`; then reshape `GET /EmbyAuth/Migration` once to carry `RecordsUnavailable`, the task record, and the per-user `State` enum together, exactly as D-02 specifies; then layer the `MigrationTarget`/`PasswordSetTarget` settings and renames on top, since `LoginMethodMove.MoveAsync` (renamed `DefaultLoginMethod`) needs the resolved target ID as a parameter regardless of which other phase-3 work has landed.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Migration status reporting (`GET /EmbyAuth/Migration`) | API / Backend | Frontend (settings page renders it) | `EmbyAuthController` is the only code that reads DB state and task state and shapes the response; the page only displays what it returns. |
| Migration list polling and the 20s/2s timing rules (D-04, D-05) | Frontend (settings page JS) | — | Purely client-side timing logic against one existing endpoint; no new server capability. |
| Move-target selection and validation (`MigrationTarget`, `PasswordSetTarget`) | API / Backend | Frontend (dropdown UI) | `EmbyAuthSettings.FindProblem` and the new enabled-provider filter are the enforcement point; the dropdown only reflects what the backend allows. |
| The move itself (`LoginMethodMove.MoveAsync`) | Database / Storage (via EF Core) | API / Backend (orchestration) | A single-column `ExecuteUpdateAsync` against `JellyfinDbContext.Users`; the backend only decides *when* and *to what* to call it. |
| Fingerprint write/read failure handling | API / Backend | — | `EmbyVerifiedPasswords` is a backend-only file-system concern; no tier below or above it is involved. |
| No-password warning (AUTH-06) | API / Backend (state computation) | Frontend (rendering) | `EmbyLoginMethodUsers.ListAsync` decides the state; the page only renders the message text keyed off it. |
| Scheduled task execution (`EmbyMigrationTask`) | API / Backend | Database / Storage | Runs inside Jellyfin's task host; reads/writes the same `JellyfinDbContext` as the login/event paths. |

## Standard Stack

### Core

No new production (`src/`) dependency. `Jellyfin.Controller` and `Jellyfin.Model` stay at `12.1.0`, already pinned in `Jellyfin.Plugin.EmbyAuth.csproj`.

### Supporting (test-only)

| Library | Version | Purpose | Why this version |
|---------|---------|---------|-------------------|
| `Microsoft.EntityFrameworkCore.Sqlite` | `10.0.11` | Real SQLite provider for `JellyfinDbContext` in unit tests (TEST-02, TEST-03) | `[VERIFIED: src/Jellyfin.Plugin.EmbyAuth/obj/project.assets.json — target net10.0]` — `Jellyfin.Database.Implementations 12.1.0` already resolves `Microsoft.EntityFrameworkCore` (and `.Relational`, `.Abstractions`, `.Analyzers`) to exactly `10.0.11`. Pinning the Sqlite provider to the same version avoids NuGet silently bumping the whole EF Core dependency set to a newer patch (`10.0.12` exists on nuget.org) than what actually ships inside the Jellyfin packages this plugin targets. |

No mocking library is added — `FakeUserManager`-style hand-written doubles extend to `ITaskManager`, `IScheduledTaskWorker`, and `IJellyfinDatabaseProvider`, per the existing `TestDoubles.cs` convention and CLAUDE.md's "no mocking library" rule (Phase 1 D-09).

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `Microsoft.EntityFrameworkCore.Sqlite` (real SQLite, in-memory mode) | `Microsoft.EntityFrameworkCore.InMemory` | **Rejected — confirmed unusable, not just discouraged.** The InMemory provider throws when `ExecuteUpdateAsync`/`ExecuteDeleteAsync` are called (`dotnet/efcore#31320`, open, unfixed as of this research) — a generic "could not be translated" error, not a clear "unsupported" message. `LoginMethodMove.MoveAsync` (current `DefaultLoginMethod.MoveAsync`) uses `ExecuteUpdateAsync` as its only write, so InMemory cannot run any test that exercises the real move path. |
| A hand-rolled fake `IJellyfinDatabaseProvider` that also fakes locking | Reusing `Jellyfin.Database.Implementations.Locking.NoLockBehavior` | `NoLockBehavior` is `public`, needs only `ILogger<NoLockBehavior>` (use `NullLogger<NoLockBehavior>.Instance`), and is exactly what a single-writer, single-connection test needs — no reason to reimplement it. |

**Installation:**
```bash
dotnet add tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj \
  package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.11
```

**Version verification:** `[VERIFIED: NuGet API, azuresearch-usnc.nuget.org query, this session]` — `Microsoft.EntityFrameworkCore.Sqlite` `10.0.11` published 2026-08-11, owners `["aspnet","dotnetframework","EntityFramework","Microsoft"]`, 289,639,276 total downloads across all versions of the package.

## Package Legitimacy Audit

| Package | Registry | Age (this version) | Downloads (all versions) | Source Repo | Verdict | Disposition |
|---------|----------|---------------------|---------------------------|--------------|---------|-------------|
| `Microsoft.EntityFrameworkCore.Sqlite` | NuGet | Published 2026-08-11 (patch release; package family is 10+ years old) | 289,639,276 | github.com/dotnet/efcore | OK | Approved — official Microsoft first-party package, same family already used transitively via `Jellyfin.Database.Implementations`. |

**Packages removed due to [SLOP] verdict:** none.
**Packages flagged as suspicious [SUS]:** none.

*The `gsd_run query package-legitimacy check` seam targets npm/PyPI/crates; this is a NuGet (.NET) package, so legitimacy was verified directly against the NuGet v3 API (owner list, download count, publish date) and cross-checked against the package already resolved transitively in `obj/project.assets.json`. No postinstall-script concept exists in the NuGet/MSBuild package model, so that check is N/A for this ecosystem.*

## Architecture Patterns

### System Architecture Diagram

```text
Settings page (configPage.html)                    Jellyfin task host
        │  pageshow / poll every 2s                          │  runs on demand
        ▼                                                     ▼
GET /EmbyAuth/Migration ◄────────────────────────── EmbyMigrationTask.ExecuteAsync
        │                                                     │
        ▼                                                     ▼
EmbyAuthController.GetMigrationStatus              EmbyLoginMethodUsers.ListAsync
        │  reads┐                                             │  reads┐
        │       ▼                                             │       ▼
        │  JellyfinDbContext.Users (via IDbContextFactory)◄───┘  EmbyVerifiedPasswords.Matches
        │       │                                                     │ (Load() may fail → RecordsUnavailable)
        │       ▼
        │  IUserManager.GetAuthenticationProviders() ──► filter out EmbyAuthenticationProvider ──► target dropdown options
        │
        ▼
ITaskManager.ScheduledTasks ──► find worker whose ScheduledTask is EmbyMigrationTask ──► State / CurrentProgress / LastExecutionResult

POST /EmbyAuth/Migration/Run
        │  1. save MigrationTarget (D-17)
        │  2. taskManager.QueueIfNotRunning<EmbyMigrationTask>()
        ▼
204 No Content ──► page records "run requested at T" ──► polls until State==Idle && LastExecutionResult.EndTimeUtc > T, or 20s elapse with no Running state observed
```

### Recommended Project Structure

No new folders. New files land beside their siblings:
```
src/Jellyfin.Plugin.EmbyAuth/
├── LoginMethodMove.cs              # renamed from DefaultLoginMethod.cs (D-20)
├── MoveAfterLogin.cs               # renamed from MoveToDefaultLoginMethod.cs (D-20)
├── EmbyMigrationTask.cs            # renamed from MoveEmbyUsersToDefaultTask.cs (D-20)
├── EmbyLoginMethodUsers.cs         # State enum added; PasswordHash-driven NoPassword case
└── Api/EmbyAuthController.cs       # MigrationStatus reshaped (D-02); +IUserManager

tests/Jellyfin.Plugin.EmbyAuth.Tests/
├── TestDoubles.cs                  # + FakeTaskManager, FakeScheduledTaskWorker, FakeJellyfinDatabaseProvider, SqliteDbContextFactory helper
├── LoginMethodMoveTests.cs         # new — SQLite in-memory
├── EmbyLoginMethodUsersTests.cs    # new — SQLite in-memory
├── EmbyMigrationTaskTests.cs       # new — SQLite in-memory + FakeTaskManager not needed here (task doesn't call ITaskManager itself)
├── EmbyAuthControllerTests.cs      # new — SQLite in-memory + FakeTaskManager + FakeUserManager
└── EmbyAuthenticationProviderVisibilityTests.cs  # new — MIGR-02 reflection guard (or fold into an existing file, per Claude's Discretion)
```

### Pattern 1: SQLite in-memory `JellyfinDbContext` factory for tests

**What:** A test helper that opens one `Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:")`, keeps it open for the test's lifetime, and hands out a fresh `JellyfinDbContext` per call — mirroring what the real `IDbContextFactory<JellyfinDbContext>.CreateDbContextAsync()` does in production (every production call site creates-and-disposes a context per unit of work; see `EmbyAuthController.cs:41-42`, `MoveToDefaultLoginMethod.cs:40-41`, `MoveEmbyUsersToDefaultTask.cs:60-61`).

**When to use:** Any TEST-02/TEST-03 test that exercises `LoginMethodMove.MoveAsync`, `EmbyLoginMethodUsers.ListAsync`, `EmbyMigrationTask.ExecuteAsync`, or `EmbyAuthController`.

**Why the connection must stay open:** A SQLite in-memory database is destroyed the instant its one connection closes. Since production code creates a **new** `JellyfinDbContext` per call through the factory, the test factory must reuse one already-open `SqliteConnection` across every `CreateDbContext()` call so writes from one context are visible to the next — a fresh `"DataSource=:memory:"` string per context would each get its own empty database.

**Example (constructor signatures and interface members confirmed by reflection this session, `[VERIFIED: Jellyfin.Database.Implementations 12.1.0, MediaBrowser.Model 12.1.0 — reflected against ~/.nuget/packages this session]`):**
```csharp
// JellyfinDbContext's real constructor (Jellyfin.Database.Implementations 12.1.0):
//   public JellyfinDbContext(
//       DbContextOptions<JellyfinDbContext> options,
//       ILogger<JellyfinDbContext> logger,
//       IJellyfinDatabaseProvider jellyfinDatabaseProvider,
//       IEntityFrameworkCoreLockingBehavior entityFrameworkCoreLocking)

public sealed class SqliteJellyfinDbContextFactory : IDbContextFactory<JellyfinDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JellyfinDbContext> _options;

    public SqliteJellyfinDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    public JellyfinDbContext CreateDbContext() => new(
        _options,
        NullLogger<JellyfinDbContext>.Instance,
        new FakeJellyfinDatabaseProvider(),
        new NoLockBehavior(NullLogger<NoLockBehavior>.Instance)); // NoLockBehavior is public, reused as-is

    public void Dispose() => _connection.Dispose();
}

// IJellyfinDatabaseProvider has no public no-op implementation in the referenced packages,
// so a minimal fake is needed. Members reflected this session:
internal sealed class FakeJellyfinDatabaseProvider : IJellyfinDatabaseProvider
{
    public IDbContextFactory<JellyfinDbContext>? DbContextFactory { get; set; }
    public void Initialise(DbContextOptionsBuilder options, DatabaseConfigurationOptions config) { }
    public void OnModelCreating(ModelBuilder modelBuilder) { }
    public void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) { }
    public Task RunScheduledOptimisation(CancellationToken ct) => Task.CompletedTask;
    public Task RunShutdownTask(CancellationToken ct) => Task.CompletedTask;
    public Task<string> MigrationBackupFast(CancellationToken ct) => Task.FromResult(string.Empty);
    public Task RestoreBackupFast(string key, CancellationToken ct) => Task.CompletedTask;
    public Task DeleteBackup(string key) => Task.CompletedTask;
    public Task PurgeDatabase(JellyfinDbContext dbContext, IEnumerable<string> tableNames) => Task.CompletedTask;
}
```
`JellyfinDbContext.OnModelCreating` calls `jellyfinDatabaseProvider.OnModelCreating(modelBuilder)` before applying its own entity configuration `[CITED: raw.githubusercontent.com/jellyfin/jellyfin/master/.../JellyfinDbContext.cs, fetched this session]`. The real SQLite provider's implementation only calls `modelBuilder.SetDefaultDateTimeKind(DateTimeKind.Utc)` and adds a `DoNotUseReturningClauseConvention` — neither affects the `Guid`/`string` columns this phase's tests touch (`User.Id`, `Username`, `Password`, `AuthenticationProviderId`), so a no-op fake is sufficient for TEST-02/TEST-03's scope. **Flag this precisely as an assumption** in the plan if a future test starts asserting on `DateTime` columns.

### Pattern 2: `ITaskManager` / `IScheduledTaskWorker` fake for TEST-03

**What:** `ITaskManager` and its supporting types live in `MediaBrowser.Model.Tasks`, not `MediaBrowser.Controller.Tasks` as the existing `using MediaBrowser.Model.Tasks;` in `EmbyAuthController.cs` already implies. Full interface reflected this session, `[VERIFIED: MediaBrowser.Model 12.1.0 — reflected this session]`:

```csharp
// ITaskManager (relevant members only):
IReadOnlyList<IScheduledTaskWorker> ScheduledTasks { get; }
void QueueIfNotRunning<T>();          // <-- what EmbyAuthController.RunMigration already calls
void QueueScheduledTask<T>();
void CancelIfRunning<T>();
// events TaskExecuting, TaskCompleted — not needed for this phase's polling design (D-04 uses HTTP polling, not events)

// IScheduledTaskWorker (relevant members only):
IScheduledTask ScheduledTask { get; }   // the task instance itself — check `is EmbyMigrationTask` or `.Key`
TaskState State { get; }                // enum: Idle, Cancelling, Running
double? CurrentProgress { get; }
TaskResult LastExecutionResult { get; } // .EndTimeUtc (DateTime), .Status (TaskCompletionStatus), .Key, .Name, .ErrorMessage

// TaskCompletionStatus: Completed, Failed, Cancelled, Aborted
```

**Pitfall:** `IScheduledTaskWorker.Id` is **not** the same as `IScheduledTask.Key`. `Id` is a worker-assigned identifier used by Jellyfin's own `/ScheduledTasks/{id}` REST route (see `e2e/helpers.bash:165`, which looks the task up by `.Key` from the `/ScheduledTasks` list response, then uses `.Id` for the run/poll route). Inside `EmbyAuthController`, find the worker with `taskManager.ScheduledTasks.FirstOrDefault(w => w.ScheduledTask is EmbyMigrationTask)` (a type check) rather than trying to match on `Id`.

**Fake shape**, following the `FakeUserManager` precedent (`TestDoubles.cs:94-233`):
```csharp
internal sealed class FakeTaskManager : ITaskManager
{
    public List<IScheduledTaskWorker> Tasks { get; } = [];
    public List<Type> QueuedTypes { get; } = [];

    public IReadOnlyList<IScheduledTaskWorker> ScheduledTasks => Tasks;
    public void QueueIfNotRunning<T>() => QueuedTypes.Add(typeof(T));
    // every other member: throw new NotImplementedException(), per the FakeUserManager convention
    // (QueueScheduledTask<T>, QueueScheduledTask<T>(TaskOptions), CancelIfRunning<T>,
    //  CancelIfRunningAndQueue<T>[()/(TaskOptions)], QueueScheduledTask(IScheduledTask, TaskOptions),
    //  AddTasks, Cancel, Execute, Execute<T>, add/remove TaskExecuting, add/remove TaskCompleted)
}

internal sealed class FakeScheduledTaskWorker(IScheduledTask task) : IScheduledTaskWorker
{
    public IScheduledTask ScheduledTask => task;
    public TaskState State { get; set; } = TaskState.Idle;
    public double? CurrentProgress { get; set; }
    public TaskResult LastExecutionResult { get; set; } = new() { EndTimeUtc = DateTime.MinValue, Status = TaskCompletionStatus.Completed };
    public string Name => task.Name;
    public string Description => task.Description;
    public string Category => task.Category;
    public string Id => "fake-id";
    public IReadOnlyList<TaskTriggerInfo> Triggers { get; set; } = [];
    public void ReloadTriggerEvents() { }
    public event EventHandler<GenericEventArgs<double>>? TaskProgress { add { } remove { } }
}
```

### Pattern 3: `Func<PluginConfiguration?>` as a DI-resolvable singleton (fixes D-24 cleanly)

**What:** `EmbyAuthenticationProvider` already takes `Func<PluginConfiguration?> configurationSource` (Phase 1 D-10), registered via a lambda in `PluginServiceRegistrator`. `MoveToDefaultLoginMethod`/`MoveAfterLogin` still reads the static `EmbyAuthPlugin.Instance?.Configuration.MigrationMode` directly (`MoveToDefaultLoginMethod.cs:34`) — D-24 requires removing this.

**Recommendation:** Register the `Func<PluginConfiguration?>` itself as a singleton once, so both classes can just declare it as an ordinary constructor parameter and let DI resolve it — no per-class factory lambda needed:
```csharp
// PluginServiceRegistrator.RegisterServices:
serviceCollection.AddSingleton<Func<PluginConfiguration?>>(_ => () => EmbyAuthPlugin.Instance?.Configuration);
// EmbyAuthenticationProvider and MoveAfterLogin constructors now both just take:
//   Func<PluginConfiguration?> configurationSource
// and DI wires it automatically — no bespoke factory lambda per class.
```
This also means `MoveAfterLogin`'s target resolution (reading `MigrationTarget`, not just `MigrationMode`) goes through the same tested seam as `EmbyAuthenticationProvider.GetSettings()`.

### Pattern 4: One-response reshape for `MigrationStatus` (D-01, D-02)

Recommended shape (server-side reasoning only — exact C# record/enum names and JSON field names are the planner's call, but the response must carry all three pieces in one payload per D-02, and per D-16's placement of the target dropdown in the same Migration section that this one endpoint already feeds):

```csharp
public enum MigrationUserState { Ready, NeedsEmbyLogin, NoPassword, Unknown }

public sealed record MigrationTaskInfo(TaskState State, double? Progress, DateTime? LastEndTimeUtc, TaskCompletionStatus? LastResult);

public sealed record MigrationUser(string Name, MigrationUserState State);

public sealed record MigrationStatus(
    bool RecordsUnavailable,
    MigrationTaskInfo Task,
    IReadOnlyList<MigrationUser> Users);
```

**Open design question** (not resolved by CONTEXT.md — see Open Questions #2): whether the enabled-login-method list for the target dropdown (D-13) rides on this same response or a second endpoint. Given D-02's stated principle ("one change ... carries everything the Migration section needs") and D-16's placement of the dropdown inside the same section this endpoint already feeds on the same `pageshow`/poll cadence, folding it in as a fourth field avoids a second round trip and a second failure-message path:

```csharp
public sealed record MigrationStatus(
    bool RecordsUnavailable,
    MigrationTaskInfo Task,
    IReadOnlyList<MigrationUser> Users,
    IReadOnlyList<NameIdPair> AvailableTargets); // NameIdPair: MediaBrowser.Model.Dto — Name, Id both string
```

### Pattern 5: Filtering the enabled-login-method list (MIGR-01, D-11, D-13)

`IUserManager.GetAuthenticationProviders()` returns `MediaBrowser.Model.Dto.NameIdPair[]` — `[VERIFIED: MediaBrowser.Model 12.1.0 — reflected this session]`. `NameIdPair.Id` is the type's full name string (matches `EmbyAuthenticationProvider.ProviderId`, `DefaultLoginMethod.ProviderId`, etc. — confirmed by `EmbyAuthenticationProvider.ProviderId`'s own implementation, `typeof(EmbyAuthenticationProvider).FullName!`).

```csharp
var targets = userManager.GetAuthenticationProviders()
    .Where(p => p.Id != EmbyAuthenticationProvider.ProviderId)
    .ToList();
```
`FakeUserManager.GetAuthenticationProviders()` currently throws `NotImplementedException` (`TestDoubles.cs:220`) and needs a settable `NameIdPair[]` field, per D-13's own note that this is "unit-testable through the `FakeUserManager` that TEST-03 needs anyway."

### Pattern 6: Node's built-in mock timers for the D-04/D-05 polling tests

**What:** `tests/js` currently has no fake-timer infrastructure — `flush()` (`testHelpers.js:227-237`) awaits three real macrotask boundaries. Testing the new 2-second poll loop and the ~20-second start-window cap without real waits needs a fake clock.

**Recommendation:** Use `node:test`'s built-in `context.mock.timers`, available since Node 20.4 and unconditionally available on this repo's pinned Node `24.21.0` (`[VERIFIED: .mise.toml:11]`). No new dependency; this is a Node built-in.

```javascript
// [CITED: nodejs.org/api/test.html#mocktimersenablefeatures, fetched this session]
test('polling stops once the task is Idle after the run was requested', async (t) => {
  t.mock.timers.enable({ apis: ['setInterval'] });
  const { document, window, api, close } = buildDom({ /* ... */ });
  try {
    clickRunMigration(document, window);
    await flush();
    t.mock.timers.tick(2000); // advance one poll interval
    await flush();
    // assert on rendered state
  } finally {
    close();
  }
});
```
Combine `tick()` (synchronous) with the existing `flush()` (drains the promise microtasks that `ApiClient.getJSON` resolves through) after each tick, since the mocked timer fires synchronously but the page's own `.then()` chain still needs real microtask turns to settle.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Locking behavior for the test `JellyfinDbContext` | A custom no-op `IEntityFrameworkCoreLockingBehavior` | `Jellyfin.Database.Implementations.Locking.NoLockBehavior` (public, `[VERIFIED: reflected this session]`) | Already exists, already public, needs only `ILogger<NoLockBehavior>`. |
| In-process EF Core test double | `Microsoft.EntityFrameworkCore.InMemory` | `Microsoft.EntityFrameworkCore.Sqlite` with `"DataSource=:memory:"` and a held-open connection | InMemory cannot execute `ExecuteUpdateAsync`, which is the only write the move classes perform. |
| A settings-page fake clock | A bespoke `setTimeout` shim/mock module | `node:test`'s built-in `context.mock.timers` | Ships with the pinned Node version; zero new dependency; already the idiomatic Node 20+ answer to this exact problem. |
| Discovering which login methods Jellyfin has enabled | Hard-coding a list, or re-deriving it from `PluginConfiguration` | `IUserManager.GetAuthenticationProviders()` | It is Jellyfin's own source of truth and already returns exactly the `(Name, Id)` shape the dropdown needs. |

**Key insight:** every piece of test infrastructure this phase needs — SQLite in-memory EF Core, Node's mock timers, Jellyfin's own `NoLockBehavior` — already exists in a package or runtime this repository already depends on. The only genuinely new code is two small fakes (`IJellyfinDatabaseProvider`, `ITaskManager`/`IScheduledTaskWorker`) that follow the exact shape of `FakeUserManager` already in `TestDoubles.cs`.

## Common Pitfalls

### Pitfall 1: `EmbyVerifiedPasswords.Matches` cannot currently distinguish "unreadable file" from "no match"
**What goes wrong:** `Matches()` returns `false` both when the file genuinely has no record for a user and when `Load()` failed to read the file at all (`EmbyVerifiedPasswords.cs:78-90`). FPRT-03 and D-07 need the Migration section to render `Unknown` for every user (not `NeedsEmbyLogin`) specifically when the file is unreadable.
**Why it happens:** `Load()` returns `null` on a failed read and the existing `Matches` collapses that into the same `false` a genuine non-match produces.
**How to avoid:** Add a way to ask "were the records available for this call" without changing the existing `Matches(Guid, string?)` signature that `EmbyAuthenticationProvider.cs:82` and `MoveToDefaultLoginMethod.cs:48` already call and don't care about availability. Options: an `out bool recordsAvailable` overload, or a separate `bool TryGetAvailability()`-style method called once per `EmbyLoginMethodUsers.ListAsync` invocation (not once per user) since `Load()` caches its result for the life of the singleton.
**Warning signs:** A test that forces a read failure and asserts every user is `Unknown` will fail if the new code path still routes through the existing two-value `Matches`.

### Pitfall 2: EF Core's InMemory provider cannot run `ExecuteUpdateAsync` — confirmed, not assumed
**What goes wrong:** A test written against `Microsoft.EntityFrameworkCore.InMemory` calling `LoginMethodMove.MoveAsync` throws a generic "the query could not be translated" `InvalidOperationException` that does not clearly say "unsupported provider."
**Why it happens:** `[CITED: github.com/dotnet/efcore issue #31320, "Throw more specific error when attempting to leverage 'ExecuteUpdate/Delete' methods with InMemory provider", open/unfixed as of this research]` — bulk `ExecuteUpdate`/`ExecuteDelete` are simply not implemented against the InMemory provider.
**How to avoid:** Use `Microsoft.EntityFrameworkCore.Sqlite` in in-memory mode instead (see Standard Stack, Pattern 1).
**Warning signs:** Any exception mentioning "could not be translated" from a test that calls `ExecuteUpdateAsync`/`ExecuteDeleteAsync` against an `UseInMemoryDatabase(...)` context.

### Pitfall 3: A fresh `"DataSource=:memory:"` per `JellyfinDbContext` yields an empty database every time
**What goes wrong:** If the test factory builds a new `DbContextOptionsBuilder<JellyfinDbContext>().UseSqlite("DataSource=:memory:")` on every `CreateDbContext()` call (mirroring how a real connection-string based factory might look), each context gets its own private in-memory database, so data written by one call is invisible to the next.
**Why it happens:** SQLite's in-memory mode ties the database's lifetime to a single open connection, not to the connection string.
**How to avoid:** Open one `SqliteConnection` in the test factory's constructor, keep it open for the whole factory's lifetime, and pass that **connection object** (not a connection string) to every `UseSqlite(connection)` call. See Pattern 1.
**Warning signs:** A test that writes a row in one call and reads it back in a "separate" call (mirroring production's create-context-per-operation pattern) sees no rows.

### Pitfall 4: `JellyfinDbContext` cannot be `new`'d with just a `DbContextOptions`
**What goes wrong:** `new JellyfinDbContext(options)` does not compile — the real constructor requires `ILogger<JellyfinDbContext>`, `IJellyfinDatabaseProvider`, and `IEntityFrameworkCoreLockingBehavior` as well (`[VERIFIED: reflected this session]`).
**Why it happens:** Jellyfin's own EF Core layer added a pluggable database-provider abstraction and pluggable locking behavior on top of a plain `DbContext`.
**How to avoid:** Supply `NullLogger<JellyfinDbContext>.Instance`, a small hand-written `IJellyfinDatabaseProvider` fake (no public no-op ships in the referenced packages), and the reused `NoLockBehavior`. See Pattern 1.
**Warning signs:** A constructor-argument-count compile error the moment a test tries to instantiate `JellyfinDbContext` directly.

### Pitfall 5: `IScheduledTaskWorker.Id` is not `IScheduledTask.Key`
**What goes wrong:** Code that tries to find the migration task's worker by comparing `worker.Id` to the plugin's own `"EmbyAuthMigration"` key never matches.
**Why it happens:** `Id` is a Jellyfin-assigned worker identifier used in REST routes (`/ScheduledTasks/{id}`); `Key` is the identifier the plugin itself defines on `IScheduledTask`.
**How to avoid:** Match on `worker.ScheduledTask is EmbyMigrationTask` (a type check) or `worker.ScheduledTask.Key == "EmbyAuthMigration"` — never on `worker.Id`.
**Warning signs:** `EmbyAuthController` always reports the task as "not found" or falls back to a default/never-run state despite the task having actually run.

### Pitfall 6: SQLite's `RETURNING` clause convention is disabled in Jellyfin's real provider — but doesn't matter for this phase's queries
**What goes wrong:** Someone notices `SqliteDatabaseProvider.ConfigureConventions` adds `DoNotUseReturningClauseConvention` `[CITED: raw.githubusercontent.com/jellyfin/jellyfin/master/.../Jellyfin.Database.Providers.Sqlite/SqliteDatabaseProvider.cs, fetched this session]` and worries the test fake needs to replicate it for `ExecuteUpdateAsync` to behave identically to production.
**Why it happens:** EF Core 7+ can generate `UPDATE ... RETURNING` for certain provider/scenario combinations; Jellyfin's real provider opts out.
**How to avoid:** `LoginMethodMove.MoveAsync` only reads the affected-row *count* from `ExecuteUpdateAsync`, never a returned column value, so the `RETURNING` clause is irrelevant to this phase's tests either way. Do not spend effort replicating this convention in the test fake; note it as a known simplification if asked.
**Warning signs:** None expected for this phase's scope — flag only if a future phase's test starts asserting on returned column values from an `ExecuteUpdateAsync`/`ExecuteDeleteAsync` call.

### Pitfall 7: DOCS-01 already exists — don't re-author it
**What goes wrong:** A plan task authors new pointer text in `how-it-works.md` linking to the `migration.md` shutdown step, duplicating or conflicting with existing text.
**Why it happens:** REQUIREMENTS.md's traceability table marks DOCS-01 "Phase 3, Pending," which reads as "not yet done."
**How to avoid:** `[VERIFIED: docs/how-it-works.md:50, commit 20ba502 "docs(02-03): state what a fingerprint-file read failure does"]` — the exact sentence already exists: *"In a rare timing case, another session of the user can save an older copy of the account and put the user back on the Emby login method. Step 2 of [Shut down Emby](migration.md#shut-down-emby) finds these users."* This satisfies DOCS-01 as written. The plan should include a verification step (confirm the sentence and the anchor still resolve correctly after `docs/migration.md`'s "Shut down Emby" section is updated for the new state model) rather than a new-authoring task.
**Warning signs:** A diff that adds a second, near-duplicate sentence to `how-it-works.md`.

## Code Examples

### FPRT-01: the write-failure log/doc correction (all three sentences are wrong today)

```csharp
// src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:128 — current (wrong) text:
// "Jellyfin cannot write {FilePath}. The plugin does not move this user to the Default
//  login method until the user logs in again through Emby."
//
// Corrected text must say the record stays in memory until Jellyfin restarts (FPRT-01, D-08):
[LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot write {FilePath}. The plugin keeps the record in memory and still moves this user; the record is lost only if Jellyfin restarts before the next successful write.")]
private static partial void LogWriteFailed(ILogger logger, Exception exception, string filePath);
```
The method's XML `<summary>` (currently: *"If the file cannot be read or cannot be written, the method logs an error and the record is lost"*) and `docs/how-it-works.md:49` (currently: *"If the fingerprint file cannot be written, the plugin logs an error and moves no affected user to Default until that user logs in through Emby again"*) both need the same correction — all three currently describe the *opposite* of what `Record()`'s actual order (cache-then-write, D-09) does.

### FPRT-01: the unit test this needs

The existing `EmbyVerifiedPasswordsTests.cs` writes real files in `Path.GetTempPath()` (`[VERIFIED: .planning/codebase/TESTING.md — "The file system: EmbyVerifiedPasswordsTests writes a real file in Path.GetTempPath()"]`). A write-failure test needs a path EF Core/the file system will reliably refuse to write — e.g. a directory used as the file path, or a read-only file — then assert (a) `Matches()` still returns `true` for that user afterward (the in-memory record survived), and (b) the `CapturingLogger` recorded exactly one `Error:` entry.

### MIGR-02: the visibility guard test

```csharp
[Fact]
public void EmbyAuthenticationProvider_StaysInternal()
{
    // A comment on the class itself must state why (Jellyfin's IAuthenticationProvider
    // scan via GetExports<T>() would otherwise let another plugin drive this provider).
    Assert.False(typeof(EmbyAuthenticationProvider).IsPublic);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|---------------|--------|
| `bool ReadyToMove` on `MigrationUser` | `MigrationUserState` enum (`Ready`/`NeedsEmbyLogin`/`NoPassword`/`Unknown`) | This phase (D-01) | Every caller of the old field (`configPage.html:91,98`, `e2e/helpers.bash:165-183` `migration_ready_state`) needs updating; the e2e helper's `jq -r .Users[]...ReadyToMove` boolean check becomes a string-state check. |
| Static `EmbyAuthPlugin.Instance?.Configuration` read in `MoveToDefaultLoginMethod` | `Func<PluginConfiguration?> configurationSource` injected, same pattern `EmbyAuthenticationProvider` already uses | This phase (D-24) | Removes the last static-state read outside `EmbyAuthPlugin.cs` itself; makes `MoveAfterLogin` unit-testable with an arbitrary `PluginConfiguration` with no static setup/teardown. |
| Task key `EmbyAuthMoveUsersToDefault` | `EmbyAuthMigration` | This phase (D-19) | `e2e/helpers.bash:165` (`run_migration_task`) must be updated in the same commit as the C# rename, or the e2e suite silently can't find the task. |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | A no-op `IJellyfinDatabaseProvider.OnModelCreating`/`ConfigureConventions` fake is sufficient for TEST-02/TEST-03's scope (only `Guid`/`string` columns on `User` are touched) | Architecture Patterns #1, Common Pitfalls #6 | Low — if a future test in this phase touches a `DateTime`-typed column or asserts on generated SQL shape, the fake may need to replicate `SetDefaultDateTimeKind`/`DoNotUseReturningClauseConvention`. Nothing in this phase's five target types (`LoginMethodMove`, `MoveAfterLogin`, `EmbyLoginMethodUsers`, `EmbyMigrationTask`, `EmbyAuthController`) reads or writes a `DateTime` column on `User`. |
| A2 | JellyfinSecurity's `<Enabled>` flag on a fresh, unconfigured install is either `false` by default or otherwise produces no observable change to an unrelated user's login | Summary, Environment Availability | Medium — this is exactly the question CONTEXT.md flagged as unsettled and asked this research to settle. The README confirms `Enabled=false` *fully* disables enforcement, and confirms "Require for all users" is off by default, but does not state the plugin's own out-of-the-box default for `Enabled` itself. Recommend an explicit e2e smoke check before wiring the full D-21–D-23 hand-over test (see Open Questions #3). |
| A3 | Folding the enabled-login-method target list into the same `GET /EmbyAuth/Migration` response (Pattern 4's `AvailableTargets` field) is preferable to a second endpoint | Architecture Patterns #4 | Low — either design satisfies D-13's requirements; this is a recommendation, not a locked decision, and the planner may choose a separate endpoint without contradicting any CONTEXT.md decision. |

## Open Questions

1. **State precedence when both `NoPassword` and `Unknown` conditions hold for the same user.**
   - What we know: `NoPassword` (D-01) is a pure database fact (`user.Password is null`) independent of the fingerprint file. `Unknown` (D-07) applies "while the fingerprint file cannot be read" and D-07's own text says "every user renders with state `Unknown`" during that condition.
   - What's unclear: CONTEXT.md does not disambiguate which state wins for a user who has no saved password **and** the fingerprint file happens to be unreadable at the same poll. AUTH-06 needs to reliably name every no-password account regardless of file state, which argues `NoPassword` should take precedence since it needs no fingerprint data at all.
   - Recommendation: `NoPassword` takes precedence over `Unknown` in the state computation, since it is independently and reliably knowable from the database alone; only apply `Unknown` to a user whose readiness genuinely depends on the fingerprint file. Confirm this ordering explicitly in the plan rather than leaving it to code-review discovery.

2. **Does the enabled-login-method target list ride on `GET /EmbyAuth/Migration` or a separate endpoint?**
   - What we know: D-13 only specifies the *source* (`IUserManager.GetAuthenticationProviders()`, filtered), not the *transport*. D-02's stated principle and D-16's UI placement both point toward folding it into the existing response (see Pattern 4 and Assumption A3).
   - What's unclear: CONTEXT.md never states this explicitly as a locked decision.
   - Recommendation: fold it in (Pattern 4), but this is safe for the planner to decide either way without contradicting any CONTEXT.md text.

3. **Is JellyfinSecurity inert on a completely unconfigured install?**
   - What we know: `<Enabled>false</Enabled>` fully disables all enforcement middleware (confirmed verbatim in the plugin's own README troubleshooting section). "Require for all users" is off by default.
   - What's unclear: the shipped default value of `Enabled` itself on first install is not stated in any documentation found this session.
   - Recommendation: add an explicit, cheap e2e smoke assertion — install JellyfinSecurity into the shared stack, then confirm an existing, uninvolved user (not part of the migration test) logs in with no 401/challenge — before building the D-21–D-23 hand-over test on top of it. If it turns out not to be inert, that is itself the finding D-22 already anticipates ("a finding about a supported path, not a reason to isolate the test") and should be reported, not silently worked around.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|--------------|-----------|---------|----------|
| Docker | e2e suite (`mise run e2e`) | Assumed available per existing e2e infra; not re-probed this session (unchanged from Phases 1-2) | — | — |
| `dotnet` / .NET SDK | Unit tests, package build | `[VERIFIED: dotnet run succeeded this session against net10.0]` | 10.x (net10.0 target) | — |
| Node.js | `tests/js` | `[VERIFIED: .mise.toml:11]` | 24.21.0 | — |
| JellyfinSecurity `v2.6.1` `-jf12` zip | Criterion 7 e2e test (D-21) | Not fetched this session (external network download); pinned URL and sha256 already verified during the discuss-phase session per `03-CONTEXT.md` §Canonical References | v2.6.1, published 2026-09-14 | None — D-21 explicitly rejects a test-only substitute provider. |

**Missing dependencies with no fallback:** none identified this session beyond the already-accepted JellyfinSecurity network dependency, which CONTEXT.md's D-21 already addresses (pin + checksum verify, no substitute).

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit v3 `4.0.1` (unit), bats `1.14.0` (e2e/script), `node:test` (settings-page JS) |
| Config file | `Jellyfin.Plugin.EmbyAuth.slnx`; `global.json` selects Microsoft.Testing.Platform |
| Quick run command | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` |
| Full suite command | `mise run test` (unit + script) and `mise run e2e` (Docker-backed, required for this phase per CLAUDE.md's "a change that depends on Jellyfin or Emby behavior needs an end-to-end test") |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|---------------------|--------------|
| FPRT-01 | Write failure keeps the in-memory record; log/doc corrected | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter EmbyVerifiedPasswordsTests` | ✅ file exists, ❌ new test case (Wave 0: none needed, extend existing file) |
| FPRT-03 | Read failure surfaces as `RecordsUnavailable` + `Unknown` states | unit + jsdom | `dotnet test ...` (controller/list) + `node --test tests/js` (page rendering) | ❌ Wave 0 — `EmbyAuthControllerTests.cs` new |
| UI-03 | Poll until task-finished, 20s start cap | jsdom | `node --test tests/js` | ❌ Wave 0 — needs `node:test` mock-timers pattern (Pattern 6) added to `testHelpers.js`/test file |
| MIGR-01 | Target restricted to enabled methods, refused otherwise | unit + e2e | `dotnet test ...` (`EmbyAuthSettingsTests`, `EmbyAuthControllerTests`) + `bats e2e/50-migration-target.bats` (new file) | ❌ Wave 0 — new e2e file |
| MIGR-02 | `EmbyAuthenticationProvider` stays internal | unit | `dotnet test --filter EmbyAuthenticationProviderVisibilityTests` (or folded into an existing file) | ❌ Wave 0 — new test |
| AUTH-06 | No-password accounts named, warned, never blocked | unit + jsdom + e2e | `dotnet test ...` (`EmbyLoginMethodUsersTests`) + `node --test tests/js` + `bats e2e/50-migration-target.bats` | ❌ Wave 0 |
| TEST-02 | Move classes against SQLite in-memory `JellyfinDbContext` | unit | `dotnet test --filter "LoginMethodMoveTests|EmbyLoginMethodUsersTests|EmbyMigrationTaskTests"` | ❌ Wave 0 — the DB seam itself is the prerequisite |
| TEST-03 | `EmbyAuthController` status/run/task-state | unit | `dotnet test --filter EmbyAuthControllerTests` | ❌ Wave 0 — needs both the DB seam and the `ITaskManager` fake |
| DOCS-01 | Pointer sentence present and accurate | manual/doc-content | none automated (Phase 2 precedent used a plain-text acceptance-criteria regex for `docs/settings.md`; consider the same for this sentence if the project wants it automated) | ✅ content already exists — verification only |
| DOCS-05 | Blank-password behavior stated and connected to AUTH-04/AUTH-06/interim-tool framing | manual/doc-content | none automated | ❌ net-new prose |

### Sampling Rate

- **Per task commit:** `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` (unit, ~1s per the measured 786ms baseline) plus `node --test tests/js` for any settings-page change.
- **Per wave merge:** `mise run test` (adds script tests) and, per CLAUDE.md, `mise run e2e` for every change under `src/` — this phase touches `src/` extensively, so e2e should run at every wave boundary, not only at phase gate.
- **Phase gate:** Full `mise run e2e` green, including the new `e2e/50-migration-target.bats` file, before `/gsd-verify-work`.

### Wave 0 Gaps

- [ ] `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — add `FakeJellyfinDatabaseProvider`, `SqliteJellyfinDbContextFactory` (or equivalent), `FakeTaskManager`, `FakeScheduledTaskWorker`; extend `FakeUserManager.GetAuthenticationProviders()`
- [ ] `Microsoft.EntityFrameworkCore.Sqlite` `10.0.11` added to `tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj`
- [ ] `tests/js/testHelpers.js` — extend for `node:test` mock timers (Pattern 6) if the polling tests need helpers beyond what `flush()` already provides
- [ ] `e2e/50-migration-target.bats` (or similar new file number) — does not exist yet; needed for MIGR-01 criterion 7 and the AUTH-06 no-password e2e assertion
- [ ] `e2e/compose.yaml` / `e2e/setup_suite.bash` — JellyfinSecurity plugin folder mount and the pinned-zip download+checksum step do not exist yet

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|----------------|---------|-------------------|
| V2 Authentication | Yes | No new authentication mechanism; the phase must preserve the existing rule that only a password Emby verified ever moves a user off the Emby method (`EmbyVerifiedPasswords.Matches`, unchanged by this phase's target-setting work). |
| V3 Session Management | No | Not touched by this phase. |
| V4 Access Control | Yes | `EmbyAuthController` keeps `[Authorize(Policy = Policies.RequiresElevation)]` on every route, including any new target-list route/field; e2e 403 coverage must extend to whatever new surface this phase adds (`.claude/rules/plugin.md` §API and settings page). |
| V5 Input Validation | Yes | `MigrationTarget`/`PasswordSetTarget` must be validated server-side against Jellyfin's actual enabled-provider list at save time (D-14) — client-side dropdown restriction alone is not a control. |
| V6 Cryptography | No | No new cryptographic operation; `EmbyVerifiedPasswords`' SHA-256 fingerprinting is unchanged by this phase. |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|-----------------------|
| Admin sets `MigrationTarget`/`PasswordSetTarget` to an arbitrary/attacker-controlled login-method ID (not one Jellyfin reports as enabled) | Tampering / Elevation of Privilege | D-14: server-side refusal at save time against `IUserManager.GetAuthenticationProviders()`'s live list — never trust a client-submitted ID without re-checking it server-side. |
| Selecting the plugin's own Emby method as the migration target (self-referential move, or the plugin appearing in its own dropdown due to `IsEnabled => true`) | Tampering | D-11: filter `EmbyAuthenticationProvider.ProviderId` out of the dynamic list server-side (same filter point as D-14's validation), not just hidden client-side. |
| An account with a blank/no saved password moved onto Jellyfin's Default login method, which accepts a blank password | Elevation of Privilege | AUTH-06: the plugin never invents a password and never blocks the move, but the settings page must warn before the move happens; this is a disclosed-risk mitigation (informational control), not a technical block — already the locked design (D-18, D-25). |
| Target that was valid at save time later becomes unavailable (its plugin removed) | Denial of Service (of the migration) / Tampering (silent misdirection) | D-14: the move is skipped with an Error log and reported in the Migration section; Emby continues to check every password regardless, so this cannot become an auth bypass — only a "stuck on Emby method" state, which is the intentionally safe failure mode. |

## Sources

### Primary (HIGH confidence)
- `~/.nuget/packages/jellyfin.controller/12.1.0`, `~/.nuget/packages/jellyfin.model/12.1.0` — reflected via a scratch .NET console app this session against the actual installed assemblies (`MediaBrowser.Model.Tasks.ITaskManager`, `IScheduledTaskWorker`, `TaskState`, `TaskResult`, `TaskCompletionStatus`, `MediaBrowser.Controller.Authentication.IAuthenticationProvider`, `MediaBrowser.Model.Dto.NameIdPair`)
- `Jellyfin.Database.Implementations 12.1.0` (via NuGet cache) — reflected this session (`JellyfinDbContext` constructor, `IJellyfinDatabaseProvider` members, `Locking.NoLockBehavior`/`OptimisticLockBehavior`/`PessimisticLockBehavior`, `Entities.User` constructor and properties)
- `src/Jellyfin.Plugin.EmbyAuth/obj/project.assets.json` — resolved package graph confirming `Microsoft.EntityFrameworkCore*` `10.0.11` pulled in transitively by `Jellyfin.Database.Implementations 12.1.0`
- `api.nuget.org` / `azuresearch-usnc.nuget.org` — version list and owner/download data for `Microsoft.EntityFrameworkCore.Sqlite`, queried this session
- `docs/how-it-works.md:50` and `git log -p -- docs/how-it-works.md` (commit `20ba502`) — confirms DOCS-01's pointer sentence already exists

### Secondary (MEDIUM confidence)
- `raw.githubusercontent.com/jellyfin/jellyfin/master/src/Jellyfin.Database/Jellyfin.Database.Implementations/JellyfinDbContext.cs` and `.../Jellyfin.Database.Providers.Sqlite/SqliteDatabaseProvider.cs` — fetched this session; matches the tag this plugin's `Jellyfin.Controller 12.1.0` package should correspond to, but was not diffed line-for-line against the exact compiled assembly (reflection confirms the interface/constructor shapes independently; the method-body quotes come from this fetch)
- `github.com/dotnet/efcore` issue #31320 — official repo, open/unfixed issue confirming InMemory provider cannot run `ExecuteUpdateAsync`/`ExecuteDeleteAsync`
- `nodejs.org/api/test.html#mocktimersenablefeatures` — fetched this session for `mock.timers` API shape
- `github.com/ZL154/JellyfinSecurity` README (raw, fetched this session) — `<Enabled>false</Enabled>` disable instructions, "Require for all users — off by default" wording

### Tertiary (LOW confidence)
- Whether JellyfinSecurity's `Enabled` flag defaults to `true` or `false` on a fresh install — not stated in any source found this session; treated as an open question requiring an e2e smoke check rather than a documentation-only answer (see Open Questions #3, Assumption A2)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — the one new package is verified against the actual resolved dependency graph and the NuGet registry directly
- Architecture (DB/task-manager seams): HIGH — every interface member cited was reflected against the actual installed assemblies this session, not inferred from memory
- JellyfinSecurity inertness: LOW — genuinely unresolved from documentation; flagged as requiring a live e2e check, not assumed either way
- Pitfalls: HIGH — the InMemory/`ExecuteUpdateAsync` limitation is a confirmed, citable, unfixed upstream issue; the constructor/interface pitfalls are reflection-verified

**Research date:** 2026-09-19
**Valid until:** 30 days for the Jellyfin/EF Core package facts (stable, pinned versions); re-verify the JellyfinSecurity inertness question at implementation time regardless of elapsed time, since it depends on live plugin behavior, not documentation.
