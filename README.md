# Jellyfin Emby Auth plugin

This plugin moves users from Emby to Jellyfin without a password reset. It adds a Jellyfin login method named **Emby**, which checks the first Jellyfin login of each user against an Emby server. Then the plugin saves the password in Jellyfin and moves the user to Jellyfin's **Default** login method. After that login, Jellyfin does not need Emby for that user.

**Status:** tested with Jellyfin 12.1.0 and Emby 4.10.0.40 in local containers only. Not tested on a production server. Not published to a plugin repository.

## How a login works

For a user on the Emby login method, or a user name that has no Jellyfin account:

1. Jellyfin sends the user name and password to Emby (`POST /Users/AuthenticateByName`).
2. If Emby refuses the login, or if Jellyfin cannot connect to Emby, Jellyfin refuses the login.
3. If Emby accepts the login, the plugin ends the Emby session that the login opened. Then it finds the Jellyfin account that has the user name Emby returned:
   - If no account exists, the plugin creates one. The account gets Jellyfin's default permissions for a new user and is not an administrator.
   - If the account uses the Emby login method, the plugin uses it.
   - If the account uses a different login method, Jellyfin refuses the login. Thus an Emby password cannot open an account on another login method, for example the Jellyfin administrator account.
4. The plugin saves a Jellyfin password hash on the account.
5. After Jellyfin completes the login, the plugin moves the user to the Default login method. From then on, Jellyfin checks the saved password and does not contact Emby for that user.

If the Emby account has no password, the plugin does not save a password. The user stays on the Emby login method, and the Jellyfin log tells you to set a password.

The plugin does not change accounts that are already on the Default login method.

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

Do this for Jellyfin accounts that you create before the first login of the user, for example so that a watch-history sync has an account to write to.

Set the login method of the account to **Emby** on the user's profile page in the dashboard. Alternatively, set `AuthenticationProviderId` to `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider` in `POST /Users/{userId}/Policy`.

The next login of that user must use the Emby password. If the Emby account has a password, Jellyfin refuses a blank password.

## Shut down Emby

1. Find the users who are still on the Emby login method. In `GET /Users`, these users have `Policy.AuthenticationProviderId` set to `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider`.
2. Ask these users to log in to Jellyfin once while Emby runs. Emby users who have no Jellyfin account yet must also log in once.
3. For a user who cannot log in before the shutdown, or whose Emby account has no password: set the login method to **Default**, then set a password for the user.
4. Shut down Emby. You can then remove the plugin.

## Limits

- After the move to Default, a password change on Emby does not change the Jellyfin password.
- Jellyfin refuses a password change for a user on the Emby login method. Change the password on Emby, or set the user's login method to Default first.
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
