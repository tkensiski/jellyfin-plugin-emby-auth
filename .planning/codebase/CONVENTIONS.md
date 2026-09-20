---
last_mapped_commit: fb0fd999638d88fabb37bd9d449c23226a5b2f8b
---

# Coding Conventions

<!-- refreshed: 2026-09-20 -->

**Analysis Date:** 2026-09-20

**Scope:** commit `fb0fd99` on `gsd/phase-04-emby-traffic-under-load-and-failure`.

## Naming Patterns

**Files:**
- PascalCase file names matching the main type: `EmbyAuthenticationProvider.cs`, `AccountAccessPolicy.cs`, `Configuration/PluginConfiguration.cs`, `Api/EmbyAuthController.cs`.
- Supporting types colocated: `EmbyClient.cs` holds `EmbyLogin` and `EmbyUser` (lines 21, 28); `LoginDecision.cs` holds `JellyfinAccount` and `LoginAction` (lines 10, 15); `PluginConfiguration.cs` holds enums `MigrationMode` and `AccountAccess` (lines 9, 31).
- Subdirectories in `src/Jellyfin.Plugin.EmbyAuth/`: `Configuration/` (namespace `Jellyfin.Plugin.EmbyAuth.Configuration`) and `Api/` (namespace `Jellyfin.Plugin.EmbyAuth.Api`).
- Shell scripts: kebab-case or lowercase (`scripts/dev-env.sh`, `scripts/package.sh`). E2E files: `NN-topic.bats`.

**Types:**
- PascalCase: classes, records, enums.
- `sealed record` for immutable data types: `EmbyLogin`, `EmbyUser`, `EmbyAuthSettings`, `EmbyLoginMethodUser`.
- Private nested records for JSON bodies and internal data structures.

**Methods:**
- PascalCase. Async methods end with `Async` (`GetStatusAsync`, `CreateAccountAsync`, `MoveAsync`), except where a Jellyfin interface sets the name (`Authenticate`, `ChangePassword`, `OnEvent`).
- `Try` prefix for validation methods with `out` parameters: `TryCreate`.
- Log methods: `Log` plus event name (`LogBlankPasswordRefused`, `LogSettingsInvalid`).
- Public method parameters: camelCase (`username`, `password`, `cancellationToken`).

**Fields and constants:**
- Private instance fields: `_camelCase` with underscore prefix.
- `static readonly` and constants: PascalCase (`RequestTimeout`, `CacheDuration`, `InvalidLogin`).
- Primary constructor parameters: camelCase, used directly without copying to a field.

**Namespaces:**
- Root: `Jellyfin.Plugin.EmbyAuth`.
- Tests: `Jellyfin.Plugin.EmbyAuth.Tests`.
- File-scoped declarations: `namespace Jellyfin.Plugin.EmbyAuth;`.

## Code Style

**Project Configuration:**
- Both projects set in `Directory.Build.props`: `TargetFramework` `net10.0`, `Nullable` `enable`, `TreatWarningsAsErrors` `true`, version `1.0.0.0`.
- Only the plugin project (`src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj`) sets `GenerateDocumentationFile` `true` and `AnalysisMode` `AllEnabledByDefault` (line 6).
- No `ImplicitUsings`, no `global using`, no `using` aliases.

**C# Features:**
- Primary constructors for dependency injection.
- Positional records.
- Collection expressions: `[]`.
- `System.Threading.Lock` for synchronization.
- Pattern matching with `is ... or ...` in exception filters.
- Raw string literals in tests and JSON bodies.

**Formatting:**
- `mise run lint` runs `dotnet format Jellyfin.Plugin.EmbyAuth.slnx --verify-no-changes` (`.mise.toml:21`).
- Pre-commit hook `lint` runs this task when `.cs`, `.csproj`, `.slnx`, `.props`, `.sh`, `.bash`, `.bats`, `.yml`, `.yaml` files, or `.mise.toml`, change.
- No `.editorconfig` file — `dotnet format` uses defaults.
- Shell files indent with tabs (verified: no 4-space indentation in scripts).

