# Coding Conventions

**Analysis Date:** 2026-09-16

**Scope:** commit `ecee1ed` on `main`.

## Naming Patterns

**Files:**
- PascalCase file names that match the main type of the file: `EmbyAuthenticationProvider.cs`, `AccountAccessPolicy.cs`, `Configuration/PluginConfiguration.cs`, `Api/EmbyAuthController.cs`.
- A file also holds the small types that its main type uses or returns: `EmbyClient.cs` holds `EmbyLogin` and `EmbyUser` (`EmbyClient.cs:21`, `:28`); `EmbyUserDirectory.cs` holds `EmbyUserStatus` (`EmbyUserDirectory.cs:13`); `LoginDecision.cs` holds `JellyfinAccount` and `LoginAction` (`LoginDecision.cs:10`, `:15`); `PluginConfiguration.cs` holds the `MigrationMode` and `AccountAccess` enums (`PluginConfiguration.cs:9`, `:31`); `EmbyLoginMethodUsers.cs` holds the `EmbyLoginMethodUser` record (`EmbyLoginMethodUsers.cs:18`); `Api/EmbyAuthController.cs` holds the response records `MigrationStatus` and `MigrationUser` (`EmbyAuthController.cs:66`, `:76`).
- Two subdirectories in `src/Jellyfin.Plugin.EmbyAuth/`: `Configuration/` (namespace `Jellyfin.Plugin.EmbyAuth.Configuration`) and `Api/` (namespace `Jellyfin.Plugin.EmbyAuth.Api`, `EmbyAuthController.cs:14`).
- Shell scripts use kebab-case or lowercase names: `scripts/dev-env.sh`, `scripts/package.sh`. E2E test files use `NN-topic.bats` (`CLAUDE.md:37`).

**Types:**
- PascalCase for classes, records, and enums.

**Methods:**
- PascalCase. Async methods end with `Async` (`GetStatusAsync`, `CreateAccountAsync`, `MoveAsync`, `ListAsync`), except where a Jellyfin interface sets the name — `Authenticate` (`EmbyAuthenticationProvider.cs:60`), `ChangePassword` (`EmbyAuthenticationProvider.cs:125`), `OnEvent` (`MoveToDefaultLoginMethod.cs:31`) — and the API action `GetMigrationStatus` (`Api/EmbyAuthController.cs:39`).
- `Try` prefix with `out` parameters for validation: `EmbyAuthSettings.TryCreate` (`EmbyAuthSettings.cs:23`).
- Log methods: `Log` plus the event: `LogBlankPasswordRefused`, `LogSettingsInvalid` (`EmbyAuthenticationProvider.cs:227-252`).

**Fields and constants:**
- Private instance fields: `_camelCase` (`_snapshot` at `EmbyUserDirectory.cs:46`, `_lock` at `EmbyVerifiedPasswords.cs:22`).
- Constants and `static readonly` fields: PascalCase (`InvalidLogin` at `EmbyAuthenticationProvider.cs:36`, `RequestTimeout` at `EmbyClient.cs:40`, `CacheDuration` at `EmbyUserDirectory.cs:39`).
- Primary constructor parameters: camelCase, used directly without a copy into a field (`EmbyClient.cs:35`, `Api/EmbyAuthController.cs:26-29`). Three classes use a classic constructor instead: `EmbyVerifiedPasswords` and `MoveEmbyUsersToDefaultTask` copy their parameters into `private readonly` `_` fields (`EmbyVerifiedPasswords.cs:20-34`, `MoveEmbyUsersToDefaultTask.cs:21-39`), and `EmbyAuthPlugin` passes its parameters to the base class (`EmbyAuthPlugin.cs:22-26`).

**Namespaces:**
- Root: `Jellyfin.Plugin.EmbyAuth`. Tests: `Jellyfin.Plugin.EmbyAuth.Tests`.
- File-scoped: `namespace Jellyfin.Plugin.EmbyAuth;`.

## Code Style

