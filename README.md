# Jellyfin Emby Auth

A Jellyfin plugin that moves users from Emby to Jellyfin without a password reset.

The plugin adds a Jellyfin login method named **Emby**. When a user logs in to Jellyfin, the plugin checks the password against your Emby server. If Emby accepts it, the plugin saves the password in Jellyfin. The user then moves to Jellyfin's **Default** login method, either at once or when you finish the migration. After that, Jellyfin checks the password of that user without Emby. When every user is on the Default login method, you can shut down Emby and remove the plugin.

**Status:** tested only in local containers, with Jellyfin 12.1.0 and Emby 4.10.0.40. Not tested on a production server. Not published to a plugin repository.

## Requirements

- Jellyfin 12.1. The plugin is built against the Jellyfin 12.1.0 packages.
- An Emby server that Jellyfin can reach over HTTP or HTTPS.
- An API key for the plugin on that Emby server.

## How it works

The plugin handles a login in two cases: the Jellyfin account uses the Emby login method, or no Jellyfin account has the typed name. Jellyfin handles all other logins as usual.

1. **Account checks.** The plugin refuses the login, and does not contact Emby, if the password is blank, or if the Jellyfin account is disabled or is an administrator.
2. **Saved password (only with "Check the saved Jellyfin password first").** If Emby verified the saved password of the account earlier, and the typed password matches it, Jellyfin accepts the login. The plugin does not contact Emby, so this works even if the Emby user was since disabled or deleted.
3. **Emby user list.** The plugin refuses the login, and does not send the password to Emby, if no enabled Emby user has exactly the typed name. The match ignores case and nothing else. The plugin reads the Emby user list with the API key and keeps it for 60 seconds.
4. **Emby check.** The plugin sends the name and password to Emby (`POST /Users/AuthenticateByName`). If Emby refuses the login or does not answer within 5 seconds, Jellyfin refuses the login. If Emby accepts, the plugin ends the Emby session that the login opened.
5. **Account rules.** Jellyfin refuses the login if Emby returns a different user name, or if the Jellyfin account uses another login method. If no Jellyfin account exists, the plugin creates one. The **Account access** setting decides the access of the new account.
6. **Password copy.** The plugin saves a Jellyfin hash of the password on the account. It also records a fingerprint of that hash, so that it can later tell a password that Emby verified from a password that an administrator set.
7. **Move to Default.** The **Migration behavior** setting decides when the user moves to the Default login method.

A Quick Connect login does not check a password, so it never moves a user to Default.

For a user on the Emby login method, a password change in Jellyfin works like this:

- **New password:** the plugin saves it and moves the user to Default at once.
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
2. In Jellyfin, open **Dashboard > Plugins > Emby Auth** and set the settings.

