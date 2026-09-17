# Migration

A user is migrated when the Jellyfin account of the user is on Jellyfin's **Default** login method. After that, Jellyfin checks the password of that user without Emby.

## Users without a Jellyfin account

No action. The first login creates the account.

## Jellyfin accounts that must exist before the first login

For example, a watch-history sync can need an account to write to.

1. Create the account with a long random password. Do not create an account without a password: Jellyfin lets anyone log in to a Default account that has no password.
2. Set the login method of the account to **Emby** on the user's profile page in the dashboard. Alternatively, set `AuthenticationProviderId` to `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider` in `POST /Users/{userId}/Policy`.

The first login must use the Emby password. The random password does not work, and the migration does not move the account until the user logs in through Emby.

## Run the migration

The **Migration** section at the bottom of **Dashboard > Plugins > Emby Auth** lists each user on the Emby login method:

- **ready**: Emby verified the saved password. The next migration run moves the user to the Default login method.
- **needs one login while Emby runs**: the user has not logged in to Jellyfin through Emby, or the password was reset. An administrator can also set a new password in Jellyfin, which moves the user at once.

**Run migration now** moves every ready user. It starts the scheduled task **Move Emby users to the Default login method**, which is also in **Dashboard > Advanced > Scheduled Tasks**, under **Emby Auth**. The task has no schedule. You can run it with any migration behavior. It never contacts Emby, and it does not create accounts: a Jellyfin account for an Emby user appears at that user's first login.

### Migration API

Both requests need an administrator token.

| Request | Result |
|---|---|
| `GET /EmbyAuth/Migration` | `{"Users": [{"Name": "dave", "ReadyToMove": false}]}` for the users on the Emby login method |
| `POST /EmbyAuth/Migration/Run` | Starts the migration task, unless it already runs. Returns `204`. |

## Shut down Emby

1. In the **Migration** section of the plugin settings, select **Run migration now**.
2. Ask each user in the list who is not ready, and each Emby user who has no Jellyfin account, to log in to Jellyfin once while Emby runs. Then do step 1 again.
3. For a user who cannot log in before the shutdown, set a new password in Jellyfin. The user moves to Default.
4. When the list says that no users are on the Emby login method, shut down Emby and remove the plugin.