**Project settings:**
- `Directory.Build.props` applies to both projects: `TargetFramework` `net10.0`, `Nullable` `enable`, `TreatWarningsAsErrors` `true`, version `1.0.0.0`.
- Only the plugin project sets `GenerateDocumentationFile` `true` and `AnalysisMode` `AllEnabledByDefault` (`src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj:5-6`). The test project does not set them.
- No `ImplicitUsings`, no `global using`, no `using` aliases.

**C# features in use:**
- Primary constructors (`EmbyClient.cs:35`, `EmbyAuthenticationProvider.cs:27-33`, `Api/EmbyAuthController.cs:26-30`).
- Positional records (see Records below).
- Collection expressions: `[]` (`EmbyAuthPlugin.cs:45-52`, `EmbyVerifiedPasswords.cs:97`, `MoveEmbyUsersToDefaultTask.cs:54`).
- The `System.Threading.Lock` type (`EmbyVerifiedPasswords.cs:22`).
- `is ... or ...` patterns in exception filters (`EmbyAuthenticationProvider.cs:161`).
- Raw string literals in tests (`EmbyClientTests.cs:28`).

**Formatting:**
- `mise run lint` runs `dotnet format Jellyfin.Plugin.EmbyAuth.slnx --verify-no-changes` (`.mise.toml:18-21`). The pre-commit `lint` hook runs that task when a `.cs`, `.csproj`, `.slnx`, `.props`, `.sh`, `.bash`, `.bats`, `.yml`, or `.yaml` file, or `.mise.toml`, changes (`.pre-commit-config.yaml:4-9`). The CI `lint` job runs the same task (`.github/workflows/ci.yml:19-32`).
- The repo has no `.editorconfig` (measured with `fd -H`), so `dotnet format` uses its default rules.
- Shell files indent with tabs (measured: no 4-space indented lines in `scripts/*.sh` or `tests/scripts/package.bats`). `shfmt -d e2e scripts tests/scripts` checks their format (`.mise.toml:23`).

**Analyzer warnings:**
- Warnings are build errors. Fix a warning. Suppress it only with a `Justification` (`CLAUDE.md:47`).
- Example: `[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Jellyfin saves plugin settings with XmlSerializer, which cannot serialize System.Uri.")]` at `PluginConfiguration.cs:57`.

**Records:**
- `sealed record` for immutable data: `EmbyLogin`, `EmbyUser` (`EmbyClient.cs:21`, `:28`), `JellyfinAccount` (`LoginDecision.cs:10`), `EmbyAuthSettings` (`EmbyAuthSettings.cs:14`), `EmbyLoginMethodUser` (`EmbyLoginMethodUsers.cs:18`), and the API responses `MigrationStatus` and `MigrationUser` (`Api/EmbyAuthController.cs:66`, `:76`).
- Private nested records for Emby JSON bodies (`EmbyClient.cs:209-223`) and for the cached user list (`EmbyUserDirectory.cs:88`).
- JSON property names for Emby are explicit: `[property: JsonPropertyName("Pw")]` (`EmbyClient.cs:211`). The API response records have no attributes; Jellyfin serializes them with PascalCase property names (`.claude/rules/plugin.md:49`).

## Import Organization

**Order:**
- `using` directives come before the file-scoped namespace.
- `System.*` first. Then all other namespaces in alphabetical order (`Jellyfin.*`, `MediaBrowser.*`, `Microsoft.*`). Examples: `MoveToDefaultLoginMethod.cs:1-10`, `Api/EmbyAuthController.cs:1-12`.
- Files do not import `Jellyfin.Plugin.EmbyAuth`. `Api/EmbyAuthController.cs` uses root-namespace types (`EmbyVerifiedPasswords`, `EmbyLoginMethodUsers`, `MoveEmbyUsersToDefaultTask`) with no `using` for them (`EmbyAuthController.cs:1-12`, `:28`, `:44`, `:57`). Files import `Jellyfin.Plugin.EmbyAuth.Configuration` when they need the settings types.

