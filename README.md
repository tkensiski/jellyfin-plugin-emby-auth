# Jellyfin Emby Auth

A Jellyfin plugin that moves users from Emby to Jellyfin without a password reset.

The plugin adds a Jellyfin login method named **Emby**. On the first Jellyfin login of a user, the plugin checks the password against your Emby server. If Emby accepts it, the plugin saves the password in Jellyfin and moves the user to Jellyfin's **Default** login method. From then on, Jellyfin checks that user's password without Emby. When every user has logged in once, you can shut down Emby and remove the plugin.

**Status:** tested only in local containers, with Jellyfin 12.1.0 and Emby 4.10.0.40. Not tested on a production server. Not published to a plugin repository.

## Requirements

- Jellyfin 12.1. The plugin is built against the Jellyfin 12.1.0 packages.
- An Emby server that Jellyfin can reach over HTTP or HTTPS.
- An API key for the plugin on that Emby server.

## How it works

The plugin handles a login in two cases: the Jellyfin account uses the Emby login method, or no Jellyfin account has the typed name. Jellyfin handles all other logins as usual.

1. **Checks before Emby.** The plugin refuses the login, and does not contact Emby, if one of these is true:
   - The password is blank.
   - The Jellyfin account is disabled.
   - No enabled Emby user has exactly the typed name. The match ignores case and nothing else. The plugin reads the Emby user list with the API key and keeps it for 60 seconds.
2. **Emby check.** The plugin sends the name and password to Emby (`POST /Users/AuthenticateByName`). If Emby refuses the login or does not answer within 5 seconds, Jellyfin refuses the login. If Emby accepts, the plugin ends the Emby session that the login opened.
3. **Account rules.** Jellyfin refuses the login if Emby returns a different user name, or if the Jellyfin account is an administrator or uses another login method. Otherwise:
   - If no Jellyfin account exists, the plugin creates one with Jellyfin's default permissions for a new user. The account is not an administrator. It allows remote access only if Emby allows remote access for the user.
   - If the account exists, the plugin uses it. If Emby does not allow remote access for the user, the plugin turns off remote access on the account. The plugin never turns remote access on.
4. **Password copy.** The plugin saves a Jellyfin hash of the password on the account.
5. **Move to Default.** After Jellyfin completes the login, the plugin moves the user to the Default login method.

A Quick Connect login does not move a user to Default, because no password was checked.

For a user on the Emby login method, a password change in Jellyfin works like this:

- **New password:** the plugin saves it and moves the user to Default.
- **Password reset:** the plugin removes the saved password. The user stays on the Emby login method, so Emby checks the next login.

## Install

1. Build the plugin. The tools are pinned in `.mise.toml`.

   ```sh
   mise install
   dotnet publish src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj -c Release -o artifacts/plugin
   ```

2. Copy `artifacts/plugin/Jellyfin.Plugin.EmbyAuth.dll` to `<jellyfin config>/plugins/EmbyAuth_1.0.0.0/`.
3. Restart Jellyfin.

## Configure

1. Create an API key for the plugin on the Emby server.
2. In Jellyfin, open **Dashboard > Plugins > Emby Auth** and set both settings.

| Setting | Value |
|---|---|
| Emby server URL | The base URL of Emby as Jellyfin reaches it, for example `http://emby:8096`. Use `https`, or an address on a private network. The URL must not contain a user name or password. |
| Emby API key | The key from step 1. |

To configure the plugin through the API, send `POST /Plugins/e973e09a-e8b4-40c1-9be2-8e51342de1f9/Configuration` with `{"EmbyServerUrl": "http://emby:8096", "EmbyApiKey": "<key>"}`.

If a setting is missing or not valid, the plugin refuses each login that it handles and writes the cause to the Jellyfin log at Error level.

## Migrate users

### Users without a Jellyfin account

No action. The first login creates the account.

### Jellyfin accounts that must exist before the first login

For example, a watch-history sync can need an account to write to.

1. Create the account with a long random password. Do not create an account without a password: Jellyfin lets anyone log in to a Default account that has no password.
2. Set the login method of the account to **Emby** on the user's profile page in the dashboard. Alternatively, set `AuthenticationProviderId` to `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider` in `POST /Users/{userId}/Policy`.

The first login must use the Emby password. The random password does not work.

### Shut down Emby

1. List the users who are still on the Emby login method. In `GET /Users`, their `Policy.AuthenticationProviderId` is `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider`.
2. Ask those users, and Emby users who have no Jellyfin account, to log in to Jellyfin once while Emby runs.
3. For a user who cannot log in before the shutdown, set a new password in Jellyfin. The user moves to Default.
4. Do step 1 again. When the list is empty, shut down Emby and remove the plugin.

## Security notes

- The plugin sends a password to Emby only when the typed name is exactly the name of an enabled Emby user, ignoring case. A name that is not an Emby user name does not reach Emby.
- The plugin never logs a password or the API key. The end-to-end tests check the Jellyfin log at Debug level.
- Jellyfin administrators can read the API key through the plugin settings API.
- Emby checks remote access against the address of the caller, which is the Jellyfin server. The plugin applies Emby's remote access setting to the Jellyfin account instead.
- The plugin never authenticates a Jellyfin administrator through Emby.

## Limits

- After the move to Default, a password change on Emby does not change the Jellyfin password.
- An Emby user without a password cannot log in through the plugin. Set a password in Jellyfin for that user.
- Logins with an Emby Connect email address, or any name other than the Emby user name, are refused.
- A new Emby user can log in only after the plugin reads the Emby user list again, up to 60 seconds later. If the plugin cannot read the list, it refuses Emby logins for 30 seconds, then tries again.
- Jellyfin counts each refused login toward the lockout limit of the account, if the account has one. This includes logins that fail because Emby is down.
- If Jellyfin does not allow an Emby user name, the plugin cannot create the account. Rename the user on Emby.
- In a rare timing case, another session of the user can save an older copy of the account and put the user back on the Emby login method. Step 1 of the Emby shutdown finds these users.

## Development

| Task | Command |
|---|---|
| Install tools | `mise install` |
| Check formatting, and lint scripts and workflows | `mise run lint` |
| Build and run unit tests (warnings are errors) | `mise run test` |
| Run end-to-end tests in Docker | `mise run e2e` |
| Run pre-commit checks | `prek run` |
| Run a CI job in a local container | `act pull_request -j lint` (or `-j test`) |
| Run the CI end-to-end job in a local container | `act pull_request -j e2e --bind --container-options "--network host"` |

CI runs `mise run lint`, `mise run test`, and `mise run e2e` on every pull request, so the commands above match CI.

The end-to-end tests start Emby, an nginx proxy that logs the requests to Emby, and Jellyfin. Emby and Jellyfin listen on `127.0.0.1:18096` and `127.0.0.1:28096`. The tests remove the containers after the run. Set `KEEP_E2E=1` to keep them.
