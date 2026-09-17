# Jellyfin Emby Auth plugin

This plugin adds a Jellyfin login method that checks each password against an Emby server. When Emby accepts a password, the plugin saves a copy of it in Jellyfin's own password format.

Use it to run Jellyfin next to Emby with the same logins. Later, you can switch users to Jellyfin's Default login method, and their passwords continue to work.

**Status:** tested with Jellyfin 12.1.0 and Emby 4.10.0.40 in local containers only. Not tested on a production server. Not published to a plugin repository.

## How a login works

For a user whose login method is **Emby**, or a user name that has no Jellyfin account:

1. Jellyfin sends the user name and password to Emby (`POST /Users/AuthenticateByName`).
2. If Emby refuses the login, or if Jellyfin cannot connect to Emby, Jellyfin refuses the login. The plugin never uses the saved copy of the password instead.
3. If Emby accepts the login, the plugin ends the Emby session that the login opened. Then it finds the Jellyfin account that has the user name Emby returned:
   - If no account exists, the plugin creates one. The account gets Jellyfin's default permissions for a new user and is not an administrator.
   - If the account uses the Emby login method, the plugin uses it.
   - If the account uses a different login method, Jellyfin refuses the login. Thus an Emby password cannot open an account on another login method, for example the Jellyfin administrator account.
4. The plugin saves a Jellyfin password hash on the account.

The plugin does not change accounts on the Default login method. Jellyfin checks their passwords as usual.

## Install

1. Build the plugin:

   ```sh
   mise install
   dotnet publish src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj -c Release -o artifacts/plugin
   ```

2. Copy `artifacts/plugin/Jellyfin.Plugin.EmbyAuth.dll` to `<jellyfin config>/plugins/EmbyAuth_1.0.0.0/`.
3. Restart Jellyfin.
4. In **Dashboard > Plugins > Emby Auth**, set the Emby server URL, for example `http://emby:8096`. You can also send the setting to the API: `POST /Plugins/e973e09a-e8b4-40c1-9be2-8e51342de1f9/Configuration` with `{"EmbyServerUrl": "http://emby:8096"}`.

The plugin sends passwords to this URL. Use `https`, or an address on a private network.

## Move existing Jellyfin accounts to the Emby login method

Set the login method of the account to **Emby** on the user's profile page in the dashboard. Alternatively, set `AuthenticationProviderId` to `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider` in `POST /Users/{userId}/Policy`.

The next login of that user must use the Emby password. If the Emby account has a password, Jellyfin refuses a blank password.

## Switch users to Jellyfin only

1. Make sure that each user logged in to Jellyfin at least once while the user was on the Emby login method. A user who did not log in has no saved password. In `GET /Users`, compare the `LastLoginDate` of each user with the date that you set the Emby login method.
2. Set the login method of each user to **Default**. Alternatively, set `AuthenticationProviderId` to `Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider` in `POST /Users/{userId}/Policy`.
3. Users log in with the password of their last successful Jellyfin login on the Emby login method.

## Limits

- Jellyfin refuses a password change for a user on the Emby login method. Change the password on Emby, or set the user's login method to Default first.
- The saved copy of a password changes only when the user logs in to Jellyfin. A password change on Emby does not update the copy until the next Jellyfin login.
- Jellyfin counts each refused login toward the lockout limit of the account, if the account has one. This includes logins that fail because Emby is down.
- If Jellyfin does not allow an Emby user name, the plugin cannot create the account. The Jellyfin log tells you to create the account manually.
- The tests do not cover logins with Emby Connect credentials.

## Tests

Unit tests:

```sh
dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx
```

End-to-end tests start Emby and Jellyfin containers with Docker, on `127.0.0.1:18096` and `127.0.0.1:28096`, then remove them:

```sh
bats e2e/emby-auth.bats
```

Set `KEEP_E2E=1` to keep the containers after the run.