**Visibility:**
- Types are `internal` by default: `EmbyClient`, `EmbyUserDirectory`, `EmbyAuthenticationProvider`, `EmbyAuthSettings`, `LoginDecision`, `AccountAccessPolicy`, `DefaultLoginMethod`, `MoveToDefaultLoginMethod`, `EmbyLoginMethodUsers`, `EmbyLoginMethodUser`.
- `public` types: `EmbyAuthPlugin`, `PluginServiceRegistrator`, `PluginConfiguration` and its two enums, `MoveEmbyUsersToDefaultTask`, `EmbyVerifiedPasswords`, `EmbyAuthController`, `MigrationStatus`, and `MigrationUser` (`Api/EmbyAuthController.cs:26`, `:66`, `:76`). Jellyfin finds scheduled tasks with `Assembly.GetExportedTypes()`, so the task and every type in its constructor must be public; other plugin types stay internal (`.claude/rules/plugin.md:22`).
- `InternalsVisibleTo Include="Jellyfin.Plugin.EmbyAuth.Tests"` gives the tests access to internal types (`Jellyfin.Plugin.EmbyAuth.csproj:19`).
- Most classes are `sealed`, including `EmbyAuthController` (`EmbyAuthController.cs:26`). `EmbyAuthPlugin`, `PluginServiceRegistrator`, and `PluginConfiguration` are not.
- Stateless logic lives in `internal static class` types: `LoginDecision`, `AccountAccessPolicy`, `DefaultLoginMethod`, `EmbyLoginMethodUsers` (`EmbyLoginMethodUsers.cs:23`).

## Error Handling

**Login failures:**
- An expected login failure throws `AuthenticationException`. Jellyfin catches only that exception from a login method; any other exception becomes an HTTP 500 (`.claude/rules/plugin.md:15`).
- The message is the constant `InvalidLogin`, "Invalid username or password" (`EmbyAuthenticationProvider.cs:36`), so the response does not tell the caller why the login failed. One exception: invalid settings throw with the settings problem text (`EmbyAuthenticationProvider.cs:151-152`). That text never repeats the configured values (`EmbyAuthSettings.cs:21`, `.claude/rules/plugin.md:56`).
- The cause goes in as the inner exception: `throw new AuthenticationException(InvalidLogin, ex)` (`EmbyAuthenticationProvider.cs:178`, `:195`, `:221`).

**Catch clauses:**
- No bare `catch`. A catch names a specific type (`catch (ArgumentException ex)` at `EmbyAuthenticationProvider.cs:175`) or catches `Exception` with a `when` filter:
  - `when (ex is DbUpdateException or ResourceNotFoundException)` (`EmbyAuthenticationProvider.cs:192`)
  - `when (IsUnreachable(ex, cancellationToken))` (`EmbyClient.cs:76`)
  - `when (ex is IOException or UnauthorizedAccessException)` (`EmbyVerifiedPasswords.cs:60`)

**Argument checks:**
- `ArgumentNullException.ThrowIfNull` at the start of public and interface methods (`EmbyClient.cs:52`, `MoveToDefaultLoginMethod.cs:33`, `AccountAccessPolicy.cs:22`, `EmbyLoginMethodUsers.cs:37-38`). `ArgumentException.ThrowIfNullOrEmpty` at `EmbyVerifiedPasswords.cs:43`.
- Guard clauses with an early `throw` or `return` (`EmbyAuthenticationProvider.cs:62-77`).

