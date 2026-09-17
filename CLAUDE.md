# Jellyfin Emby Auth plugin

A Jellyfin authentication provider plugin. It checks passwords against Emby and saves a Jellyfin password hash on each accepted login. See `README.md` for behavior.

## Layout

- `src/Jellyfin.Plugin.EmbyAuth/` — the plugin.
  - `LoginDecision.cs` holds the account rules as a pure function.
  - `EmbyClient.cs` is the only code that talks to Emby.
  - `EmbyAuthenticationProvider.cs` connects both to Jellyfin's `IUserManager`.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/` — xUnit v3 unit tests for `LoginDecision` and `EmbyClient`. The tests replace HTTP with a stub `HttpMessageHandler`.
- `e2e/` — bats tests against real Emby and Jellyfin containers (`compose.yaml`). The tests run in file order and share one pair of servers.

## Conventions

- `.mise.toml` pins the tools. Package and image versions are exact. Look up the current stable version before a bump.
- The target Jellyfin version sets `Jellyfin.Controller`/`Jellyfin.Model`, the `jellyfin/jellyfin` image tag, and the target framework together. Change all three at once.
- Warnings are errors, with `AnalysisMode` `AllEnabledByDefault` on the plugin project. Fix the warning. Suppress a warning only with a `Justification`.
- Write the test first. Every behavior change needs a unit test, or an e2e test when it depends on Jellyfin or Emby behavior.
- Log and exception messages must never contain a password. `e2e` test 10 checks the Jellyfin log.
- `dotnet test` uses Microsoft.Testing.Platform (`global.json`), so pass `--solution`.
- Run `prek run` before a commit (dotnet format, build and unit tests, shellcheck). Run `bats e2e/emby-auth.bats` for any change to `src/`.

## Facts the design depends on

- Jellyfin tries every login method for an unknown user name. For a known user, Jellyfin tries only the assigned method (`UserManager.GetAuthenticationProviders`).
- Jellyfin catches only `AuthenticationException` from a login method (`UserManager.AuthenticateWithProvider`). Any other exception escapes the login request.
- After a login, Jellyfin saves only some columns. The provider must call `IUserManager.UpdateUserAsync` to save the password hash.
- `IUserManager` depends on all login methods. The provider resolves it from `IServiceProvider` at login time, not in its constructor.
- Emby answers HTTP 400 to a chunked request body. `EmbyClient` sends a body with a `Content-Length`.