**Analyzer Rules (Warnings are Errors):**
- `AnalysisMode` `AllEnabledByDefault` enables all Microsoft analyzers.
- `TreatWarningsAsErrors` `true` in `Directory.Build.props` makes warnings build failures.
- **Suppress only with a `Justification`** attribute. Example: `[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Jellyfin saves plugin settings with XmlSerializer, which cannot serialize System.Uri.")]` (`PluginConfiguration.cs:57`).
- A suppression without justification is a compiler error.

**Imports:**
- `using` directives before the file-scoped namespace.
- Order: `System.*` first, then all others alphabetically (`Jellyfin.*`, `MediaBrowser.*`, `Microsoft.*`).
- Do not import the root namespace. Use full type names for root-namespace types in some files (e.g., `Api/EmbyAuthController.cs` uses `EmbyVerifiedPasswords` and `EmbyLoginMethodUsers` without `using`).

**Visibility:**
- Types are `internal` by default: `EmbyClient`, `EmbyUserDirectory`, `EmbyAuthenticationProvider`, `LoginDecision`, etc.
- `public` types: `EmbyAuthPlugin`, `PluginServiceRegistrator`, `PluginConfiguration`, scheduled task types, `EmbyAuthController`, and API response types. Jellyfin discovers scheduled tasks via `Assembly.GetExportedTypes()`, so tasks and their constructor dependencies must be public.
- `InternalsVisibleTo Include="Jellyfin.Plugin.EmbyAuth.Tests"` grants tests access to internal types.
- Most classes are `sealed`.
- Stateless logic lives in `internal static class` types: `LoginDecision`, `AccountAccessPolicy`, `DefaultLoginMethod`, `EmbyLoginMethodUsers`.

## Documentation

**XML Documentation (in `src/`):**
- Every public and internal type and member has a `<summary>`.
- `<param>`, `<returns>`, `<remarks>`, `<exception>` where applicable.
- Private helpers and log methods do not document.
- `<inheritdoc />` on interface and base members.
- Positional records document parameters with `<param>` on the record itself.
- Summaries are plain sentences using `<see cref="..."/>` and `<c>` for code.

**Comments (rare):**
- Three line comments in `src/`, each explaining behavior the code cannot show:
  - `EmbyClient.cs:62`: Emby rejects chunked request bodies; send buffered bodies with `Content-Length`.
  - `EmbyClient.cs:151`: `ReadFromJsonAsync` throws `InvalidOperationException` for unsupported charsets.
  - `EmbyAuthenticationProvider.cs:185`: Why a failed account save deletes the new account in a finally block.
- Jellyfin and Emby behavior is documented in `.claude/rules/plugin.md`, not in code comments.
- Shell scripts and workflows start with a header comment (purpose, usage).
- Helper functions have a one-line usage comment: `# name ARGS -> result`.

**README and Docs:**
- `README.md` stays short (install, configure, migrate).
- Details live in `docs/`: `how-it-works.md`, `settings.md`, `migration.md`, `development.md`.
- Keep `README.md` and `docs/` accurate when behavior changes.

## Error Handling

**Login Failures:**
- Expected failures throw `AuthenticationException`. Jellyfin catches only this exception; others become HTTP 500.
- Message is always the constant `InvalidLogin` ("Invalid username or password"), so the response does not leak the reason.
- **Exception:** Invalid settings throw with the problem text (e.g., "Emby server URL is not valid"). Settings never repeat configured values (URL could contain credentials).
- Cause goes in as the inner exception: `throw new AuthenticationException(InvalidLogin, ex)`.

**Catch Clauses:**
- No bare `catch`. Name the exception type or use a `when` filter:
  - `catch (ArgumentException ex)` — specific type.
  - `catch (Exception ex) when (IsUnreachable(ex, cancellationToken))` — predicate.
  - `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)` — pattern.

