---
paths:
  - "src/**"
  - "tests/**"
---

# Jellyfin and Emby behavior that the plugin depends on

Line references are to Jellyfin v12.1 (`Jellyfin.Server.Implementations/Users/UserManager.cs` unless stated).

## Jellyfin login flow

- For a known user name, Jellyfin calls only the login method assigned to the user. For an unknown name, it calls every enabled login method (`GetAuthenticationProviders`).
- Jellyfin calls login methods inside a lock. All logins for unknown names share one lock key, `Guid.Empty`. Keep Emby calls short, and skip them when the user list rules out the name.
- Jellyfin catches only `AuthenticationException` from a login method (`AuthenticateWithProvider`). Any other exception escapes the login request and becomes an HTTP 500. Convert expected failures to `AuthenticationException`.
- `IUserManager` depends on all login methods. Resolve it from `IServiceProvider` at login time, never in a constructor.
- After a login, Jellyfin saves only some columns. Save the password hash with `IUserManager.UpdateUserAsync`.
- After a successful login, `AuthenticateUser` sets the login method of the user to the method that accepted the login. So the provider cannot move the user to Default. `MoveToDefaultLoginMethod` does it on `AuthenticationResultEventArgs`, which `SessionManager` publishes after the login completes.
- Quick Connect (`SessionManager.AuthenticateDirect`) publishes the same event without a password check. A user moves only if `EmbyVerifiedPasswords` matches the saved hash.
- `EventManager` logs and ignores an exception from an event consumer. If the move fails, the login still succeeds.
- `DefaultLoginMethod.MoveAsync` changes one column with `ExecuteUpdateAsync`, only while the user is on the Emby login method and still has the verified hash. A full `UpdateUserAsync` there could overwrite concurrent changes, such as an admin disabling the user.
- Jellyfin discovers scheduled tasks with `Assembly.GetExportedTypes()` (`ApplicationHost`). So `MoveEmbyUsersToDefaultTask` and every type in its constructor must be public. Other plugin types stay internal.
- `UserManager.ChangePassword` calls the assigned login method, then saves the user. An empty password means a reset.

## Verified passwords

- `EmbyVerifiedPasswords` stores a SHA-256 fingerprint of each hash that Emby verified, never the hash. Record it only after Emby accepts a login and the hash is saved.
- Every path that moves a user to Default through the plugin, or that accepts a saved password without Emby, must check `EmbyVerifiedPasswords.Matches`. Otherwise a password that an administrator set on a pre-created account would work.
- `ChangePassword` with a new password moves the user to Default directly, because the administrator or user chose that password in Jellyfin.

## Blank passwords

- The Default login method lets anyone log in to an account without a password by sending a blank password (`DefaultAuthenticationProvider`).
- `CreateUserAsync` commits the new account on the Default login method without a password. The provider computes the hash first, then saves it immediately, and deletes the account if that save fails.
- Never leave or move a user to Default without a password.

## Emby

- Emby answers HTTP 400 to a chunked request body. Send a body with a `Content-Length` (`StringContent`, not `JsonContent`).
- `GET /Users` with the `X-Emby-Token` header returns every user with `Policy.IsDisabled` and `Policy.EnableRemoteAccess`.
- The `AuthenticateByName` response includes `User.Policy.EnableRemoteAccess`.
- Emby checks remote access against the address of the caller, which is the Jellyfin server (measured on Emby 4.10.0.40 in the code review). So the plugin copies the setting to the Jellyfin account.
- Emby 4.10.0.40 accepted a typed name with a trailing space for the user without the space (measured in the code review). So the plugin requires the exact Emby user name, ignoring case only, before it sends a password.
- `POST /Sessions/Logout` with the session token ends the session and revokes the token.

## API and settings page

- Every plugin API controller uses `[Authorize(Policy = Policies.RequiresElevation)]` (`MediaBrowser.Common.Api`), so only administrators can call it. Keep an e2e test that a regular user gets 403.
- The settings page calls the plugin API with `ApiClient.getJSON(ApiClient.getUrl(...))` and `ApiClient.ajax(...)`. Jellyfin serializes the responses with PascalCase property names.
- Build page content from user names with `textContent`, never `innerHTML`.
- In Jellyfin 12.1, the scheduled tasks page is **Dashboard > Advanced > Scheduled Tasks**.

## Settings

- Jellyfin saves plugin settings with `XmlSerializer`, which cannot serialize `System.Uri`. Keep URL settings as strings, and validate them in `EmbyAuthSettings`.
- Settings messages never repeat the configured values, because the URL can contain credentials.
