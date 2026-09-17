# Jellyfin Emby Auth plugin

This plugin moves users from Emby to Jellyfin without a password reset. It adds a Jellyfin login method named **Emby**, which checks the first Jellyfin login of each user against an Emby server. Then the plugin saves the password in Jellyfin and moves the user to Jellyfin's **Default** login method. After that login, Jellyfin does not need Emby for that user.

**Status:** tested with Jellyfin 12.1.0 and Emby 4.10.0.40 in local containers only. Not tested on a production server. Not published to a plugin repository.

## How a login works

For a user on the Emby login method, or a user name that has no Jellyfin account:

1. The plugin refuses the login without contacting Emby if one of these conditions is true:
   - The password is blank.
   - The Jellyfin account is disabled.
   - No enabled Emby user has exactly the typed name. The match ignores case, and nothing else. The plugin reads the Emby user list with the Emby API key and keeps it for 60 seconds.
2. Jellyfin sends the user name and password to Emby (`POST /Users/AuthenticateByName`). If Emby refuses the login, or if Jellyfin cannot connect to Emby, Jellyfin refuses the login.
3. If Emby accepts the login, the plugin ends the Emby session that the login opened. Then it applies these rules:
   - If the name that Emby returns is not the typed name, ignoring case, Jellyfin refuses the login.
   - If no Jellyfin account has that name, the plugin creates one. The account gets Jellyfin's default permissions for a new user and is not an administrator. If Emby does not allow the user to connect from outside the local network, the account does not allow remote access either.
   - If the account is a Jellyfin administrator, or uses a login method other than Emby, Jellyfin refuses the login. Thus an Emby password cannot open an administrator account or an account on another login method.
   - Otherwise, the plugin uses the account. If Emby does not allow remote access for the user, the plugin turns off remote access on the account. The plugin never turns remote access on.
4. The plugin saves a Jellyfin password hash on the account.
5. After Jellyfin completes the login, the plugin moves the user to the Default login method. From then on, Jellyfin checks the saved password and does not contact Emby for that user.

A Quick Connect login does not move a user to Default, because Emby does not check a password for it.

The plugin does not change accounts that are already on the Default login method.

## Install

1. Build the plugin:

   ```sh
   mise install
   dotnet publish src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj -c Release -o artifacts/plugin
   ```

2. Copy `artifacts/plugin/Jellyfin.Plugin.EmbyAuth.dll` to `<jellyfin config>/plugins/EmbyAuth_1.0.0.0/`.
3. Restart Jellyfin.
4. Create an API key on the Emby server for the plugin.
5. In **Dashboard > Plugins > Emby Auth**, set the Emby server URL, for example `http://emby:8096`, and the Emby API key. You can also send the settings to the API: `POST /Plugins/e973e09a-e8b4-40c1-9be2-8e51342de1f9/Configuration` with `{"EmbyServerUrl": "http://emby:8096", "EmbyApiKey": "<key>"}`.

The plugin sends passwords to the Emby server URL. Use `https`, or an address on a private network. The URL must not contain a user name or password.

Jellyfin administrators can read the API key through the plugin settings API.

## Create a Jellyfin account before the first login

Do this for users who need a Jellyfin account before they log in, for example so that a watch-history sync has an account to write to.

1. Create the account with a long random password. Do not create it without a password, because Jellyfin lets anyone log in to a Default account that has no password.
2. Set the login method of the account to **Emby**. Use the user's profile page in the dashboard, or set `AuthenticationProviderId` to `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider` in `POST /Users/{userId}/Policy`.

The first login of that user must use the Emby password. The random password does not work.

## Shut down Emby

1. Find the users who are still on the Emby login method. In `GET /Users`, these users have `Policy.AuthenticationProviderId` set to `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider`.
2. Ask these users to log in to Jellyfin once while Emby runs. Emby users who have no Jellyfin account yet must also log in once.
3. For a user who cannot log in before the shutdown, set a password in Jellyfin. The plugin saves the password and moves the user to Default.
4. Do step 1 again. Then shut down Emby and remove the plugin.

## Limits

- After the move to Default, a password change on Emby does not change the Jellyfin password.
- An Emby user who has no password cannot log in through the plugin. Set a password in Jellyfin for this user.
- The plugin refuses logins with an Emby Connect email address or any other name that is not exactly the Emby user name.
- A new Emby user can log in through the plugin only after the plugin reads the Emby user list again, up to 60 seconds later. If the plugin cannot read the list, it refuses Emby logins for 30 seconds before it tries again.
- Jellyfin counts each refused login toward the lockout limit of the account, if the account has one. This includes logins that fail because Emby is down.
- If Jellyfin does not allow an Emby user name, the plugin cannot create the account. Rename the user on Emby.
- In a rare timing case, another session of the user can save an older copy of the account and put the user back on the Emby login method. Step 1 of the Emby shutdown finds these users.

## Tests

Unit tests:

```sh
dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx
```

End-to-end tests start Emby, an nginx proxy that logs the requests to Emby, and Jellyfin with Docker. Emby and Jellyfin listen on `127.0.0.1:18096` and `127.0.0.1:28096`. The tests remove the containers after the run:

```sh
bats e2e/emby-auth.bats
```

Set `KEEP_E2E=1` to keep the containers after the run.
