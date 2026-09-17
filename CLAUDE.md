# Jellyfin Emby Auth

Jellyfin 12.1 authentication plugin in C# (.NET 10). It checks Jellyfin logins against Emby, saves a Jellyfin password hash, and moves users to Jellyfin's Default login method, at once or when an administrator runs the migration task. `README.md` describes the behavior, the settings, and the admin procedures.

## Commands

- `mise install` — install the pinned tools (`.mise.toml`).
- `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` — build with warnings as errors, then run the unit tests. `--solution` is required because `global.json` selects Microsoft.Testing.Platform.
- `bats e2e` — run the end-to-end tests against Emby and Jellyfin containers. Needs Docker. `bats e2e/NN-topic.bats` runs one file.
- `prek run` — run the pre-commit checks: `dotnet format`, build and unit tests, shellcheck.

## Layout

- `src/Jellyfin.Plugin.EmbyAuth/`
  - `EmbyAuthenticationProvider.cs` — the login method. Connects the parts below to Jellyfin's `IUserManager`.
  - `Configuration/PluginConfiguration.cs` — the settings, including the `MigrationMode` and `AccountAccess` enums.
  - `EmbyAuthSettings.cs` — settings validation.
  - `LoginDecision.cs` — the account rules, as a pure function.
  - `AccountAccessPolicy.cs` — applies `AccountAccess` to an account.
  - `EmbyClient.cs` — the only code that sends requests to Emby.
  - `EmbyUserDirectory.cs` — the cached Emby user list.
  - `EmbyVerifiedPasswords.cs` — the file of fingerprints of password hashes that Emby verified.
  - `DefaultLoginMethod.cs` — the single-column move to Default.
  - `MoveToDefaultLoginMethod.cs` — the move after a login, in `MoveAfterFirstLogin` mode.
  - `MoveEmbyUsersToDefaultTask.cs` — the migration task.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/` — xUnit v3 unit tests. `TestDoubles.cs` has the HTTP stub, the manual clock, and the capturing logger.
- `e2e/` — bats tests in independent `NN-topic.bats` files, `setup_suite.bash` (shared servers and Emby users), Docker Compose file, and the logging proxy for Emby.

## Rules

- Write the test first. Then break the code once and watch the test fail.
- A change that depends on Jellyfin or Emby behavior needs an end-to-end test. Run `bats e2e` for every change to `src/`.
- Run `prek run` before each commit.
- Never put a password or the API key in a log or exception message.
- Warnings are errors, and the plugin project uses `AnalysisMode` `AllEnabledByDefault`. Fix a warning. Suppress it only with a `Justification`.
- Pin exact versions. Look up the current stable version before a bump.
- A Jellyfin version bump changes three pins together: `Jellyfin.Controller` and `Jellyfin.Model`, the `jellyfin/jellyfin` image tag in `e2e/compose.yaml`, and the target framework.
- Keep `README.md` accurate when behavior changes.

Path-scoped rules add details: `.claude/rules/plugin.md` for `src/` and `tests/`, and `.claude/rules/e2e.md` for `e2e/`.
