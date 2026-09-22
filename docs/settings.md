# Settings

Open **Dashboard > Plugins > Emby Auth** in Jellyfin.

| Setting | Value |
|---|---|
| Emby server URL | The base URL of Emby as Jellyfin reaches it, for example `http://emby:8096`. Use `https`, or an address on a private network. The URL must not contain a user name or password. |
| Emby API key | An API key that you create for the plugin on the Emby server. |
| Migration behavior | When a user moves to the migration target. See [Migration behavior](#migration-behavior). Default: **Move each user to Jellyfin after the first login**. |
| Migration target | Where each user moves. Set in the Migration section of the settings page, not in this form. See [Migration target](#migration-target). Default: **Move to Default**. |
| Password-set target | Where a user moves when a password is set for that user in Jellyfin. See [Password-set target](#password-set-target). Default: **Same as the migration target**. |
| Account access | The access of accounts that the plugin creates. See [Account access](#account-access). Default: **Jellyfin defaults, with Emby's remote access setting**. |

If a setting is missing or not valid, the plugin refuses each login that it handles and writes the cause to the Jellyfin log at Error level.

If Jellyfin cannot load the settings, the page shows a message under Save and turns Save off, so that empty fields cannot replace the saved settings. Save turns back on when the settings load, which happens when you open the page again after the cause is fixed. If Jellyfin cannot save the settings, the page shows a message under Save, and Save stays off until you open the page again. Both messages point to the Jellyfin log, and neither message repeats the Emby server URL or the API key, because the URL can contain a user name and password.

## Set the settings through the API

Send `POST /Plugins/e973e09a-e8b4-40c1-9be2-8e51342de1f9/Configuration` with an administrator token and a body like this:

```json
{
  "EmbyServerUrl": "http://emby:8096",
  "EmbyApiKey": "<key>",
  "MigrationMode": "MoveAfterFirstLogin",
  "AccountAccess": "CopyEmbyRemoteAccess",
  "MigrationTarget": "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider",
  "PasswordSetTarget": ""
}
```

## Migration behavior

| Choice | API value | Who checks the password | When the user moves to the migration target |
|---|---|---|---|
| Move each user to Jellyfin after the first login | `MoveAfterFirstLogin` | Emby, once | Right after the first login that Emby accepts |
| Keep Emby in charge until the migration task runs | `KeepEmbyInCharge` | Emby, on every login. A password change on Emby applies to the next login. | When you [run the migration](migration.md#run-the-migration) |

This setting answers *when* a user moves; [Migration target](#migration-target) answers *where*. While the migration target is **Remain on Emby Login**, this setting has no effect, because no path moves anyone at all.

## Migration target

Where each user moves once the after-login move or the migration task accepts a saved, Emby-verified password. Set the dropdown in the **Migration** section of the settings page, directly above **Run migration now** — not in the settings form above — because it is both the destination picker for a manual run and the persisted setting at the same time; one control keeps them from disagreeing.

| Choice | API value | Meaning |
|---|---|---|
| Move to Default | The login method ID of Jellyfin's Default login method, `Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider` | Jellyfin's own Default login method. The out-of-the-box choice. |
| Move to *(a login method name)* | The login method's own ID | Any other login method Jellyfin reports as enabled, other than this plugin's own Emby method. One entry per such method. |
| Remain on Emby Login | `RemainOnEmbyLoginMethod` | No path moves anyone: not the after-login move, not the migration task, not the password-set path when [Password-set target](#password-set-target) resolves to it. There is no hidden fallback to Default. |

A migration target that Jellyfin does not report as an enabled login method is refused when you save, and so is this plugin's own Emby method. The previously saved settings are unchanged by a refused save.

Clicking **Run migration now** saves the picked destination first, then queues the migration task. A one-off run to a different destination also changes where the automatic after-login move goes from then on, because there is only one migration target — there is no separate, temporary destination for a single run.

## Password-set target

Where a user moves when an administrator or the user sets a password for that user in Jellyfin. Offers the same choices as [Migration target](#migration-target), plus a first entry, **Same as the migration target** (API value: an empty string), which is the default. Leaving it at the default makes an existing install, and an administrator who does not need the two paths to differ, keep today's behavior with nothing to configure.

## Account access

| Choice | API value | New accounts | Existing accounts on the Emby login method |
|---|---|---|---|
| Jellyfin defaults, with Emby's remote access setting | `CopyEmbyRemoteAccess` | Jellyfin's default permissions. Remote access only if Emby allows it. | Lose remote access when Emby does not allow it. |
| No libraries until an administrator grants them, with Emby's remote access setting | `NoLibraries` | Jellyfin's default permissions, but no library access. Remote access only if Emby allows it. | Lose remote access when Emby does not allow it. |
| Jellyfin defaults only | `JellyfinDefaults` | Jellyfin's default permissions, including remote access. | No change. |

The plugin never gives administrator rights, and never turns remote access on for an existing account.

With **Jellyfin defaults only**, a user that Emby limits to the local network can connect to Jellyfin from the internet. Emby checks remote access against the address of the caller, which is the Jellyfin server, so Emby does not block these logins.