**Failure results instead of exceptions:**
- `EmbyClient` returns `null` for each failed Emby call: a rejected status, no response, a timeout, or an unreadable body. It logs the cause (`EmbyClient.cs:68-91`, `:117-140`). The callers turn `null` into a refused login (`EmbyAuthenticationProvider.cs:95-96`) or into `EmbyUserStatus.Unavailable` (`EmbyUserDirectory.cs:71-74`).
- A failed Emby sign-out only logs a warning. The login still succeeds (`EmbyClient.cs:165-186`).
- `EmbyVerifiedPasswords` logs file read and write errors at Error level and continues (`EmbyVerifiedPasswords.cs:60-63`, `:107-110`).
- `EmbyAuthSettings.TryCreate` returns `false` and a problem message (`EmbyAuthSettings.cs:23-34`).
- Cleanup on failure: `CreateAccountAsync` deletes the new account in a `finally` block if the save did not complete (`EmbyAuthenticationProvider.cs:185-203`).

**API and settings page:**
- `EmbyAuthController` has no `try`/`catch` (`Api/EmbyAuthController.cs:37-59`). `RunMigration` queues the task with `taskManager.QueueIfNotRunning<MoveEmbyUsersToDefaultTask>()` and returns 204 (`EmbyAuthController.cs:53-59`).
- The Migration section of the settings page catches a failed API call and shows a sentence that points to the Jellyfin log (`Configuration/configPage.html:77-79`, `:102-104`). The settings load and save calls have no `.catch` (`configPage.html:85-93`, `:110-118`); see `CONCERNS.md`.

## Logging

**Framework:**
- Source-generated `[LoggerMessage]` methods, declared `private static partial void Log...(ILogger logger, ...)` at the end of a `partial` class (`EmbyClient.cs:188-207`, `MoveEmbyUsersToDefaultTask.cs:86-90`).
- The logger comes in through the constructor as `ILogger<T>`.
- `EmbyAuthController` and `EmbyLoginMethodUsers` do not log.

**Messages:**
- Message templates with named placeholders (`{Username}`, `{EmbyServerUrl}`, `{StatusCode}`). No string interpolation.
- Full sentences in plain language. Where an administrator can act, the message says what to do: "Make sure that the Emby API key in the plugin settings is correct." (`EmbyClient.cs:191`); "Set it in Dashboard > Plugins > Emby Auth." (`EmbyAuthenticationProvider.cs:251`); "The user must log in once while Emby runs, or an administrator must set a new password." (`MoveEmbyUsersToDefaultTask.cs:86`).
- An exception goes to the log method as its `exception` parameter, not into the message text (`EmbyClient.cs:195`, `:198`).

**Levels, as the code uses them:**
- Error: the plugin cannot do its job. Settings not valid, account create or save failed, fingerprint file read or write failed (`EmbyAuthenticationProvider.cs:245-252`, `EmbyVerifiedPasswords.cs:115-119`).
- Warning: Emby is unreachable or refused the user list request, an unreadable response, a failed sign-out, a refused administrator, an account conflict, an unreadable saved password, the user list is unavailable (`EmbyClient.cs:191-207`, `EmbyAuthenticationProvider.cs:230-240`, `EmbyUserDirectory.cs:85`).
- Information: normal events and ordinary refusals. Blank password, password not sent to Emby, Emby refused the login, account created, user moved to Default, migration task results (`EmbyAuthenticationProvider.cs:227-234`, `EmbyClient.cs:188`, `MoveToDefaultLoginMethod.cs:60`, `MoveEmbyUsersToDefaultTask.cs:86-90`).
- Debug and Trace: not used in `src/`.

**Secrets:**
- Never put a password or the API key in a log or exception message (`CLAUDE.md:46`). `EmbyClient` states this on its logger parameter (`EmbyClient.cs:34`).
- User names and the Emby server URL are logged. The URL cannot contain credentials, because settings validation rejects a URL with user info (`EmbyAuthSettings.cs:55-58`).
- The unit tests check this with `AssertNoSecretsLogged` (`EmbyClientTests.cs:36-43`). The e2e test `e2e/90-jellyfin-log.bats` checks the whole Jellyfin log at Debug level.

## Comments

