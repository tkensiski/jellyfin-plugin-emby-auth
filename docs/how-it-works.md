# How it works

The plugin adds a Jellyfin login method named **Emby**. It handles a login in two cases: the Jellyfin account uses the Emby login method, or no Jellyfin account has the typed name. Jellyfin handles all other logins as usual.

## Login steps

1. **Account checks.** The plugin refuses the login, and does not contact Emby, if the password is blank, or if the Jellyfin account is disabled or is an administrator.
2. **Emby user list.** The plugin refuses the login, and does not send the password to Emby, if no enabled Emby user has exactly the typed name. The match ignores case and nothing else. The plugin reads the Emby user list with the API key and keeps it for 60 seconds.
3. **Emby check.** The plugin sends the name and password to Emby (`POST /Users/AuthenticateByName`). If Emby refuses the login or does not answer within 5 seconds, Jellyfin refuses the login. The plugin ends the Emby session whenever it can read an access token in Emby's response — including a response that has no user name, which Jellyfin still refuses.
4. **Account rules.** Jellyfin refuses the login if Emby returns a different user name, or if the Jellyfin account uses another login method. If no Jellyfin account exists, the plugin creates one. The [Account access](settings.md#account-access) setting decides the access of the new account.
5. **Password copy.** The plugin saves a Jellyfin hash of the password on the account. It also records a fingerprint of that hash, so that it can later tell a password that Emby verified from a password that an administrator set.
6. **Move to the migration target.** The [Migration behavior](settings.md#migration-behavior) setting decides when the user moves, and the [Migration target](settings.md#migration-target) setting decides which login method the user moves to.

A Quick Connect login does not check a password, so it never moves a user.

### The account-creation window

Jellyfin has no login call that creates an account with a password already set: `IUserManager.CreateUserAsync` takes only a name. So step 4's account creation happens in two calls. The plugin creates the account, then saves the Emby-verified hash and the Emby login method in the very next call. Between those two calls, the new account exists on the Default login method with no password. During that moment, a blank password on the Default login method would open the account.

The plugin makes the moment as short as it can. It computes the password hash before it creates the account, so the save is the very next call after creation, with nothing else in between.

If that save fails, the plugin deletes the new account and refuses the login. If the delete also fails, the account stays on the Default login method with no password. Either way, the plugin logs the account name at Error level in the Jellyfin log, so an administrator can remove the account or give it a password. Jellyfin's own account creation (`POST /Users/New`) has the same window, for the same reason.

## Password changes

For a user on the Emby login method, a password change in Jellyfin works like this:

- **New password:** the plugin saves it and moves the user to the [Password-set target](settings.md#password-set-target) at once.
- **Password reset:** the plugin removes the saved password. The user stays on the Emby login method, so Emby checks the next login.

## Security notes

- The plugin sends a password to Emby only when the typed name is exactly the name of an enabled Emby user, ignoring case. A name that is not an Emby user name does not reach Emby.
- The plugin never authenticates a Jellyfin administrator through Emby.
- The plugin never logs a password or the API key. The end-to-end tests check the Jellyfin log at Debug level.
- Jellyfin administrators can read the API key through the plugin settings API.
- A SQLite database named `Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.db`, in the plugin's own data folder inside Jellyfin's plugins directory rather than the plugin configuration folder, holds a SHA-256 fingerprint of each password hash that Emby verified. It does not hold the hashes.

### Accounts with no saved password

Jellyfin's Default login method opens an account that has no saved password with a blank password, verified in Jellyfin 12.1 at `DefaultAuthenticationProvider.cs:61-68`. This shapes the plugin in three places: the account that a failed password save leaves behind is deleted rather than left on Default without a password; the settings page names every account on the Emby login method that has no saved password and warns before a move; and the plugin is an interim tool whose end state is every user on a login method that checks a password the user chose. JellyfinSecurity 2.6.1's `TwoFactorAuthProvider` delegates its own password check to the first other enabled login method, which is Default, and so opens the same account the same way; its `BlockEmptyPasswordLogin` guard against this ships off by default. A login method not tested here that delegates its password check to Default the same way inherits the same behavior.

## Limits

- A new account has a brief moment on the Default login method with no password before the plugin saves the Emby-verified hash. See [The account-creation window](#the-account-creation-window).
- After a user moves off the Emby login method, a password change on Emby does not change the Jellyfin password.
- An Emby user without a password cannot log in through the plugin. Set a password in Jellyfin for that user.
- Logins with an Emby Connect email address, or any name other than the Emby user name, are refused.
- A new Emby user can log in only after the plugin reads the Emby user list again, up to 60 seconds later. If the plugin cannot read the list, it refuses the Emby check for 30 seconds, then tries again.
- Jellyfin counts each refused login toward the lockout limit of the account, if the account has one. This includes logins that fail because Emby is down.
- If Jellyfin does not allow an Emby user name, the plugin cannot create the account. Rename the user on Emby.
- If the plugin cannot read the verified-password records, it logs an error. Each record commits on its own as it is written, so a read failure never loses or rolls back a record already committed. While the read fails, the migration list reports every user with a saved password as readiness unknown, rather than ready or needing an Emby login, until the read succeeds again.
- If a record cannot be written, the plugin logs an error. That login's fingerprint was not saved, so the migration list reports the user as needing an Emby login, until a later Emby login succeeds and records the fingerprint.
- On the first start after this version's upgrade, the plugin imports the records from the old JSON file into the database once, leaves that JSON file on disk unchanged, and does not import again on a later start. A server that has started this version and then rolls back to an older one keeps whatever the JSON file held but loses every record written after the upgrade; affected users are not locked out — each simply logs in through Emby once more.
- When Emby sends a success response whose body the plugin cannot read, the access token is inside the body that failed to parse, so the plugin cannot end that Emby session, and one session can stay open on the Emby server. Such a response still refuses the Jellyfin login, and so does a response with no user name — this limit is about a session on the Emby server, never about who gets in.
- Jellyfin calls a login method inside a lock, and every login for a name Jellyfin does not know shares one lock key, so a burst of first logins runs one at a time. The plugin's calls to Emby happen inside that lock, so a slow Emby slows that queue. No fix removes this: the plugin has to ask Emby whether the password is right before it can answer. The lever that already exists is the 5-second request timeout, already described in [Login steps](#login-steps).
- Jellyfin saves the account on every accepted login. This is Jellyfin's own account save, not something the plugin does itself, so what it costs is Jellyfin's to decide — the plugin only calls it.
- In a rare timing case, another session of the user can save an older copy of the account and put the user back on the Emby login method. Step 2 of [Shut down Emby](migration.md#shut-down-emby) finds these users.
