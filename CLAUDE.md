# Jellyfin Emby Auth plugin

A Jellyfin authentication provider plugin that moves users from Emby to Jellyfin. It checks the first login against Emby, saves a Jellyfin password hash, then moves the user to the Default login method. See `README.md` for behavior.

## Layout

- `src/Jellyfin.Plugin.EmbyAuth/` — the plugin.
  - `EmbyAuthSettings.cs` validates the settings. Its messages never repeat the configured values.
  - `LoginDecision.cs` holds the account rules as a pure function.
  - `EmbyClient.cs` is the only code that talks to Emby.
  - `EmbyUserDirectory.cs` caches the Emby user list, so that the plugin sends a password only for a real Emby user name.
  - `EmbyAuthenticationProvider.cs` connects the parts to Jellyfin's `IUserManager`.
  - `VerifiedLogins.cs` and `MoveToDefaultLoginMethod.cs` move the user to the Default login method after a login that Emby verified.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/` — xUnit v3 unit tests. The tests replace HTTP with a stub `HttpMessageHandler` and the clock with `ManualTimeProvider`.
- `e2e/` — bats tests against real Emby and Jellyfin containers (`compose.yaml`). Jellyfin reaches Emby through an nginx proxy (`emby-proxy.conf`) that logs request bodies, so tests can prove that a password did not reach Emby. The tests run in file order and share one set of servers.

## Conventions

- `.mise.toml` pins the tools. Package and image versions are exact. Look up the current stable version before a bump.
- The target Jellyfin version sets `Jellyfin.Controller`/`Jellyfin.Model`, the `jellyfin/jellyfin` image tag, and the target framework together. Change all three at once.
- Warnings are errors, with `AnalysisMode` `AllEnabledByDefault` on the plugin project. Fix the warning. Suppress a warning only with a `Justification`.
- Write the test first. Every behavior change needs a unit test, or an e2e test when it depends on Jellyfin or Emby behavior. Break the code once to see a new test fail.
- Log and exception messages must never contain a password or the API key. The last test in `e2e/emby-auth.bats` checks the Jellyfin log at Debug level.
- Do not check the Emby activity log in a test. Emby writes the entries some time after the request, so a check that expects no entry passes too early. Use the proxy log (`emby_login_requests`).
- `dotnet test` uses Microsoft.Testing.Platform (`global.json`), so pass `--solution`.
- Run `prek run` before a commit (dotnet format, build and unit tests, shellcheck). Run `bats e2e/emby-auth.bats` for any change to `src/`.

## Facts the design depends on

- Jellyfin tries every login method for an unknown user name. For a known user, Jellyfin tries only the assigned method (`UserManager.GetAuthenticationProviders`).
- Jellyfin calls login methods inside a lock. For an unknown user name, all such logins share one lock key (`Guid.Empty`). Keep Emby calls short and avoid them when the user list rules out the name.
- Jellyfin catches only `AuthenticationException` from a login method (`UserManager.AuthenticateWithProvider`). Any other exception escapes the login request.
- The Default login method lets anyone log in to an account that has no password with a blank password. So the plugin refuses blank passwords, computes the hash before it creates an account, and deletes a new account if the save fails.
- After a login, Jellyfin saves only some columns. The provider must call `IUserManager.UpdateUserAsync` to save the password hash.
- After a successful login, `UserManager.AuthenticateUser` sets the login method of the user to the method that accepted the login. So the provider cannot move the user to Default. `MoveToDefaultLoginMethod` does it in the `AuthenticationResultEventArgs` event, which `SessionManager` publishes after the login completes.
- Quick Connect (`SessionManager.AuthenticateDirect`) publishes the same event without a password check. `VerifiedLogins` limits the move to logins that Emby verified.
- `MoveToDefaultLoginMethod` changes one column with `ExecuteUpdateAsync`, and only while the user is on the Emby login method, so that it does not overwrite concurrent changes.
- `EventManager` logs and ignores an exception from an event consumer. If the move fails, the user stays on the Emby login method and the login still succeeds.
- `UserManager.ChangePassword` calls the assigned login method and then saves the user. The provider sets the hash and moves the user to Default there.
- `IUserManager` depends on all login methods. The provider resolves it from `IServiceProvider` at login time, not in its constructor.
- Emby answers HTTP 400 to a chunked request body. `EmbyClient` sends a body with a `Content-Length`.
- Emby checks remote access against the address of the caller, which is the Jellyfin server. The plugin copies Emby's `Policy.EnableRemoteAccess` from the login response instead.