**When to Comment:**
- Line comments are rare in C#. `src/` has three (measured with `rg '^\s*//[^/]'`), and each gives a cause that the code cannot show:
  - `EmbyClient.cs:62`: Emby answers HTTP 400 to a chunked body.
  - `EmbyClient.cs:151`: `ReadFromJsonAsync` throws `InvalidOperationException` for an unsupported charset.
  - `EmbyAuthenticationProvider.cs:185`: why a failed save deletes the new account.
- The Jellyfin and Emby behavior that the code depends on is in `.claude/rules/plugin.md`, not in code comments.
- Shell scripts and workflows start with a header comment that says what the file does and how to use it (`scripts/package.sh:4-13`, `scripts/dev-env.sh:4-13`, `e2e/setup_suite.bash:2-6`, `.github/workflows/ci.yml:3`, `.github/workflows/release.yml:3-4`). A helper function has a one-line usage comment in the form `# name ARGS -> result` (`e2e/helpers.bash:25`, `:35`, `:90`, `:128`).

**XML Documentation:**
- Every public and internal type and member in `src/` has a `<summary>`, with `<param>`, `<returns>`, `<remarks>`, and `<exception>` where they apply. Private helper methods and log methods have none.
- With `GenerateDocumentationFile` on, the compiler warns about a publicly visible member without an XML comment (CS1591), and warnings are errors.
- `<inheritdoc />` on interface and base members (`EmbyAuthenticationProvider.cs:43`, `EmbyAuthPlugin.cs:33`, `MoveEmbyUsersToDefaultTask.cs:41`).
- Positional records document their parameters with `<param>` on the record (`EmbyLoginMethodUsers.cs:11-18`, `Api/EmbyAuthController.cs:68-76`).
- Summaries describe behavior in plain sentences and use `<see cref="..."/>` and `<c>` (`MoveToDefaultLoginMethod.cs:14-20`, `EmbyClient.cs:49`). Example: `LoginDecision.cs:5-10`.

## Function Design

**Size:**
- The longest method is `EmbyAuthenticationProvider.Authenticate`, 54 lines (`EmbyAuthenticationProvider.cs:60-113`). No repo file sets a size limit.
- Logic with its own rules is in a separate type: `LoginDecision.Decide` (account rules, a pure function), `AccountAccessPolicy` (permissions), `DefaultLoginMethod.MoveAsync` (the one-column update), `EmbyLoginMethodUsers.ListAsync` (the user list that the task and the API share, `CLAUDE.md:34`).

**Parameters:**
- The most parameters on a method in `src/` is 5 (`SavePasswordAsync` at `EmbyAuthenticationProvider.cs:209`). The largest constructor takes 6 injected services (`EmbyAuthenticationProvider.cs:27-33`). No repo file sets a limit.
- `CancellationToken` is the last parameter (`EmbyClient.cs:50`, `EmbyUserDirectory.cs:55`, `DefaultLoginMethod.cs:31`, `EmbyLoginMethodUsers.cs:35`, `Api/EmbyAuthController.cs:39`).
- Named arguments for a literal whose meaning is not clear at the call site: `File.Move(temporaryPath, _filePath, overwrite: true)` (`EmbyVerifiedPasswords.cs:58`); `LoginDecision.Decide("alice", typedAccount: null, embyUserName: "alice", Bridge)` (`LoginDecisionTests.cs:13`).

**Return Values:**
- `bool` plus `out` parameters with `[NotNullWhen]` for validation (`EmbyAuthSettings.cs:23`).
- A nullable result for "no result": `Task<EmbyLogin?>` (`EmbyClient.cs:50`), `Task<IReadOnlyList<EmbyUser>?>` (`EmbyClient.cs:105`).
- An enum for a decision: `LoginAction` from `LoginDecision.Decide`, `EmbyUserStatus` from `EmbyUserDirectory.GetStatusAsync`.
- `bool` for "the change happened": `DefaultLoginMethod.MoveAsync` returns `true` when one row changed (`DefaultLoginMethod.cs:40`).
- `IReadOnlyList<T>` for lists (`EmbyClient.cs:105`, `EmbyLoginMethodUsers.cs:32`, `Api/EmbyAuthController.cs:66`). API actions return `ActionResult<T>` or `ActionResult` (`EmbyAuthController.cs:39`, `:55`).

