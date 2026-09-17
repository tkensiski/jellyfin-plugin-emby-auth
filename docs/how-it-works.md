# How it works

The plugin adds a Jellyfin login method named **Emby**. It handles a login in two cases: the Jellyfin account uses the Emby login method, or no Jellyfin account has the typed name. Jellyfin handles all other logins as usual.

## Login steps

1. **Account checks.** The plugin refuses the login, and does not contact Emby, if the password is blank, or if the Jellyfin account is disabled or is an administrator.
2. **Saved password (only with "Check the saved Jellyfin password first").** If Emby verified the saved password of the account earlier, and the typed password matches it, Jellyfin accepts the login. The plugin does not contact Emby, so this works even if the Emby user was since disabled or deleted.
3. **Emby user list.** The plugin refuses the login, and does not send the password to Emby, if no enabled Emby user has exactly the typed name. The match ignores case and nothing else. The plugin reads the Emby user list with the API key and keeps it for 60 seconds.
4. **Emby check.** The plugin sends the name and password to Emby (`POST /Users/AuthenticateByName`). If Emby refuses the login or does not answer within 5 seconds, Jellyfin refuses the login. If Emby accepts, the plugin ends the Emby session that the login opened.
5. **Account rules.** Jellyfin refuses the login if Emby returns a different user name, or if the Jellyfin account uses another login method. If no Jellyfin account exists, the plugin creates one. The [Account access](settings.md#account-access) setting decides the access of the new account.
6. **Password copy.** The plugin saves a Jellyfin hash of the password on the account. It also records a fingerprint of that hash, so that it can later tell a password that Emby verified from a password that an administrator set.
7. **Move to Default.** The [Migration behavior](settings.md#migration-behavior) setting decides when the user moves to the Default login method.

A Quick Connect login does not check a password, so it never moves a user to Default.

## Password changes

For a user on the Emby login method, a password change in Jellyfin works like this:

- **New password:** the plugin saves it and moves the user to Default at once.
- **Password reset:** the plugin removes the saved password. The user stays on the Emby login method, so Emby checks the next login.

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
- In a rare timing case, another session of the user can save an older copy of the account and put the user back on the Emby login method. Step 2 of [Shut down Emby](migration.md#shut-down-emby) finds these users.
