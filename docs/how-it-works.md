# How it works

The plugin adds a Jellyfin login method named **Emby**. It handles a login in two cases: the Jellyfin account uses the Emby login method, or no Jellyfin account has the typed name. Jellyfin handles all other logins as usual.

## Login steps

1. **Account checks.** The plugin refuses the login, and does not contact Emby, if the password is blank, or if the Jellyfin account is disabled or is an administrator.
2. **Emby user list.** The plugin refuses the login, and does not send the password to Emby, if no enabled Emby user has exactly the typed name. The match ignores case and nothing else. The plugin reads the Emby user list with the API key and keeps it for 60 seconds.
3. **Emby check.** The plugin sends the name and password to Emby (`POST /Users/AuthenticateByName`). If Emby refuses the login or does not answer within 5 seconds, Jellyfin refuses the login. If Emby accepts, the plugin ends the Emby session that the login opened.
4. **Account rules.** Jellyfin refuses the login if Emby returns a different user name, or if the Jellyfin account uses another login method. If no Jellyfin account exists, the plugin creates one. The [Account access](settings.md#account-access) setting decides the access of the new account.
5. **Password copy.** The plugin saves a Jellyfin hash of the password on the account. It also records a fingerprint of that hash, so that it can later tell a password that Emby verified from a password that an administrator set.
6. **Move to Default.** The [Migration behavior](settings.md#migration-behavior) setting decides when the user moves to the Default login method.

A Quick Connect login does not check a password, so it never moves a user to Default.

### The account-creation window

Jellyfin has no login call that creates an account with a password already set: `IUserManager.CreateUserAsync` takes only a name. So step 4's account creation happens in two calls. The plugin creates the account, then saves the Emby-verified hash and the Emby login method in the very next call. Between those two calls, the new account exists on the Default login method with no password. During that moment, a blank password on the Default login method would open the account.

The plugin makes the moment as short as it can. It computes the password hash before it creates the account, so the save is the very next call after creation, with nothing else in between.

If that save fails, the plugin deletes the new account and refuses the login. If the delete also fails, the account stays on the Default login method with no password. Either way, the plugin logs the account name at Error level in the Jellyfin log, so an administrator can remove the account or give it a password. Jellyfin's own account creation (`POST /Users/New`) has the same window, for the same reason.

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

- A new account has a brief moment on the Default login method with no password before the plugin saves the Emby-verified hash. See [The account-creation window](#the-account-creation-window).
- After a user moves to Default, a password change on Emby does not change the Jellyfin password.
- An Emby user without a password cannot log in through the plugin. Set a password in Jellyfin for that user.
- Logins with an Emby Connect email address, or any name other than the Emby user name, are refused.
- A new Emby user can log in only after the plugin reads the Emby user list again, up to 60 seconds later. If the plugin cannot read the list, it refuses the Emby check for 30 seconds, then tries again.
- Jellyfin counts each refused login toward the lockout limit of the account, if the account has one. This includes logins that fail because Emby is down.
- If Jellyfin does not allow an Emby user name, the plugin cannot create the account. Rename the user on Emby.
- If Jellyfin cannot read the fingerprint file, the plugin logs an error. While the read fails, the plugin records no verified password and moves no user to the Default login method; a user who logs in during that time shows in the migration list as needing one login while Emby runs. The plugin keeps the records that are in the file — it does not replace the file while it cannot read it — and reads the file again on the next login, so a temporary problem, such as a file that is locked while Jellyfin starts, clears on its own and does not need a Jellyfin restart.
- If the fingerprint file cannot be written, the plugin logs an error and moves no affected user to Default until that user logs in through Emby again.
- In a rare timing case, another session of the user can save an older copy of the account and put the user back on the Emby login method. Step 2 of [Shut down Emby](migration.md#shut-down-emby) finds these users.