**Async:**
- Every `await` in `src/` ends with `.ConfigureAwait(false)` (`EmbyClient.cs:67`). The database context uses `await using (dbContext.ConfigureAwait(false))` (`MoveToDefaultLoginMethod.cs:41`, `MoveEmbyUsersToDefaultTask.cs:61`, `Api/EmbyAuthController.cs:42`).
- No `.Result` or `.Wait()` in `src/`.
- A method passes its cancellation token on when it has one (`MoveEmbyUsersToDefaultTask.cs:60-70`, `Api/EmbyAuthController.cs:41-44`). Jellyfin's `Authenticate` and `OnEvent` do not supply a token, so those paths pass `CancellationToken.None` (`EmbyAuthenticationProvider.cs:88`, `:95`; `MoveToDefaultLoginMethod.cs:53`).

## Module Design

**Dependency Injection:**
- `PluginServiceRegistrator.RegisterServices` registers `EmbyClient`, `EmbyUserDirectory`, `EmbyVerifiedPasswords`, and the login method as singletons, `TimeProvider.System` with `TryAddSingleton`, and `MoveToDefaultLoginMethod` as a scoped event consumer (`PluginServiceRegistrator.cs:26-36`). It does not register `EmbyAuthController`.
- Constructor injection. `EmbyUserDirectory` takes a `TimeProvider` and `EmbyClient` takes an `IHttpClientFactory` (`EmbyUserDirectory.cs:34`, `EmbyClient.cs:35`). The tests pass `ManualTimeProvider` and `StubHttpClientFactory` for them. `EmbyAuthController` takes `IDbContextFactory<JellyfinDbContext>`, `EmbyVerifiedPasswords`, and `ITaskManager` (`Api/EmbyAuthController.cs:26-29`).
- `IUserManager` is resolved from `IServiceProvider` at login time, never in a constructor, because `IUserManager` depends on all login methods (`.claude/rules/plugin.md:16`, `EmbyAuthenticationProvider.cs:106`).
- Settings are read on each login from `EmbyAuthPlugin.Instance?.Configuration`, validated into an `EmbyAuthSettings` record, and passed down as a parameter (`EmbyAuthenticationProvider.cs:144-153`, `EmbyUserDirectory.cs:55`).

**Database updates:**
- An update that must not overwrite concurrent changes uses one conditional `ExecuteUpdateAsync` statement, not `UpdateUserAsync` (`DefaultLoginMethod.cs:31-41`, `.claude/rules/plugin.md:21`).
- Queries project only the columns they need with `Select` (`EmbyLoginMethodUsers.cs:42`, `MoveToDefaultLoginMethod.cs:45`).

**API:**
- Every plugin API controller has `[Authorize(Policy = Policies.RequiresElevation)]`, so only administrators can call it, and an e2e test checks that a regular user gets 403 (`.claude/rules/plugin.md:48`, `Api/EmbyAuthController.cs:23`, `e2e/30-migration-modes.bats:95-110`).
- Attributes on the controller: `[ApiController]`, `[Route("EmbyAuth")]`, `[Produces(MediaTypeNames.Application.Json)]`; on each action, an HTTP verb attribute and `[ProducesResponseType]` (`EmbyAuthController.cs:22-25`, `:37-38`, `:53-54`).
- The API reuses the task's logic instead of copying it: the status comes from `EmbyLoginMethodUsers.ListAsync`, and "run" queues the scheduled task (`EmbyAuthController.cs:44`, `:57`).

