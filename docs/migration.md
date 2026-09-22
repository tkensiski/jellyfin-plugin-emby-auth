# Migration

A user is migrated when the Jellyfin account of the user is on the configured [migration target](settings.md#migration-target) login method — Jellyfin's Default login method out of the box, or another login method you choose. After that, the migration target's own login method checks the password of that user; Emby is no longer involved.

## Users without a Jellyfin account

No action. The first login creates the account.

## Jellyfin accounts that must exist before the first login

For example, a watch-history sync can need an account to write to.

1. Create the account with a long random password. Do not create an account without a password: Jellyfin lets anyone log in to a Default account that has no password.
2. Set the login method of the account to **Emby** on the user's profile page in the dashboard. Alternatively, set `AuthenticationProviderId` to `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider` in `POST /Users/{userId}/Policy`.

The first login must use the Emby password. The random password does not work, and the migration does not move the account until the user logs in through Emby.

## Run the migration

The **Migration** section at the bottom of **Dashboard > Plugins > Emby Auth** lists each user on the Emby login method:

- **will move on the next run**: Emby verified the saved password. The next migration run moves the user to the migration target.
- **needs one login while Emby runs**: the user has not logged in to Jellyfin through Emby, or the password was reset. An administrator can also set a new password in Jellyfin, which moves the user at once, to the [password-set target](settings.md#password-set-target).
- **has no saved password**: the account has never had a Jellyfin password. The migration never moves this account and never invents a password for it; set one in Jellyfin first.
- **readiness unknown**: Jellyfin could not read the record of passwords Emby verified. The account is named, but its readiness cannot be reported until the read succeeds again; see the Jellyfin log.

Above **Run migration now**, the **Migration target** dropdown picks the destination login method. Clicking **Run migration now** saves that pick as the [migration target](settings.md#migration-target) setting, then starts the scheduled task **Finish the Emby migration**, which is also in **Dashboard > Advanced > Scheduled Tasks**, under **Emby Auth**. The task has no schedule. You can run it with any migration behavior. It never contacts Emby, and it does not create accounts: a Jellyfin account for an Emby user appears at that user's first login.

### Migration API

Both requests need an administrator token.

`GET /EmbyAuth/Migration` returns the current migration status:

```json
{
  "RecordsUnavailable": false,
  "Task": {
    "State": "Idle",
    "Progress": null,
    "LastEndTimeUtc": "2026-09-19T12:00:00Z",
    "LastResult": "Completed"
  },
  "Users": [
    { "Name": "dave", "State": "Ready" },
    { "Name": "erin", "State": "NoPassword" }
  ],
  "AvailableTargets": [
    { "Name": "Default", "Id": "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider" }
  ]
}
```

| Field | Meaning |
|---|---|
| `RecordsUnavailable` | `true` while Jellyfin cannot read the record of passwords Emby verified. Every user with a saved password then reports state `Unknown`. |
| `Task` | The migration task's state, or absent if no scheduled-task worker is registered for it. |
| `Users` | Each user on the Emby login method, with state `Ready`, `NeedsEmbyLogin`, `NoPassword`, or `Unknown`. |
| `AvailableTargets` | The login methods Jellyfin reports as enabled, with this plugin's own Emby method removed — the set of valid values for [Migration target](settings.md#migration-target) and [Password-set target](settings.md#password-set-target). |

`POST /EmbyAuth/Migration/Run` starts the migration task, unless it already runs. Returns `204`.

## Shut down Emby

1. In the **Migration** section of the plugin settings, select **Run migration now**.
2. Ask each user in the list who is not ready, and each Emby user who has no Jellyfin account, to log in to Jellyfin once while Emby runs. Then do step 1 again.
3. For a user who cannot log in before the shutdown, set a new password in Jellyfin. The user moves to the configured [password-set target](settings.md#password-set-target).
4. When the list says that no users are on the Emby login method, shut down Emby and remove the plugin.
