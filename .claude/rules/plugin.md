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
- Quick Connect (`SessionManager.AuthenticateDirect`) publishes the same event without a password check. Only users in `VerifiedLogins` move.
- `EventManager` logs and ignores an exception from an event consumer. If the move fails, the login still succeeds.
- `MoveToDefaultLoginMethod` changes one column with `ExecuteUpdateAsync`, only while the user is on the Emby login method. A full `UpdateUserAsync` there could overwrite concurrent changes, such as an admin disabling the user.
- `UserManager.ChangePassword` calls the assigned login method, then saves the user. An empty password means a reset.

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

## Settings

- Jellyfin saves plugin settings with `XmlSerializer`, which cannot serialize `System.Uri`. Keep URL settings as strings, and validate them in `EmbyAuthSettings`.
- Settings messages never repeat the configured values, because the URL can contain credentials.