**Settings page (`Configuration/configPage.html`):**
- Plain JavaScript in one inline `<script>`, in ES5 style with `var` and `function` (`configPage.html:58-123`).
- Calls the plugin API with `ApiClient.getJSON(ApiClient.getUrl(...))` and `ApiClient.ajax(...)` (`configPage.html:66`, `:99`; `.claude/rules/plugin.md:49`), and reads PascalCase response properties such as `status.Users` and `user.ReadyToMove` (`configPage.html:67`).
- Builds content from user names with `textContent` and `document.createElement`, never `innerHTML` (`configPage.html:68-76`; `.claude/rules/plugin.md:50`). Measured: `rg innerHTML src` finds nothing.
- Uses Jellyfin's `emby-input`, `emby-select`, and `emby-button` elements, and `Dashboard.showLoadingMsg`/`hideLoadingMsg` (`configPage.html:8`, `:84`, `:92`).

**Shell scripts:**
- Executable scripts start with `set -euo pipefail` (`scripts/dev-env.sh:2`, `scripts/package.sh:2`). Sourced files start with `# shellcheck shell=bash` (`e2e/helpers.bash:1`, `e2e/setup_suite.bash:1`), and a `source` has a `# shellcheck source=` directive (`scripts/dev-env.sh:21`, `e2e/setup_suite.bash:9`).
- A script requires an explicit action argument. Without a known action it prints the usage to stderr and exits with status 2 (`scripts/dev-env.sh:24-26`, `:75-91`; `scripts/package.sh:25-27`, `:107-124`). `tests/scripts/package.bats:20-30` tests this for `package.sh`.
- Functions declare their variables with `local` (`scripts/package.sh:35`, `:41`, `:50`; `e2e/helpers.bash:27`). Script constants are `readonly` and UPPER_CASE (`scripts/package.sh:16-23`).
- Settings that a caller can change are environment variables with a default: `${VAR:-default}` (`scripts/package.sh:21`, `:23`, `:53`; `scripts/dev-env.sh:18-19`; `e2e/helpers.bash:7-8`). The header comment lists them (`scripts/package.sh:10-13`, `scripts/dev-env.sh:12`).
- Error messages go to stderr and say what went wrong and, where possible, what to do (`scripts/package.sh:44`, `e2e/helpers.bash:59`, `:212`).
- Helper conditions use `[[ ]]` (`e2e/helpers.bash:19`, `:69`). Test assertions use `[ ... ]`, for example `[ "$output" = "401" ]` (`e2e/10-login-checks.bats:51`). Inside a loop in a test, use `if [[ ... ]]; then ...; return 1; fi`, not a bare `[[ ]]` (`.claude/rules/e2e.md:25`).
- Scripts must pass `shellcheck -x` and `shfmt -d` (`.claude/rules/e2e.md:25`). `mise run lint` runs `shellcheck -x` on `e2e/*.bash`, `e2e/*.bats`, `scripts/*.sh`, and `tests/scripts/*.bats`, and `shfmt -d` on `e2e`, `scripts`, and `tests/scripts` (`.mise.toml:22-23`), in the pre-commit `lint` hook and the CI `lint` job.

**GitHub Actions workflows:**
- Each CI job runs one mise task, so `mise run <task>` reproduces it locally. When a CI step changes, change the mise task, not only the workflow (`.github/workflows/ci.yml:3`, `CLAUDE.md:16`, `.mise.toml:16`).
- Actions are pinned by full commit SHA with the version in a comment (`ci.yml:24`, `:29`; `release.yml:22`, `:27`). Checkout sets `persist-credentials: false` (`ci.yml:25-26`).
- Workflow-level `permissions: contents: read` (`ci.yml:11-12`, `release.yml:11-12`). Only the release job raises it to `contents: write` (`release.yml:18-19`).
- Every job sets `timeout-minutes` (`ci.yml:21`, `:36`, `:51`, `:69`; `release.yml:17`).
- `${{ }}` values reach `run` scripts through `env:`, not inline in the script (`ci.yml:72-76`, `release.yml:33-35`, `:44-48`).
- `actionlint` and `zizmor --offline .github/workflows` check the workflows (`.mise.toml:24-25`).

---

*Convention analysis: 2026-09-16*