**Argument Validation:**
- Public and interface methods start with `ArgumentNullException.ThrowIfNull(parameter)`.
- `ArgumentException.ThrowIfNullOrEmpty` for non-empty strings.
- Guard clauses with early `throw` or `return`.

**Failure Results (instead of exceptions):**
- `EmbyClient` returns `null` for any failed Emby call (rejected status, timeout, unreadable response). Logs the cause. Callers turn `null` into a refused login or unavailable status.
- Emby sign-out failure only logs a warning; the login succeeds.
- `EmbyVerifiedPasswords` logs file I/O errors and continues.
- `EmbyAuthSettings.TryCreate` returns `false` with a problem message.
- **Cleanup on failure:** `CreateAccountAsync` deletes a new account in a `finally` block if the password save fails.

**API (Settings Page):**
- `EmbyAuthController` has no `try`/`catch`; queues the migration task and returns 204.
- The settings page catches failed API calls and shows a message pointing to the Jellyfin log.

## Logging

**Framework:**
- Source-generated `[LoggerMessage]` methods: `private static partial void Log...(ILogger logger, ...)`.
- Logger injected as `ILogger<T>` in the constructor.
- `EmbyAuthController` and `EmbyLoginMethodUsers` do not log.

**Message Format:**
- Named placeholders: `{Username}`, `{EmbyServerUrl}`, `{StatusCode}`. No string interpolation.
- Full sentences. Where an administrator can act, the message says what to do:
  - "Make sure that the Emby API key in the plugin settings is correct."
  - "Set it in Dashboard > Plugins > Emby Auth."
  - "The user must log in once while Emby runs, or an administrator must set a new password."
- Exception goes to the `exception` parameter, not the message text.

**Log Levels (as used in `src/`):**
- **Error:** Plugin cannot do its job. Settings invalid, account create/save failed, fingerprint file I/O failed.
- **Warning:** Emby unreachable/refused, unreadable response, failed sign-out, refused administrator, account conflict, unavailable user list.
- **Information:** Normal events and ordinary refusals. Blank password, password not sent, Emby rejected login, account created, user moved, migration task results.
- **Debug and Trace:** Not used in production code.

**Secrets Rule (CRITICAL):**
- **Never put a password or the API key in a log or exception message.** (`CLAUDE.md:46`)
- `EmbyClient` states this on the logger parameter documentation.
- User names and Emby server URLs are logged (URL cannot contain credentials; settings validation rejects URLs with user info).
- Unit tests assert no secrets in logs (`EmbyClientTests.cs:36-43`).
- E2E test `90-jellyfin-log.bats` scans the whole Jellyfin Debug log for password patterns.

## Function Design

**Size:**
- Longest method: `EmbyAuthenticationProvider.Authenticate` (54 lines).
- Logic with distinct rules goes in separate types: `LoginDecision`, `AccountAccessPolicy`, `DefaultLoginMethod`, `EmbyLoginMethodUsers`.

**Parameters:**
- Max 5 positional parameters on a method. Larger constructors use dependency injection (up to 6 injected services in `EmbyAuthenticationProvider`).
- Primary constructors for short parameter lists.

## Project Rules

From `CLAUDE.md` and `.claude/rules/plugin.md`:

- **Test-first:** Write the test, watch it fail, then implement.
- **E2E required:** Any change depending on Jellyfin or Emby behavior needs an end-to-end test.
- **Pre-commit hooks:** Run `prek run` before committing (calls `mise run lint` and `mise run test`).
- **No secrets in logs or exceptions:** Hard rule.
- **Warnings are errors:** `AnalysisMode AllEnabledByDefault`; suppress only with `Justification`.
- **Exact versions:** Pin all dependency versions; look up current stable before bumping.
- **Jellyfin version bumps:** Change `Jellyfin.Controller`, `Jellyfin.Model`, `jellyfin/jellyfin` image tag in `e2e/compose.yaml`, and target framework together.
- **Documentation accuracy:** Keep `README.md` short and `docs/` detailed. Update when behavior changes.

---

*Convention analysis: 2026-09-20*