| Setting | Value |
|---|---|
| Emby server URL | The base URL of Emby as Jellyfin reaches it, for example `http://emby:8096`. Use `https`, or an address on a private network. The URL must not contain a user name or password. |
| Emby API key | The key from step 1. |
| Migration behavior | When users move to the Default login method. See [Migration behavior](#migration-behavior). Default: **Move each user to Jellyfin after the first login**. |
| Account access | The access of accounts that the plugin creates. See [Account access](#account-access). Default: **Jellyfin defaults, with Emby's remote access setting**. |

To configure the plugin through the API, send `POST /Plugins/e973e09a-e8b4-40c1-9be2-8e51342de1f9/Configuration` with a body like this:

```json
{
  "EmbyServerUrl": "http://emby:8096",
  "EmbyApiKey": "<key>",
  "MigrationMode": "MoveAfterFirstLogin",
  "AccountAccess": "CopyEmbyRemoteAccess"
}
```

If a setting is missing or not valid, the plugin refuses each login that it handles and writes the cause to the Jellyfin log at Error level.

### Migration behavior

| Choice | API value | Who checks the password | When the user moves to Default |
|---|---|---|---|
| Move each user to Jellyfin after the first login | `MoveAfterFirstLogin` | Emby, once | Right after the first login that Emby accepts |
| Keep Emby in charge until the migration task runs | `KeepEmbyInCharge` | Emby, on every login. A password change on Emby applies to the next login. | When you run the migration task |
| Check the saved Jellyfin password first, then Emby | `JellyfinPasswordFirst` | Jellyfin, if the saved password matches. Otherwise Emby. Users log in while Emby is down. After a password change on Emby, the old password works until the user logs in with the new one. | When you run the migration task |

### Run the migration

The **Migration** section at the bottom of **Dashboard > Plugins > Emby Auth** lists each user on the Emby login method:

- **ready**: Emby verified the saved password. The next migration run moves the user to the Default login method.
- **needs one login while Emby runs**: the user has not logged in to Jellyfin through Emby, or the password was reset. An administrator can also set a new password in Jellyfin, which moves the user at once.

**Run migration now** moves every ready user. It starts the scheduled task **Move Emby users to the Default login method**, which is also in **Dashboard > Advanced > Scheduled Tasks**, under **Emby Auth**. The task has no schedule. You can run it with any migration behavior. It never contacts Emby, and it does not create accounts: a Jellyfin account for an Emby user appears at that user's first login.

The same actions are in the API. Both need an administrator token:

| Request | Result |
|---|---|
| `GET /EmbyAuth/Migration` | `{"Users": [{"Name": "dave", "ReadyToMove": false}]}` for the users on the Emby login method |
| `POST /EmbyAuth/Migration/Run` | Starts the migration task, unless it already runs. Returns `204`. |

### Account access

| Choice | API value | New accounts | Existing accounts on the Emby login method |
|---|---|---|---|
| Jellyfin defaults, with Emby's remote access setting | `CopyEmbyRemoteAccess` | Jellyfin's default permissions. Remote access only if Emby allows it. | Lose remote access when Emby does not allow it. |
| No libraries until an administrator grants them, with Emby's remote access setting | `NoLibraries` | Jellyfin's default permissions, but no library access. Remote access only if Emby allows it. | Lose remote access when Emby does not allow it. |
| Jellyfin defaults only | `JellyfinDefaults` | Jellyfin's default permissions, including remote access. | No change. |

The plugin never gives administrator rights, and never turns remote access on for an existing account.

With **Jellyfin defaults only**, a user that Emby limits to the local network can connect to Jellyfin from the internet. Emby checks remote access against the address of the caller, which is the Jellyfin server, so Emby does not block these logins.

## Migrate users

### Users without a Jellyfin account

No action. The first login creates the account.

### Jellyfin accounts that must exist before the first login

For example, a watch-history sync can need an account to write to.

1. Create the account with a long random password. Do not create an account without a password: Jellyfin lets anyone log in to a Default account that has no password.
2. Set the login method of the account to **Emby** on the user's profile page in the dashboard. Alternatively, set `AuthenticationProviderId` to `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider` in `POST /Users/{userId}/Policy`.

The first login must use the Emby password. The random password does not work, and the migration task does not move the account until the user logs in through Emby.

### Shut down Emby

1. In the **Migration** section of the plugin settings, select **Run migration now**.
2. Ask each user in the list who is not ready, and each Emby user who has no Jellyfin account, to log in to Jellyfin once while Emby runs. Then do step 1 again.
3. For a user who cannot log in before the shutdown, set a new password in Jellyfin. The user moves to Default.
4. When the list says that no users are on the Emby login method, shut down Emby and remove the plugin.

## Security notes

- The plugin sends a password to Emby only when the typed name is exactly the name of an enabled Emby user, ignoring case. A name that is not an Emby user name does not reach Emby.
- The plugin never authenticates a Jellyfin administrator through Emby.
- The plugin never logs a password or the API key. The end-to-end tests check the Jellyfin log at Debug level.
- Jellyfin administrators can read the API key through the plugin settings API.
- The file `Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json`, in Jellyfin's plugin configuration folder, holds a SHA-256 fingerprint of each password hash that Emby verified. It does not hold the hashes.

## Limits

- After a user moves to Default, a password change on Emby does not change the Jellyfin password.
- An Emby user without a password cannot log in through the plugin. Set a password in Jellyfin for that user.
- Logins with an Emby Connect email address, or any name other than the Emby user name, are refused.
- A new Emby user can log in only after the plugin reads the Emby user list again, up to 60 seconds later. If the plugin cannot read the list, it refuses the Emby check for 30 seconds, then tries again.
- Jellyfin counts each refused login toward the lockout limit of the account, if the account has one. This includes logins that fail because Emby is down.
- If Jellyfin does not allow an Emby user name, the plugin cannot create the account. Rename the user on Emby.
- If the fingerprint file cannot be read or written, the plugin logs an error and moves no affected user to Default until that user logs in through Emby again.
- In a rare timing case, another session of the user can save an older copy of the account and put the user back on the Emby login method. Step 2 of the Emby shutdown finds these users.

## Development

| Task | Command |
|---|---|
| Install tools | `mise install` |
| Build and run unit tests (warnings are errors) | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` |
| Run end-to-end tests in Docker | `bats e2e` |
| Run pre-commit checks | `prek run` |
| Start a demo in Docker to log in and look around | `scripts/dev-env.sh up` |
| Show or remove the demo | `scripts/dev-env.sh status` or `scripts/dev-env.sh down` |

The end-to-end tests start Emby, an nginx proxy that logs the requests to Emby, and Jellyfin. Emby and Jellyfin listen on `127.0.0.1:18096` and `127.0.0.1:28096`; set `EMBY_PORT` and `JELLYFIN_PORT` to use other ports. The tests remove the containers after the run. Set `KEEP_E2E=1` to keep them.

The demo uses the same containers, on `127.0.0.1:18196` (Emby) and `127.0.0.1:28196` (Jellyfin), so it can run while the end-to-end tests run. `up` builds the plugin, starts new containers, configures the plugin, and creates demo users with different Emby settings. At the end, `up` shows the URLs, the logins, and what each demo user shows. `down` removes the containers and their data.
